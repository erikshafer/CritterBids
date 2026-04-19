using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using JasperFx.Events.Tags;
using Marten;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// End-to-end dispatch smoke test for the DCB PlaceBid handler. Verifies that
/// <see cref="PlaceBidHandler"/> is discovered by Wolverine, the
/// <c>EventTagQuery</c> loads <see cref="BidConsistencyState"/>, the boundary commit
/// appends <see cref="BidPlaced"/> to the listing's tagged stream, and the same commit
/// applies companion signals (here: the BuyItNow option removal on the first bid).
///
/// The 15 scenario tests in <see cref="PlaceBidHandlerTests"/> already exercise the bus
/// for rejection paths; this single acceptance-path dispatch covers the remaining half of
/// the M3-S4 exit criterion "handler registered and routable via IMessageBus".
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class PlaceBidDispatchTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public PlaceBidDispatchTests(AuctionsTestFixture fixture)
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
    public async Task PlaceBid_DispatchedViaBus_AppendsBidPlacedToTaggedStream()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, buyItNow: 100m, close: now.AddMinutes(5));

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 30m, CreditCeiling: 500m));

        await using var session = _fixture.GetDocumentSession();
        var tag = new ListingStreamId(listingId);
        var query = new EventTagQuery()
            .Or<BiddingOpened, ListingStreamId>(tag)
            .Or<BidPlaced, ListingStreamId>(tag)
            .Or<BuyItNowOptionRemoved, ListingStreamId>(tag);

        var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
        boundary.Aggregate.ShouldNotBeNull();
        boundary.Aggregate!.CurrentHighBid.ShouldBe(30m);
        boundary.Aggregate.BidCount.ShouldBe(1);
        boundary.Aggregate.BuyItNowAvailable.ShouldBeFalse();
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
