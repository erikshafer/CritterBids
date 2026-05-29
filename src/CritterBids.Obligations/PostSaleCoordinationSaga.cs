using CritterBids.Contracts.Obligations;
using Marten;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.ScheduledMessageManagement;
using Wolverine.Persistence.Sagas;

namespace CritterBids.Obligations;

/// <summary>
/// Obligations BC's post-sale coordination workflow. Wolverine Saga per ADR-022 (confirming the
/// ADR-019 Settlement precedent). State is a single mutable document persisted via Marten under
/// the deterministic UUID v5 <c>ObligationId</c> (per
/// <see cref="ObligationsIdentityNamespaces.ObligationId"/>) — one obligation per sold listing.
///
/// <para><b>Lifecycle (M6-S3 happy path).</b> The saga starts via
/// <see cref="SettlementCompletedHandler"/> on inbound <c>SettlementCompleted</c> (M6-S2),
/// entering <see cref="ObligationStatus.AwaitingShipment"/> with a computed
/// <see cref="ShipByDeadline"/>, and scheduling — via <c>bus.ScheduleAsync()</c> — a
/// <see cref="SendShippingReminder"/> (at the reminder offset) and a
/// <see cref="SendDeadlineEscalation"/> (at the deadline). The seller provides tracking
/// (<see cref="ProvideTracking"/>), which cancels both pending timers, records
/// <see cref="TrackingInfoProvided"/>, schedules <see cref="ConfirmDelivery"/>, and advances to
/// <see cref="ObligationStatus.Shipped"/>. Delivery auto-confirms (<see cref="ConfirmDelivery"/>),
/// emitting <see cref="DeliveryConfirmed"/> then <see cref="ObligationFulfilled"/> and calling
/// <c>MarkCompleted()</c> at <see cref="ObligationStatus.Fulfilled"/>.</para>
///
/// <para><b>Scheduled-message cancellation.</b> Wolverine 5.x has no <c>bus.CancelScheduledAsync</c>
/// — cancellation is done through <see cref="IMessageStore"/>'s scheduled-message store, keyed on
/// the exact instant the message was scheduled for (mirrors
/// <c>AuctionClosingSaga.CancelPendingCloseAsync</c>). The scheduled instants are persisted on saga
/// state (<see cref="ReminderScheduledAt"/> / <see cref="EscalationScheduledAt"/>) so the
/// cancellation query can target them. See <see cref="CancelScheduledAsync"/>.</para>
///
/// <para><b>Escalation (M6-S4).</b> <see cref="Handle(SendDeadlineEscalation)"/> is a routable
/// no-op stub in M6-S3 so the timer is schedulable and cancellable; the <c>DeadlineEscalated</c>
/// emission, the <see cref="ObligationStatus.Escalated"/> transition, late-tracking recovery, and
/// the dispute sub-workflow land in M6-S4.</para>
///
/// <para><b>Numeric revisions</b> provide optimistic concurrency for saga writes, mirroring
/// <c>SettlementSaga</c> / <c>AuctionClosingSaga</c>. <see cref="Id"/> binds the saga document's
/// primary key to the deterministic <c>ObligationId</c>.</para>
/// </summary>
public sealed class PostSaleCoordinationSaga : Wolverine.Saga
{
    /// <summary>Deterministic <c>ObligationId</c> (UUID v5 from <c>ListingId</c>); the saga's stream + document key.</summary>
    public Guid Id { get; set; }

    /// <summary>The sold listing this obligation coordinates.</summary>
    public Guid ListingId { get; set; }

    /// <summary>The winning bidder owed delivery.</summary>
    public Guid WinnerId { get; set; }

    /// <summary>The seller responsible for shipping.</summary>
    public Guid SellerId { get; set; }

    /// <summary>The final sale price carried from <c>SettlementCompleted</c>.</summary>
    public decimal HammerPrice { get; set; }

    /// <summary>The seller's ship-by deadline (start time + the configured ship-by window).</summary>
    public DateTimeOffset ShipByDeadline { get; set; }

    /// <summary>Current lifecycle state; <see cref="ObligationStatus.AwaitingShipment"/> at start.</summary>
    public ObligationStatus Status { get; set; }

    /// <summary>Instant the <see cref="SendShippingReminder"/> timer was scheduled for; the cancellation key.</summary>
    public DateTimeOffset ReminderScheduledAt { get; set; }

    /// <summary>Instant the <see cref="SendDeadlineEscalation"/> timer was scheduled for; the cancellation key.</summary>
    public DateTimeOffset EscalationScheduledAt { get; set; }

    /// <summary>Instant the <see cref="ConfirmDelivery"/> timer was scheduled for (after tracking); null before.</summary>
    public DateTimeOffset? ConfirmScheduledAt { get; set; }

