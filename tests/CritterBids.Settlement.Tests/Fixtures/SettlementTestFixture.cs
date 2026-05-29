using Alba;
using CritterBids.Settlement;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Settlement.Tests.Fixtures;

public class SettlementTestFixture : IAsyncLifetime
{
    // Only PostgreSQL is needed for the Settlement BC — Marten is the only store registered
    // per ADR 011 (All-Marten Pivot). The Settlement saga document and its future projections
    // (PendingSettlement, BidderCreditView) all live in the shared primary store.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"settlement-postgres-test-{Guid.NewGuid():N}")
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
                .IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true);

                // Register the Settlement BC module so its ConfigureMarten contributions
                // (settlement schema for SettlementSaga, numeric-revisions saga registration)
                // are present. Program.cs guards its AddSettlementModule() call inside the
                // postgres null check, which the ConfigureServices path bypasses.
                services.AddSettlementModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // ─── Cross-BC handler isolation (per project_cross_bc_handler_isolation.md) ──
                //
                // The Settlement fixture only registers AddSettlementModule(). Wolverine still
                // discovers handlers from every assembly listed in Program.cs's IncludeAssembly
                // calls (Participants, Selling, Listings, Auctions, Settlement). Foreign-BC
                // handlers whose modules aren't registered here will fail either DI validation
                // (Selling: ISellerRegistrationService) or Marten code-gen (Auctions / Listings
                // operate on aggregates whose schema mappings aren't configured). Excluding them
                // from handler discovery is the canonical fix.

                // Exclude Selling BC handlers — ISellerRegistrationService is not registered
                // in this fixture (no AddSellingModule() call). Without exclusion, Wolverine's
                // handler discovery for CreateDraftListingHandler.ValidateAsync would fail
                // code-gen due to the unresolvable ISellerRegistrationService dependency.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());

                // Exclude Auctions BC handlers — handlers operate on the Listing aggregate and
                // the AuctionClosingSaga document; their schema mappings come from
                // AddAuctionsModule(), which the Settlement fixture does not call.
                services.AddSingleton<IWolverineExtension>(new AuctionsBcDiscoveryExclusion());

                // Exclude Listings BC handlers — AuctionStatusHandler / ListingSnapshotHandler
                // operate on CatalogListingView, whose schema mapping comes from
                // AddListingsModule(). The Settlement fixture does not register that module,
                // so the projection handlers have nowhere useful to run.
                services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());

                // Exclude Obligations BC handlers — SettlementCompletedHandler (M6-S2) starts the
                // PostSaleCoordinationSaga on the SettlementCompleted this fixture's saga emits.
                // AddObligationsModule() is not called here, so the obligations schema (saga doc +
                // event stream) is absent; without exclusion the co-consumer would fail on its
                // StartStream and fault the SettlementSaga happy-path test's tracked session.
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
}

/// <summary>
/// Excludes Selling BC handlers from Wolverine's handler discovery in the Settlement test fixture.
/// The Selling BC module is not registered here (no ISellerRegistrationService), so handlers
/// like CreateDraftListingHandler that depend on it cannot be code-generated.
/// </summary>
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — ISellerRegistrationService not registered (no AddSellingModule in Settlement fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });
    }
}

/// <summary>
/// Excludes Auctions BC handlers from Wolverine's handler discovery in the Settlement test fixture.
/// Auctions handlers operate on the Listing aggregate and the AuctionClosingSaga document; their
/// schema mappings come from AddAuctionsModule(), which the Settlement fixture does not call.
/// </summary>
internal sealed class AuctionsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Auctions BC inactive — Listing / AuctionClosingSaga schema not registered (no AddAuctionsModule in Settlement fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Auctions") == true);
        });
    }
}

/// <summary>
/// Excludes Listings BC handlers from Wolverine's handler discovery in the Settlement test fixture.
/// AuctionStatusHandler / ListingSnapshotHandler operate on CatalogListingView, whose schema
/// mapping comes from AddListingsModule(); the Settlement fixture does not register that module.
/// </summary>
internal sealed class ListingsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Listings BC inactive — CatalogListingView schema not registered (no AddListingsModule in Settlement fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Listings") == true);
        });
    }
}

/// <summary>
/// Excludes Obligations BC handlers from Wolverine's handler discovery in the Settlement test
/// fixture. SettlementCompletedHandler (M6-S2) starts the PostSaleCoordinationSaga on the
/// SettlementCompleted this fixture's SettlementSaga emits; AddObligationsModule() is not called
/// here, so the obligations saga + event-stream schema is absent. Without exclusion the co-consumer
/// would fault on StartStream and break the saga happy-path test's tracked session.
/// </summary>
internal sealed class ObligationsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Obligations BC inactive — PostSaleCoordinationSaga schema not registered (no AddObligationsModule in Settlement fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Obligations") == true);
        });
    }
}
