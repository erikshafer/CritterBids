using CritterBids.Contracts.Obligations;
using CritterBids.Contracts.Settlement;
using CritterBids.Obligations.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.ScheduledMessageManagement;
using Wolverine.Tracking;

namespace CritterBids.Obligations.Tests;

/// <summary>
/// Integration tests for the <see cref="PostSaleCoordinationSaga"/> failure paths (M6-S4, opsx
/// scenarios 9.4 + 9.5): missed-deadline escalation, late-tracking recovery, and the dispute
/// sub-workflow's three resolutions. Runs against the demo-duration fixture so no scheduled timer
/// fires on its own — every transition is driven deterministically via <c>InvokeMessageAndWaitAsync</c>
/// (the AuctionClosingSagaTests / S3 precedent), with the would-be timer firings simulated by direct
/// invocation of the scheduled command.
///
/// <para>The relay-obligations-events publish routes live in Program.cs's RabbitMQ block, which the
/// fixture skips (no broker), so the saga's OutgoingMessages-emitted integration events
/// (<see cref="DeadlineEscalated"/> / <see cref="DisputeOpened"/> / <see cref="DisputeResolved"/>)
/// land in <c>tracked.NoRoutes</c> — the established CritterBids stance for cross-BC events whose
/// consumer has not shipped.</para>
/// </summary>
[Collection(ObligationsLifecycleTestCollection.Name)]
public class ObligationsFailurePathsTests : IAsyncLifetime
{
    private readonly ObligationsLifecycleTestFixture _fixture;

    public ObligationsFailurePathsTests(ObligationsLifecycleTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static SettlementCompleted NewSettlementCompleted(Guid listingId, Guid winnerId, Guid sellerId) =>
        new(
            SettlementId: Guid.CreateVersion7(),
            ListingId: listingId,
            WinnerId: winnerId,
            SellerId: sellerId,
            HammerPrice: 85m,
            FeeAmount: 8.50m,
            SellerPayout: 76.50m,
            CompletedAt: DateTimeOffset.UtcNow);

    private async Task<IReadOnlyList<ScheduledMessageSummary>> QueryScheduledOfTypeAsync(Type messageType)
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var result = await store.ScheduledMessages.QueryAsync(
            new ScheduledMessageQuery { PageSize = 1000 },
            CancellationToken.None);
        return result.Messages
            .Where(m => m.MessageType != null && m.MessageType.Contains(messageType.Name))
            .ToList();
    }

    // Scheduled messages live in Wolverine's envelope store, which CleanAllMartenDataAsync does not
    // truncate, so counts accumulate across tests sharing this collection's host. Assertions use
    // before/after deltas rather than absolute counts to stay isolation-independent.
    private async Task<int> CountScheduledOfTypeAsync(Type messageType) =>
        (await QueryScheduledOfTypeAsync(messageType)).Count;

