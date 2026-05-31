using CritterBids.Contracts.Obligations;
using Marten;

namespace CritterBids.Operations;

/// <summary>
/// Operations BC's Obligations-family consumer — the single ADR-014 Path A, Sub-Option A sibling
/// handler that folds the Obligations BC's four integration events into
/// <see cref="OperationsObligationsView"/>. One sibling class per source BC; Obligations is the only
/// source for the obligations view, so the four <c>Handle</c> overloads (one per event) live
/// together here. The handler returns <see cref="Task"/> and writes only via the injected Marten
/// session — Operations is a pure consumer, so there are <b>no</b> <c>OutgoingMessages</c> and
/// <b>no</b> <c>IMessageBus</c> (it publishes nothing).
///
/// <para><b>Tolerant upsert.</b> Each overload loads-or-constructs the row by
/// <see cref="OperationsObligationsView.ObligationId"/>, mutates via record <c>with</c>, and stores
/// — the lived shape from <see cref="SettlementQueueHandler"/>/<see cref="LotBoardSellingHandler"/>.
/// Because every event tolerantly seeds the row, an obligation that fulfils on the happy path
/// without ever escalating or disputing (or a stray terminal <c>DisputeResolved</c>) materialises a
/// terminal row with the fulfilment/resolution fields set and the rest null — the standard Path A
/// tolerant-upsert default (flagged in the M7-S4 retro as the chosen unseen-obligation behaviour).</para>
///
/// <para><b>Guard — ignore the event, do not just freeze the state.</b> Before mutating, each
/// overload computes whether the row is <see cref="QueueStateRules.IsTerminal"/> (absorbing) or the
/// incoming event is strictly older than the row's latest-known timestamp (a stale re-delivery).
/// In either case it returns without writing — so neither the <see cref="QueueState"/> nor any
/// payload field is touched, making re-delivery and out-of-order arrival true no-ops (and so a late
/// <c>DeadlineEscalated</c> can never regress <see cref="QueueState.Disputed"/> →
/// <see cref="QueueState.Escalated"/>, nor rewrite a dispute field). The sanctioned
/// <c>Extension</c> backward move (<see cref="QueueState.Disputed"/> → <see cref="QueueState.Active"/>)
/// is never older than the <c>DisputeOpened</c> it follows, so the ordering guard does not block it,
/// and <see cref="QueueState.Active"/> is non-terminal so the terminal guard does not block it.</para>
///
/// <para><b>Latest-known timestamp is derived</b> from the populated <c>…At</c> fields (W006 §4 is
/// implemented exactly — there is no separate <c>LastUpdatedAt</c> column).
/// <see cref="OperationsObligationsView.ListingId"/> is set-once via the
/// <see cref="System.Guid.Empty"/> sentinel.</para>
/// </summary>
public static class OperationsObligationsHandler
{
    public static async Task Handle(
        DeadlineEscalated message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ObligationId, cancellationToken);
        if (IsIgnored(view, message.EscalatedAt))
            return;

        session.Store(view with
        {
            ListingId   = SetOnce(view.ListingId, message.ListingId),
            EscalatedAt = message.EscalatedAt,
            QueueState  = QueueState.Escalated,
        });
    }

    public static async Task Handle(
        DisputeOpened message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ObligationId, cancellationToken);
        if (IsIgnored(view, message.OpenedAt))
            return;

        session.Store(view with
        {
            ListingId       = SetOnce(view.ListingId, message.ListingId),
            DisputeId       = message.DisputeId,
            RaisedBy        = message.RaisedBy,
            DisputeReason   = message.Reason,
            DisputeOpenedAt = message.OpenedAt,
            QueueState      = QueueState.Disputed,
        });
    }

    public static async Task Handle(
        DisputeResolved message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ObligationId, cancellationToken);
        if (IsIgnored(view, message.ResolvedAt))
            return;

        // Extension is the one non-terminal resolution — it returns the obligation to the active set
        // (out of both queues, narrative-008 Moment 2). Refund/Closed resolve terminally.
        var resolvedState = message.ResolutionType == "Extension"
            ? QueueState.Active
            : QueueState.Resolved;

        session.Store(view with
        {
            ListingId               = SetOnce(view.ListingId, message.ListingId),
            DisputeId               = message.DisputeId,
            ResolutionType          = message.ResolutionType,
            ResolutionParticipantId = message.ParticipantId,
            DisputeResolvedAt       = message.ResolvedAt,
            QueueState              = resolvedState,
        });
    }

    public static async Task Handle(
        ObligationFulfilled message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ObligationId, cancellationToken);
        if (IsIgnored(view, message.FulfilledAt))
            return;

        session.Store(view with
        {
            ListingId   = SetOnce(view.ListingId, message.ListingId),
            WinnerId    = message.WinnerId,
            SellerId    = message.SellerId,
            FulfilledAt = message.FulfilledAt,
            QueueState  = QueueState.Fulfilled,
        });
    }

    private static async Task<OperationsObligationsView> LoadOrCreate(
        IDocumentSession session,
        Guid obligationId,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<OperationsObligationsView>(obligationId, cancellationToken);
        return existing ?? new OperationsObligationsView { ObligationId = obligationId };
    }

    /// <summary>
    /// True when the event must be ignored entirely (no state change, no field write): the row is
    /// already terminal (absorbing), or the event is <i>strictly older</i> than the row's
    /// latest-known timestamp (a stale out-of-order re-delivery). Strictly-older — not
    /// older-or-equal — so a legitimate same-instant forward transition is never dropped; an exact
    /// re-delivery of a non-terminal event is harmless (it rewrites identical data).
    /// </summary>
    private static bool IsIgnored(OperationsObligationsView view, DateTimeOffset incoming) =>
        QueueStateRules.IsTerminal(view.QueueState) || incoming < LatestKnown(view);

    /// <summary>
    /// The most recent timestamp the row has seen, derived from the populated <c>…At</c> fields so
    /// no separate column is stored (W006 §4 field set, exactly). A brand-new row has none set, so
    /// the first event is never stale.
    /// </summary>
    private static DateTimeOffset LatestKnown(OperationsObligationsView view)
    {
        var stamps = new[] { view.EscalatedAt, view.DisputeOpenedAt, view.DisputeResolvedAt, view.FulfilledAt };
        return stamps
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();
    }

    private static Guid SetOnce(Guid current, Guid incoming) =>
        current == Guid.Empty ? incoming : current;
}
