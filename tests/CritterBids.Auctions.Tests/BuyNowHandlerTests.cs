using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using JasperFx.Events.Tags;
using Marten;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// All 4 BuyNow scenarios from docs/workshops/002-scenarios.md §2. Method names match
/// docs/milestones/M3-auctions-bc.md §7 §2 rows exactly. Mirrors PlaceBidHandlerTests' shape:
/// acceptance-path test uses <c>BuyNowHandler.Decide</c>; rejection-path tests go through
/// <c>IMessageBus.InvokeMessageAndWaitAsync</c> and assert <see cref="BidRejected"/> landed
/// on the shared <see cref="BidRejectionAudit"/> stream with the correct reason code.
///
/// Rejections reuse <see cref="BidRejected"/> rather than introducing a <c>BuyNowRejected</c>
/// event — per the M3-S4 retro's "What M3-S4b should know" guidance. The reason string
/// discriminates the BuyNow path.
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class BuyNowHandlerTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public BuyNowHandlerTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Scenario 2.1 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyNow_NoPriorBids_ProducesBuyItNowPurchased()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var buyerId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, buyItNow: 100m,
            scheduledCloseAt: now.AddMinutes(5));

        var state = await LoadState(listingId);
        var events = BuyNowHandler.Decide(
            new BuyNow(listingId, buyerId, CreditCeiling: 200m),
            state,
            TimeProvider.System);

        events.Count.ShouldBe(1);
        var purchased = events.OfType<BuyItNowPurchased>().Single();
        purchased.ListingId.ShouldBe(listingId);
        purchased.BuyerId.ShouldBe(buyerId);
        purchased.Price.ShouldBe(100m);
    }

    // ─── Scenario 2.2 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyNow_OptionRemoved_Rejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var buyerId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, buyItNow: 100m,
            scheduledCloseAt: now.AddMinutes(5));
        await SeedBidPlaced(listingId, bidderId: Guid.CreateVersion7(), amount: 30m, bidCount: 1);
        await SeedBuyItNowRemoved(listingId);

        var command = new BuyNow(listingId, buyerId, CreditCeiling: 200m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.BidderId.ShouldBe(buyerId);
        rejected.Reason.ShouldBe("BuyItNowNotAvailable");
        (await LoadListingEvents(listingId)).OfType<BuyItNowPurchased>().ShouldBeEmpty();
    }

    // ─── Scenario 2.3 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyNow_ExceedsCreditCeiling_Rejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var buyerId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedListing(listingId, sellerId, startingBid: 25m, buyItNow: 100m,
            scheduledCloseAt: now.AddMinutes(5));

        var command = new BuyNow(listingId, buyerId, CreditCeiling: 50m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.BidderId.ShouldBe(buyerId);
        rejected.AttemptedAmount.ShouldBe(100m);
        rejected.Reason.ShouldBe("ExceedsCreditCeiling");
        (await LoadListingEvents(listingId)).OfType<BuyItNowPurchased>().ShouldBeEmpty();
    }

    // ─── Scenario 2.4 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BuyNow_ListingClosed_Rejected()
    {
        var (listingId, sellerId) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var buyerId = Guid.CreateVersion7();
        // ScheduledCloseAt in the past — BiddingClosed / ListingPassed are S5 scope; until
        // then, closure is derived from timing. Mirrors PlaceBidHandlerTests scenario 1.7.
        var pastCloseAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await SeedListing(listingId, sellerId, startingBid: 25m, buyItNow: 100m,
            scheduledCloseAt: pastCloseAt);

        var command = new BuyNow(listingId, buyerId, CreditCeiling: 200m);
        await _fixture.Host.InvokeMessageAndWaitAsync(command);

        var rejected = await LoadSingleRejection(listingId);
        rejected.BidderId.ShouldBe(buyerId);
        rejected.Reason.ShouldBe("ListingClosed");
        (await LoadListingEvents(listingId)).OfType<BuyItNowPurchased>().ShouldBeEmpty();
    }

    // ─── Seeding + query helpers ──────────────────────────────────────────────

    private async Task SeedListing(
        Guid listingId,
        Guid sellerId,
        decimal startingBid,
        decimal buyItNow,
        DateTimeOffset scheduledCloseAt)
    {
        await using var session = _fixture.GetDocumentSession();
        var opened = new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: startingBid,
            ReserveThreshold: null,
            BuyItNowPrice: buyItNow,
            ScheduledCloseAt: scheduledCloseAt,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromMinutes(5),
            OpenedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Listing>(listingId, opened);
        session.PendingChanges.Streams().Single().Events.Single().AddTag(new ListingStreamId(listingId));
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

    private async Task<BidConsistencyState> LoadState(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        var query = BuyNowHandler.BuildQuery(listingId);
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
