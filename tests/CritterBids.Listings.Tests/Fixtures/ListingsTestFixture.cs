using Alba;
using CritterBids.Listings;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Listings.Tests.Fixtures;

public class ListingsTestFixture : IAsyncLifetime
{
    // Only PostgreSQL is needed for the Listings BC — Marten is the only store registered.
    // Program.cs's AddParticipantsModule() is null-guarded on the sqlserver connection string,
    // which is absent in this fixture. Polecat is never registered here.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"listings-postgres-test-{Guid.NewGuid():N}")
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

                // Register the Listings BC module so its ConfigureMarten contributions are
                // present. Program.cs guards this call inside the postgres null check, which
                // the ConfigureServices path bypasses.
                services.AddListingsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // Exclude Selling BC handlers — ISellerRegistrationService is not registered
                // in this fixture (AddSellingModule() is not called). Without exclusion,
                // Wolverine's handler discovery for CreateDraftListingHandler.ValidateAsync
                // would fail code-gen due to the unresolvable ISellerRegistrationService dependency.
                // See critter-stack-testing-patterns.md §Cross-BC Handler Isolation.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
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

    // ─── M3-S6 catalog-extension helpers ──────────────────────────────────────

    /// <summary>
    /// Seed a CatalogListingView document directly in its M2 published-but-not-opened
    /// baseline state — lets auction-status tests start from a realistic post-publish view
    /// without re-running the full ListingPublished pipeline. Mirrors the
    /// SeedAuctionClosingSagaAsync shape from AuctionsTestFixture.
    /// </summary>
    public async Task SeedCatalogListingViewAsync(
        Guid listingId,
        Guid sellerId,
        string title = "Mint Condition Foil Black Lotus",
        string format = "Timed",
        decimal startingBid = 50_000m,
        decimal? buyItNow = 150_000m,
        TimeSpan? duration = null,
        DateTimeOffset? publishedAt = null)
    {
        await using var session = GetDocumentSession();
        session.Store(new CatalogListingView
        {
            Id          = listingId,
            SellerId    = sellerId,
            Title       = title,
            Format      = format,
            StartingBid = startingBid,
            BuyItNow    = buyItNow,
            Duration    = duration ?? TimeSpan.FromDays(7),
            PublishedAt = publishedAt ?? DateTimeOffset.UtcNow,
            Status      = "Published"
        });
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Load the current CatalogListingView document by ListingId. Returns null if the
    /// view does not exist — auction-status tests assert on the post-handler state by
    /// re-reading after Host.InvokeMessageAndWaitAsync returns.
    /// </summary>
    public async Task<CatalogListingView?> LoadCatalogListingViewAsync(Guid listingId)
    {
        await using var session = GetDocumentSession();
        return await session.LoadAsync<CatalogListingView>(listingId);
    }
}

/// <summary>
/// Excludes Selling BC handlers from Wolverine's handler discovery in the Listings test fixture.
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
                "Selling BC inactive — ISellerRegistrationService not registered (no AddSellingModule in Listings fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });
    }
}
