using Alba;
using CritterBids.Auctions;
using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Auctions.Tests.Fixtures;

public class AuctionsTestFixture : IAsyncLifetime
{
    // Only PostgreSQL is needed for the Auctions BC — Marten is the only store registered
    // per ADR 011 (All-Marten Pivot). Program.cs's AddParticipantsModule() is registered in
    // the postgres-guarded block alongside the other modules, so no separate SQL Server
    // container is needed.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"auctions-postgres-test-{Guid.NewGuid():N}")
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

                // Register the Auctions BC module so its ConfigureMarten contributions
                // (auctions schema for Listing, LiveStreamAggregation<Listing>) are present.
                // Program.cs guards its AddAuctionsModule() call inside the postgres null
                // check, which the ConfigureServices path bypasses.
                services.AddAuctionsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // Exclude Selling BC handlers — ISellerRegistrationService is not registered
                // in this fixture (AddSellingModule() is not called). Without exclusion,
                // Wolverine's handler discovery for CreateDraftListingHandler.ValidateAsync
                // would fail code-gen due to the unresolvable ISellerRegistrationService
                // dependency. See critter-stack-testing-patterns.md §Cross-BC Handler Isolation.
                // Listings and Participants handlers have no unresolvable dependencies in this
                // fixture and do not need exclusion.
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

    public async Task<T?> LoadSaga<T>(Guid id) where T : Wolverine.Saga
    {
        await using var session = GetDocumentSession();
        return await session.LoadAsync<T>(id);
    }

    // ─── M3-S5b saga seed helpers ─────────────────────────────────────────────

    /// <summary>
    /// Seed an Auction Closing saga document directly in a specified pre-terminal state.
    /// Bypasses the Start handler — appropriate for scenario tests that need to land at
    /// a specific point in the saga's state machine (e.g. Active with N bids and reserve
    /// met) without re-running the BiddingOpened → BidPlaced → ReserveMet replay.
    /// Direct session.Store() does not forward — the saga is simply persisted, not started.
    /// </summary>
    public async Task SeedAuctionClosingSagaAsync(
        Guid listingId,
        AuctionClosingStatus status,
        DateTimeOffset scheduledCloseAt,
        DateTimeOffset originalCloseAt,
        int bidCount = 0,
        decimal currentHighBid = 0m,
        Guid? currentHighBidderId = null,
        bool reserveHasBeenMet = false,
        bool extendedBiddingEnabled = false)
    {
        await using var session = GetDocumentSession();
        session.Store(new AuctionClosingSaga
        {
            Id = listingId,
            ListingId = listingId,
            Status = status,
            ScheduledCloseAt = scheduledCloseAt,
            OriginalCloseAt = originalCloseAt,
            BidCount = bidCount,
            CurrentHighBid = currentHighBid,
            CurrentHighBidderId = currentHighBidderId,
            ReserveHasBeenMet = reserveHasBeenMet,
            ExtendedBiddingEnabled = extendedBiddingEnabled,
        });
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Seed a Listing primary stream with a single BiddingOpened event. The saga's
    /// Handle(CloseAuction) loads the Listing aggregate via AggregateStreamAsync to read
    /// SellerId for ListingSold emission — scenarios 3.5 and 3.11 need this seed alongside
    /// the saga seed. Session-scoped writes do not forward (per M3-S5 retro §OQ4), so this
    /// does not start a saga via the start handler.
    /// </summary>
    public async Task SeedListingStreamAsync(
        Guid listingId,
        Guid sellerId,
        DateTimeOffset scheduledCloseAt,
        decimal startingBid = 25m,
        decimal? buyItNowPrice = null)
    {
        await using var session = GetDocumentSession();
        var opened = new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: startingBid,
            ReserveThreshold: null,
            BuyItNowPrice: buyItNowPrice,
            ScheduledCloseAt: scheduledCloseAt,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromHours(24),
            OpenedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Listing>(listingId, opened);
        session.PendingChanges.Streams().Single().Events.Single().AddTag(new ListingStreamId(listingId));
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Append a synthetic ListingWithdrawn event to an existing listing stream, tagged so
    /// UseFastEventForwarding picks it up. Used by scenario 3.10 only — the Selling-side
    /// publisher is deferred per milestone doc §3, so the test fixture is the sole producer.
    /// Note: session-scoped appends do not forward; for forwarding-driven dispatch the test
    /// uses Host.InvokeMessageAndWaitAsync(new ListingWithdrawn(...)) instead. This helper
    /// exists for future stream-replay scenarios where the event must live on the stream.
    /// </summary>
    public async Task AppendListingWithdrawnAsync(Guid listingId)
    {
        await using var session = GetDocumentSession();
        var withdrawn = new ListingWithdrawn(listingId);
        session.Events.Append(listingId, withdrawn);
        session.PendingChanges.Streams().Single().Events.Single().AddTag(new ListingStreamId(listingId));
        await session.SaveChangesAsync();
    }
}

/// <summary>
/// Excludes Selling BC handlers from Wolverine's handler discovery in the Auctions test fixture.
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
                "Selling BC inactive — ISellerRegistrationService not registered (no AddSellingModule in Auctions fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });
    }
}