    // ───────────────────────────────────────────────────────────────────────────
    // 9.4 — Missed deadline escalates (non-terminal), then late tracking recovers
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MissedDeadline_Escalates_ThenLateTracking_RecoversToFulfilled()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);

        // ── Start the saga (awaiting shipment, reminder + escalation pending) ──
        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));

        // ── The ship-by deadline elapses with no tracking (simulated timer firing) ──
        var escalationTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new SendDeadlineEscalation(obligationId));

        // DeadlineEscalated is published as a cascading integration message (no route in test → NoRoutes).
        var escalated = escalationTracked.NoRoutes.MessagesOf<DeadlineEscalated>().ShouldHaveSingleItem();
        escalated.ObligationId.ShouldBe(obligationId);
        escalated.ListingId.ShouldBe(listingId);

        await using (var session = _fixture.GetDocumentSession())
        {
            // Appended to the stream and non-terminal — the saga is still alive in Escalated.
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga.ShouldNotBeNull();
            saga.Status.ShouldBe(ObligationStatus.Escalated);

            var events = await session.Events.FetchStreamAsync(obligationId);
            events.Select(e => e.Data).OfType<DeadlineEscalated>().ShouldHaveSingleItem();

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Escalated);
            view.EscalatedAt.ShouldNotBeNull();
        }

        // ── Late tracking recovers the happy path from the Escalated state ──
        var confirmBefore = await CountScheduledOfTypeAsync(typeof(ConfirmDelivery));
        var trackingTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ProvideTracking(obligationId, "1Z999AA10123456784"));

        trackingTracked.NoRoutes.MessagesOf<TrackingInfoProvided>().ShouldHaveSingleItem();
        (await CountScheduledOfTypeAsync(typeof(ConfirmDelivery))).ShouldBe(confirmBefore + 1);

        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga!.Status.ShouldBe(ObligationStatus.Shipped);

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Shipped);
            view.TrackingNumber.ShouldBe("1Z999AA10123456784");
        }

        // ── Delivery auto-confirms — the recovered obligation fulfills and completes ──
        var confirmTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ConfirmDelivery(obligationId));

        confirmTracked.NoRoutes.MessagesOf<ObligationFulfilled>().ShouldHaveSingleItem();

        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga.ShouldBeNull();

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Fulfilled);
            view.FulfilledAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Escalation_AfterTracking_IsNoOp()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);

        // Start, then provide tracking — the obligation advances to Shipped.
        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ProvideTracking(obligationId, "1Z999AA10123456784"));

        // A deadline escalation that slipped through cancellation fires after tracking. The no-op
        // guard (Status != AwaitingShipment) drops it — nothing appended, no state change.
        var escalationTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new SendDeadlineEscalation(obligationId));

        escalationTracked.NoRoutes.MessagesOf<DeadlineEscalated>().ShouldBeEmpty();

        await using var session = _fixture.GetDocumentSession();
        var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
        saga!.Status.ShouldBe(ObligationStatus.Shipped);

        var events = await session.Events.FetchStreamAsync(obligationId);
        events.Select(e => e.Data).OfType<DeadlineEscalated>().ShouldBeEmpty();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 9.5 — Dispute resolutions: Refund / Closed terminate; Extension reschedules
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispute_RefundResolution_Terminates()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);
        var disputeId = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));

        // ── Winner opens a non-delivery dispute — cancels timers, advances to Disputed ──
        var reminderBefore   = await CountScheduledOfTypeAsync(typeof(SendShippingReminder));
        var escalationBefore = await CountScheduledOfTypeAsync(typeof(SendDeadlineEscalation));
        var openTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new OpenDispute(obligationId, disputeId, winnerId, "NonDelivery"));

        var opened = openTracked.NoRoutes.MessagesOf<DisputeOpened>().ShouldHaveSingleItem();
        opened.DisputeId.ShouldBe(disputeId);
        opened.Reason.ShouldBe("NonDelivery");

        (await CountScheduledOfTypeAsync(typeof(SendShippingReminder))).ShouldBe(reminderBefore - 1);
        (await CountScheduledOfTypeAsync(typeof(SendDeadlineEscalation))).ShouldBe(escalationBefore - 1);

        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga!.Status.ShouldBe(ObligationStatus.Disputed);
            saga.DisputeId.ShouldBe(disputeId);

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Disputed);
            view.DisputeReason.ShouldBe("NonDelivery");
        }

        // ── Operations resolves with Refund — terminal (MarkCompleted) ──
        var resolveTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ResolveDispute(obligationId, disputeId, "Refund"));

        var resolved = resolveTracked.NoRoutes.MessagesOf<DisputeResolved>().ShouldHaveSingleItem();
        resolved.DisputeId.ShouldBe(disputeId);
        resolved.ResolutionType.ShouldBe("Refund");

        await using (var session = _fixture.GetDocumentSession())
        {
            // MarkCompleted() deleted the saga document.
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga.ShouldBeNull();

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.DisputeResolution.ShouldBe("Refund");
            view.DisputeResolvedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Dispute_ClosedResolution_Terminates()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);
        var disputeId = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new OpenDispute(obligationId, disputeId, winnerId, "ItemCondition"));

        var resolveTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ResolveDispute(obligationId, disputeId, "Closed"));

        var resolved = resolveTracked.NoRoutes.MessagesOf<DisputeResolved>().ShouldHaveSingleItem();
        resolved.ResolutionType.ShouldBe("Closed");

        await using var session = _fixture.GetDocumentSession();
        var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
        saga.ShouldBeNull();
    }

    [Fact]
    public async Task Dispute_ExtensionResolution_RecomputesDeadline_AndContinues()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);
        var disputeId = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));

        DateTimeOffset originalDeadline;
        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            originalDeadline = saga!.ShipByDeadline;
        }

        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new OpenDispute(obligationId, disputeId, winnerId, "MissedDeadline"));

        // ── Operations resolves with Extension — non-terminal: reschedules and continues ──
        var reminderAfterOpen   = await CountScheduledOfTypeAsync(typeof(SendShippingReminder));
        var escalationAfterOpen = await CountScheduledOfTypeAsync(typeof(SendDeadlineEscalation));
        var resolveTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ResolveDispute(obligationId, disputeId, "Extension"));

        var resolved = resolveTracked.NoRoutes.MessagesOf<DisputeResolved>().ShouldHaveSingleItem();
        resolved.ResolutionType.ShouldBe("Extension");

        // Fresh reminder + escalation timers were rescheduled.
        (await CountScheduledOfTypeAsync(typeof(SendShippingReminder))).ShouldBe(reminderAfterOpen + 1);
        (await CountScheduledOfTypeAsync(typeof(SendDeadlineEscalation))).ShouldBe(escalationAfterOpen + 1);

        await using (var session = _fixture.GetDocumentSession())
        {
            // The saga is alive — not completed — and back to awaiting shipment with a new deadline.
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga.ShouldNotBeNull();
            saga.Status.ShouldBe(ObligationStatus.AwaitingShipment);
            saga.DisputeId.ShouldBeNull();
            saga.ShipByDeadline.ShouldBeGreaterThan(originalDeadline);

            var events = await session.Events.FetchStreamAsync(obligationId);
            events.Select(e => e.Data).OfType<ShipByDeadlineExtended>().ShouldHaveSingleItem();

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.AwaitingShipment);
            view.DisputeResolution.ShouldBe("Extension");
            view.ShipByDeadline.ShouldBeGreaterThan(originalDeadline);
        }

        // ── The extended obligation still recovers: tracking → fulfilled ──
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ProvideTracking(obligationId, "1Z999AA10123456784"));
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ConfirmDelivery(obligationId));

        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga.ShouldBeNull();

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Fulfilled);
        }
    }

    [Fact]
    public async Task ResolveDispute_WithMismatchedDisputeId_IsNoOp()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);
        var disputeId = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new OpenDispute(obligationId, disputeId, winnerId, "NonDelivery"));

        // A resolution carrying a different dispute id must not resolve the open dispute.
        var resolveTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ResolveDispute(obligationId, Guid.CreateVersion7(), "Refund"));

        resolveTracked.NoRoutes.MessagesOf<DisputeResolved>().ShouldBeEmpty();

        await using var session = _fixture.GetDocumentSession();
        var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
        saga!.Status.ShouldBe(ObligationStatus.Disputed);
        saga.DisputeId.ShouldBe(disputeId);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 8.2 — Awaiting-delivery todo-list projection
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AwaitingDeliveryView_AppearsOnTracking_AndSelfRemovesOnDelivery()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);

        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));

        // No row before tracking.
        await using (var session = _fixture.GetDocumentSession())
        {
            (await session.LoadAsync<ObligationsAwaitingDelivery>(obligationId)).ShouldBeNull();
        }

        // Tracking creates the row.
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ProvideTracking(obligationId, "1Z999AA10123456784"));

        await using (var session = _fixture.GetDocumentSession())
        {
            var row = await session.LoadAsync<ObligationsAwaitingDelivery>(obligationId);
            row.ShouldNotBeNull();
            row.ListingId.ShouldBe(listingId);
            row.SellerId.ShouldBe(sellerId);
            row.TrackingNumber.ShouldBe("1Z999AA10123456784");
        }

        // Delivery confirmation self-removes the row.
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ConfirmDelivery(obligationId));

        await using (var session = _fixture.GetDocumentSession())
        {
            (await session.LoadAsync<ObligationsAwaitingDelivery>(obligationId)).ShouldBeNull();
        }
    }
}
