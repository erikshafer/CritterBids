using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// End-to-end dispatch smoke test for the DCB BuyNow handler. Verifies that
/// <see cref="BuyNowHandler"/> is discovered by Wolverine, the <c>EventTagQuery</c> loads
/// <see cref="BidConsistencyState"/>, and the boundary commit appends
/// <see cref="BuyItNowPurchased"/> to the listing's tagged stream with the BIN price from
/// the boundary state.
///
/// The 4 scenario tests in <see cref="BuyNowHandlerTests"/> exercise the bus for rejection
/// paths; this single acceptance-path dispatch covers the "handler registered and routable
/// via IMessageBus" half of the M3-S4b exit criterion.
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class BuyNowDispatchTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public BuyNowDispatchTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BuyNow_DispatchedViaBus_AppendsBuyItNowPurchasedToTaggedStream()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, buyItNow: 100m, close: now.AddMinutes(5));

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new BuyNow(listingId, buyerId, CreditCeiling: 200m));

        await using var session = _fixture.GetDocumentSession();
        var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(
            BuyNowHandler.BuildQuery(listingId));

        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.IsOpen.ShouldBeFalse();
        boundary.Aggregate.BuyItNowAvailable.ShouldBeFalse();

        var streamEvents = await session.Events.FetchStreamAsync(listingId);
        var purchased = streamEvents.Select(e => e.Data).OfType<BuyItNowPurchased>().Single();
        purchased.BuyerId.ShouldBe(buyerId);
        purchased.Price.ShouldBe(100m);
    }

    private async Task SeedOpenListing(Guid listingId, Guid sellerId, decimal startingBid, decimal buyItNow, DateTimeOffset close)
    {
        await using var session = _fixture.GetDocumentSession();
        var opened = new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: startingBid,
            ReserveThreshold: null,
            BuyItNowPrice: buyItNow,
            ScheduledCloseAt: close,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromMinutes(5),
            OpenedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Listing>(listingId, opened);
        session.PendingChanges.Streams().Single().Events.Single().AddTag(new ListingStreamId(listingId));
        await session.SaveChangesAsync();
    }
}
