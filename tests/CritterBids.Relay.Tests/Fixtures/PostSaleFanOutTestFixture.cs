using CritterBids.Obligations;
using CritterBids.Relay;
using CritterBids.Relay.Hubs;
using JasperFx;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Relay.Tests.Fixtures;

/// <summary>
/// M6-S7 close-out fixture. Composes <b>Settlement-publish + Obligations + Relay together in one
/// host</b> — the inverse of the per-BC sibling-exclusion fixtures — to prove the post-sale
/// <b>fan-out</b>: one <c>SettlementCompleted</c> drives two independent sibling consumers, the
/// Obligations <c>PostSaleCoordinationSaga</c> start <i>and</i> the Relay <c>BiddingHub</c> winner
/// push, with no chain between them.
///
/// <para>The two halves have conflicting host requirements that no existing fixture satisfies at
/// once: the Relay push needs a <b>real Kestrel</b> host (SignalR's WebSocket transport cannot run
/// under Alba's in-memory <c>TestServer</c> — see <c>RelayHubTestFixture</c> and
/// <c>docs/skills/wolverine-signalr.md</c> §9), while the Obligations saga + <c>ObligationStatusView</c>
/// projection need <b>Marten</b>. This fixture is the merge: <c>RelayHubTestFixture</c>'s real-Kestrel
/// shape plus a Testcontainers PostgreSQL store, with both <c>AddObligationsModule()</c> and
/// <c>AddRelayModule()</c> active.</para>
///
/// <para><b>Sibling-consumer fidelity.</b> <c>MultipleHandlerBehavior.Separated</c> +
/// <c>MessageIdentity.IdAndDestination</c> mirror <c>Program.cs</c> so the Obligations and Relay
/// <c>SettlementCompleted</c> handlers run as independent destinations off one local publish — a true
/// fan-out, not a co-handled single execution. Every other BC is excluded from discovery (reusing the
/// existing <c>*BcDiscoveryExclusion</c> extensions); Obligations and Relay are <b>not</b> excluded.</para>
///
/// <para><b>No real-clock waits.</b> Default <see cref="ObligationsOptions"/> (Production durations —
/// days) means the saga's scheduled reminder/escalation timers never fire during a test; the test
/// drives the publish through Wolverine tracking and observes the push on a
/// <c>TaskCompletionSource</c> failsafe timeout. Single test, fresh container — no cross-test
/// envelope leakage.</para>
/// </summary>
public class PostSaleFanOutTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"fanout-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private WebApplication _app = null!;

    public string BaseUrl { get; private set; } = null!;

    public IHost Host => _app;

    public IMessageBus Bus => _app.Services.GetRequiredService<IMessageBus>();

    public IDocumentStore DocumentStore => _app.Services.GetRequiredService<IDocumentStore>();

    public string BiddingHubUrl => $"{BaseUrl}/hub/bidding";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var postgresConnectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        var builder = WebApplication.CreateBuilder();

        // Ephemeral port on loopback — discovered from app.Urls after start (mirrors RelayHubTestFixture).
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Marten primary store — required for the Obligations saga document + ObligationStatusView
        // Inline projection and for Relay's NotificationHistoryView writer. Same shape as the sibling
        // fixtures (ADR 011).
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

        builder.Services.AddObligationsModule();
        builder.Services.AddRelayModule();

        builder.Host.UseWolverine(opts =>
        {
            // Mirror Program.cs's modular-monolith isolation so the two SettlementCompleted handlers
            // are independent sibling destinations rather than one co-handled execution.
            opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
            opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
            opts.Durability.MessageStorageSchemaName = "wolverine";

            // Activate exactly the two BCs under test. Obligations is reached via PostSaleCoordinationSaga;
            // Relay via BiddingHub.
            opts.Discovery.IncludeAssembly(typeof(PostSaleCoordinationSaga).Assembly);
            opts.Discovery.IncludeAssembly(typeof(BiddingHub).Assembly);

            // Exclude every OTHER BC's handlers — their modules are not registered here, so their
            // aggregate / saga / read-model schema mappings and DI dependencies are absent. Obligations
            // and Relay are intentionally NOT excluded: they are the fan-out's two consumers. Reuses the
            // proven exclusion extensions defined in RelayTestFixture.cs.
            opts.Services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new AuctionsBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new ParticipantsBcDiscoveryExclusion());

            opts.Services.DisableAllExternalWolverineTransports();
            opts.Services.RunWolverineInSoloMode();

            // AutoApplyTransactions commits the Obligations event stream + ObligationStatusView and
            // Relay's NotificationHistoryView write. UseDurableLocalQueues mirrors Program.cs so the
            // saga's bus.ScheduleAsync envelopes persist (they target day-out instants and never fire
            // during the test).
            opts.Policies.AutoApplyTransactions();
            opts.Policies.UseDurableLocalQueues();
        });

        _app = builder.Build();

        _app.MapHub<BiddingHub>("/hub/bidding").DisableAntiforgery();
        _app.MapHub<OperationsHub>("/hub/operations").DisableAntiforgery();

        await _app.StartAsync();

        BaseUrl = _app.Urls.Single();
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