    /// <summary>The carrier tracking string the seller entered; null before tracking is provided.</summary>
    public string? TrackingNumber { get; set; }

    /// <summary>The open dispute's id while <see cref="ObligationStatus.Disputed"/>; null otherwise. Validates the matching <see cref="ResolveDispute"/>.</summary>
    public Guid? DisputeId { get; set; }

    // ─── Reminder ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Fire the single shipping reminder. Appends <see cref="ShippingReminderSent"/> to the
    /// obligation stream while the obligation is still awaiting shipment. No-op guard: once
    /// tracking has been provided (state advanced past <see cref="ObligationStatus.AwaitingShipment"/>),
    /// a reminder scheduled before tracking but delivered after it is dropped — the seller already
    /// shipped (spec "stale reminder after tracking" scenario / opsx 4.1).
    /// </summary>
    public void Handle(
        [SagaIdentityFrom(nameof(SendShippingReminder.ObligationId))] SendShippingReminder message,
        IDocumentSession session)
    {
        if (Status != ObligationStatus.AwaitingShipment) return;

        session.Events.Append(Id, new ShippingReminderSent(Id, SellerId, DateTimeOffset.UtcNow));
    }

    // Wolverine's named-method convention — invoked instead of throwing UnknownSagaException when a
    // scheduled SendShippingReminder arrives after the saga was already completed (MarkCompleted
    // deleted the document) and the pending timer slipped through cancellation. Mirrors
    // AuctionClosingSaga.NotFound(CloseAuction).
    public static OutgoingMessages NotFound(SendShippingReminder message) => new();

    // ─── Deadline escalation (M6-S4) ─────────────────────────────────────────────

