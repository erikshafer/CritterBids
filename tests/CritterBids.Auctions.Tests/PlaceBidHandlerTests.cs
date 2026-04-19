using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// All 15 PlaceBid scenarios from docs/workshops/002-scenarios.md §1. Method names match
/// docs/milestones/M3-auctions-bc.md §7 exactly. These tests exercise PlaceBidHandler
/// through the full Wolverine pipeline — tag registration, boundary load, and
/// acceptance/rejection paths — via IMessageBus.InvokeAsync, with seeding using the
/// canonical BuildEvent + WithTag + Append shape from boundary_model_workflow_tests.cs.
///
/// TimeProvider is overridden per test via FakeTimeProvider so the extended-bidding
/// scenarios (1.11–1.15) can pin "now" relative to the seeded listing's close time.
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class PlaceBidHandlerTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public PlaceBidHandlerTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Scenario 1.1 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstBid_ProducesBidPlaced_AndBuyItNowOptionRemoved()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: 50m, buyItNow: 100m,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 30m, CreditCeiling: 500m),
            state,
            TimeProvider.System);

        events.Count.ShouldBe(2);
        var placed = events.OfType<BidPlaced>().Single();
        placed.Amount.ShouldBe(30m);
        placed.BidCount.ShouldBe(1);
        placed.IsProxy.ShouldBeFalse();
        events.OfType<BuyItNowOptionRemoved>().ShouldHaveSingleItem();
    }

    // ─── Scenario 1.2 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Outbid_ProducesBidPlaced_NoBuyItNowOptionRemoved()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId2 = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: 100m,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);
        await SeedBidPlaced(listingId, bidderId: Guid.CreateVersion7(), amount: 30m, bidCount: 1);
        await SeedBuyItNowRemoved(listingId);

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId2, 35m, CreditCeiling: 200m),
            state,
            TimeProvider.System);

        events.Count.ShouldBe(1);
        var placed = events.OfType<BidPlaced>().Single();
        placed.Amount.ShouldBe(35m);
        placed.BidCount.ShouldBe(2);
        events.OfType<BuyItNowOptionRemoved>().ShouldBeEmpty();
    }

    // ─── Scenario 1.3 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BelowStartingBid_ProducesBidRejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);

        var command = new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 20m, CreditCeiling: 500m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.AttemptedAmount.ShouldBe(20m);
        rejected.CurrentHighBid.ShouldBe(0m);
        rejected.Reason.ShouldBe("BelowMinimumBid");
        (await LoadListingEvents(listingId)).OfType<BidPlaced>().ShouldBeEmpty();
    }

    // ─── Scenario 1.4 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BelowIncrement_ProducesBidRejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);
        await SeedBidPlaced(listingId, bidderId: Guid.CreateVersion7(), amount: 30m, bidCount: 1);

        var command = new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 30.50m, CreditCeiling: 200m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.AttemptedAmount.ShouldBe(30.50m);
        rejected.CurrentHighBid.ShouldBe(30m);
        rejected.Reason.ShouldBe("BelowMinimumBid");
    }

    // ─── Scenario 1.5 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExceedsCreditCeiling_ProducesBidRejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);

        var command = new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 250m, CreditCeiling: 200m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.AttemptedAmount.ShouldBe(250m);
        rejected.Reason.ShouldBe("ExceedsCreditCeiling");
    }

    // ─── Scenario 1.6 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NoBiddingOpened_ProducesBidRejected()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();

        // Intentionally no SeedListing — no BiddingOpened in the stream, state.ListingId stays default.
        var command = new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 30m, CreditCeiling: 500m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.Reason.ShouldBe("ListingNotOpen");
    }

    // ─── Scenario 1.7 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingClosed_ProducesBidRejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        // ScheduledCloseAt in the past — S4 derives closure from timing since BiddingClosed
        // is not yet registered (S5 scope).
        var pastCloseAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: pastCloseAt, originalCloseAt: pastCloseAt,
            extendedEnabled: false);

        var command = new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 100m, CreditCeiling: 200m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.Reason.ShouldBe("ListingClosed");
    }

    // ─── Scenario 1.8 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SellerCannotBidOnOwnListing_ProducesBidRejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);

        var command = new PlaceBid(listingId, Guid.CreateVersion7(), sellerId, 30m, CreditCeiling: 500m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.BidderId.ShouldBe(sellerId);
        rejected.Reason.ShouldBe("SellerCannotBid");
    }

    // ─── Scenario 1.9 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReserveCrossed_ProducesReserveMet()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: 50m, buyItNow: null,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);
        await SeedBidPlaced(listingId, bidderId: Guid.CreateVersion7(), amount: 45m, bidCount: 3);

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 55m, CreditCeiling: 200m),
            state,
            TimeProvider.System);

        events.OfType<BidPlaced>().Single().Amount.ShouldBe(55m);
        events.OfType<BidPlaced>().Single().BidCount.ShouldBe(4);
        events.OfType<ReserveMet>().Single().Amount.ShouldBe(55m);
    }

    // ─── Scenario 1.10 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReserveAlreadyMet_NoDuplicateSignal()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: 50m, buyItNow: null,
            scheduledCloseAt: now.AddMinutes(5), originalCloseAt: now.AddMinutes(5),
            extendedEnabled: false);
        await SeedBidPlaced(listingId, bidderId: Guid.CreateVersion7(), amount: 55m, bidCount: 1);
        await SeedReserveMet(listingId, amount: 55m);

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 60m, CreditCeiling: 500m),
            state,
            TimeProvider.System);

        events.OfType<BidPlaced>().Single().Amount.ShouldBe(60m);
        events.OfType<ReserveMet>().ShouldBeEmpty();
    }

    // ─── Scenario 1.11 ────────────────────────────────────────────────────────

    [Fact]
    public async Task BidInTriggerWindow_ProducesExtendedBiddingTriggered()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var anchorT0 = DateTimeOffset.UtcNow; // T+0
        var close = anchorT0.AddMinutes(5);    // T+5:00
        var now = anchorT0.AddMinutes(4).AddSeconds(40); // T+4:40

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: close, originalCloseAt: close,
            extendedEnabled: true,
            triggerWindow: TimeSpan.FromSeconds(30),
            extension: TimeSpan.FromSeconds(15),
            maxDuration: TimeSpan.FromMinutes(5));
        await SeedBidPlaced(listingId, bidderId: Guid.CreateVersion7(), amount: 30m, bidCount: 1);
        await SeedBuyItNowRemoved(listingId);

        var state = await LoadState(listingId);
        var fakeTime = new FixedTimeProvider(now);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 35m, CreditCeiling: 200m),
            state,
            fakeTime);

        events.OfType<BidPlaced>().Single().Amount.ShouldBe(35m);
        var triggered = events.OfType<ExtendedBiddingTriggered>().Single();
        triggered.PreviousCloseAt.ShouldBe(close);
        triggered.NewCloseAt.ShouldBe(now.AddSeconds(15)); // T+4:55 per workshop formula
        triggered.TriggeredByBidderId.ShouldBe(bidderId);
    }

    // ─── Scenario 1.12 ────────────────────────────────────────────────────────

    [Fact]
    public async Task BidOutsideTriggerWindow_NoExtendedBiddingTriggered()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var anchorT0 = DateTimeOffset.UtcNow;
        var close = anchorT0.AddMinutes(5);
        var now = anchorT0.AddMinutes(2); // T+2:00 — 3 minutes before close, outside 30s window

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: 100m,
            scheduledCloseAt: close, originalCloseAt: close,
            extendedEnabled: true,
            triggerWindow: TimeSpan.FromSeconds(30),
            extension: TimeSpan.FromSeconds(15),
            maxDuration: TimeSpan.FromMinutes(5));

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 30m, CreditCeiling: 500m),
            state,
            new FixedTimeProvider(now));

        events.OfType<BidPlaced>().ShouldHaveSingleItem();
        events.OfType<BuyItNowOptionRemoved>().ShouldHaveSingleItem();
        events.OfType<ExtendedBiddingTriggered>().ShouldBeEmpty();
    }

    // ─── Scenario 1.13 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtendedBiddingDisabled_NoExtension()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var anchorT0 = DateTimeOffset.UtcNow;
        var close = anchorT0.AddMinutes(5);
        var now = anchorT0.AddMinutes(4).AddSeconds(50); // T+4:50 — 10s before close

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: close, originalCloseAt: close,
            extendedEnabled: false);
        await SeedBidPlaced(listingId, bidderId: Guid.CreateVersion7(), amount: 30m, bidCount: 1);

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 35m, CreditCeiling: 200m),
            state,
            new FixedTimeProvider(now));

        events.OfType<BidPlaced>().ShouldHaveSingleItem();
        events.OfType<ExtendedBiddingTriggered>().ShouldBeEmpty();
    }

    // ─── Scenario 1.14 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtensionWithinMaxDuration_Fires()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var anchorT0 = DateTimeOffset.UtcNow;
        var originalClose = anchorT0.AddMinutes(5); // T+5:00
        // Current ScheduledCloseAt is T+9:50 (multiple prior extensions).
        var currentClose = anchorT0.AddMinutes(9).AddSeconds(50);
        var now = anchorT0.AddMinutes(9).AddSeconds(40); // T+9:40 — 10s before close

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: currentClose, originalCloseAt: originalClose,
            extendedEnabled: true,
            triggerWindow: TimeSpan.FromSeconds(30),
            extension: TimeSpan.FromSeconds(15),
            maxDuration: TimeSpan.FromMinutes(5));

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 90m, CreditCeiling: 200m),
            state,
            new FixedTimeProvider(now));

        var triggered = events.OfType<ExtendedBiddingTriggered>().Single();
        triggered.NewCloseAt.ShouldBe(now.AddSeconds(15)); // T+9:55, within max close T+10:00
    }

    // ─── Scenario 1.15 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtensionExceedsMaxDuration_Blocked()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var bidderId = Guid.CreateVersion7();
        var anchorT0 = DateTimeOffset.UtcNow;
        var originalClose = anchorT0.AddMinutes(5);                   // T+5:00
        var currentClose = anchorT0.AddMinutes(9).AddSeconds(55);     // T+9:55
        var now = anchorT0.AddMinutes(9).AddSeconds(50);              // T+9:50 — 5s before close

        await SeedListing(listingId, sellerId, startingBid: 25m, reserve: null, buyItNow: null,
            scheduledCloseAt: currentClose, originalCloseAt: originalClose,
            extendedEnabled: true,
            triggerWindow: TimeSpan.FromSeconds(30),
            extension: TimeSpan.FromSeconds(15),
            maxDuration: TimeSpan.FromMinutes(5));

        var state = await LoadState(listingId);
        var events = PlaceBidHandler.Decide(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 95m, CreditCeiling: 200m),
            state,
            new FixedTimeProvider(now));

        events.OfType<BidPlaced>().ShouldHaveSingleItem();
        // newCloseAt would be T+10:05, exceeding maxClose T+10:00 (OriginalCloseAt + MaxDuration).
        events.OfType<ExtendedBiddingTriggered>().ShouldBeEmpty();
    }

    // ─── Seeding + query helpers ──────────────────────────────────────────────

    private async Task SeedListing(
        Guid listingId,
        Guid sellerId,
        decimal startingBid,
        decimal? reserve,
        decimal? buyItNow,
        DateTimeOffset scheduledCloseAt,
        DateTimeOffset originalCloseAt,
        bool extendedEnabled,
        TimeSpan? triggerWindow = null,
        TimeSpan? extension = null,
        TimeSpan? maxDuration = null)
    {
        await using var session = _fixture.GetDocumentSession();
        // Seed BiddingOpened with the ORIGINAL close so Apply(BiddingOpened) sets
        // OriginalCloseAt := originalCloseAt. If the current ScheduledCloseAt differs (test is
        // simulating an already-extended listing), append a synthetic ExtendedBiddingTriggered
        // to move ScheduledCloseAt forward while leaving OriginalCloseAt alone.
        var opened = new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: startingBid,
            ReserveThreshold: reserve,
            BuyItNowPrice: buyItNow,
            ScheduledCloseAt: originalCloseAt,
            ExtendedBiddingEnabled: extendedEnabled,
            ExtendedBiddingTriggerWindow: triggerWindow,
            ExtendedBiddingExtension: extension,
            MaxDuration: maxDuration ?? TimeSpan.FromMinutes(5),
            OpenedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Listing>(listingId, opened);
        var wrapped = session.PendingChanges.Streams().Single().Events.Single();
        wrapped.AddTag(new ListingStreamId(listingId));
        await session.SaveChangesAsync();

        if (originalCloseAt != scheduledCloseAt)
        {
            await SeedSyntheticExtension(listingId, originalCloseAt, scheduledCloseAt);
        }
    }

    private async Task SeedSyntheticExtension(Guid listingId, DateTimeOffset previousClose, DateTimeOffset newClose)
    {
        await using var session = _fixture.GetDocumentSession();
        var extended = new ExtendedBiddingTriggered(
            ListingId: listingId,
            PreviousCloseAt: previousClose,
            NewCloseAt: newClose,
            TriggeredByBidderId: Guid.Empty,
            TriggeredAt: DateTimeOffset.UtcNow);
        var wrapped = session.Events.BuildEvent(extended);
        wrapped.AddTag(new ListingStreamId(listingId));
        session.Events.Append(listingId, wrapped);
        await session.SaveChangesAsync();
    }

    private async Task SeedBidPlaced(Guid listingId, Guid bidderId, decimal amount, int bidCount)
    {
        await using var session = _fixture.GetDocumentSession();
        var placed = new BidPlaced(
            ListingId: listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: bidderId,
            Amount: amount,
            BidCount: bidCount,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow);
        var wrapped = session.Events.BuildEvent(placed);
        wrapped.AddTag(new ListingStreamId(listingId));
        session.Events.Append(listingId, wrapped);
        await session.SaveChangesAsync();
    }

    private async Task SeedBuyItNowRemoved(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        var removed = new BuyItNowOptionRemoved(listingId, DateTimeOffset.UtcNow);
        var wrapped = session.Events.BuildEvent(removed);
        wrapped.AddTag(new ListingStreamId(listingId));
        session.Events.Append(listingId, wrapped);
        await session.SaveChangesAsync();
    }

    private async Task SeedReserveMet(Guid listingId, decimal amount)
    {
        await using var session = _fixture.GetDocumentSession();
        var met = new ReserveMet(listingId, amount, DateTimeOffset.UtcNow);
        var wrapped = session.Events.BuildEvent(met);
        wrapped.AddTag(new ListingStreamId(listingId));
        session.Events.Append(listingId, wrapped);
        await session.SaveChangesAsync();
    }

    private async Task<BidConsistencyState> LoadState(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        var tag = new ListingStreamId(listingId);
        var query = new EventTagQuery()
            .Or<BiddingOpened, ListingStreamId>(tag)
            .Or<BidPlaced, ListingStreamId>(tag)
            .Or<BuyItNowOptionRemoved, ListingStreamId>(tag)
            .Or<ReserveMet, ListingStreamId>(tag)
            .Or<ExtendedBiddingTriggered, ListingStreamId>(tag);
        var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
        return boundary.Aggregate ?? new BidConsistencyState();
    }

    private async Task<BidRejected> LoadSingleRejection(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        var streamKey = BidRejectionAudit.StreamKey(listingId);
        var events = await session.Events.FetchStreamAsync(streamKey);
        events.ShouldHaveSingleItem();
        return events.Single().Data.ShouldBeOfType<BidRejected>();
    }

    private async Task<IReadOnlyList<object>> LoadListingEvents(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(listingId);
        return events.Select(e => e.Data).ToList();
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) { _now = now; }
    public override DateTimeOffset GetUtcNow() => _now;
}
