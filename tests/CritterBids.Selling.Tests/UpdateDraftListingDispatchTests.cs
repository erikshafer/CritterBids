using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;
using Marten;

namespace CritterBids.Selling.Tests;

/// <summary>
/// Verifies that <see cref="UpdateDraftListing"/> dispatches correctly through the full Wolverine
/// pipeline — specifically that the <c>[WriteAggregate]</c> attribute resolves the
/// <see cref="SellerListing"/> stream ID from the command even though the command property is
/// named <c>ListingId</c>, not <c>SellerListingId</c>.
/// Sibling to <see cref="SubmitListingDispatchTests"/>; addresses the final implicit-convention
/// <c>[WriteAggregate]</c> usage flagged in the M2.5-S1 retrospective.
/// </summary>
[Collection(SellingTestCollection.Name)]
public class UpdateDraftListingDispatchTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public UpdateDraftListingDispatchTests(SellingTestFixture fixture)
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
    public async Task UpdateDraftListing_ViaWolverineDispatch_LoadsAggregateAndAppliesUpdate()
    {
        // Arrange — seed a Draft SellerListing stream with known initial values.
        var sellerId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();

        await using (var seedSession = _fixture.GetDocumentSession())
        {
            seedSession.Store(new RegisteredSeller { Id = sellerId });

            seedSession.Events.StartStream<SellerListing>(listingId, new DraftListingCreated(
                ListingId: listingId,
                SellerId: sellerId,
                Title: "Original Title",
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
        // stream ID resolution, which the direct-call tests do not cover.
        await _fixture.ExecuteAndWaitAsync(new UpdateDraftListing(
            ListingId: listingId,
            Title: "Revised Title",
            ReservePrice: 150m,
            BuyItNowPrice: 300m));

        // Assert — the aggregate reflects the applied update after re-aggregation.
        await using var querySession = _fixture.GetDocumentSession();
        var listing = await querySession.Events.AggregateStreamAsync<SellerListing>(listingId);

        listing.ShouldNotBeNull();
        listing.Id.ShouldBe(listingId);
        listing.SellerId.ShouldBe(sellerId);
        listing.Status.ShouldBe(ListingStatus.Draft);
        listing.Title.ShouldBe("Revised Title");
        listing.ReservePrice.ShouldBe(150m);
        listing.BuyItNowPrice.ShouldBe(300m);
    }
}
