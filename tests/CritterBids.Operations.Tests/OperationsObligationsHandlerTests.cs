using CritterBids.Contracts.Obligations;
using CritterBids.Operations.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Operations.Tests;

/// <summary>
/// End-to-end Testcontainers projection tests for the M7-S4 obligations view (W006 §4). Each test
/// dispatches the Obligations-family integration events through the in-process Wolverine bus so the
/// full path is exercised — handler discovery + code-gen, the injected Marten session, and the
/// AutoApplyTransactions commit, not just the projection arithmetic. The foreign BCs are excluded in
/// <see cref="OperationsTestFixture"/>, so <see cref="OperationsObligationsHandler"/> is the sole
/// consumer of each obligations event (one handler per event — no fan-out — so
/// <see cref="TestingExtensions.InvokeMessageAndWaitAsync"/> dispatches inline).
///
/// Coverage: the escalation-queue seed; the <c>Escalated → Disputed</c> advance and open-dispute
/// membership; the three <c>DisputeResolved</c> branches (<c>Extension</c> returns to the active set
/// non-terminally; <c>Refund</c>/<c>Closed</c> resolve terminally); the <c>ObligationFulfilled</c>
/// terminal clear with winner/seller; the narrative-008 Moment-3 <c>Active → Fulfilled</c> path; the
/// terminal-no-regress guard on a re-delivered earlier event; idempotent terminal re-delivery; the
/// unseen-obligation tolerant seed; and the pure-consumer (no OutgoingMessages) contract.
/// </summary>
[Collection(OperationsTestCollection.Name)]
public class OperationsObligationsHandlerTests : IAsyncLifetime
{
    private readonly OperationsTestFixture _fixture;

    public OperationsObligationsHandlerTests(OperationsTestFixture fixture)
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

    // Fixed, strictly-increasing timestamps so the handler's strictly-older ordering guard is
    // deterministic (each lifecycle event is newer than the last).
    private static readonly DateTimeOffset EscalatedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OpenedAt    = EscalatedAt.AddMinutes(1);
    private static readonly DateTimeOffset ResolvedAt  = EscalatedAt.AddMinutes(2);
    private static readonly DateTimeOffset FulfilledAt = EscalatedAt.AddMinutes(3);

    // ───────────────────────────────────────────────────────────────────────────
    // Escalation-queue seed: DeadlineEscalated materialises a row in Escalated.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeadlineEscalated_SeedsRow_InEscalationQueue()
    {
        var obligationId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DeadlineEscalated(obligationId, listingId, EscalatedAt));

        var view = await Load(obligationId);
        view.ShouldNotBeNull();
        view.QueueState.ShouldBe(QueueState.Escalated);
        view.ListingId.ShouldBe(listingId);
        view.EscalatedAt.ShouldBe(EscalatedAt);
        view.DisputeId.ShouldBeNull();

