using CritterBids.Contracts.Settlement;
using CritterBids.Operations.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Operations.Tests;

/// <summary>
/// End-to-end Testcontainers projection tests for the M7-S2 settlement-queue surface. Each test
/// dispatches the Settlement-family integration events through the in-process Wolverine bus
/// (<see cref="TestingExtensions.InvokeMessageAndWaitAsync"/>) so the full path is exercised:
/// handler discovery + code-gen, the injected Marten session, and the AutoApplyTransactions commit
/// — not just the projection arithmetic. The six foreign BCs are excluded in
/// <see cref="OperationsTestFixture"/>, so the Operations <c>SettlementQueueHandler</c> is the sole
/// consumer of each event.
///
/// Coverage: the full <c>Failed → Completed → PaidOut</c> lifecycle + the <c>PaidOut</c>-does-not-
/// regress guard on a re-delivered <c>SettlementCompleted</c> (W006 §1); the
/// <c>SellerPayoutIssued</c>-first-arrival minimal row; and later enrichment of the set-once
/// <c>ListingId</c>/<c>WinnerId</c> by a <c>SettlementCompleted</c> that arrives after the payout.
/// </summary>
[Collection(OperationsTestCollection.Name)]
public class SettlementQueueHandlerTests : IAsyncLifetime
{
    private readonly OperationsTestFixture _fixture;

    public SettlementQueueHandlerTests(OperationsTestFixture fixture)
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
    private static readonly DateTimeOffset FailedAt    = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAt = FailedAt.AddMinutes(1);
    private static readonly DateTimeOffset IssuedAt    = FailedAt.AddMinutes(2);

