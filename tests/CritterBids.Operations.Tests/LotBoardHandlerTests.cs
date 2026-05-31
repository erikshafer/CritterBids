using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Operations.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Operations.Tests;

/// <summary>
/// End-to-end Testcontainers projection tests for the M7-S3 lot-board surface (W006 §2). Each test
/// dispatches the Selling- and Auctions-family integration events through the in-process Wolverine
/// bus (<see cref="TestingExtensions.InvokeMessageAndWaitAsync"/>) so the full path is exercised:
/// handler discovery + code-gen, the injected Marten session, and the AutoApplyTransactions commit
/// — not just the projection arithmetic. The foreign BCs are excluded in
/// <see cref="OperationsTestFixture"/>, so the two Operations lot-board siblings
/// (<see cref="LotBoardSellingHandler"/> / <see cref="LotBoardAuctionsHandler"/>) are the sole
/// consumers of each event.
///
/// Coverage: the full <c>Draft → Open → Sold</c> lifecycle plus the <c>Passed</c> and
/// <c>Withdrawn</c> terminal paths; the set-once <c>SellerId</c> guard (incl. <c>ListingSold</c> as
/// first carrier); the terminal-does-not-regress-to-<c>Open</c> guard on a late <c>BidPlaced</c>;
/// the seed-late load-and-preserve case (<c>ListingPublished</c> after Auctions events); and the
/// <c>LastUpdatedAt</c> latest-wins no-rewind rule.
/// </summary>
[Collection(OperationsTestCollection.Name)]
public class LotBoardHandlerTests : IAsyncLifetime
{
    private readonly OperationsTestFixture _fixture;

    public LotBoardHandlerTests(OperationsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _fixture.CleanAllMartenDataAsync();
        }
        catch (ObjectDisposedException)
        {
            // Host failed to start — let the test fail with a clearer message rather than
            // cascading ObjectDisposedExceptions.
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Fixed, ordered timestamps so LastUpdatedAt (latest-wins) assertions are deterministic.
    private static readonly DateTimeOffset PublishedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OpenedAt    = PublishedAt.AddMinutes(1);
    private static readonly DateTimeOffset BidOneAt    = PublishedAt.AddMinutes(2);
    private static readonly DateTimeOffset BidTwoAt    = PublishedAt.AddMinutes(3);
    private static readonly DateTimeOffset SoldAt      = PublishedAt.AddMinutes(4);

    // ───────────────────────────────────────────────────────────────────────────
    // Full Draft → Open → Sold lifecycle + set-once SellerId + terminal-no-regress
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LotBoard_WalksFullLifecycle_ThroughDraftOpenSold()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();

        // ── Draft (ListingPublished) ──────────────────────────────────────────
        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingPublished(
            listingId, sellerId, Title: "Vintage Critter Plush", Format: "Auction",
            StartingBid: 10m, ReservePrice: 50m, BuyItNow: 120m, Duration: TimeSpan.FromDays(7),
            ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: TimeSpan.FromMinutes(5),
            ExtendedBiddingExtension: TimeSpan.FromMinutes(5), FeePercentage: 0.10m, PublishedAt));

        var afterPublished = await Load(listingId);
        afterPublished.ShouldNotBeNull();
        afterPublished.Status.ShouldBe(LotBoardStatus.Draft);
        afterPublished.SellerId.ShouldBe(sellerId);
        afterPublished.Title.ShouldBe("Vintage Critter Plush");
        afterPublished.Format.ShouldBe("Auction");
        afterPublished.StartingBid.ShouldBe(10m);
        afterPublished.ReservePrice.ShouldBe(50m);
        afterPublished.BuyItNow.ShouldBe(120m);
        afterPublished.FeePercentage.ShouldBe(0.10m);
        afterPublished.LastUpdatedAt.ShouldBe(PublishedAt);

        // ── Open (BiddingOpened) ──────────────────────────────────────────────
        var closeAt = OpenedAt.AddDays(7);
        await _fixture.Host.InvokeMessageAndWaitAsync(new BiddingOpened(
            listingId, sellerId, StartingBid: 10m, ReserveThreshold: 50m, BuyItNowPrice: 120m,
            ScheduledCloseAt: closeAt, ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromMinutes(5),
            ExtendedBiddingExtension: TimeSpan.FromMinutes(5), MaxDuration: TimeSpan.FromDays(10),
            OpenedAt));

