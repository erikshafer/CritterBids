using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Regression tests for the Flash/Timed open-cascade fix (see
/// docs/notes/integrated-host-flash-bidding-findings.md, Bug #1). The shipped open-handlers wrote
/// <see cref="BiddingOpened"/> via an UNTAGGED <c>StartStream&lt;Listing&gt;</c> guarded by
/// <c>FetchStreamStateAsync</c>. Both were wrong in the integrated shared store: the bid DCB reads
/// <see cref="BiddingOpened"/> by TAG (so an untagged event is invisible — every bid would reject
/// <c>ListingNotOpen</c>), and the stream always pre-exists because Selling's <c>SellerListing</c>
/// owns the <c>listingId</c> stream (so the guard skipped forever). These tests exercise the lived
/// open → bid path, which the per-handler tests did not.
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class OpenListingForBiddingTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public OpenListingForBiddingTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ListingPublished TimedListing(Guid listingId, Guid sellerId) => new(
        ListingId: listingId,
        SellerId: sellerId,
        Title: "Open-then-bid regression",
        Format: "Timed",
        StartingBid: 100m,
        ReservePrice: null,
        BuyItNow: null,
        Duration: TimeSpan.FromDays(7), // close is far out, so the bid is never "ListingClosed"
        ExtendedBiddingEnabled: false,
        ExtendedBiddingTriggerWindow: null,
        ExtendedBiddingExtension: null,
        FeePercentage: 0.10m,
        PublishedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task OpenedListing_TagsBiddingOpened_SoTheDcbAcceptsABid()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();

        await using (var session = _fixture.GetDocumentSession())
        {
            await ListingPublishedHandler.Handle(TimedListing(listingId, sellerId), session);
            await session.SaveChangesAsync();
        }

        // The bid DCB (PlaceBidHandler) loads BidConsistencyState BY TAG. If the open path appended
        // BiddingOpened untagged (the bug), the boundary is empty and this clears-everything bid
        // rejects ListingNotOpen instead of being accepted.
        await using var bidSession = _fixture.GetDocumentSession();
        var outcome = await PlaceBidHandler.Execute(
            new PlaceBid(listingId, Guid.CreateVersion7(), Guid.CreateVersion7(), 150m, CreditCeiling: 1000m),
            bidSession,
            TimeProvider.System);
        await bidSession.SaveChangesAsync();

        var accepted = outcome.ShouldBeOfType<BidOutcome.Accepted>();
        accepted.Amount.ShouldBe(150m);
        accepted.BidCount.ShouldBe(1);
    }

    [Fact]
    public async Task ListingPublished_ToAStreamThatAlreadyExists_StillOpensAndAcceptsABid()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();

        // Simulate the integrated shared store, where the listingId stream already exists before
        // Auctions opens it (in production, Selling's SellerListing aggregate's draft/submit/publish
        // events). The seeded event is untagged, so it is invisible to the bid DCB and does not make
        // the listing "open" — it only makes the STREAM exist. The old guard skipped on stream
        // existence; the fix appends to the existing stream instead.
        await using (var seed = _fixture.GetDocumentSession())
        {
            seed.Events.StartStream<Listing>(
                listingId,
                new ReserveMet(listingId, 1m, DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }

        await using (var session = _fixture.GetDocumentSession())
        {
            await ListingPublishedHandler.Handle(TimedListing(listingId, sellerId), session);
            await session.SaveChangesAsync();
        }

        await using var verify = _fixture.GetDocumentSession();
        var events = await verify.Events.FetchStreamAsync(listingId);
        events.Select(e => e.Data).OfType<BiddingOpened>().ShouldHaveSingleItem();

        await using var bidSession = _fixture.GetDocumentSession();
        var outcome = await PlaceBidHandler.Execute(
            new PlaceBid(listingId, Guid.CreateVersion7(), Guid.CreateVersion7(), 150m, CreditCeiling: 1000m),
            bidSession,
            TimeProvider.System);
        await bidSession.SaveChangesAsync();

        outcome.ShouldBeOfType<BidOutcome.Accepted>();
    }
}