        // Derived queue membership: in the escalation queue, not the open-dispute queue.
        (await EscalationQueueIds()).ShouldContain(obligationId);
        (await OpenDisputeQueueIds()).ShouldNotContain(obligationId);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // DisputeOpened advances Escalated → Disputed; row moves to the open-dispute queue.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisputeOpened_Advances_EscalatedToDisputed()
    {
        var obligationId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var disputeId    = Guid.CreateVersion7();
        var raisedBy     = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DeadlineEscalated(obligationId, listingId, EscalatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeOpened(obligationId, listingId, disputeId, raisedBy, "NonDelivery", OpenedAt));

        var view = await Load(obligationId);
        view.ShouldNotBeNull();
        view.QueueState.ShouldBe(QueueState.Disputed);
        view.DisputeId.ShouldBe(disputeId);
        view.RaisedBy.ShouldBe(raisedBy);
        view.DisputeReason.ShouldBe("NonDelivery");
        view.DisputeOpenedAt.ShouldBe(OpenedAt);

        (await OpenDisputeQueueIds()).ShouldContain(obligationId);
        (await EscalationQueueIds()).ShouldNotContain(obligationId);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // DisputeResolved { Extension } returns the row to the active set: out of BOTH
    // queues, NON-terminal (the narrative-008 Moment-2 backward transition).
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisputeResolved_Extension_ReturnsRowToActiveSet_NonTerminal()
    {
        var obligationId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var disputeId    = Guid.CreateVersion7();
        var participant  = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DeadlineEscalated(obligationId, listingId, EscalatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeOpened(obligationId, listingId, disputeId, Guid.CreateVersion7(), "ItemCondition", OpenedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeResolved(obligationId, listingId, disputeId, "Extension", ResolvedAt, participant));

        var view = await Load(obligationId);
        view.ShouldNotBeNull();
        view.QueueState.ShouldBe(QueueState.Active);
        view.ResolutionType.ShouldBe("Extension");
        view.ResolutionParticipantId.ShouldBe(participant);
        view.DisputeResolvedAt.ShouldBe(ResolvedAt);

        // Out of both queues, but not terminal — the obligation can still fulfil.
        (await EscalationQueueIds()).ShouldNotContain(obligationId);
        (await OpenDisputeQueueIds()).ShouldNotContain(obligationId);
        QueueStateRules.IsTerminal(view.QueueState).ShouldBeFalse();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // DisputeResolved { Refund | Closed } resolves terminally.
    // ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Refund")]
    [InlineData("Closed")]
    public async Task DisputeResolved_RefundOrClosed_ResolvesTerminally(string resolutionType)
    {
        var obligationId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var disputeId    = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DeadlineEscalated(obligationId, listingId, EscalatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeOpened(obligationId, listingId, disputeId, Guid.CreateVersion7(), "MissedDeadline", OpenedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeResolved(obligationId, listingId, disputeId, resolutionType, ResolvedAt));

        var view = await Load(obligationId);
        view.ShouldNotBeNull();
        view.QueueState.ShouldBe(QueueState.Resolved);
        view.ResolutionType.ShouldBe(resolutionType);
        QueueStateRules.IsTerminal(view.QueueState).ShouldBeTrue();
        (await OpenDisputeQueueIds()).ShouldNotContain(obligationId);

        // Terminal Resolved must not regress on a re-delivered earlier event.
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DeadlineEscalated(obligationId, listingId, EscalatedAt));
        var afterReplay = await Load(obligationId);
        afterReplay!.QueueState.ShouldBe(QueueState.Resolved);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Full escalation → dispute → Extension → fulfilment path (narrative-008
    // Moment 3): the extended obligation fulfils and drops from the active set. Proves
    // Active is non-terminal (fulfilment proceeds from it) and the terminal guard.
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtensionThenFulfilled_ReachesTerminalFulfilled_AndClearsQueues()
    {
        var obligationId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var disputeId    = Guid.CreateVersion7();
        var winnerId     = Guid.CreateVersion7();
        var sellerId     = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DeadlineEscalated(obligationId, listingId, EscalatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeOpened(obligationId, listingId, disputeId, Guid.CreateVersion7(), "NonDelivery", OpenedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeResolved(obligationId, listingId, disputeId, "Extension", ResolvedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ObligationFulfilled(obligationId, listingId, winnerId, sellerId, FulfilledAt));

        var view = await Load(obligationId);
        view.ShouldNotBeNull();
        view.QueueState.ShouldBe(QueueState.Fulfilled);
        view.WinnerId.ShouldBe(winnerId);
        view.SellerId.ShouldBe(sellerId);
        view.FulfilledAt.ShouldBe(FulfilledAt);
        // History preserved through the lifecycle (status-flip, not delete).
        view.ResolutionType.ShouldBe("Extension");
        view.RaisedBy.ShouldNotBeNull();

        (await EscalationQueueIds()).ShouldNotContain(obligationId);
        (await OpenDisputeQueueIds()).ShouldNotContain(obligationId);

        // Terminal-no-regress: a late re-delivered DisputeOpened (older timestamp) must not pull a
        // Fulfilled row back into the open-dispute queue nor rewrite its dispute fields.
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeOpened(obligationId, listingId, Guid.CreateVersion7(), Guid.CreateVersion7(), "ItemCondition", OpenedAt));
        var afterLate = await Load(obligationId);
        afterLate!.QueueState.ShouldBe(QueueState.Fulfilled);
        afterLate.DisputeReason.ShouldBe("NonDelivery"); // original dispute field not clobbered
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Idempotent re-delivery of the terminal event is a no-op (no duplicate row, no
    // state regress) — and the handler publishes nothing (pure-consumer contract).
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TerminalRedelivery_IsNoOp_AndHandlerPublishesNothing()
    {
        var obligationId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var winnerId     = Guid.CreateVersion7();
        var sellerId     = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ObligationFulfilled(obligationId, listingId, winnerId, sellerId, FulfilledAt));

        var tracked = await _fixture.Host.InvokeMessageAndWaitAsync(
            new ObligationFulfilled(obligationId, listingId, winnerId, sellerId, FulfilledAt));

        // Exactly one row; terminal state preserved.
        await using var session = _fixture.GetDocumentSession();
        var rows = await session.Query<OperationsObligationsView>()
            .Where(x => x.ObligationId == obligationId)
            .ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].QueueState.ShouldBe(QueueState.Fulfilled);

        // Pure consumer (ADR-014 Path A): no integration messages are published. Re-emitting any
        // consumed event would surface here.
        tracked.Sent.MessagesOf<DeadlineEscalated>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<DisputeOpened>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<DisputeResolved>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<ObligationFulfilled>().ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Unseen obligation: a happy-path ObligationFulfilled arriving for an
    // ObligationId this view never escalated/disputed seeds a terminal Fulfilled row
    // (the W006 §4 tolerant-upsert default; flagged in the M7-S4 retro).
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObligationFulfilled_ForUnseenObligation_SeedsTerminalRow()
    {
        var obligationId = Guid.CreateVersion7();
        var listingId    = Guid.CreateVersion7();
        var winnerId     = Guid.CreateVersion7();
        var sellerId     = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ObligationFulfilled(obligationId, listingId, winnerId, sellerId, FulfilledAt));

        var view = await Load(obligationId);
        view.ShouldNotBeNull();
        view.QueueState.ShouldBe(QueueState.Fulfilled);
        view.ListingId.ShouldBe(listingId);
        view.WinnerId.ShouldBe(winnerId);
        view.SellerId.ShouldBe(sellerId);
        view.FulfilledAt.ShouldBe(FulfilledAt);
        // Never escalated or disputed — those fields stay null.
        view.EscalatedAt.ShouldBeNull();
        view.DisputeId.ShouldBeNull();
        view.RaisedBy.ShouldBeNull();
        view.QueueState.ShouldNotBe(QueueState.None);
    }

    private async Task<OperationsObligationsView?> Load(Guid obligationId)
    {
        await using var session = _fixture.GetDocumentSession();
        return await session.LoadAsync<OperationsObligationsView>(obligationId);
    }

    // The two derived operator queues, expressed as query filters on QueueState (W006 §4).
    private async Task<IReadOnlyList<Guid>> EscalationQueueIds()
    {
        await using var session = _fixture.GetDocumentSession();
        return await session.Query<OperationsObligationsView>()
            .Where(x => x.QueueState == QueueState.Escalated)
            .Select(x => x.ObligationId)
            .ToListAsync();
    }

    private async Task<IReadOnlyList<Guid>> OpenDisputeQueueIds()
    {
        await using var session = _fixture.GetDocumentSession();
        return await session.Query<OperationsObligationsView>()
            .Where(x => x.QueueState == QueueState.Disputed)
            .Select(x => x.ObligationId)
            .ToListAsync();
    }
}
