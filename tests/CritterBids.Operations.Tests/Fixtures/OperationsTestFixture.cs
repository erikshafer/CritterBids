using Alba;
using CritterBids.Operations;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Operations.Tests.Fixtures;

public class OperationsTestFixture : IAsyncLifetime
{
    // Only PostgreSQL is needed for the Operations BC — Marten is the only store registered per
    // ADR 011 (All-Marten Pivot). Operations is documents-only (M7 §5): the SettlementQueueView
    // read model lives in the shared primary store under the "operations" schema; there is no
    // saga or event-sourced aggregate.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"operations-postgres-test-{Guid.NewGuid():N}")
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

                // Register the Operations BC module so its ConfigureMarten contribution (the
                // operations schema for SettlementQueueView) is present. Program.cs guards its
                // AddOperationsModule() call inside the postgres null check, which the
                // ConfigureServices path bypasses.
                services.AddOperationsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // ─── Cross-BC handler isolation (per project_cross_bc_handler_isolation.md) ──
                //
                // The Operations fixture only registers AddOperationsModule(). Wolverine still
                // discovers handlers from every assembly listed in Program.cs's IncludeAssembly
                // calls. Foreign-BC handlers whose modules aren't registered here would fail
                // either DI validation (Selling: ISellerRegistrationService) or Marten code-gen
                // (Auctions / Listings / Settlement / Obligations operate on aggregates and saga
                // documents whose schema mappings aren't configured), and several co-consume the
                // Settlement-family events this fixture dispatches directly. Excluding the six
                // foreign BCs from handler discovery is the canonical fix.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new AuctionsBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new ObligationsBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new RelayBcDiscoveryExclusion());
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
/// Excludes Selling BC handlers from Wolverine's handler discovery in the Operations test
/// fixture. The Selling BC module is not registered here (no ISellerRegistrationService), so
/// handlers like CreateDraftListingHandler that depend on it cannot be code-generated.
/// </summary>
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — ISellerRegistrationService not registered (no AddSellingModule in Operations fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });
    }
}

/// <summary>
/// Excludes Auctions BC handlers from Wolverine's handler discovery in the Operations test
/// fixture. Auctions handlers operate on the Listing aggregate and the AuctionClosingSaga
/// document; their schema mappings come from AddAuctionsModule(), which this fixture does not call.
/// </summary>
internal sealed class AuctionsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Auctions BC inactive — Listing / AuctionClosingSaga schema not registered (no AddAuctionsModule in Operations fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Auctions") == true);
        });
    }
}

/// <summary>
/// Excludes Listings BC handlers from Wolverine's handler discovery in the Operations test
/// fixture. Listings' SettlementStatusHandler co-consumes SettlementCompleted (which the
/// settlement-queue projection test dispatches directly) and operates on CatalogListingView, whose
/// schema mapping comes from AddListingsModule(); this fixture does not register that module.
/// </summary>
internal sealed class ListingsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Listings BC inactive — CatalogListingView schema not registered (no AddListingsModule in Operations fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Listings") == true);
        });
    }
}

/// <summary>
/// Excludes Settlement BC handlers from Wolverine's handler discovery in the Operations test
/// fixture. The Settlement saga + projection handlers operate on the SettlementSaga and
/// PendingSettlement documents, whose schema mappings come from AddSettlementModule(); this
/// fixture does not register that module. Excluding them also prevents Settlement handlers from
/// co-consuming the Settlement-family events the settlement-queue projection test dispatches.
/// </summary>
internal sealed class SettlementBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Settlement BC inactive — SettlementSaga / PendingSettlement schema not registered (no AddSettlementModule in Operations fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Settlement") == true);
        });
    }
}

/// <summary>
/// Excludes Obligations BC handlers from Wolverine's handler discovery in the Operations test
/// fixture. Obligations' SettlementCompletedHandler starts the PostSaleCoordination saga on
/// SettlementCompleted (co-consuming the event the projection test dispatches) and operates on the
/// PostSaleCoordinationSaga document, whose schema mapping comes from AddObligationsModule(); this
/// fixture does not register that module.
/// </summary>
internal sealed class ObligationsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Obligations BC inactive — PostSaleCoordinationSaga schema not registered (no AddObligationsModule in Operations fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Obligations") == true);
        });
    }
}

/// <summary>
/// Excludes Relay BC handlers from Wolverine's handler discovery in the Operations test fixture.
/// Relay's SettlementCompleted notification handler is globally discovered via Program.cs
/// IncludeAssembly and the unconditional AddRelayModule(). The settlement-queue projection test
/// dispatches SettlementCompleted directly; excluding Relay keeps its push handler from
/// co-consuming it.
/// </summary>
internal sealed class RelayBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Relay BC inactive — push-only consumer excluded from Operations fixture to avoid co-consuming shared events",
                t => t.Namespace?.StartsWith("CritterBids.Relay") == true);
        });
    }
}
