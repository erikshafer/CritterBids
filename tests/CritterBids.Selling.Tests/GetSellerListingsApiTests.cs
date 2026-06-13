using CritterBids.Contracts;
using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;

namespace CritterBids.Selling.Tests;

/// <summary>
/// HTTP-level integration tests for <c>GET /api/selling/listings?sellerId={sellerId}</c> (M9-S2).
/// Queries the <see cref="SellerListingSummary"/> inline projection.
/// </summary>
[Collection(SellingTestCollection.Name)]
public class GetSellerListingsApiTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public GetSellerListingsApiTests(SellingTestFixture fixture)
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

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid sellerId, Guid listingId)> SeedDraftListingAsync(
        Guid? sellerId = null, string title = "Hand-Forged Damascus Steel Knife")
    {
        sellerId ??= Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();

        await using var session = _fixture.GetDocumentSession();
        session.Store(new RegisteredSeller { Id = sellerId.Value });
        session.Events.StartStream<SellerListing>(listingId, new DraftListingCreated(
            ListingId: listingId,
            SellerId: sellerId.Value,
            Title: title,
            Format: ListingFormat.Flash,
            StartingBid: 50m,
            ReservePrice: 100m,
            BuyItNowPrice: 200m,
            Duration: null,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            CreatedAt: DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        return (sellerId.Value, listingId);
    }

    // ── happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSellerListings_WithListings_ReturnsSellersListings()
    {
        var (sellerId, listingId) = await SeedDraftListingAsync();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/selling/listings?sellerId={sellerId}");
            s.StatusCodeShouldBe(200);
        });

        var listings = result.ReadAsJson<SellerListingSummary[]>();
        listings.ShouldNotBeNull();
        listings.Length.ShouldBe(1);
        listings[0].Id.ShouldBe(listingId);
        listings[0].SellerId.ShouldBe(sellerId);
        listings[0].Title.ShouldBe("Hand-Forged Damascus Steel Knife");
        listings[0].Format.ShouldBe(ListingFormat.Flash);
        listings[0].Status.ShouldBe(ListingStatus.Draft);
        listings[0].StartingBid.ShouldBe(50m);
    }

    [Fact]
    public async Task GetSellerListings_MultipleListings_ReturnsAll()
    {
        var sellerId = Guid.CreateVersion7();
        await SeedDraftListingAsync(sellerId, "Listing One");
        await SeedDraftListingAsync(sellerId, "Listing Two");

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/selling/listings?sellerId={sellerId}");
            s.StatusCodeShouldBe(200);
        });

        var listings = result.ReadAsJson<SellerListingSummary[]>();
        listings.ShouldNotBeNull();
        listings.Length.ShouldBe(2);
    }

    // ── empty result ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSellerListings_UnknownSeller_ReturnsEmptyList()
    {
        var unknownSellerId = Guid.CreateVersion7();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/selling/listings?sellerId={unknownSellerId}");
            s.StatusCodeShouldBe(200);
        });

        var listings = result.ReadAsJson<SellerListingSummary[]>();
        listings.ShouldNotBeNull();
        listings.Length.ShouldBe(0);
    }

    // ── seller isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSellerListings_DifferentSellers_OnlyReturnOwnListings()
    {
        var (sellerA, _) = await SeedDraftListingAsync(title: "Seller A Listing");
        var (sellerB, _) = await SeedDraftListingAsync(title: "Seller B Listing");

        var resultA = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/selling/listings?sellerId={sellerA}");
            s.StatusCodeShouldBe(200);
        });

        var listingsA = resultA.ReadAsJson<SellerListingSummary[]>();
        listingsA.ShouldNotBeNull();
        listingsA.Length.ShouldBe(1);
        listingsA[0].Title.ShouldBe("Seller A Listing");

        var resultB = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/selling/listings?sellerId={sellerB}");
            s.StatusCodeShouldBe(200);
        });

        var listingsB = resultB.ReadAsJson<SellerListingSummary[]>();
        listingsB.ShouldNotBeNull();
        listingsB.Length.ShouldBe(1);
        listingsB[0].Title.ShouldBe("Seller B Listing");
    }

    // ── projection reflects status changes ────────────────────────────────────

    [Fact]
    public async Task GetSellerListings_AfterSubmit_StatusReflectsPublished()
    {
        var (sellerId, listingId) = await SeedDraftListingAsync();

        // Submit the listing (transitions Draft → Published)
        await _fixture.ExecuteAndWaitAsync(new SubmitListing(listingId, sellerId));

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/selling/listings?sellerId={sellerId}");
            s.StatusCodeShouldBe(200);
        });

        var listings = result.ReadAsJson<SellerListingSummary[]>();
        listings.ShouldNotBeNull();
        listings.Length.ShouldBe(1);
        listings[0].Status.ShouldBe(ListingStatus.Published);
        listings[0].PublishedAt.ShouldNotBeNull();
    }
}
