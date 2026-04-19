using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.ScheduledMessageManagement;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Integration tests for the Auction Closing saga's forward path (M3-S5 scope, scenarios
/// 3.1–3.4). Each test dispatches the saga's input integration events through the Wolverine
/// bus and asserts the resulting saga document state + scheduled-message store.
///
/// Production path: DCB handlers append events to the listing's Marten stream, and
/// UseFastEventForwarding=true on IntegrateWithWolverine republishes those events to the
/// Wolverine bus inside the same outbox scope (see AuctionsModule / Program.cs). Tests
/// dispatch directly to the bus — semantically equivalent from the saga's perspective, and
/// decoupled from the session-listener lifecycle that only wires forwarding on the
/// handler-scoped IDocumentSession.
///
/// Correlation: Saga.Id == ListingId via [SagaIdentityFrom(nameof(X.ListingId))] on each
/// handler parameter (M3-S5 OQ1 Path A — zero contract changes).
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class AuctionClosingSagaTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public AuctionClosingSagaTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
        await CancelAllScheduledCloseAuctionsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BiddingOpened_StartsSaga_SchedulesClose()
    {
        var listingId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, closeAt));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.Id.ShouldBe(listingId);
        saga.ListingId.ShouldBe(listingId);
        saga.Status.ShouldBe(AuctionClosingStatus.AwaitingBids);
        saga.BidCount.ShouldBe(0);
        saga.ReserveHasBeenMet.ShouldBeFalse();
        saga.ScheduledCloseAt.ShouldBe(closeAt);
        saga.OriginalCloseAt.ShouldBe(closeAt);

        var allPending = await QueryAllScheduledAsync();
        var pending = await QueryPendingCloseAuctionsAsync();
        if (pending.Count == 0)
            throw new Xunit.Sdk.XunitException($"No CloseAuction found. All scheduled: [{string.Join(", ", allPending.Select(m => $"{m.MessageType}@{m.ScheduledTime:O}"))}]");
        pending.ShouldHaveSingleItem();
        pending[0].ScheduledTime.ShouldNotBeNull();
        pending[0].ScheduledTime!.Value.ShouldBe(closeAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FirstBid_TransitionsToActive()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, closeAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(new BidPlaced(
            ListingId: listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: bidderId,
            Amount: 30m,
            BidCount: 1,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.Status.ShouldBe(AuctionClosingStatus.Active);
        saga.BidCount.ShouldBe(1);
        saga.CurrentHighBid.ShouldBe(30m);
        saga.CurrentHighBidderId.ShouldBe(bidderId);
    }

    [Fact]
    public async Task ReserveMet_UpdatesSagaState()
    {
        var listingId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, closeAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(new ReserveMet(
            ListingId: listingId,
            Amount: 100m,
            MetAt: DateTimeOffset.UtcNow));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.ReserveHasBeenMet.ShouldBeTrue();
    }

    [Fact]
    public async Task Close_ReserveMet_ProducesListingSold()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        await _fixture.SeedListingStreamAsync(listingId, sellerId, closeAt, startingBid: 25m);
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt,
            bidCount: 12,
            currentHighBid: 85m,
            currentHighBidderId: winnerId,
            reserveHasBeenMet: true);

        var tracked = await TrackAndDispatchCloseAsync(listingId, closeAt);

        var biddingClosed = tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldHaveSingleItem();
        biddingClosed.ListingId.ShouldBe(listingId);

        var sold = tracked.NoRoutes.MessagesOf<ListingSold>().ShouldHaveSingleItem();
        sold.ListingId.ShouldBe(listingId);
        sold.SellerId.ShouldBe(sellerId);
        sold.WinnerId.ShouldBe(winnerId);
        sold.HammerPrice.ShouldBe(85m);
        sold.BidCount.ShouldBe(12);

        tracked.NoRoutes.MessagesOf<ListingPassed>().ShouldBeEmpty();

        // Saga document deleted by MarkCompleted.
        (await _fixture.LoadSaga<AuctionClosingSaga>(listingId)).ShouldBeNull();
    }

    [Fact]
    public async Task Close_ReserveNotMet_ProducesListingPassed()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt,
            bidCount: 5,
            currentHighBid: 40m,
            currentHighBidderId: bidderId,
            reserveHasBeenMet: false);

        var tracked = await TrackAndDispatchCloseAsync(listingId, closeAt);

        tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldHaveSingleItem();

        var passed = tracked.NoRoutes.MessagesOf<ListingPassed>().ShouldHaveSingleItem();
        passed.ListingId.ShouldBe(listingId);
        passed.Reason.ShouldBe("ReserveNotMet");
        passed.HighestBid.ShouldBe(40m);
        passed.BidCount.ShouldBe(5);

        tracked.NoRoutes.MessagesOf<ListingSold>().ShouldBeEmpty();
        (await _fixture.LoadSaga<AuctionClosingSaga>(listingId)).ShouldBeNull();
    }

    [Fact]
    public async Task Close_NoBids_ProducesListingPassed()
    {
        var listingId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.AwaitingBids,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt,
            bidCount: 0,
            reserveHasBeenMet: false);

        var tracked = await TrackAndDispatchCloseAsync(listingId, closeAt);

        tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldHaveSingleItem();

        var passed = tracked.NoRoutes.MessagesOf<ListingPassed>().ShouldHaveSingleItem();
        passed.Reason.ShouldBe("NoBids");
        passed.HighestBid.ShouldBeNull();
        passed.BidCount.ShouldBe(0);

        tracked.NoRoutes.MessagesOf<ListingSold>().ShouldBeEmpty();
        (await _fixture.LoadSaga<AuctionClosingSaga>(listingId)).ShouldBeNull();
    }

    [Fact]
    public async Task BuyItNowPurchased_CompletesSaga()
    {
        var listingId = Guid.CreateVersion7();
        var buyerId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        // Seed an open saga and a real pending CloseAuction at closeAt so the cancel path
        // in Handle(BuyItNowPurchased) has something to remove (assertion below).
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.AwaitingBids,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);
        await ScheduleCloseAuctionAsync(listingId, closeAt);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new BuyItNowPurchased(
                ListingId: listingId,
                BuyerId: buyerId,
                Price: 100m,
                PurchasedAt: DateTimeOffset.UtcNow));

        // Saga document deleted by MarkCompleted (Wolverine.Saga.cs:12-28).
        (await _fixture.LoadSaga<AuctionClosingSaga>(listingId)).ShouldBeNull();

        // No outcome events on the BIN terminal path — BuyItNowPurchased is itself the
        // terminal outcome contract (BiddingClosed.cs explicit "Not emitted on the BuyItNow
        // terminal path"; workshop scenario 3.8 explicit "no BiddingClosed, no ListingSold").
        tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<ListingSold>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<ListingPassed>().ShouldBeEmpty();

        // Pending CloseAuction cancelled (M3-S5b OQ2 Path a).
        var pending = await QueryPendingCloseAuctionsAsync();
        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task CloseAuction_AfterBuyItNow_NoOp()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Seed a saga directly in Resolved state — represents "saga already terminated by
        // BuyItNowPurchased" without going through MarkCompleted (which would delete the
        // doc, leaving nothing for Handle(CloseAuction) to find). This tests the
        // Status == Resolved early-return guard, which is the production path when the
        // pending CloseAuction was somehow not cancelled before the saga terminated.
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Resolved,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt,
            bidCount: 3,
            currentHighBid: 50m,
            currentHighBidderId: bidderId,
            reserveHasBeenMet: true);

        var beforeSaga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        beforeSaga.ShouldNotBeNull();
        var beforeRevision = beforeSaga!.Version;

        var tracked = await TrackAndDispatchCloseAsync(listingId, closeAt);

        // No outcome events — early-return guard fired.
        tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<ListingSold>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<ListingPassed>().ShouldBeEmpty();

        // Saga state byte-identical (no MarkCompleted called on the early-return path).
        var afterSaga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        afterSaga.ShouldNotBeNull();
        afterSaga!.Status.ShouldBe(AuctionClosingStatus.Resolved);
        afterSaga.BidCount.ShouldBe(3);
        afterSaga.CurrentHighBid.ShouldBe(50m);
        afterSaga.CurrentHighBidderId.ShouldBe(bidderId);
        afterSaga.ReserveHasBeenMet.ShouldBeTrue();
    }

    [Fact]
    public async Task ListingWithdrawn_TerminatesWithoutEvaluation()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        // Seed an Active saga with bids on the books — withdrawal must skip reserve
        // evaluation entirely, so even with bids present no ListingSold/ListingPassed is
        // emitted (workshop scenario 3.10; M3 milestone doc §3 "terminates without
        // evaluation").
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt,
            bidCount: 5,
            currentHighBid: 75m,
            currentHighBidderId: bidderId,
            reserveHasBeenMet: true);
        await ScheduleCloseAuctionAsync(listingId, closeAt);

        // Dispatch via the bus — the fixture's session-scoped AppendListingWithdrawnAsync
        // does not forward (per M3-S5 retro §OQ4), and the Selling-side publisher remains
        // deferred (M3 §3), so the test acts as the synthetic producer.
        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ListingWithdrawn(listingId));

        // Saga document deleted by MarkCompleted.
        (await _fixture.LoadSaga<AuctionClosingSaga>(listingId)).ShouldBeNull();

        // No outcome events on the withdrawal terminal path — no BiddingClosed (OQ1 Path B
        // — terminal handlers do not emit it), no ListingSold even though reserve was met,
        // no ListingPassed even though bids existed.
        tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<ListingSold>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<ListingPassed>().ShouldBeEmpty();

        // Pending CloseAuction cancelled (M3-S5b OQ2 Path a — same shape as BIN).
        var pending = await QueryPendingCloseAuctionsAsync();
        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task Close_AfterExtension_UsesRescheduledTime()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();
        var originalCloseAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var extendedCloseAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        await _fixture.SeedListingStreamAsync(listingId, sellerId, extendedCloseAt, startingBid: 25m);
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Extended,
            scheduledCloseAt: extendedCloseAt,
            originalCloseAt: originalCloseAt,
            bidCount: 12,
            currentHighBid: 85m,
            currentHighBidderId: winnerId,
            reserveHasBeenMet: true);

        // Dispatch CloseAuction at the extended time — same evaluation logic regardless of
        // whether the listing was extended (workshop scenario 3.11).
        var tracked = await TrackAndDispatchCloseAsync(listingId, extendedCloseAt);

        tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldHaveSingleItem();
        var sold = tracked.NoRoutes.MessagesOf<ListingSold>().ShouldHaveSingleItem();
        sold.HammerPrice.ShouldBe(85m);
        sold.BidCount.ShouldBe(12);
        (await _fixture.LoadSaga<AuctionClosingSaga>(listingId)).ShouldBeNull();
    }

    [Fact]
    public async Task ExtendedBidding_CancelsAndReschedules()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var originalCloseAt = DateTimeOffset.UtcNow.AddHours(1);
        var extendedCloseAt = originalCloseAt.AddMinutes(2);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, originalCloseAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(new ExtendedBiddingTriggered(
            ListingId: listingId,
            PreviousCloseAt: originalCloseAt,
            NewCloseAt: extendedCloseAt,
            TriggeredByBidderId: bidderId,
            TriggeredAt: DateTimeOffset.UtcNow));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.Status.ShouldBe(AuctionClosingStatus.Extended);
        saga.ScheduledCloseAt.ShouldBe(extendedCloseAt);

        var pending = await QueryPendingCloseAuctionsAsync();
        pending.ShouldHaveSingleItem();
        pending[0].ScheduledTime!.Value.ShouldBe(extendedCloseAt, TimeSpan.FromSeconds(1));
        pending.ShouldNotContain(m =>
            m.ScheduledTime.HasValue &&
            Math.Abs((m.ScheduledTime.Value - originalCloseAt).TotalMilliseconds) < 100);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static BiddingOpened BuildBiddingOpened(Guid listingId, DateTimeOffset closeAt) =>
        new(
            ListingId: listingId,
            SellerId: Guid.CreateVersion7(),
            StartingBid: 25m,
            ReserveThreshold: null,
            BuyItNowPrice: null,
            ScheduledCloseAt: closeAt,
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromMinutes(2),
            ExtendedBiddingExtension: TimeSpan.FromMinutes(2),
            MaxDuration: TimeSpan.FromHours(24),
            OpenedAt: DateTimeOffset.UtcNow);

    private async Task<IReadOnlyList<ScheduledMessageSummary>> QueryPendingCloseAuctionsAsync()
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var result = await store.ScheduledMessages.QueryAsync(
            new ScheduledMessageQuery { PageSize = 1000 },
            CancellationToken.None);
        return result.Messages
            .Where(m => m.MessageType != null && m.MessageType.Contains(nameof(CloseAuction)))
            .ToList();
    }

    private async Task<IReadOnlyList<ScheduledMessageSummary>> QueryAllScheduledAsync()
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var result = await store.ScheduledMessages.QueryAsync(
            new ScheduledMessageQuery { PageSize = 1000 },
            CancellationToken.None);
        return result.Messages;
    }

    /// <summary>
    /// Dispatch a CloseAuction through the bus inside a TrackedSession so cascaded outcome
    /// events (BiddingClosed, ListingSold/ListingPassed) emitted via OutgoingMessages can be
    /// observed. Outcome events have no routing rule wired in this fixture (RabbitMQ is
    /// disabled, no opts.Publish for these types until M3-S6), so cascaded messages land in
    /// tracked.NoRoutes — the envelope record carries the message body for assertions via
    /// MessagesOf&lt;T&gt;().
    /// </summary>
    private async Task<Wolverine.Tracking.ITrackedSession> TrackAndDispatchCloseAsync(
        Guid listingId, DateTimeOffset scheduledAt)
    {
        return await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new CloseAuction(listingId, scheduledAt));
    }

    /// <summary>
    /// Schedule a real pending CloseAuction for the listing — used to verify the
    /// cancel-on-terminal path in scenarios 3.8 / 3.10. Mirrors what
    /// StartAuctionClosingSagaHandler does in production for the BiddingOpened path.
    /// </summary>
    private async Task ScheduleCloseAuctionAsync(Guid listingId, DateTimeOffset scheduledAt)
    {
        // IMessageBus is registered scoped — resolving from Host.Services (the root provider)
        // throws InvalidOperationException. Create an async scope so the request-scoped bus
        // resolves cleanly. ScheduleAsync writes to the scheduled-message store synchronously,
        // so the scope can dispose immediately after the await.
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await bus.ScheduleAsync(new CloseAuction(listingId, scheduledAt), scheduledAt);
    }

    private async Task CancelAllScheduledCloseAuctionsAsync()
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var all = await QueryAllScheduledAsync();
        var ids = all.Where(m => m.MessageType != null && m.MessageType.Contains(nameof(CloseAuction)))
            .Select(m => m.Id).ToArray();
        if (ids.Length == 0) return;
        await store.ScheduledMessages.CancelAsync(
            new ScheduledMessageQuery { MessageIds = ids },
            CancellationToken.None);
    }
}
