using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Listings;
using CritterBids.Listings.Tests.Fixtures;
using Marten;

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

    // ─────────────────────────────────────────────────────────────────────────
    // M3-S6 — Auction-status projection extension (milestone doc §7)
    //
    // Each test seeds a CatalogListingView in its post-publish baseline, then
    // invokes AuctionStatusHandler.Handle directly with an IDocumentSession and
    // explicitly SaveChangesAsync — same shape as the M2 SeedCatalogEntry helper.
    // Direct invocation is required here because opts.ListenToRabbitQueue in
    // Program.cs creates a sticky binding for these message types to the
    // RabbitMQ endpoint, and the test fixture calls DisableAllExternalWolverine-
    // Transports — bus dispatch (Host.InvokeMessageAndWaitAsync) raises
    // NoHandlerForEndpointException ("sticky handler" pattern; see
    // C:\Users\Erik\.claude\projects\C--Code-CritterBids\memory\
    // project_wolverine_sticky_handler.md). Direct invocation is the M2-S7
    // precedent and exercises the same handler logic — only the dispatch
    // mechanism differs.
    // ─────────────────────────────────────────────────────────────────────────

    private async Task InvokeAuctionHandlerAsync<TMessage>(
        Func<TMessage, IDocumentSession, CancellationToken, Task> handler,
        TMessage message)
    {
        await using var session = _fixture.GetDocumentSession();
        await handler(message, session, CancellationToken.None);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task BiddingOpened_SetsCatalogStatusOpen()
    {
        // Arrange — view exists in published-but-not-opened state
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);

        var scheduledCloseAt = DateTimeOffset.UtcNow.AddHours(24);
        var opened = new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: scheduledCloseAt,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(7),
            OpenedAt: DateTimeOffset.UtcNow);

        // Act
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, opened);

        // Assert — M2 fields preserved, auction fields populated
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Id.ShouldBe(listingId);
        view.SellerId.ShouldBe(sellerId);                              // M2 field preserved
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");        // M2 field preserved
        view.Status.ShouldBe("Open");
        view.ScheduledCloseAt.ShouldBe(scheduledCloseAt);
    }
}
