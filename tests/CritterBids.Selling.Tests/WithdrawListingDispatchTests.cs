using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;
using Marten;
using Wolverine.Tracking;
using ContractListingWithdrawn = CritterBids.Contracts.Selling.ListingWithdrawn;

namespace CritterBids.Selling.Tests;

/// <summary>
/// Verifies that <see cref="WithdrawListing"/> dispatches correctly through the full Wolverine
/// pipeline — the <c>[WriteAggregate]</c> attribute resolves the <see cref="SellerListing"/>
/// stream ID from the command's <c>ListingId</c> property, the handler runs against the loaded
/// aggregate, and the outbound <see cref="ContractListingWithdrawn"/> event is emitted.
/// Mirrors <see cref="SubmitListingDispatchTests"/>; the SellingTestFixture's
/// <c>DisableAllExternalWolverineTransports()</c> strips the RabbitMQ routes, so the contract
/// event lands on <c>tracked.NoRoutes</c> rather than <c>tracked.Sent</c> per the M5-S6 retro's
/// Key Learning #1 on fixture-stance routing assertions.
/// </summary>
[Collection(SellingTestCollection.Name)]
public class WithdrawListingDispatchTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public WithdrawListingDispatchTests(SellingTestFixture fixture)
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
    public async Task WithdrawListing_ViaWolverineDispatch_TransitionsAggregateAndEmitsContractEvent()
    {
        var sellerId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();
        var withdrawnBy = Guid.CreateVersion7();

        await using (var seedSession = _fixture.GetDocumentSession())
        {
            seedSession.Store(new RegisteredSeller { Id = sellerId });

            seedSession.Events.StartStream<SellerListing>(
                listingId,
                new DraftListingCreated(
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
                    CreatedAt: DateTimeOffset.UtcNow),
                new ListingSubmitted(listingId, sellerId, DateTimeOffset.UtcNow),
                new ListingApproved(listingId, DateTimeOffset.UtcNow),
                new ListingPublished(listingId, DateTimeOffset.UtcNow));

            await seedSession.SaveChangesAsync();
        }

        var tracked = await _fixture.ExecuteAndWaitAsync(new WithdrawListing(listingId, withdrawnBy));

        await using var querySession = _fixture.GetDocumentSession();
        var listing = await querySession.Events.AggregateStreamAsync<SellerListing>(listingId);

        listing.ShouldNotBeNull();
        listing.Id.ShouldBe(listingId);
        listing.Status.ShouldBe(ListingStatus.Withdrawn);

        // External transports are disabled in the fixture, so the routed contract event lands
        // on NoRoutes rather than Sent. This asserts the handler's emission contract — production
        // routing is verified by code review of src/CritterBids.Api/Program.cs.
        var noRoutesContract = tracked.NoRoutes
            .MessagesOf<ContractListingWithdrawn>()
            .ShouldHaveSingleItem();
        noRoutesContract.ListingId.ShouldBe(listingId);
        noRoutesContract.WithdrawnBy.ShouldBe(withdrawnBy);
        noRoutesContract.Reason.ShouldBeNull();
    }
}