        var afterOpened = await Load(listingId);
        afterOpened.ShouldNotBeNull();
        afterOpened.Status.ShouldBe(LotBoardStatus.Open);
        afterOpened.ScheduledCloseAt.ShouldBe(closeAt);
        // Catalog fields preserved across the Auctions event.
        afterOpened.Title.ShouldBe("Vintage Critter Plush");
        afterOpened.LastUpdatedAt.ShouldBe(OpenedAt);

        // ── BidPlaced ×2 — CurrentBid latest/highest + BidCount ───────────────
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Amount: 25m, BidCount: 1, IsProxy: false, BidOneAt));
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Amount: 55m, BidCount: 2, IsProxy: false, BidTwoAt));

        var afterBids = await Load(listingId);
        afterBids.ShouldNotBeNull();
        afterBids.Status.ShouldBe(LotBoardStatus.Open);
        afterBids.CurrentBid.ShouldBe(55m);
        afterBids.BidCount.ShouldBe(2);
        afterBids.LastUpdatedAt.ShouldBe(BidTwoAt);

        // ── Sold (ListingSold) ────────────────────────────────────────────────
        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingSold(
            listingId, sellerId, winnerId, HammerPrice: 55m, BidCount: 2, SoldAt));

        var afterSold = await Load(listingId);
        afterSold.ShouldNotBeNull();
        afterSold.Status.ShouldBe(LotBoardStatus.Sold);
        afterSold.HammerPrice.ShouldBe(55m);
        afterSold.WinnerId.ShouldBe(winnerId);
        afterSold.BidCount.ShouldBe(2);
        afterSold.LastUpdatedAt.ShouldBe(SoldAt);

        // ── Late BidPlaced after terminal must NOT regress Sold → Open, and must not
        //    disturb the final figures or rewind LastUpdatedAt ──────────────────
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Amount: 999m, BidCount: 3, IsProxy: false, SoldAt.AddMinutes(1)));

        var afterLateBid = await Load(listingId);
        afterLateBid.ShouldNotBeNull();
        afterLateBid.Status.ShouldBe(LotBoardStatus.Sold);   // terminal-no-regress guard
        afterLateBid.CurrentBid.ShouldBe(55m);               // final figure preserved
        afterLateBid.BidCount.ShouldBe(2);
        afterLateBid.HammerPrice.ShouldBe(55m);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Passed terminal path
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LotBoard_PassedPath_SetsPassedWithReason()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingPublished(
            listingId, sellerId, "Rare Beast", "Auction", 10m, ReservePrice: 200m, BuyItNow: null,
            Duration: TimeSpan.FromDays(7), ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null, ExtendedBiddingExtension: null,
            FeePercentage: 0.10m, PublishedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(new BiddingOpened(
            listingId, sellerId, 10m, 200m, null, OpenedAt.AddDays(7), false, null, null,
            TimeSpan.FromDays(10), OpenedAt));

        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingPassed(
            listingId, Reason: "ReserveNotMet", HighestBid: 80m, BidCount: 3, PassedAt: SoldAt));

        var view = await Load(listingId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(LotBoardStatus.Passed);
        view.PassReason.ShouldBe("ReserveNotMet");
        view.BidCount.ShouldBe(3);
        view.LastUpdatedAt.ShouldBe(SoldAt);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Withdrawn terminal path
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LotBoard_WithdrawnPath_SetsWithdrawnWithInitiatorAndReason()
    {
        var listingId   = Guid.CreateVersion7();
        var sellerId    = Guid.CreateVersion7();
        var withdrawnBy = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingPublished(
            listingId, sellerId, "Withdrawable Widget", "Auction", 10m, null, null,
            TimeSpan.FromDays(7), false, null, null, 0.10m, PublishedAt));

        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingWithdrawn(
            listingId, withdrawnBy, Reason: "Seller changed mind", WithdrawnAt: OpenedAt));

        var view = await Load(listingId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(LotBoardStatus.Withdrawn);
        view.WithdrawnBy.ShouldBe(withdrawnBy);
        view.WithdrawalReason.ShouldBe("Seller changed mind");
        view.LastUpdatedAt.ShouldBe(OpenedAt);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Seed-late / load-and-preserve: ListingPublished arriving AFTER Auctions events
    // fills catalog fields without clobbering auction state or regressing Status to
    // Draft; the older PublishedAt does not rewind LastUpdatedAt. Also asserts the
    // ADR-014 Path A pure-consumer contract — the Operations handlers publish nothing.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LotBoard_SeedLate_PreservesAuctionState_AndDoesNotRegressOrRewind()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var closeAt   = OpenedAt.AddDays(7);

        // Auctions events arrive FIRST: BiddingOpened (sets SellerId/Schedule/Open) then a bid.
        await _fixture.Host.InvokeMessageAndWaitAsync(new BiddingOpened(
            listingId, sellerId, StartingBid: 10m, ReserveThreshold: 50m, BuyItNowPrice: null,
            ScheduledCloseAt: closeAt, ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null, ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromDays(10), OpenedAt));
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Amount: 30m, BidCount: 1, IsProxy: false, BidOneAt));

        // ListingPublished arrives LATE with an OLDER timestamp (PublishedAt < OpenedAt). It must
        // fill catalog fields but NOT regress Status to Draft, NOT clobber CurrentBid/BidCount/
        // ScheduledCloseAt, and NOT rewind LastUpdatedAt below the latest auction event.
        var tracked = await _fixture.Host.InvokeMessageAndWaitAsync(new ListingPublished(
            listingId, sellerId, Title: "Seeded Late", Format: "Auction", StartingBid: 10m,
            ReservePrice: 50m, BuyItNow: 120m, Duration: TimeSpan.FromDays(7),
            ExtendedBiddingEnabled: false, ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null, FeePercentage: 0.10m, PublishedAt));

        var view = await Load(listingId);
        view.ShouldNotBeNull();
        // Catalog fields now filled by the late seed.
        view.Title.ShouldBe("Seeded Late");
        view.ReservePrice.ShouldBe(50m);
        view.BuyItNow.ShouldBe(120m);
        view.FeePercentage.ShouldBe(0.10m);
        // Auction state preserved — not regressed/clobbered by the late seed.
        view.Status.ShouldBe(LotBoardStatus.Open);
        view.CurrentBid.ShouldBe(30m);
        view.BidCount.ShouldBe(1);
        view.ScheduledCloseAt.ShouldBe(closeAt);
        // Latest-wins keeps the newer bid timestamp; the older PublishedAt does not rewind it.
        view.LastUpdatedAt.ShouldBe(BidOneAt);

        // Pure consumer (ADR-014 Path A): no integration messages are published by the Operations
        // lot-board handlers. Re-emitting any consumed event would surface here.
        tracked.Sent.MessagesOf<ListingPublished>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<BiddingOpened>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<BidPlaced>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<ListingSold>().ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Set-once SellerId: ListingSold populates it when it is the FIRST carrier to
    // arrive (W006 §2 — SellerId traces to three events under one set-once guard).
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LotBoard_ListingSold_PopulatesSellerId_WhenFirstCarrier()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();

        // A bid arrives first — BidPlaced carries no SellerId, so the row is seeded with the
        // Guid.Empty sentinel. (Out-of-order, but proves the set-once-from-empty fill.)
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            Amount: 40m, BidCount: 1, IsProxy: false, BidOneAt));

        var afterBid = await Load(listingId);
        afterBid.ShouldNotBeNull();
        afterBid.SellerId.ShouldBe(Guid.Empty);

        // ListingSold is the first event carrying SellerId — it must fill it.
        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingSold(
            listingId, sellerId, winnerId, HammerPrice: 40m, BidCount: 1, SoldAt));

        var afterSold = await Load(listingId);
        afterSold.ShouldNotBeNull();
        afterSold.SellerId.ShouldBe(sellerId);
        afterSold.Status.ShouldBe(LotBoardStatus.Sold);
        afterSold.WinnerId.ShouldBe(winnerId);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Set-once SellerId is not overwritten by a later carrier with a different value.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LotBoard_SellerId_IsSetOnce_AcrossPublishedThenSold()
    {
        var listingId   = Guid.CreateVersion7();
        var sellerFirst = Guid.CreateVersion7();
        var sellerOther = Guid.CreateVersion7();
        var winnerId    = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingPublished(
            listingId, sellerFirst, "SetOnce", "Auction", 10m, null, null, TimeSpan.FromDays(7),
            false, null, null, 0.10m, PublishedAt));

        // A later ListingSold carrying a DIFFERENT seller must not overwrite the set-once SellerId.
        await _fixture.Host.InvokeMessageAndWaitAsync(new ListingSold(
            listingId, sellerOther, winnerId, HammerPrice: 60m, BidCount: 1, SoldAt));

        var view = await Load(listingId);
        view.ShouldNotBeNull();
        view.SellerId.ShouldBe(sellerFirst);   // set-once: first carrier preserved
        view.Status.ShouldBe(LotBoardStatus.Sold);
    }

    private async Task<LotBoardView?> Load(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        return await session.LoadAsync<LotBoardView>(listingId);
    }
}
