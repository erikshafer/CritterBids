using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Listings;
using CritterBids.Listings.Tests.Fixtures;
using JasperFx;
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

    private static BiddingOpened BuildOpened(Guid listingId, Guid sellerId, DateTimeOffset scheduledCloseAt) =>
        new(
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

    private async Task SeedCatalogEntry(Guid listingId, Guid sellerId)
    {
        var message = BuildMessage(listingId, sellerId);

        // Invoke handler directly — no Wolverine pipeline, so SaveChangesAsync() is explicit here.
        // AutoApplyTransactions() only fires when Wolverine dispatches the handler.
        // M5-S6: handler signature is now async with CancellationToken (load-and-preserve pattern).
        await using var session = _fixture.GetDocumentSession();
        await ListingPublishedHandler.Handle(message, session, CancellationToken.None);
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

    [Fact]
    public async Task BidPlaced_UpdatesCatalogHighBid()
    {
        // Arrange — view is already in Open state with no bids yet
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: DateTimeOffset.UtcNow.AddHours(24),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(7),
            OpenedAt: DateTimeOffset.UtcNow));

        var bidPlaced = new BidPlaced(
            ListingId: listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: bidderId,
            Amount: 60_000m,
            BidCount: 3,                  // authoritative from source — not "+1"
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow);

        // Act
        await InvokeAuctionHandlerAsync<BidPlaced>(AuctionStatusHandler.Handle, bidPlaced);

        // Assert — bid fields populated; prior auction-status fields preserved
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.CurrentHighBid.ShouldBe(60_000m);
        view.CurrentHighBidderId.ShouldBe(bidderId);
        view.BidCount.ShouldBe(3);
        view.Status.ShouldBe("Open");                 // BiddingOpened transition preserved
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");  // M2 field preserved
    }

    [Fact]
    public async Task BiddingClosed_SetsCatalogStatusClosed()
    {
        // Arrange — view is in Open state (post-BiddingOpened)
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: DateTimeOffset.UtcNow.AddHours(24),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(7),
            OpenedAt: DateTimeOffset.UtcNow));

        var closedAt = DateTimeOffset.UtcNow.AddHours(24);
        var biddingClosed = new BiddingClosed(ListingId: listingId, ClosedAt: closedAt);

        // Act
        await InvokeAuctionHandlerAsync<BiddingClosed>(AuctionStatusHandler.Handle, biddingClosed);

        // Assert
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Closed");
        view.ClosedAt.ShouldBe(closedAt);
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");  // M2 field preserved
    }

    [Fact]
    public async Task ListingSold_SetsCatalogStatusSold()
    {
        // Arrange — view is in Closed state (post-BiddingClosed on timer path)
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: DateTimeOffset.UtcNow.AddHours(24),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(7),
            OpenedAt: DateTimeOffset.UtcNow));
        await InvokeAuctionHandlerAsync<BiddingClosed>(AuctionStatusHandler.Handle, new BiddingClosed(
            ListingId: listingId,
            ClosedAt: DateTimeOffset.UtcNow));

        var soldAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var listingSold = new ListingSold(
            ListingId: listingId,
            SellerId: sellerId,
            WinnerId: winnerId,
            HammerPrice: 100_000m,
            BidCount: 5,
            SoldAt: soldAt);

        // Act
        await InvokeAuctionHandlerAsync<ListingSold>(AuctionStatusHandler.Handle, listingSold);

        // Assert
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Sold");
        view.HammerPrice.ShouldBe(100_000m);
        view.WinnerId.ShouldBe(winnerId);
        view.BidCount.ShouldBe(5);
        view.ClosedAt.ShouldBe(soldAt);
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");  // M2 field preserved
    }

    [Theory]
    [InlineData("NoBids", null)]                  // no bids landed before close — HighestBid null
    [InlineData("ReserveNotMet", 60_000.0)]       // bids landed but none reached reserve
    public async Task ListingPassed_SetsCatalogStatusPassed(string reason, double? highestBidNullable)
    {
        // Theory data: doubles unbox cleanly to decimal? — InlineData rejects decimal literals.
        decimal? highestBid = highestBidNullable.HasValue ? (decimal)highestBidNullable.Value : null;

        // Arrange — view in Closed state on the no-sale path
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: DateTimeOffset.UtcNow.AddHours(24),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(7),
            OpenedAt: DateTimeOffset.UtcNow));
        await InvokeAuctionHandlerAsync<BiddingClosed>(AuctionStatusHandler.Handle, new BiddingClosed(
            ListingId: listingId,
            ClosedAt: DateTimeOffset.UtcNow));

        var passedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var bidCount = reason == "NoBids" ? 0 : 2;
        var listingPassed = new ListingPassed(
            ListingId: listingId,
            Reason: reason,
            HighestBid: highestBid,
            BidCount: bidCount,
            PassedAt: passedAt);

        // Act
        await InvokeAuctionHandlerAsync<ListingPassed>(AuctionStatusHandler.Handle, listingPassed);

        // Assert
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Passed");
        view.PassedReason.ShouldBe(reason);
        view.FinalHighestBid.ShouldBe(highestBid);
        view.BidCount.ShouldBe(bidCount);
        view.ClosedAt.ShouldBe(passedAt);
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");  // M2 field preserved
    }

    [Fact]
    public async Task BuyItNowPurchased_SetsCatalogStatusSold()
    {
        // Arrange — view in Open state; BIN purchased before any bid.
        // BIN terminal path skips BiddingClosed entirely (S5b OQ1 Path B + retro §5).
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: DateTimeOffset.UtcNow.AddHours(24),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(7),
            OpenedAt: DateTimeOffset.UtcNow));

        var purchasedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var binPurchased = new BuyItNowPurchased(
            ListingId: listingId,
            BuyerId: buyerId,
            Price: 150_000m,
            PurchasedAt: purchasedAt);

        // Act — note: no intermediate BiddingClosed dispatch.
        await InvokeAuctionHandlerAsync<BuyItNowPurchased>(AuctionStatusHandler.Handle, binPurchased);

        // Assert — Status transitions Open → Sold directly; HammerPrice = BIN price
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Sold");
        view.HammerPrice.ShouldBe(150_000m);
        view.WinnerId.ShouldBe(buyerId);
        view.ClosedAt.ShouldBe(purchasedAt);
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");  // M2 field preserved
    }

    // ─────────────────────────────────────────────────────────────────────────
    // M9-S3 — ExtendedBiddingTriggered handler (M8-S7 carry-forward)
    //
    // Advances CatalogListingView.ScheduledCloseAt when extended bidding triggers.
    // Direct handler invocation per the M3-S6 pattern above.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtendedBiddingTriggered_AdvancesScheduledCloseAt()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);

        var originalCloseAt = DateTimeOffset.UtcNow.AddHours(1);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: originalCloseAt,
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
            ExtendedBiddingExtension: TimeSpan.FromSeconds(15),
            MaxDuration: TimeSpan.FromHours(2),
            OpenedAt: DateTimeOffset.UtcNow));

        var newCloseAt = originalCloseAt.AddSeconds(15);
        var extended = new ExtendedBiddingTriggered(
            ListingId: listingId,
            PreviousCloseAt: originalCloseAt,
            NewCloseAt: newCloseAt,
            TriggeredByBidderId: bidderId,
            TriggeredAt: DateTimeOffset.UtcNow);

        await InvokeAuctionHandlerAsync<ExtendedBiddingTriggered>(AuctionStatusHandler.Handle, extended);

        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.ScheduledCloseAt.ShouldBe(newCloseAt);
        view.Status.ShouldBe("Open");
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");
    }

    [Fact]
    public async Task ExtendedBiddingTriggered_WithdrawnListing_NoOp()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);

        var withdrawnAt = DateTimeOffset.UtcNow;
        await InvokeAuctionHandlerAsync<ListingWithdrawn>(
            SellingListingWithdrawnHandler.Handle,
            new ListingWithdrawn(
                ListingId: listingId,
                WithdrawnBy: sellerId,
                Reason: null,
                WithdrawnAt: withdrawnAt));

        var newCloseAt = DateTimeOffset.UtcNow.AddHours(2);
        var extended = new ExtendedBiddingTriggered(
            ListingId: listingId,
            PreviousCloseAt: DateTimeOffset.UtcNow.AddHours(1),
            NewCloseAt: newCloseAt,
            TriggeredByBidderId: Guid.CreateVersion7(),
            TriggeredAt: DateTimeOffset.UtcNow);

        await InvokeAuctionHandlerAsync<ExtendedBiddingTriggered>(AuctionStatusHandler.Handle, extended);

        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Withdrawn");
        view.ScheduledCloseAt.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // M4-S6 — Session-membership + Withdrawn extension (milestone doc §7)
    //
    // Sibling handlers per ADR-014 Sub-Option A (resolved at M4-S6 session open):
    //   - AuctionsSessionHandler consumes ListingAttachedToSession + SessionStarted
    //   - SellingListingWithdrawnHandler consumes ListingWithdrawn
    // Direct handler invocation (no bus dispatch) per the M3-S6 in-fixture note above.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingAttachedToSession_SetsSessionId()
    {
        // Arrange — view at Status = "Published" baseline (seeded directly; the M2
        // SeedCatalogEntry path is exercised separately in the earlier tests).
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);

        var attached = new ListingAttachedToSession(
            SessionId: sessionId,
            ListingId: listingId,
            AttachedAt: DateTimeOffset.UtcNow);

        // Act
        await InvokeAuctionHandlerAsync<ListingAttachedToSession>(
            AuctionsSessionHandler.Handle, attached);

        // Assert — SessionId populated; all prior fields preserved
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.SessionId.ShouldBe(sessionId);
        view.SessionStartedAt.ShouldBeNull();         // not started yet
        view.Status.ShouldBe("Published");            // attach does not transition status
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");  // M2 field preserved
    }

    [Fact]
    public async Task SessionStarted_SetsSessionStartedAtForMemberListings()
    {
        // Arrange — three views all attached to the same session; one stray view in a
        // different session-or-no-session state. The fan-out should touch only the three.
        var sessionId = Guid.CreateVersion7();
        var memberA = Guid.CreateVersion7();
        var memberB = Guid.CreateVersion7();
        var memberC = Guid.CreateVersion7();
        var unrelated = Guid.CreateVersion7();
        var unrelatedSeller = Guid.CreateVersion7();

        await _fixture.SeedSessionAttachedListingAsync(sessionId, memberA);
        await _fixture.SeedSessionAttachedListingAsync(sessionId, memberB);
        await _fixture.SeedSessionAttachedListingAsync(sessionId, memberC);
        await _fixture.SeedCatalogListingViewAsync(unrelated, unrelatedSeller);

        var startedAt = DateTimeOffset.UtcNow;
        var started = new SessionStarted(
            SessionId: sessionId,
            ListingIds: new[] { memberA, memberB, memberC },
            StartedAt: startedAt);

        // Act
        await InvokeAuctionHandlerAsync<SessionStarted>(
            AuctionsSessionHandler.Handle, started);

        // Assert — every member listing carries SessionStartedAt; the unrelated row is
        // untouched (SessionId still null, SessionStartedAt still null).
        var viewA = await _fixture.LoadCatalogListingViewAsync(memberA);
        var viewB = await _fixture.LoadCatalogListingViewAsync(memberB);
        var viewC = await _fixture.LoadCatalogListingViewAsync(memberC);
        var viewUnrelated = await _fixture.LoadCatalogListingViewAsync(unrelated);

        viewA.ShouldNotBeNull();
        viewB.ShouldNotBeNull();
        viewC.ShouldNotBeNull();
        viewUnrelated.ShouldNotBeNull();

        viewA!.SessionStartedAt.ShouldBe(startedAt);
        viewA.SessionId.ShouldBe(sessionId);
        viewB!.SessionStartedAt.ShouldBe(startedAt);
        viewB.SessionId.ShouldBe(sessionId);
        viewC!.SessionStartedAt.ShouldBe(startedAt);
        viewC.SessionId.ShouldBe(sessionId);

        viewUnrelated!.SessionStartedAt.ShouldBeNull();
        viewUnrelated.SessionId.ShouldBeNull();
    }

    [Theory]
    [InlineData("Published")]    // attached but session never started
    [InlineData("Open")]          // Workshop 002 §5 "attach withdrawn between attach and start" path
    public async Task ListingWithdrawn_SetsCatalogStatusWithdrawn(string startingStatus)
    {
        // Arrange — view at the given legal pre-state. If the test data is "Open",
        // we run BiddingOpened first to land the transition (mirrors the M3-S6
        // BidPlaced_UpdatesCatalogHighBid test's two-step arrangement shape).
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);

        if (startingStatus == "Open")
        {
            await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
                ListingId: listingId,
                SellerId: sellerId,
                StartingBid: 50_000m,
                ReserveThreshold: 75_000m,
                BuyItNowPrice: 150_000m,
                ScheduledCloseAt: DateTimeOffset.UtcNow.AddHours(24),
                ExtendedBiddingEnabled: false,
                ExtendedBiddingTriggerWindow: null,
                ExtendedBiddingExtension: null,
                MaxDuration: TimeSpan.FromDays(7),
                OpenedAt: DateTimeOffset.UtcNow));
        }

        var withdrawnAt = DateTimeOffset.UtcNow.AddSeconds(1);
        var withdrawn = new ListingWithdrawn(
            ListingId: listingId,
            WithdrawnBy: sellerId,
            Reason: null,
            WithdrawnAt: withdrawnAt);

        // Act
        await InvokeAuctionHandlerAsync<ListingWithdrawn>(
            SellingListingWithdrawnHandler.Handle, withdrawn);

        // Assert — Withdrawn terminal landed; ClosedAt stamped from WithdrawnAt
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Withdrawn");
        view.ClosedAt.ShouldBe(withdrawnAt);
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");  // M2 field preserved
    }

    [Fact]
    public async Task SiblingHandlers_CoexistOnSameView_NoOverwrites()
    {
        // Arrange — exercise a realistic Flash arrival order:
        //   ListingPublished (seed) → ListingAttachedToSession → BiddingOpened →
        //   BidPlaced → SessionStarted
        // Each handler owns a disjoint field set; this test pins that none of them
        // clobbers another's writes (additive-field discipline per ADR-014 §"Decision"
        // §3 — "Disjoint field sets per handler").
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();

        // Step 1 — ListingPublished seeds the M2 fields via the seed handler.
        await SeedCatalogEntry(listingId, sellerId);

        // Step 2 — ListingAttachedToSession sets SessionId.
        var attachedAt = DateTimeOffset.UtcNow;
        await InvokeAuctionHandlerAsync<ListingAttachedToSession>(
            AuctionsSessionHandler.Handle,
            new ListingAttachedToSession(sessionId, listingId, attachedAt));

        // Step 3 — BiddingOpened sets Status = "Open" + ScheduledCloseAt.
        var scheduledCloseAt = DateTimeOffset.UtcNow.AddHours(24);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
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
            OpenedAt: DateTimeOffset.UtcNow));

        // Step 4 — BidPlaced sets the bid fields.
        await InvokeAuctionHandlerAsync<BidPlaced>(AuctionStatusHandler.Handle, new BidPlaced(
            ListingId: listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: bidderId,
            Amount: 60_000m,
            BidCount: 1,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow));

        // Step 5 — SessionStarted sets SessionStartedAt.
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        await InvokeAuctionHandlerAsync<SessionStarted>(AuctionsSessionHandler.Handle, new SessionStarted(
            SessionId: sessionId,
            ListingIds: new[] { listingId },
            StartedAt: startedAt));

        // Assert — every field from every handler lands at its expected value with no
        // mutual overwrite. M2 / M3-S6 / M4-S6 contributions all co-exist on the row.
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();

        // M2 fields (ListingPublishedHandler)
        view!.SellerId.ShouldBe(sellerId);
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");
        view.Format.ShouldBe("Timed");
        view.StartingBid.ShouldBe(50_000m);

        // M3-S6 fields (AuctionStatusHandler)
        view.Status.ShouldBe("Open");                 // BiddingOpened; not regressed by later events
        view.ScheduledCloseAt.ShouldBe(scheduledCloseAt);
        view.CurrentHighBid.ShouldBe(60_000m);
        view.CurrentHighBidderId.ShouldBe(bidderId);
        view.BidCount.ShouldBe(1);

        // M4-S6 fields (AuctionsSessionHandler)
        view.SessionId.ShouldBe(sessionId);
        view.SessionStartedAt.ShouldBe(startedAt);

        // M5-S6 field is null (no SettlementCompleted dispatched)
        view.SettledAt.ShouldBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // M4-S6 — Cross-BC composition test (OQ3 Path α terminal-state pin)
    //
    // Closes the M4-S5 retro's "What M4-S6 should know" §"OQ3 Path α" observation
    // gap: the fan-out emits BiddingOpened for every listing in SessionStarted's
    // ListingIds — including withdrawn listings. This test asserts that the
    // catalog's Withdrawn-preservation guard (AuctionStatusHandler.Handle(BiddingOpened),
    // M4-S6) holds the row at "Withdrawn" rather than flipping it back to "Open".
    //
    // Load-bearing for the M4 milestone doc §3 stance ("Defensive pre-filtering at
    // StartSession time is post-MVP hardening") — without this assertion, that
    // stance is assumption; with it, it's observed behaviour.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus()
    {
        // Arrange — seed a view at Status = "Published" (the natural state for a Flash
        // listing that's been attached and is awaiting its session's start). Capture the
        // pre-composition ScheduledCloseAt so the assertion can prove the guard didn't
        // advance it.
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);

        // Step 1 — Listing is attached to the session (Status stays Published).
        await InvokeAuctionHandlerAsync<ListingAttachedToSession>(
            AuctionsSessionHandler.Handle,
            new ListingAttachedToSession(sessionId, listingId, DateTimeOffset.UtcNow));

        var preCompositionView = await _fixture.LoadCatalogListingViewAsync(listingId);
        preCompositionView.ShouldNotBeNull();
        preCompositionView!.Status.ShouldBe("Published");
        preCompositionView.ScheduledCloseAt.ShouldBeNull();      // never opened

        // Step 2 — Seller withdraws the listing before the session starts. Status
        // transitions Published → Withdrawn; ClosedAt is stamped from WithdrawnAt.
        var withdrawnAt = DateTimeOffset.UtcNow.AddSeconds(1);
        await InvokeAuctionHandlerAsync<ListingWithdrawn>(
            SellingListingWithdrawnHandler.Handle,
            new ListingWithdrawn(
                ListingId: listingId,
                WithdrawnBy: sellerId,
                Reason: null,
                WithdrawnAt: withdrawnAt));

        // Step 3 — The Auctions fan-out emits BiddingOpened for this listing anyway
        // (no defensive pre-filtering at StartSession time per M4 milestone doc §3).
        var fanOutScheduledCloseAt = DateTimeOffset.UtcNow.AddHours(24);
        await InvokeAuctionHandlerAsync<BiddingOpened>(AuctionStatusHandler.Handle, new BiddingOpened(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: 50_000m,
            ReserveThreshold: 75_000m,
            BuyItNowPrice: 150_000m,
            ScheduledCloseAt: fanOutScheduledCloseAt,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(7),
            OpenedAt: DateTimeOffset.UtcNow));

        // Assert — Status stays Withdrawn (not Open). The guard is total per OQ5
        // Path α: ScheduledCloseAt is NOT advanced even though the BiddingOpened
        // event carries one. SessionId is preserved (still attached to the session).
        // SessionStartedAt is null (this test does not dispatch SessionStarted).
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Withdrawn");                       // NOT "Open"
        view.ScheduledCloseAt.ShouldBeNull();                     // NOT fanOutScheduledCloseAt
        view.ClosedAt.ShouldBe(withdrawnAt);                      // preserved from withdrawal
        view.SessionId.ShouldBe(sessionId);                       // attach fact preserved
        view.SessionStartedAt.ShouldBeNull();                     // SessionStarted not dispatched here
        view.Title.ShouldBe("Mint Condition Foil Black Lotus");   // M2 field preserved
    }

    // ─────────────────────────────────────────────────────────────────────────
    // M9-S7 — Cross-queue read-model create race (Insert-on-create + retry)
    //
    // The sibling handlers ride different RabbitMQ queues and can both LoadAsync the
    // same listing as null, then both take the create path. The SiblingHandlers test
    // above exercises *sequential* arrival (the merge logic); these two exercise the
    // *concurrent* create — the data-loss interleaving the memory observed on
    // 2026-06-13. Two sessions both stage a fresh row; the loser's commit collides on
    // the document primary key with DocumentAlreadyExistsException (the trip-wire that
    // triggers ListingsConcurrencyRetryPolicies). Re-running the losing handler in a
    // fresh session simulates Wolverine's retry: LoadAsync now finds the committed row,
    // so the handler takes the merge path and both writers' field sets survive.
    //
    // Direct handler invocation per the M3-S6 in-fixture note — the retry *policy* is
    // Wolverine's own (framework-tested); what these tests pin is the BC's two
    // contributions: the create-path collision and the merge-on-reload recovery.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossQueueCreateRace_SellingCommitsFirst_AuctionRetryPreservesBothFieldSets()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var scheduledCloseAt = DateTimeOffset.UtcNow.AddHours(24);

        var published = BuildMessage(listingId, sellerId);
        var opened = BuildOpened(listingId, sellerId, scheduledCloseAt);

        // Stage both writes against an empty catalog — neither has committed, so both
        // handlers' LoadAsync returns null and both take the Insert create path.
        await using var sellingSession = _fixture.GetDocumentSession();
        await using var auctionSession = _fixture.GetDocumentSession();
        await ListingPublishedHandler.Handle(published, sellingSession, CancellationToken.None);
        await AuctionStatusHandler.Handle(opened, auctionSession, CancellationToken.None);

        // Selling commits first (full record); the auction Insert collides on the primary key.
        await sellingSession.SaveChangesAsync();
        await Should.ThrowAsync<DocumentAlreadyExistsException>(
            async () => await auctionSession.SaveChangesAsync());

        // The collision left the selling record intact — no silent overwrite.
        var afterCollision = await _fixture.LoadCatalogListingViewAsync(listingId);
        afterCollision.ShouldNotBeNull();
        afterCollision!.Title.ShouldBe("Mint Condition Foil Black Lotus");
        afterCollision.Status.ShouldBe("Published");        // auction's minimal "Open" never landed

        // Retry: the losing auction handler re-runs in a fresh session. LoadAsync now finds
        // the committed row, so it merges via Store and preserves the selling fields.
        await using var retrySession = _fixture.GetDocumentSession();
        await AuctionStatusHandler.Handle(opened, retrySession, CancellationToken.None);
        await retrySession.SaveChangesAsync();

        var afterRetry = await _fixture.LoadCatalogListingViewAsync(listingId);
        afterRetry.ShouldNotBeNull();
        afterRetry!.Title.ShouldBe("Mint Condition Foil Black Lotus");   // selling preserved
        afterRetry.SellerId.ShouldBe(sellerId);
        afterRetry.StartingBid.ShouldBe(50_000m);
        afterRetry.Status.ShouldBe("Open");                              // auction merged in
        afterRetry.ScheduledCloseAt.ShouldBe(scheduledCloseAt);
    }

    [Fact]
    public async Task CrossQueueCreateRace_AuctionCommitsFirst_SellingRetryPreservesBothFieldSets()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var scheduledCloseAt = DateTimeOffset.UtcNow.AddHours(24);

        var published = BuildMessage(listingId, sellerId);
        var opened = BuildOpened(listingId, sellerId, scheduledCloseAt);

        await using var sellingSession = _fixture.GetDocumentSession();
        await using var auctionSession = _fixture.GetDocumentSession();
        await ListingPublishedHandler.Handle(published, sellingSession, CancellationToken.None);
        await AuctionStatusHandler.Handle(opened, auctionSession, CancellationToken.None);

        // Auction commits first (minimal record, empty selling fields); the selling Insert collides.
        await auctionSession.SaveChangesAsync();
        await Should.ThrowAsync<DocumentAlreadyExistsException>(
            async () => await sellingSession.SaveChangesAsync());

        // The collision left the auction record intact — the selling fields are simply not there yet.
        var afterCollision = await _fixture.LoadCatalogListingViewAsync(listingId);
        afterCollision.ShouldNotBeNull();
        afterCollision!.Status.ShouldBe("Open");            // auction's record stands
        afterCollision.Title.ShouldBe("");                  // selling fields not yet present

        // Retry: the losing selling handler re-runs. LoadAsync finds the row, so it merges the
        // M2 fields while its load-and-preserve block keeps the auction's Status = "Open".
        await using var retrySession = _fixture.GetDocumentSession();
        await ListingPublishedHandler.Handle(published, retrySession, CancellationToken.None);
        await retrySession.SaveChangesAsync();

        var afterRetry = await _fixture.LoadCatalogListingViewAsync(listingId);
        afterRetry.ShouldNotBeNull();
        afterRetry!.Title.ShouldBe("Mint Condition Foil Black Lotus");   // selling filled in
        afterRetry.SellerId.ShouldBe(sellerId);
        afterRetry.StartingBid.ShouldBe(50_000m);
        afterRetry.Status.ShouldBe("Open");                              // auction status preserved
        afterRetry.ScheduledCloseAt.ShouldBe(scheduledCloseAt);
    }
}
