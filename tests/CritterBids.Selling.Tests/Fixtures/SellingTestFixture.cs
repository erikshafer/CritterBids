using Alba;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Tracking;

namespace CritterBids.Selling.Tests.Fixtures;

public class SellingTestFixture : IAsyncLifetime
{
    // Only PostgreSQL is needed for the Selling BC — Marten is the only store registered.
    // Program.cs's AddParticipantsModule() is null-guarded on the sqlserver connection string,
    // which is absent in this fixture. Polecat is never registered here.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"selling-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var postgresConnectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Register the primary Marten store with the Testcontainers connection string.
                // Program.cs's AddMarten() is null-guarded on the Aspire postgres connection
                // string, which is absent in tests. ConfigureServices runs after Program.cs, so
                // this registration is always present and wins for IDocumentStore resolution.
                services.AddMarten(opts =>
                {
                    opts.Connection(postgresConnectionString);
                    opts.DatabaseSchemaName = "public";
                    opts.Events.AppendMode = EventAppendMode.Quick;
                    opts.Events.UseMandatoryStreamTypeDeclaration = true;
                    opts.DisableNpgsqlLogging = true;
                })
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine();

                // Register the Selling BC module so ISellerRegistrationService and
                // ConfigureMarten contributions are present. Program.cs guards this call
                // inside the postgres null check, which the ConfigureServices path bypasses.
                services.AddSellingModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // Exclude Settlement BC handlers — PendingSettlementHandler (M5-S3) handles
                // ListingPublished and ListingWithdrawn (events Selling produces). Without
                // exclusion, the handler would be discovered and attempt to write to a
                // PendingSettlement schema that isn't registered (AddSettlementModule isn't
                // called in this fixture). Per critter-stack-testing-patterns.md
                // §Cross-BC Handler Isolation.
                services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());

                // Exclude Auctions BC handlers — AuctionClosingSaga handles ListingWithdrawn
                // (event Selling now produces as of M4-S2) and ListingPublishedHandler handles
                // ListingPublished. Both operate on Auctions-owned schema (Listing aggregate,
                // AuctionClosingSaga document) registered by AddAuctionsModule(), which this
                // fixture does not call. The saga lookup throws UnknownSagaException without
                // exclusion. Mirrors the Settlement-fixture pattern.
                services.AddSingleton<IWolverineExtension>(new AuctionsBcDiscoveryExclusion());

                // Exclude Listings BC handlers — ListingSnapshotHandler / AuctionStatusHandler /
                // SettlementStatusHandler operate on CatalogListingView, whose schema mapping
                // comes from AddListingsModule(). Mirrors the Settlement-fixture pattern; added
                // alongside the Auctions exclusion at M4-S2 to keep the Selling fixture's
                // cross-BC exclusion posture consistent across all foreign BCs that consume
                // Selling-produced contracts.
                services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());

                // Exclude Obligations BC handlers — SettlementCompletedHandler (M6-S2) is globally
                // discovered via Program.cs assembly inclusion. AddObligationsModule() is not called
                // here, so the obligations saga + event-stream schema is absent. Selling never
                // produces SettlementCompleted in-process, but excluding keeps this fixture's
                // foreign-BC posture consistent and pre-empts any saga-handler code-gen surprise.
                services.AddSingleton<IWolverineExtension>(new ObligationsBcDiscoveryExclusion());
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }

    // ─── Cleanup helpers ──────────────────────────────────────────────────────

    public Task CleanAllMartenDataAsync() => Host.CleanAllMartenDataAsync();
    public Task ResetAllMartenDataAsync() => Host.ResetAllMartenDataAsync();

    // ─── Query helpers ────────────────────────────────────────────────────────

    public Marten.IDocumentSession GetDocumentSession() =>
        Host.DocumentStore().LightweightSession();

    // ─── Wolverine invocation helpers ────────────────────────────────────────

    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.ExecuteAndWaitAsync(
            async ctx => await ctx.InvokeAsync(message),
            timeoutSeconds * 1000);
    }

    public async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
        Action<Scenario> configuration)
    {
        IScenarioResult result = null!;
        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });
        return (tracked, result);
    }
}

/// <summary>
/// Excludes Settlement BC handlers from Wolverine's handler discovery in the Selling test fixture.
/// PendingSettlementHandler (M5-S3) handles ListingPublished and ListingWithdrawn — events Selling
/// produces. The Settlement module is not registered here, so the PendingSettlement schema is
/// absent; the handler would attempt to write to an unregistered schema if discovered.
/// Per critter-stack-testing-patterns.md §Cross-BC Handler Isolation.
/// </summary>
internal sealed class SettlementBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Settlement BC inactive — AddSettlementModule not called in Selling fixture; PendingSettlementHandler would write to an unregistered schema",
                t => t.Namespace?.StartsWith("CritterBids.Settlement") == true);
        });
    }
}

/// <summary>
/// Excludes Auctions BC handlers from Wolverine's handler discovery in the Selling test fixture.
/// AuctionClosingSaga handles ListingWithdrawn (added at M4-S2 as a real producer) and
/// ListingPublishedHandler handles ListingPublished — both operate on schema registered by
/// AddAuctionsModule(), which the Selling fixture does not call. The saga lookup throws
/// UnknownSagaException without exclusion.
/// </summary>
internal sealed class AuctionsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Auctions BC inactive — AddAuctionsModule not called in Selling fixture; AuctionClosingSaga / Listing schema absent",
                t => t.Namespace?.StartsWith("CritterBids.Auctions") == true);
        });
    }
}

/// <summary>
/// Excludes Listings BC handlers from Wolverine's handler discovery in the Selling test fixture.
/// CatalogListingView projection handlers (ListingSnapshotHandler, AuctionStatusHandler,
/// SettlementStatusHandler) require schema mappings from AddListingsModule(), which the Selling
/// fixture does not call. Pre-empts the same failure shape that the Settlement and Auctions
/// exclusions guard against.
/// </summary>
internal sealed class ListingsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Listings BC inactive — AddListingsModule not called in Selling fixture; CatalogListingView schema absent",
                t => t.Namespace?.StartsWith("CritterBids.Listings") == true);
        });
    }
}

/// <summary>
/// Excludes Obligations BC handlers from Wolverine's handler discovery in the Selling test fixture.
/// SettlementCompletedHandler (M6-S2) is globally discovered via Program.cs assembly inclusion;
/// AddObligationsModule() is not called here, so the obligations saga + event-stream schema is
/// absent. Keeps the Selling fixture's foreign-BC exclusion posture consistent.
/// </summary>
internal sealed class ObligationsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Obligations BC inactive — AddObligationsModule not called in Selling fixture; PostSaleCoordinationSaga schema absent",
                t => t.Namespace?.StartsWith("CritterBids.Obligations") == true);
        });
    }
}
