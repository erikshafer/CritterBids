using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;
using Marten;

namespace CritterBids.Selling.Tests;

/// <summary>
/// Verifies that <see cref="SubmitListing"/> dispatches correctly through the full Wolverine
/// pipeline — specifically that the <c>[WriteAggregate]</c> attribute resolves the
/// <see cref="SellerListing"/> stream ID from the command even though the command property is
/// named <c>ListingId</c>, not <c>SellerListingId</c>.
/// Addresses the fragility noted in the M2 retrospective under "What M3 Should Know."
/// </summary>
/// <remarks>
/// The existing <see cref="SubmitListingTests"/> class calls <c>SubmitListingHandler.Handle</c>
/// directly with a pre-built aggregate and bypasses Wolverine entirely. This class is the only
/// coverage of the dispatch-time stream ID resolution.
/// </remarks>
[Collection(SellingTestCollection.Name)]
public class SubmitListingDispatchTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public SubmitListingDispatchTests(SellingTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _fixture.CleanAllMartenDataAsync();
        }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SubmitListing_ViaWolverineDispatch_LoadsAggregateAndProducesPublishedListing()
    {
        // Arrange — seed a RegisteredSeller row and a Draft SellerListing stream.
        var sellerId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();

        await using (var seedSession = _fixture.GetDocumentSession())
        {
            seedSession.Store(new RegisteredSeller { Id = sellerId });

            seedSession.Events.StartStream<SellerListing>(listingId, new DraftListingCreated(
                ListingId: listingId,
                SellerId: sellerId,
                Title: "Hand-Forged Damascus Steel Knife",
                Format: ListingFormat.Flash,
                StartingBid: 50m,
                ReservePrice: 100m,
                BuyItNowPrice: 200m,
                Duration: null,
                ExtendedBiddingEnabled: true,
                ExtendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
                ExtendedBiddingExtension: TimeSpan.FromSeconds(15),
                CreatedAt: DateTimeOffset.UtcNow));

            await seedSession.SaveChangesAsync();
        }

        // Act — dispatch through the full Wolverine pipeline. This exercises [WriteAggregate]
        // stream ID resolution, which the direct-call tests in SubmitListingTests do not cover.
        await _fixture.ExecuteAndWaitAsync(new SubmitListing(listingId, sellerId));

        // Assert — the aggregate advanced to Published, proving the stream was loaded,
        // Handle() ran against the real aggregate, and the resulting events were persisted.
        await using var querySession = _fixture.GetDocumentSession();
        var listing = await querySession.Events.AggregateStreamAsync<SellerListing>(listingId);

        listing.ShouldNotBeNull();
        listing.Id.ShouldBe(listingId);
        listing.SellerId.ShouldBe(sellerId);
        listing.Status.ShouldBe(ListingStatus.Published);
    }
}