    /// <summary>
    /// The ship-by deadline elapsed with no tracking. Appends and emits
    /// <see cref="DeadlineEscalated"/> (the fifth Obligations integration event — ADR 005 additive)
    /// and advances to the non-terminal <see cref="ObligationStatus.Escalated"/> state. The saga
    /// stays alive — a later <see cref="ProvideTracking"/> still recovers the happy path (opsx 6.1,
    /// narrative 007). No-op guard: if state has already advanced past awaiting-shipment (tracking
    /// arrived first and the timer was cancelled, but a redelivery slipped through), the escalation
    /// is dropped — defense-in-depth mirroring the stale-reminder guard. No <c>MarkCompleted()</c>.
    /// </summary>
    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(SendDeadlineEscalation.ObligationId))] SendDeadlineEscalation message,
        IDocumentSession session)
    {
        if (Status != ObligationStatus.AwaitingShipment) return new OutgoingMessages();

        var escalatedAt = DateTimeOffset.UtcNow;
        var escalated = new DeadlineEscalated(Id, ListingId, escalatedAt);

        // Append to the obligation stream (drives the views to Escalated) and emit on the bus for
        // cross-BC operator alerting (Relay / Operations) via the relay-obligations-events route.
        session.Events.Append(Id, escalated);

        Status = ObligationStatus.Escalated;

        return new OutgoingMessages { escalated };
    }

    public static OutgoingMessages NotFound(SendDeadlineEscalation message) => new();

    // ─── Tracking ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The seller provides shipping tracking. Cancels the pending reminder + escalation timers,
    /// appends and emits <see cref="TrackingInfoProvided"/>, schedules <see cref="ConfirmDelivery"/>
    /// at the auto-confirm offset, and advances to <see cref="ObligationStatus.Shipped"/>
    /// (opsx 4.2 / 4.3). State-tolerant (opsx 6.2, narrative 007): accepted from both
    /// <see cref="ObligationStatus.AwaitingShipment"/> and <see cref="ObligationStatus.Escalated"/>,
    /// so a seller who ships after a missed-deadline escalation rejoins the happy path. Idempotent:
    /// a duplicate <see cref="ProvideTracking"/> after the obligation has already shipped (or while
    /// disputed/fulfilled) is a no-op.
    /// </summary>
    public async Task<OutgoingMessages> Handle(
        [SagaIdentityFrom(nameof(ProvideTracking.ObligationId))] ProvideTracking message,
        IDocumentSession session,
        IMessageBus bus,
        IMessageStore messageStore,
        IOptions<ObligationsOptions> options,
        CancellationToken cancellationToken)
    {
        if (Status is not (ObligationStatus.AwaitingShipment or ObligationStatus.Escalated))
            return new OutgoingMessages();

        // Cancel both pending timers. Keyed on the exact scheduled instant + message type — the
        // working Wolverine 5.x cancellation path (there is no bus.CancelScheduledAsync). Mirrors
        // AuctionClosingSaga.CancelPendingCloseAsync.
        await CancelScheduledAsync(messageStore, ReminderScheduledAt, typeof(SendShippingReminder).FullName, cancellationToken);
        await CancelScheduledAsync(messageStore, EscalationScheduledAt, typeof(SendDeadlineEscalation).FullName, cancellationToken);

        var providedAt = DateTimeOffset.UtcNow;
        var tracking = new TrackingInfoProvided(Id, ListingId, SellerId, message.TrackingNumber, providedAt);

        // Append to the obligation stream (drives ObligationStatusView) and emit on the bus for
        // cross-BC consumers (Relay / Operations) via the relay-obligations-events route.
        session.Events.Append(Id, tracking);

        var confirmAt = providedAt + options.Value.Active.AutoConfirmWindow;
        await bus.ScheduleAsync(new ConfirmDelivery(Id), confirmAt);

        TrackingNumber = message.TrackingNumber;
        ConfirmScheduledAt = confirmAt;
        Status = ObligationStatus.Shipped;

        return new OutgoingMessages { tracking };
    }

    public static OutgoingMessages NotFound(ProvideTracking message) => new();

    // ─── Auto-confirm / fulfillment ───────────────────────────────────────────────

    /// <summary>
    /// Delivery auto-confirms after the configured window. Appends <see cref="DeliveryConfirmed"/>,
    /// emits <see cref="ObligationFulfilled"/>, advances to <see cref="ObligationStatus.Fulfilled"/>,
    /// and calls <c>MarkCompleted()</c> — the happy-path terminal transition (opsx 5.1 / 5.2).
    /// No-op guard: a duplicate <see cref="ConfirmDelivery"/> after fulfillment does nothing.
    /// </summary>
    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(ConfirmDelivery.ObligationId))] ConfirmDelivery message,
        IDocumentSession session)
    {
        if (Status != ObligationStatus.Shipped) return new OutgoingMessages();

        var confirmedAt = DateTimeOffset.UtcNow;
        session.Events.Append(Id, new DeliveryConfirmed(Id, confirmedAt));

        Status = ObligationStatus.Fulfilled;
        MarkCompleted();

        return new OutgoingMessages
        {
            new ObligationFulfilled(Id, ListingId, WinnerId, SellerId, confirmedAt),
        };
    }

    public static OutgoingMessages NotFound(ConfirmDelivery message) => new();

    // ─── Dispute sub-workflow (M6-S4) ─────────────────────────────────────────────

    /// <summary>
    /// A dispute is raised against the obligation (opsx 7.1, narrative 008). Allowed from
    /// <see cref="ObligationStatus.AwaitingShipment"/>, <see cref="ObligationStatus.Escalated"/>, or
    /// <see cref="ObligationStatus.Shipped"/>. Cancels whatever timer is pending in the current
    /// state so the lifecycle clock freezes while the dispute is open, appends and emits the frozen
    /// <see cref="DisputeOpened"/> contract, records the <see cref="DisputeId"/>, and advances to
    /// <see cref="ObligationStatus.Disputed"/> — without terminating.
    ///
    /// <para>Idempotency: an empty <see cref="OpenDispute.DisputeId"/> is rejected; a re-delivery
    /// while a dispute is already open is a no-op (MVP allows one open dispute per obligation, so a
    /// different id does not overwrite the open one).</para>
    /// </summary>
    public async Task<OutgoingMessages> Handle(
        [SagaIdentityFrom(nameof(OpenDispute.ObligationId))] OpenDispute message,
        IDocumentSession session,
        IMessageStore messageStore,
        CancellationToken cancellationToken)
    {
        if (message.DisputeId == Guid.Empty) return new OutgoingMessages();

        // One open dispute per obligation: a redelivery (same id) or a second dispute (different id)
        // while already disputed is dropped without overwriting the open dispute.
        if (Status == ObligationStatus.Disputed) return new OutgoingMessages();

        if (Status is not (ObligationStatus.AwaitingShipment
            or ObligationStatus.Escalated
            or ObligationStatus.Shipped))
        {
            return new OutgoingMessages();
        }

        // Freeze the lifecycle clock by cancelling the timer pending in the current state. From
        // AwaitingShipment the reminder + escalation are pending; from Shipped the auto-confirm is
        // pending (cancelling it prevents the obligation auto-fulfilling mid-dispute); from
        // Escalated nothing is pending (the escalation already fired).
        switch (Status)
        {
            case ObligationStatus.AwaitingShipment:
                await CancelScheduledAsync(messageStore, ReminderScheduledAt, typeof(SendShippingReminder).FullName, cancellationToken);
                await CancelScheduledAsync(messageStore, EscalationScheduledAt, typeof(SendDeadlineEscalation).FullName, cancellationToken);
                break;
            case ObligationStatus.Shipped when ConfirmScheduledAt is { } confirmAt:
                await CancelScheduledAsync(messageStore, confirmAt, typeof(ConfirmDelivery).FullName, cancellationToken);
                break;
        }

        var openedAt = DateTimeOffset.UtcNow;
        var opened = new DisputeOpened(Id, ListingId, message.DisputeId, message.RaisedBy, message.Reason, openedAt);
        session.Events.Append(Id, opened);

        DisputeId = message.DisputeId;
        Status = ObligationStatus.Disputed;

        return new OutgoingMessages { opened };
    }

    public static OutgoingMessages NotFound(OpenDispute message) => new();

    /// <summary>
    /// An open dispute is resolved by Operations (opsx 7.2–7.4, narrative 008). Validated against
    /// the open <see cref="DisputeId"/> so a stale or mismatched resolution is a no-op. Appends and
    /// emits the frozen <see cref="DisputeResolved"/> contract, then branches on
    /// <see cref="ResolveDispute.ResolutionType"/>:
    /// <list type="bullet">
    /// <item><c>Extension</c> — the one non-terminal path: recomputes a fresh
    /// <see cref="ShipByDeadline"/>, reschedules the reminder + escalation timers (the S3 start-time
    /// scheduling path), appends the internal <see cref="ShipByDeadlineExtended"/> (keeping the view
    /// rebuild-correct on the new deadline), and returns to
    /// <see cref="ObligationStatus.AwaitingShipment"/> <b>without</b> <c>MarkCompleted()</c>.</item>
    /// <item><c>Refund</c> / <c>Closed</c> (and any unrecognized value, defensively) — terminal:
    /// calls <c>MarkCompleted()</c>.</item>
    /// </list>
    /// </summary>
    public async Task<OutgoingMessages> Handle(
        [SagaIdentityFrom(nameof(ResolveDispute.ObligationId))] ResolveDispute message,
        IDocumentSession session,
        IMessageBus bus,
        IOptions<ObligationsOptions> options,
        CancellationToken cancellationToken)
    {
        if (Status != ObligationStatus.Disputed || message.DisputeId != DisputeId)
            return new OutgoingMessages();

        var resolvedAt = DateTimeOffset.UtcNow;
        var resolved = new DisputeResolved(Id, ListingId, message.DisputeId, message.ResolutionType, resolvedAt);
        session.Events.Append(Id, resolved);

        if (message.ResolutionType == DisputeResolutions.Extension)
        {
            var durations = options.Value.Active;
            var newDeadline = resolvedAt + durations.ShipByDeadline;
            var newReminderAt = resolvedAt + durations.ReminderOffset;

            // Internal stream event carrying the recomputed deadline so the projection replays the
            // post-extension ShipByDeadline (the frozen DisputeResolved contract cannot carry it).
            session.Events.Append(Id, new ShipByDeadlineExtended(Id, newDeadline, resolvedAt));

            // Reschedule the cancellable reminder + escalation timers (reuses the S3 start path).
            await bus.ScheduleAsync(new SendShippingReminder(Id), newReminderAt);
            await bus.ScheduleAsync(new SendDeadlineEscalation(Id), newDeadline);

            ShipByDeadline = newDeadline;
            ReminderScheduledAt = newReminderAt;
            EscalationScheduledAt = newDeadline;
            DisputeId = null;
            Status = ObligationStatus.AwaitingShipment;

            return new OutgoingMessages { resolved };
        }

        // Refund / Closed (and any unrecognized resolution, defensively) are terminal.
        DisputeId = null;
        MarkCompleted();

        return new OutgoingMessages { resolved };
    }

    public static OutgoingMessages NotFound(ResolveDispute message) => new();

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancels a pending scheduled message keyed on the exact instant it was scheduled for plus its
    /// message type. A narrow ±100ms window brackets the single pending message for this obligation;
    /// wider windows risk cross-obligation cancels if two obligations share a scheduled instant
    /// (same limitation <c>AuctionClosingSaga</c> documents — acceptable for demo/test where
    /// instants are unique).
    /// </summary>
    internal static async Task CancelScheduledAsync(
        IMessageStore messageStore,
        DateTimeOffset at,
        string? messageType,
        CancellationToken cancellationToken)
    {
        var query = new ScheduledMessageQuery
        {
            ExecutionTimeFrom = at.AddMilliseconds(-100),
            ExecutionTimeTo = at.AddMilliseconds(100),
            MessageType = messageType,
        };

        await messageStore.ScheduledMessages.CancelAsync(query, cancellationToken);
    }
}
