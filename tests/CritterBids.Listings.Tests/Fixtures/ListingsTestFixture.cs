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

                // Exclude Settlement BC handlers — PendingSettlementHandler (M5-S3) handles
                // ListingPublished, ListingPassed, and ListingWithdrawn against the
                // PendingSettlement schema. AddSettlementModule() is not called here, so the
                // schema isn't configured; allowing the handler to be discovered would either
                // crash on session.Store or land messages in the wrong tracked-bucket.
                services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());

                // Exclude Obligations BC handlers — SettlementCompletedHandler (M6-S2) is globally
                // discovered via Program.cs assembly inclusion. AddObligationsModule() is not called
                // here, so the obligations saga + event-stream schema is absent. Keeps the Listings
                // fixture's foreign-BC exclusion posture consistent.
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

    // ─── M4-S6 session-membership helpers ─────────────────────────────────────

    /// <summary>
    /// Seed a CatalogListingView document at Status = "Published" with SessionId
    /// pre-populated — convenience for tests that want the post-attach baseline without
    /// running the full ListingPublished → ListingAttachedToSession dispatch chain. The
    /// SessionStartedAt field is left null; tests exercising SessionStarted invoke
    /// AuctionsSessionHandler.Handle(SessionStarted) directly after seeding.
    /// </summary>
    public async Task SeedSessionAttachedListingAsync(
        Guid sessionId,
        Guid listingId,
        Guid? sellerId = null,
        string title = "Mint Condition Foil Black Lotus",
        string format = "Flash",
        decimal startingBid = 50_000m,
        decimal? buyItNow = 150_000m,
        TimeSpan? duration = null,
        DateTimeOffset? publishedAt = null)
    {
        await using var session = GetDocumentSession();
        session.Store(new CatalogListingView
        {
            Id          = listingId,
            SellerId    = sellerId ?? Guid.CreateVersion7(),
            Title       = title,
            Format      = format,
            StartingBid = startingBid,
            BuyItNow    = buyItNow,
            Duration    = duration,
            PublishedAt = publishedAt ?? DateTimeOffset.UtcNow,
            Status      = "Published",
            SessionId   = sessionId,
        });
        await session.SaveChangesAsync();
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

/// <summary>
/// Excludes Settlement BC handlers from Wolverine's handler discovery in the Listings test fixture.
/// PendingSettlementHandler (M5-S3) handles ListingPublished and other cross-BC events the Listings
/// projection also consumes. The Settlement module is not registered here, so the PendingSettlement
/// schema is absent; the handler would either crash on session.Store or interfere with tracked-
/// bucket assertions if discovered.
/// </summary>
internal sealed class SettlementBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Settlement BC inactive — AddSettlementModule not called in Listings fixture; PendingSettlementHandler would interfere with handler routing under MultipleHandlerBehavior.Separated",
                t => t.Namespace?.StartsWith("CritterBids.Settlement") == true);
        });
    }
}

/// <summary>
/// Excludes Obligations BC handlers from Wolverine's handler discovery in the Listings test fixture.
/// SettlementCompletedHandler (M6-S2) is globally discovered via Program.cs assembly inclusion;
/// AddObligationsModule() is not called here, so the obligations saga + event-stream schema is
/// absent. Keeps the Listings fixture's foreign-BC exclusion posture consistent.
/// </summary>
internal sealed class ObligationsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Obligations BC inactive — AddObligationsModule not called in Listings fixture; PostSaleCoordinationSaga schema absent",
                t => t.Namespace?.StartsWith("CritterBids.Obligations") == true);
        });
    }
}
