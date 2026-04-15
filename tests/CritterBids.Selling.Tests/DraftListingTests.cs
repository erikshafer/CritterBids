using CritterBids.Selling;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritterBids.Selling.Tests;

/// <summary>
/// Aggregate unit tests for the <see cref="SellerListing"/> draft lifecycle.
/// Tests call handler static methods directly — no Testcontainers, no host required.
/// <see cref="WolverineContinue"/> and <see cref="IStartStream"/> are from WolverineFx.Http
/// and WolverineFx.Marten respectively, both already referenced transitively.
/// Mapping: scenarios 1.1–1.5 from <c>docs/workshops/004-scenarios.md</c> §1.
/// </summary>
public class DraftListingTests
{
    // ── stubs ──────────────────────────────────────────────────────────────────

    private sealed class AlwaysRegisteredService : ISellerRegistrationService
    {
        public Task<bool> IsRegisteredAsync(Guid sellerId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class NeverRegisteredService : ISellerRegistrationService
    {
        public Task<bool> IsRegisteredAsync(Guid sellerId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static CreateDraftListing BuildCmd(Guid? sellerId = null) => new(
        SellerId: sellerId ?? Guid.CreateVersion7(),
        Title: "Hand-Forged Damascus Steel Knife",
        Format: ListingFormat.Flash,
        StartingBid: 50m,
        ReservePrice: 100m,
        BuyItNowPrice: 200m,
        Duration: null,
        ExtendedBiddingEnabled: true,
        ExtendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
        ExtendedBiddingExtension: TimeSpan.FromSeconds(15));

    // ── 1.1 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraft_WithRegisteredSeller_ProducesDraftListingCreated()
    {
        var sellerId = Guid.CreateVersion7();
        var cmd = BuildCmd(sellerId);

        // ValidateAsync must clear for a registered seller
        var problems = await CreateDraftListingHandler.ValidateAsync(
            cmd, new AlwaysRegisteredService(), CancellationToken.None);
        problems.ShouldBe(WolverineContinue.NoProblems);

        // Handle produces a new SellerListing stream with DraftListingCreated
        var (response, startStream) = CreateDraftListingHandler.Handle(cmd);

        startStream.AggregateType.ShouldBe(typeof(SellerListing));
        startStream.Events.Count.ShouldBe(1);

        var evt = startStream.Events[0].ShouldBeOfType<DraftListingCreated>();
        evt.SellerId.ShouldBe(sellerId);
        evt.Title.ShouldBe("Hand-Forged Damascus Steel Knife");
        evt.Format.ShouldBe(ListingFormat.Flash);
        evt.StartingBid.ShouldBe(50m);
        evt.ReservePrice.ShouldBe(100m);
        evt.BuyItNowPrice.ShouldBe(200m);

        // Apply the event to verify aggregate reaches Draft state
        var listing = new SellerListing();
        listing.Apply(evt);
        listing.Status.ShouldBe(ListingStatus.Draft);
        listing.SellerId.ShouldBe(sellerId);

        // 201 Location header points to the new listing
        response.Url.ShouldStartWith("/api/listings/");
    }

    // ── 1.2 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraft_WithUnregisteredSeller_ThrowsSellerNotRegisteredException()
    {
        // When the seller is not in the RegisteredSellers projection, ValidateAsync returns
        // ProblemDetails { Status = 403 } — Wolverine HTTP short-circuits with 403 Forbidden.
        // "Throws" in the scenario refers to the pipeline rejection, mirrored here as a 403.
        var cmd = BuildCmd();

        var problems = await CreateDraftListingHandler.ValidateAsync(
            cmd, new NeverRegisteredService(), CancellationToken.None);

        problems.Status.ShouldBe(403);
        problems.Detail.ShouldBe("Seller is not registered");
    }

    // ── 1.3 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDraft_ValidChange_ProducesDraftListingUpdated()
    {
        var listingId = Guid.CreateVersion7();
        var listing = new SellerListing
        {
            Id = listingId,
            Status = ListingStatus.Draft,
            Title = "Original Title",
            ReservePrice = 100m,
            BuyItNowPrice = 200m
        };

        var cmd = new UpdateDraftListing(listingId, Title: "Improved Description");

        var events = UpdateDraftListingHandler.Handle(cmd, listing);

        var updatedEvt = events.Single().ShouldBeOfType<DraftListingUpdated>();
        updatedEvt.ListingId.ShouldBe(listingId);
        updatedEvt.Title.ShouldBe("Improved Description");
    }

    // ── 1.4 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDraft_BinBelowReserve_ThrowsValidationException()
    {
        var listingId = Guid.CreateVersion7();
        var listing = new SellerListing
        {
            Id = listingId,
            Status = ListingStatus.Draft,
            ReservePrice = 100m,
            BuyItNowPrice = 200m
        };

        // $75 < $100 reserve — violates BIN >= Reserve invariant
        var cmd = new UpdateDraftListing(listingId, BuyItNowPrice: 75m);

        var ex = Should.Throw<ListingValidationException>(() =>
            UpdateDraftListingHandler.Handle(cmd, listing));

        ex.Message.ShouldBe("BuyItNowPrice must be >= ReservePrice");
    }

    // ── 1.5 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateDraft_WhenPublished_ThrowsInvalidStateException()
    {
        var listingId = Guid.CreateVersion7();
        var listing = new SellerListing
        {
            Id = listingId,
            Status = ListingStatus.Published
        };

        var cmd = new UpdateDraftListing(listingId, Title: "New Title");

        var ex = Should.Throw<InvalidListingStateException>(() =>
            UpdateDraftListingHandler.Handle(cmd, listing));

        ex.Message.ShouldBe("Cannot update draft on non-draft listing");
    }
}
