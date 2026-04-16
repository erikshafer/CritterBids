using CritterBids.Contracts.Selling;
using CritterBids.Listings;
using CritterBids.Listings.Tests.Fixtures;

namespace CritterBids.Listings.Tests;

/// <summary>
/// Integration tests for the Listings BC catalog read path.
/// Covers scenarios 1.3 (catalog browse) and 1.4 (listing detail).
/// Handler is invoked directly — no Wolverine bus dispatch — and SaveChangesAsync()
/// is called explicitly in test setup (AutoApplyTransactions() only fires through the pipeline).
/// </summary>
[Collection(ListingsTestCollection.Name)]
public class CatalogListingViewTests : IAsyncLifetime
{
    private readonly ListingsTestFixture _fixture;

    public CatalogListingViewTests(ListingsTestFixture fixture)
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

    private static ListingPublished BuildMessage(Guid listingId, Guid sellerId) =>
        new(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Mint Condition Foil Black Lotus",
            Format: "Timed",
            StartingBid: 50_000m,
            ReservePrice: 75_000m,
            BuyItNow: 150_000m,
            Duration: TimeSpan.FromDays(7),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            FeePercentage: 0.10m,
            PublishedAt: DateTimeOffset.UtcNow);

    private async Task SeedCatalogEntry(Guid listingId, Guid sellerId)
    {
        var message = BuildMessage(listingId, sellerId);

        // Invoke handler directly — no Wolverine pipeline, so SaveChangesAsync() is explicit here.
        // AutoApplyTransactions() only fires when Wolverine dispatches the handler.
        await using var session = _fixture.GetDocumentSession();
        ListingPublishedHandler.Handle(message, session);
        await session.SaveChangesAsync();
    }

    // ── 1.3: Catalog browse — listings appear after publish ──────────────────

    [Fact]
    public async Task GetCatalog_AfterListingPublished_ReturnsCatalogEntry()
    {
        // Arrange
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        await SeedCatalogEntry(listingId, sellerId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/listings");
            s.StatusCodeShouldBe(200);
        });
        var response = await result.ReadAsJsonAsync<List<CatalogListingView>>();

        // Assert
        response.ShouldNotBeNull();
        response.Count.ShouldBe(1);
        response[0].Id.ShouldBe(listingId);
        response[0].Title.ShouldBe("Mint Condition Foil Black Lotus");
        response[0].Format.ShouldBe("Timed");
        response[0].StartingBid.ShouldBe(50_000m);
    }

    // ── 1.3: Catalog browse — no listings yet ────────────────────────────────

    [Fact]
    public async Task GetCatalog_BeforePublish_ReturnsEmptyList()
    {
        // Act — no data seeded; endpoint must return [] not 404
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url("/api/listings");
            s.StatusCodeShouldBe(200);
        });
        var response = await result.ReadAsJsonAsync<List<CatalogListingView>>();

        // Assert
        response.ShouldNotBeNull();
        response.ShouldBeEmpty();
    }

    // ── 1.4: Listing detail — published listing ──────────────────────────────

    [Fact]
    public async Task GetListingDetail_PublishedListing_ReturnsDetail()
    {
        // Arrange
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        await SeedCatalogEntry(listingId, sellerId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/listings/{listingId}");
            s.StatusCodeShouldBe(200);
        });

        var view = await result.ReadAsJsonAsync<CatalogListingView>();

        // Assert
        view.ShouldNotBeNull();
        view.Id.ShouldBe(listingId);
        view.SellerId.ShouldBe(sellerId);
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");
        view.Format.ShouldBe("Timed");
        view.StartingBid.ShouldBe(50_000m);
    }

    // ── 1.4: Listing detail — unknown ID ─────────────────────────────────────

    [Fact]
    public async Task GetListingDetail_UnknownId_Returns404()
    {
        // Act & Assert — no data seeded; endpoint must return 404 for unknown ID
        await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/listings/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }
}