    // ───────────────────────────────────────────────────────────────────────────
    // Full Failed → Completed → PaidOut lifecycle + the PaidOut-no-regress guard
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SettlementQueue_WalksFullLifecycle_AndDoesNotRegressFromPaidOut()
    {
        var settlementId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var winnerId     = Guid.CreateVersion7();
        var sellerId     = Guid.CreateVersion7();

        // ── Failed ──────────────────────────────────────────────────────────────
        // This single-stream walk exercises the W006 §1 Status-derivation rule literally: a real
        // settlement is Failed XOR (Completed → PaidOut), so PaymentFailed-then-SettlementCompleted
        // is not a domain-realistic sequence — it is the spec-mandated exercise of every status
        // transition on one row, including the no-regress guard at the end.
        await _fixture.Host.InvokeMessageAndWaitAsync(new PaymentFailed(
            settlementId, listingId, winnerId, "ReserveNotMet", FailedAt));

        var afterFailed = await Load(settlementId);
        afterFailed.ShouldNotBeNull();
        afterFailed.Status.ShouldBe(SettlementQueueStatus.Failed);
        afterFailed.FailureReason.ShouldBe("ReserveNotMet");
        afterFailed.ListingId.ShouldBe(listingId);
        afterFailed.WinnerId.ShouldBe(winnerId);
        afterFailed.LastUpdatedAt.ShouldBe(FailedAt);

        // ── Completed ───────────────────────────────────────────────────────────
        await _fixture.Host.InvokeMessageAndWaitAsync(new SettlementCompleted(
            settlementId, listingId, winnerId, sellerId,
            HammerPrice: 85m, FeeAmount: 8.50m, SellerPayout: 76.50m, CompletedAt));

        var afterCompleted = await Load(settlementId);
        afterCompleted.ShouldNotBeNull();
        afterCompleted.Status.ShouldBe(SettlementQueueStatus.Completed);
        afterCompleted.SellerId.ShouldBe(sellerId);
        afterCompleted.HammerPrice.ShouldBe(85m);
        afterCompleted.FeeAmount.ShouldBe(8.50m);
        afterCompleted.SellerPayout.ShouldBe(76.50m);
        afterCompleted.LastUpdatedAt.ShouldBe(CompletedAt);
        // W006 §1 lists no Failed-reason clear; the field lingers across the (spec-only) transition.
        afterCompleted.FailureReason.ShouldBe("ReserveNotMet");

        // ── PaidOut ─────────────────────────────────────────────────────────────
        await _fixture.Host.InvokeMessageAndWaitAsync(new SellerPayoutIssued(
            settlementId, sellerId, PayoutAmount: 76.50m, FeeDeducted: 8.50m, IssuedAt));

        var afterPaidOut = await Load(settlementId);
        afterPaidOut.ShouldNotBeNull();
        afterPaidOut.Status.ShouldBe(SettlementQueueStatus.PaidOut);
        afterPaidOut.PayoutAmount.ShouldBe(76.50m);
        afterPaidOut.FeeDeducted.ShouldBe(8.50m);
        afterPaidOut.LastUpdatedAt.ShouldBe(IssuedAt);
        // Set-once fields preserved — SellerPayoutIssued carries neither ListingId nor WinnerId.
        afterPaidOut.ListingId.ShouldBe(listingId);
        afterPaidOut.WinnerId.ShouldBe(winnerId);

        // ── Re-delivered SettlementCompleted must not regress PaidOut → Completed ──
        await _fixture.Host.InvokeMessageAndWaitAsync(new SettlementCompleted(
            settlementId, listingId, winnerId, sellerId,
            HammerPrice: 85m, FeeAmount: 8.50m, SellerPayout: 76.50m, CompletedAt));

        var afterRedelivery = await Load(settlementId);
        afterRedelivery.ShouldNotBeNull();
        afterRedelivery.Status.ShouldBe(SettlementQueueStatus.PaidOut);
        // Latest-wins keeps the later payout timestamp; the older re-delivered CompletedAt
        // does not rewind LastUpdatedAt.
        afterRedelivery.LastUpdatedAt.ShouldBe(IssuedAt);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // SellerPayoutIssued as the first event to arrive — minimal row, set-once fields unset
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SellerPayoutIssued_FirstArrival_LeavesSetOnceFieldsEmpty()
    {
        var settlementId = Guid.CreateVersion7();
        var sellerId     = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(new SellerPayoutIssued(
            settlementId, sellerId, PayoutAmount: 50m, FeeDeducted: 5m, IssuedAt));

        var view = await Load(settlementId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SettlementQueueStatus.PaidOut);
        view.SellerId.ShouldBe(sellerId);
        view.PayoutAmount.ShouldBe(50m);
        view.FeeDeducted.ShouldBe(5m);
        view.LastUpdatedAt.ShouldBe(IssuedAt);

        // SellerPayoutIssued carries no ListingId/WinnerId; the minimal row leaves them at
        // Guid.Empty until a later listing-bearing event fills them (M7-S2 retro: first-arrival
        // behavior). No HammerPrice/FeeAmount/SellerPayout either — those are SettlementCompleted's.
        view.ListingId.ShouldBe(Guid.Empty);
        view.WinnerId.ShouldBe(Guid.Empty);
        view.HammerPrice.ShouldBeNull();
        view.FailureReason.ShouldBeNull();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Out-of-order: SellerPayoutIssued first, then SettlementCompleted enriches the row
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SettlementCompleted_AfterPayout_EnrichesSetOnceFields_WithoutRegressingStatus()
    {
        var settlementId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var winnerId     = Guid.CreateVersion7();
        var sellerId     = Guid.CreateVersion7();

        // Payout arrives first — minimal PaidOut row with empty set-once fields.
        await _fixture.Host.InvokeMessageAndWaitAsync(new SellerPayoutIssued(
            settlementId, sellerId, PayoutAmount: 76.50m, FeeDeducted: 8.50m, IssuedAt));

        // SettlementCompleted arrives late (its CompletedAt predates the payout). It must fill the
        // set-once ListingId/WinnerId and the completed-fields, but must NOT regress PaidOut →
        // Completed, and must NOT rewind LastUpdatedAt below the payout timestamp.
        await _fixture.Host.InvokeMessageAndWaitAsync(new SettlementCompleted(
            settlementId, listingId, winnerId, sellerId,
            HammerPrice: 85m, FeeAmount: 8.50m, SellerPayout: 76.50m, CompletedAt));

        var view = await Load(settlementId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SettlementQueueStatus.PaidOut);
        view.ListingId.ShouldBe(listingId);
        view.WinnerId.ShouldBe(winnerId);
        view.HammerPrice.ShouldBe(85m);
        view.FeeAmount.ShouldBe(8.50m);
        view.SellerPayout.ShouldBe(76.50m);
        // Payout fields intact; latest-wins keeps the payout timestamp over the older CompletedAt.
        view.PayoutAmount.ShouldBe(76.50m);
        view.FeeDeducted.ShouldBe(8.50m);
        view.LastUpdatedAt.ShouldBe(IssuedAt);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Set-once ListingId/WinnerId are NOT overwritten by a later conflicting event,
    // while SellerId IS last-write (W006 §1: set-once for ListingId/WinnerId; no
    // set-once guard for SellerId). Also asserts the ADR-014 Path A pure-consumer
    // contract: the Operations handler publishes nothing.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetOnceFields_NotOverwritten_ByConflictingEvent_AndHandlerPublishesNothing()
    {
        var settlementId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();
        var winnerA  = Guid.CreateVersion7();
        var listingB = Guid.CreateVersion7();
        var winnerB  = Guid.CreateVersion7();
        var sellerB  = Guid.CreateVersion7();

        // First event fixes ListingId/WinnerId to the A-values.
        await _fixture.Host.InvokeMessageAndWaitAsync(new PaymentFailed(
            settlementId, listingA, winnerA, "ReserveNotMet", FailedAt));

        // A later SettlementCompleted carrying DIFFERENT listing/winner must not overwrite the
        // set-once fields, but its SellerId is written (SellerId is last-write, not set-once).
        var tracked = await _fixture.Host.InvokeMessageAndWaitAsync(new SettlementCompleted(
            settlementId, listingB, winnerB, sellerB,
            HammerPrice: 85m, FeeAmount: 8.50m, SellerPayout: 76.50m, CompletedAt));

        var view = await Load(settlementId);
        view.ShouldNotBeNull();
        view.ListingId.ShouldBe(listingA);   // set-once: A preserved, not B
        view.WinnerId.ShouldBe(winnerA);     // set-once: A preserved, not B
        view.SellerId.ShouldBe(sellerB);     // last-write: B written
        view.Status.ShouldBe(SettlementQueueStatus.Completed);

        // Pure consumer (ADR-014 Path A): no cascading/integration messages are published by the
        // Operations handler. Re-emitting any settlement-family event would surface here.
        tracked.Sent.MessagesOf<SettlementCompleted>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<SellerPayoutIssued>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<PaymentFailed>().ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // SellerId is last-write: a SellerPayoutIssued seller overwrites the one a prior
    // SettlementCompleted set (W006 §1 line 83 — no set-once guard for SellerId).
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SellerId_IsLastWrite_AcrossCompletedThenPayout()
    {
        var settlementId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var winnerId     = Guid.CreateVersion7();
        var sellerFirst  = Guid.CreateVersion7();
        var sellerSecond = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(new SettlementCompleted(
            settlementId, listingId, winnerId, sellerFirst,
            HammerPrice: 85m, FeeAmount: 8.50m, SellerPayout: 76.50m, CompletedAt));

        var afterCompleted = await Load(settlementId);
        afterCompleted.ShouldNotBeNull();
        afterCompleted.SellerId.ShouldBe(sellerFirst);

        // SellerPayoutIssued carries a different SellerId — last-write wins.
        await _fixture.Host.InvokeMessageAndWaitAsync(new SellerPayoutIssued(
            settlementId, sellerSecond, PayoutAmount: 76.50m, FeeDeducted: 8.50m, IssuedAt));

        var afterPayout = await Load(settlementId);
        afterPayout.ShouldNotBeNull();
        afterPayout.SellerId.ShouldBe(sellerSecond);
        afterPayout.Status.ShouldBe(SettlementQueueStatus.PaidOut);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // The intentional asymmetry (W006 §1): PaidOut does not regress to Completed, but
    // there is NO Failed-regression guard — a PaymentFailed after PaidOut still flips
    // Status to Failed. An older PaymentFailed timestamp must not rewind LastUpdatedAt.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaymentFailed_AfterPaidOut_SetsFailed_AndOlderTimestampDoesNotRewind()
    {
        var settlementId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var winnerId     = Guid.CreateVersion7();
        var sellerId     = Guid.CreateVersion7();

        // Reach PaidOut first (latest stamp = IssuedAt).
        await _fixture.Host.InvokeMessageAndWaitAsync(new SellerPayoutIssued(
            settlementId, sellerId, PayoutAmount: 50m, FeeDeducted: 5m, IssuedAt));

        // A PaymentFailed arrives afterwards with an OLDER timestamp (FailedAt < IssuedAt). W006
        // mandates no Failed-regression guard, so Status flips to Failed; latest-wins keeps the
        // newer IssuedAt stamp. Payout-first left ListingId/WinnerId empty, so this fills them.
        await _fixture.Host.InvokeMessageAndWaitAsync(new PaymentFailed(
            settlementId, listingId, winnerId, "PayoutReversed", FailedAt));

        var view = await Load(settlementId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SettlementQueueStatus.Failed);          // no Failed-regression guard
        view.FailureReason.ShouldBe("PayoutReversed");
        view.LastUpdatedAt.ShouldBe(IssuedAt);                       // older event does not rewind
        view.ListingId.ShouldBe(listingId);                          // set-once fill from empty
        view.WinnerId.ShouldBe(winnerId);
    }

    private async Task<SettlementQueueView?> Load(Guid settlementId)
    {
        await using var session = _fixture.GetDocumentSession();
        return await session.LoadAsync<SettlementQueueView>(settlementId);
    }
}
