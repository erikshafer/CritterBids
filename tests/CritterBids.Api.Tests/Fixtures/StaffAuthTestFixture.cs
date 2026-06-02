using CritterBids.Api.Auth;
using CritterBids.Auctions;
using CritterBids.Listings;
using CritterBids.Obligations;
using CritterBids.Operations;
using CritterBids.Participants;
using CritterBids.Relay;
using CritterBids.Relay.Hubs;
using CritterBids.Selling;
using CritterBids.Settlement;
using JasperFx;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritterBids.Api.Tests.Fixtures;

/// <summary>
/// The M7-S6 staff-auth proof fixture (ADR-024 item 8). Stands up the <b>real production wiring</b>
/// — Marten over a Testcontainers PostgreSQL instance, every BC module, the Relay hubs, the
/// <c>StaffToken</c> scheme + <c>StaffOnly</c> policy, and <c>MapWolverineEndpoints()</c> — on a
/// <b>real Kestrel</b> host bound to an ephemeral loopback port. Alba's in-memory <c>TestServer</c>
/// is deliberately not used: the SignalR WebSocket transport needs a real socket, and ADR-024 item 8
/// mandates the gate be proven against the real host, not a test double.
///
/// <para>The host mirrors <c>Program.cs</c> as closely as a hand-built fixture can: it registers all
/// seven Marten BC modules plus Relay, wires the same shared <c>AddStaffTokenAuthentication()</c> /
/// <c>AddStaffAuthorizationPolicy()</c> extensions the host uses, and runs the same
/// <c>UseAuthentication()</c> → <c>UseAuthorization()</c> → <c>MapWolverineEndpoints()</c> order. The
/// only deliberate divergences are test-shaped: external transports are disabled (no RabbitMQ),
/// Wolverine runs solo, and local queues stay <b>buffered</b> (not durable) so the benign background
/// failures of mutation cascades against non-existent aggregates — the gated mutation endpoints
/// return 202 before their command is handled — do not persist or storm retries.</para>
/// </summary>
public class StaffAuthTestFixture : IAsyncLifetime
{
    /// <summary>The configured staff token. Valid requests present exactly this; invalid requests present anything else.</summary>
    public const string ValidStaffToken = "m7-s6-staff-auth-test-token";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"staff-auth-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private WebApplication _app = null!;

    public string BaseUrl { get; private set; } = null!;

    public IDocumentStore DocumentStore => _app.Services.GetRequiredService<IDocumentStore>();

    public string OperationsHubUrl => $"{BaseUrl}/hub/operations";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var postgresConnectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [StaffAuthConstants.StaffTokenConfigKey] = ValidStaffToken,
        });

        builder.Services.AddMarten(opts =>
            {
                opts.Connection(postgresConnectionString);
                opts.DatabaseSchemaName = "public";
                opts.Events.AppendMode = EventAppendMode.Quick;
                opts.Events.UseMandatoryStreamTypeDeclaration = true;
                opts.DisableNpgsqlLogging = true;
            })
            .UseLightweightSessions()
            .ApplyAllDatabaseChangesOnStartup()
            .IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true);

        // Every Marten BC module + Relay — the full production surface, so all gated endpoints map and
        // their handler dependencies resolve exactly as in Program.cs (no discovery exclusions needed).
        builder.Services.AddParticipantsModule();
        builder.Services.AddSellingModule();
        builder.Services.AddListingsModule();
        builder.Services.AddAuctionsModule();
        builder.Services.AddSettlementModule();
        builder.Services.AddObligationsModule();
        builder.Services.AddOperationsModule();
        builder.Services.AddRelayModule();

        builder.Services.AddWolverineHttp();

        // The exact production auth wiring (ADR-024) — shared so prod and test never drift.
        builder.Services.AddStaffTokenAuthentication();
        builder.Services.AddStaffAuthorizationPolicy();

        builder.UseWolverine(opts =>
        {
            opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
            opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
            opts.Durability.MessageStorageSchemaName = "wolverine";

            opts.Discovery.IncludeAssembly(typeof(Participant).Assembly);
            opts.Discovery.IncludeAssembly(typeof(SellerListing).Assembly);
            opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly);
            opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);
            opts.Discovery.IncludeAssembly(typeof(SettlementSaga).Assembly);
            opts.Discovery.IncludeAssembly(typeof(PostSaleCoordinationSaga).Assembly);
            opts.Discovery.IncludeAssembly(typeof(SettlementQueueView).Assembly);
            opts.Discovery.IncludeAssembly(typeof(BiddingHub).Assembly);

            opts.Services.DisableAllExternalWolverineTransports();
            opts.Services.RunWolverineInSoloMode();

            opts.Policies.AutoApplyTransactions();
            // Buffered (not durable) local queues: the gated mutation endpoints return 202 before their
            // command is handled, and several cascades target non-existent aggregates and fail by design.
            // Keeping queues buffered means those benign failures neither persist nor storm retries.
        });

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapWolverineEndpoints();

        _app.MapHub<BiddingHub>("/hub/bidding").DisableAntiforgery();
        _app.MapHub<OperationsHub>("/hub/operations").DisableAntiforgery();

        await _app.StartAsync();

        BaseUrl = _app.Urls.Single();
    }

    /// <summary>An <see cref="HttpClient"/> rooted at the host with no staff credential.</summary>
    public HttpClient CreateAnonymousClient() => new() { BaseAddress = new Uri(BaseUrl) };

    /// <summary>An <see cref="HttpClient"/> presenting the valid staff token on every request.</summary>
    public HttpClient CreateStaffClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add(StaffAuthConstants.StaffTokenHeader, ValidStaffToken);
        return client;
    }

    /// <summary>An <see cref="HttpClient"/> presenting an incorrect staff token on every request.</summary>
    public HttpClient CreateInvalidTokenClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add(StaffAuthConstants.StaffTokenHeader, "not-the-staff-token");
        return client;
    }

    /// <summary>Builds an OperationsHub connection, optionally appending the access_token query credential.</summary>
    public HubConnection BuildOperationsConnection(string? accessToken)
    {
        var url = accessToken is null
            ? OperationsHubUrl
            : $"{OperationsHubUrl}?{StaffAuthConstants.AccessTokenQueryKey}={accessToken}";

        return new HubConnectionBuilder().WithUrl(url).Build();
    }

    /// <summary>Resets all Marten data between tests so seeded view rows do not leak across cases.</summary>
    public async Task ResetMartenAsync()
    {
        var store = DocumentStore;
        await store.Advanced.Clean.DeleteAllDocumentsAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
            e is OperationCanceledException or ObjectDisposedException)) { }

        await _postgres.DisposeAsync();
    }
}

/// <summary>The xUnit collection that serializes the staff-auth tests onto one shared fixture/container.</summary>
[CollectionDefinition(Name)]
public sealed class StaffAuthTestCollection : ICollectionFixture<StaffAuthTestFixture>
{
    public const string Name = "Staff auth integration";
}
