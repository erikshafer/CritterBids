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
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());

                // Exclude Listings BC handlers — AuctionStatusHandler (M3-S6) handles the same
                // five auction integration events the Auctions saga starts/advances on
                // (BiddingOpened, BidPlaced, BiddingClosed, ListingSold, ListingPassed). With
                // MultipleHandlerBehavior.Separated, each cross-BC handler gets its own endpoint
                // and Host.InvokeMessageAndWaitAsync becomes ambiguous, surfacing as a sticky-
                // handler NoHandlerForEndpointException. The Auctions fixture does not register
                // AddListingsModule(), so dropping these handlers from discovery is safe.
                services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());

                // Exclude Settlement BC handlers — PendingSettlementHandler (M5-S3) handles
                // ListingPassed (and others) the same way Listings handlers do. Without this
                // exclusion the saga's NoRoutes-bucket assertions on ListingPassed flip to
                // the Sent bucket because Settlement's handler claims a local in-process route.
                // The Auctions fixture does not register AddSettlementModule(), so the
                // PendingSettlement schema isn't present and the handler couldn't run anyway.
                services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());
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
    /// Seed an Auctions-side <see cref="ParticipantCreditCeiling"/> row directly. M4-S4
    /// added this projection (sourced from <c>ParticipantSessionStarted</c> on the
    /// <c>auctions-participants-events</c> queue); <see cref="StartProxyBidManagerSagaHandler"/>
    /// reads it at saga-start and throws <see cref="ParticipantCreditCeilingNotFoundException"/>
    /// on miss. Test fixtures bypass the cross-BC event flow by seeding the projection
    /// row directly — workshop default <c>500m</c> (participant-002's ceiling per Workshop
    /// 002 setup) keeps S3-era scenarios safely above their exhaustion thresholds.
    /// </summary>
    public async Task SeedParticipantCreditCeilingAsync(Guid bidderId, decimal creditCeiling = 500m)
    {
        await using var session = GetDocumentSession();
        session.Store(new ParticipantCreditCeiling
        {
            BidderId      = bidderId,
            CreditCeiling = creditCeiling,
            RegisteredAt  = DateTimeOffset.UtcNow,
        });
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Seed an Auctions-side <see cref="PublishedListings"/> cache row directly. M4-S5
    /// added this projection (sourced from <c>ListingPublished</c> on the existing
    /// <c>auctions-selling-events</c> queue wired at M3-S3). Two consumers read it within
    /// Auctions at handler hot-paths: <c>AttachListingToSession</c>'s handler
    /// (Workshop 002 §5.3 reject-not-published check) and <see cref="SessionStartedHandler"/>'s
    /// per-listing fan-out (Workshop 002 §5.5).
    ///
    /// <para>Workshop 002 default parameter values per "Listing-A" preamble:
    /// <c>StartingBid: $25</c>, no reserve, no BIN, no extended bidding. Defaults stay
    /// explicit in the signature so test bodies show their data choices — same precedent
    /// as <see cref="SeedParticipantCreditCeilingAsync"/> from M4-S4.</para>
    ///
    /// <para><c>Duration</c> defaults to <c>null</c> (Flash format); Timed-format tests
    /// pass an explicit non-null TimeSpan. Both shapes are valid for the projection's
    /// field contract; the Flash null shape is the one Session fan-out tests use, since
    /// fan-out reads <c>DurationMinutes</c> off the Session aggregate (OQ5 Path B), not
    /// off this projection.</para>
    /// </summary>
    public async Task SeedPublishedListingAsync(
        Guid listingId,
        Guid sellerId,
        decimal startingBid = 25m,
        decimal? reservePrice = null,
        decimal? buyItNowPrice = null,
        TimeSpan? duration = null,
        bool extendedBiddingEnabled = false,
        TimeSpan? extendedBiddingTriggerWindow = null,
        TimeSpan? extendedBiddingExtension = null,
        DateTimeOffset? publishedAt = null,
        PublishedListingsStatus status = PublishedListingsStatus.Published)
    {
        await using var session = GetDocumentSession();
        session.Store(new PublishedListings
        {
            Id                            = listingId,
            SellerId                      = sellerId,
            StartingBid                   = startingBid,
            ReservePrice                  = reservePrice,
            BuyItNowPrice                 = buyItNowPrice,
            Duration                      = duration,
            ExtendedBiddingEnabled        = extendedBiddingEnabled,
            ExtendedBiddingTriggerWindow  = extendedBiddingTriggerWindow,
            ExtendedBiddingExtension      = extendedBiddingExtension,
            PublishedAt                   = publishedAt ?? DateTimeOffset.UtcNow,
            WithdrawnAt                   = status == PublishedListingsStatus.Withdrawn
                                              ? DateTimeOffset.UtcNow
                                              : null,
            Status                        = status,
        });
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// Append a synthetic ListingWithdrawn event to an existing listing stream, tagged so
    /// UseFastEventForwarding picks it up. Originally authored at M3-S5b as a stand-in for
    /// the absent Selling-side producer; as of M4-S2 the real producer lives in
    /// <c>CritterBids.Selling.WithdrawListingHandler</c>.
    ///
    /// Unit-test shortcut, kept after M4-S2 for coverage economy. Real-producer integration
    /// coverage lives in <c>RealSellingProducerSagaTerminationTests</c>, which dispatches
    /// the actual Selling <c>WithdrawListing</c> command through a combined Selling+Auctions
    /// Wolverine runtime. Continue to use this helper for saga-replay or fixture-seeding
    /// scenarios that only need the event shape on the stream.
    /// </summary>
    public async Task AppendListingWithdrawnAsync(Guid listingId)
    {
        await using var session = GetDocumentSession();
        // M4-S1: ListingWithdrawn extended with WithdrawnBy/Reason/WithdrawnAt under ADR 005
        // additive versioning. M3 Auction Closing saga path (scenario 3.10) does not read the
        // new fields — seller-withdrawal defaults used here.
        var withdrawn = new ListingWithdrawn(
            ListingId: listingId,
            WithdrawnBy: Guid.NewGuid(),
            Reason: null,
            WithdrawnAt: DateTimeOffset.UtcNow);
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

/// <summary>
/// Excludes Listings BC handlers from Wolverine's handler discovery in the Auctions test fixture.
/// AuctionStatusHandler (M3-S6) handles five auction integration events the saga also handles;
/// MultipleHandlerBehavior.Separated splits them across endpoints, breaking InvokeMessageAndWaitAsync.
/// The Listings module isn't registered here, so the projection handlers have nowhere useful to run.
/// </summary>
internal sealed class ListingsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Listings BC inactive — AddListingsModule not called in Auctions fixture; AuctionStatusHandler would shadow saga handlers under MultipleHandlerBehavior.Separated",
                t => t.Namespace?.StartsWith("CritterBids.Listings") == true);
        });
    }
}

/// <summary>
/// Excludes Settlement BC handlers from Wolverine's handler discovery in the Auctions test fixture.
/// PendingSettlementHandler (M5-S3) handles ListingPassed (and others) the same way Listings's
/// AuctionStatusHandler does — under MultipleHandlerBehavior.Separated, the second handler claims
/// its own endpoint, flipping the saga's NoRoutes-bucket assertions to the Sent bucket. The
/// Settlement module isn't registered in this fixture, so the PendingSettlement schema isn't
/// present and the handler couldn't run anyway.
/// </summary>
internal sealed class SettlementBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Settlement BC inactive — AddSettlementModule not called in Auctions fixture; PendingSettlementHandler would shadow saga handlers under MultipleHandlerBehavior.Separated",
                t => t.Namespace?.StartsWith("CritterBids.Settlement") == true);
        });
    }
}
