using CritterBids.Selling;

namespace CritterBids.Selling.Tests;

/// <summary>
/// Aggregate unit tests for the <see cref="SellerListing"/> submit lifecycle.
/// Tests call <see cref="SubmitListingHandler.Handle"/> directly — no Testcontainers, no host.
/// Mapping: scenarios 2.1–2.4 from <c>docs/workshops/004-scenarios.md</c> §2.
/// </summary>
public class SubmitListingTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="SellerListing"/> in Draft state with all valid field values.
    /// Used as the baseline for happy-path and resubmission tests.
    /// </summary>
    private static SellerListing BuildValidDraftListing(Guid? listingId = null, Guid? sellerId = null)
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
        return listing;
    }

    // ── 2.1 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubmitListing_ValidDraft_ProducesThreeEventsAtomically()
    {
        var listing = BuildValidDraftListing();
        var cmd = new SubmitListing(listing.Id, listing.SellerId);

        var (events, outgoing) = SubmitListingHandler.Handle(cmd, listing);

        // Three-event chain: Submitted → Approved → Published
        events.Count.ShouldBe(3);

        var submitted = events[0].ShouldBeOfType<ListingSubmitted>();
        submitted.ListingId.ShouldBe(listing.Id);
        submitted.SellerId.ShouldBe(listing.SellerId);

        var approved = events[1].ShouldBeOfType<ListingApproved>();
        approved.ListingId.ShouldBe(listing.Id);

        var published = events[2].ShouldBeOfType<ListingPublished>();
        published.ListingId.ShouldBe(listing.Id);

        // Integration contract published to outbox
        outgoing.Count().ShouldBe(1);
        var contract = outgoing.Single().ShouldBeOfType<CritterBids.Contracts.Selling.ListingPublished>();
        contract.ListingId.ShouldBe(listing.Id);
        contract.SellerId.ShouldBe(listing.SellerId);
        contract.Title.ShouldBe("Hand-Forged Damascus Steel Knife");
        contract.Format.ShouldBe("Flash");
        contract.StartingBid.ShouldBe(50m);
        contract.ReservePrice.ShouldBe(100m);
        contract.BuyItNow.ShouldBe(200m);
        contract.FeePercentage.ShouldBe(0.10m);
        contract.ExtendedBiddingEnabled.ShouldBeTrue();
    }

    // ── 2.2 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubmitListing_InvalidDraft_ProducesSubmittedAndRejected()
    {
        // Build a listing with an empty title — will fail Rule 5.2 (Title required)
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var listing = new SellerListing();
        listing.Apply(new DraftListingCreated(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Valid Title",
            Format: ListingFormat.Flash,
            StartingBid: 50m,
            ReservePrice: null,
            BuyItNowPrice: null,
            Duration: null,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            CreatedAt: DateTimeOffset.UtcNow));

        // Patch to invalid state: empty title via UpdateDraftListing
        listing.Apply(new DraftListingUpdated(listingId, Title: "", ReservePrice: null, BuyItNowPrice: null, UpdatedAt: DateTimeOffset.UtcNow));

        var cmd = new SubmitListing(listingId, sellerId);
        var (events, outgoing) = SubmitListingHandler.Handle(cmd, listing);

        // Two events: Submitted + Rejected
        events.Count.ShouldBe(2);
        events[0].ShouldBeOfType<ListingSubmitted>();

        var rejected = events[1].ShouldBeOfType<ListingRejected>();
        rejected.ListingId.ShouldBe(listingId);
        rejected.RejectionReason.ShouldNotBeNullOrWhiteSpace();

        // No integration contract published on rejection
        outgoing.ShouldBeEmpty();
    }

    // ── 2.3 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubmitListing_FromRejectedStateAfterFix_ProducesThreeEvents()
    {
        // Bring the listing to Rejected state by replaying submit events
        var listing = BuildValidDraftListing();
        listing.Apply(new ListingSubmitted(listing.Id, listing.SellerId, DateTimeOffset.UtcNow));
        listing.Apply(new ListingRejected(listing.Id, "Title is required", DateTimeOffset.UtcNow));

        listing.Status.ShouldBe(ListingStatus.Rejected);

        // Resubmit — listing now has valid fields so it should produce the 3-event chain
        var cmd = new SubmitListing(listing.Id, listing.SellerId);
        var (events, outgoing) = SubmitListingHandler.Handle(cmd, listing);

        events.Count.ShouldBe(3);
        events[0].ShouldBeOfType<ListingSubmitted>();
        events[1].ShouldBeOfType<ListingApproved>();
        events[2].ShouldBeOfType<ListingPublished>();

        outgoing.Count().ShouldBe(1);
        outgoing.Single().ShouldBeOfType<CritterBids.Contracts.Selling.ListingPublished>();
    }

    // ── 2.4 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SubmitListing_WhenAlreadyPublished_ThrowsInvalidStateException()
    {
        var listing = BuildValidDraftListing();

        // Bring to Published state via the normal 3-event chain
        listing.Apply(new ListingSubmitted(listing.Id, listing.SellerId, DateTimeOffset.UtcNow));
        listing.Apply(new ListingApproved(listing.Id, DateTimeOffset.UtcNow));
        listing.Apply(new ListingPublished(listing.Id, DateTimeOffset.UtcNow));

        listing.Status.ShouldBe(ListingStatus.Published);

        var cmd = new SubmitListing(listing.Id, listing.SellerId);

        var ex = Should.Throw<InvalidListingStateException>(() =>
            SubmitListingHandler.Handle(cmd, listing));

        ex.Message.ShouldContain("Published");
    }
}
