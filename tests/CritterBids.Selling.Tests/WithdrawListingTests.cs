using CritterBids.Selling;
using ContractListingWithdrawn = CritterBids.Contracts.Selling.ListingWithdrawn;

namespace CritterBids.Selling.Tests;

/// <summary>
/// Aggregate unit tests for the <see cref="SellerListing"/> withdraw lifecycle.
/// Tests call <see cref="WithdrawListingHandler.Handle"/> directly — no Testcontainers, no host.
/// Mirrors the <see cref="SubmitListingTests"/> shape with three [Fact] methods covering
/// happy-path, reject-not-published, and reject-already-withdrawn.
/// </summary>
public class WithdrawListingTests
{
    private static SellerListing BuildPublishedListing(Guid? listingId = null, Guid? sellerId = null)
    {
        var id = listingId ?? Guid.CreateVersion7();
        var seller = sellerId ?? Guid.CreateVersion7();
        var listing = new SellerListing();
        listing.Apply(new DraftListingCreated(
            ListingId: id,
            SellerId: seller,
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
        listing.Apply(new ListingSubmitted(id, seller, DateTimeOffset.UtcNow));
        listing.Apply(new ListingApproved(id, DateTimeOffset.UtcNow));
        listing.Apply(new ListingPublished(id, DateTimeOffset.UtcNow));
        return listing;
    }

    [Fact]
    public void WithdrawListing_Published_ProducesListingWithdrawn()
    {
        var listing = BuildPublishedListing();
        var withdrawnBy = Guid.CreateVersion7();
        var before = DateTimeOffset.UtcNow;

        var cmd = new WithdrawListing(listing.Id, withdrawnBy);
        var (events, outgoing) = WithdrawListingHandler.Handle(cmd, listing);

        events.Count.ShouldBe(1);
        var domainEvent = events[0].ShouldBeOfType<ListingWithdrawn>();
        domainEvent.ListingId.ShouldBe(listing.Id);
        domainEvent.WithdrawnAt.ShouldBeGreaterThanOrEqualTo(before);

        outgoing.Count().ShouldBe(1);
        var contract = outgoing.Single().ShouldBeOfType<ContractListingWithdrawn>();
        contract.ListingId.ShouldBe(listing.Id);
        contract.WithdrawnBy.ShouldBe(withdrawnBy);
        contract.Reason.ShouldBeNull();
        contract.WithdrawnAt.ShouldBe(domainEvent.WithdrawnAt);
    }

    [Fact]
    public void WithdrawListing_NotPublished_Rejected()
    {
        // Build a Draft listing (no submit/approve/publish events applied).
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var listing = new SellerListing();
        listing.Apply(new DraftListingCreated(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Hand-Forged Damascus Steel Knife",
            Format: ListingFormat.Flash,
            StartingBid: 50m,
            ReservePrice: null,
            BuyItNowPrice: null,
            Duration: null,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            CreatedAt: DateTimeOffset.UtcNow));

        listing.Status.ShouldBe(ListingStatus.Draft);

        var cmd = new WithdrawListing(listingId, Guid.CreateVersion7());

        var ex = Should.Throw<InvalidListingStateException>(() =>
            WithdrawListingHandler.Handle(cmd, listing));

        ex.Message.ShouldContain("Draft");
        ex.Message.ShouldContain("Published");
    }

    [Fact]
    public void WithdrawListing_AlreadyWithdrawn_Rejected()
    {
        var listing = BuildPublishedListing();
        listing.Apply(new ListingWithdrawn(listing.Id, DateTimeOffset.UtcNow));

        listing.Status.ShouldBe(ListingStatus.Withdrawn);

        var cmd = new WithdrawListing(listing.Id, Guid.CreateVersion7());

        var ex = Should.Throw<InvalidListingStateException>(() =>
            WithdrawListingHandler.Handle(cmd, listing));

        ex.Message.ShouldContain("Withdrawn");
    }
}
