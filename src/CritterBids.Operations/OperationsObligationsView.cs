namespace CritterBids.Operations;

/// <summary>
/// Operations BC's obligations read model — the narrative-008 operator surface carrying <b>two
/// derived queues</b>: the <b>escalation queue</b> (rows where <see cref="QueueState"/> is
/// <see cref="CritterBids.Operations.QueueState.Escalated"/>) and the <b>open-dispute queue</b>
/// (rows where <see cref="QueueState"/> is <see cref="CritterBids.Operations.QueueState.Disputed"/>).
/// Folded from a single source family (W006 §4): the Obligations events
/// <c>DeadlineEscalated</c>, <c>DisputeOpened</c>, <c>DisputeResolved</c>, <c>ObligationFulfilled</c>.
/// Operations is a <b>pure consumer</b> (ADR-014 Path A, Sub-Option A): the one source BC has the
/// one sibling handler (<see cref="OperationsObligationsHandler"/>) upserting this single
/// <see cref="ObligationId"/>-keyed document. It appends to no local stream and publishes nothing.
///
/// <para><b>One view, two queues.</b> This is <i>one</i> upsert row per obligation with a
/// <see cref="QueueState"/> discriminator — not two physical views and not a per-queue row split.
/// Queue membership is a query filter on <see cref="QueueState"/> (W006 §4); a terminal
/// resolve/fulfil flips the state out of both queues but <b>preserves</b> the row for operator
/// history (status-flip, not document delete).</para>
///
/// <para><b>Non-monotone lifecycle + guard.</b> <see cref="QueueState"/> advances
/// <c>Escalated → Disputed → (Active-via-Extension or Resolved-via-Refund/Closed) → Fulfilled</c>.
/// <c>Extension</c> is a sanctioned backward move to
/// <see cref="CritterBids.Operations.QueueState.Active"/> (out of both queues, non-terminal);
/// only <see cref="CritterBids.Operations.QueueState.Fulfilled"/> and
/// <see cref="CritterBids.Operations.QueueState.Resolved"/> are terminal/absorbing. See
/// <see cref="QueueStateRules"/> for the terminal-absorbing + strictly-older-is-stale guard the
/// handler applies as an ignore-the-event decision.</para>
///
/// <para><b>Set-once + cross-view fields.</b> <see cref="ListingId"/> is the join key carried by all
/// four events; it is set-once via the <see cref="System.Guid.Empty"/> sentinel. The dispute card's
/// listing <c>Title</c> is on <b>no</b> obligations event (W006 §4 cross-view finding) — the
/// dashboard joins <see cref="ListingId"/> → lot-board <c>Title</c> at render time (M8); this view
/// stores only <see cref="ListingId"/>. On the open-dispute card the disputing party is
/// <see cref="RaisedBy"/> (from <c>DisputeOpened</c>), not <see cref="WinnerId"/> —
/// <see cref="WinnerId"/>/<see cref="SellerId"/> arrive only with <c>ObligationFulfilled</c>.</para>
///
/// <para><b>Derived ordering.</b> The handler derives the row's latest-known timestamp from the
/// populated <c>…At</c> fields (max of <see cref="EscalatedAt"/>/<see cref="DisputeOpenedAt"/>/
/// <see cref="DisputeResolvedAt"/>/<see cref="FulfilledAt"/>) rather than persisting a separate
/// <c>LastUpdatedAt</c> column — the W006 §4 field set is implemented exactly, no more, no fewer.</para>
///
/// <para><b>Marten Id convention.</b> <see cref="ObligationId"/> doubles as the Marten document key
/// via the <see cref="Id"/> expression-bodied alias — the natural-key-as-id idiom shared with
/// <see cref="SettlementQueueView"/>/<see cref="LotBoardView"/>; no <c>.Identity()</c> override is
/// needed in the module.</para>
///
/// <para><b>Confidentiality.</b> This is a staff-only board; no bidder-facing surface reads it (auth
/// gating lands in M7-S6).</para>
/// </summary>
public sealed record OperationsObligationsView
{
    /// <summary>The obligation this row tracks (all four source events carry it). Doubles as the Marten document key.</summary>
    public Guid ObligationId { get; init; }

    /// <summary>
    /// The listing the obligation is for — the cross-view join key to the lot board (W006 §4).
    /// Carried by all four events; set-once via the <see cref="System.Guid.Empty"/> sentinel.
    /// </summary>
    public Guid ListingId { get; init; }

    /// <summary>The dispute instance. Carried by <c>DisputeOpened</c>/<c>DisputeResolved</c>; null until a dispute opens.</summary>
    public Guid? DisputeId { get; init; }

    /// <summary>The disputing party (the buyer/winner who raised it). Carried by <c>DisputeOpened.RaisedBy</c>; the open-dispute card's "who".</summary>
    public Guid? RaisedBy { get; init; }

    /// <summary>Why the dispute was raised ("NonDelivery"/"ItemCondition"/"MissedDeadline"). Carried by <c>DisputeOpened.Reason</c>.</summary>
    public string? DisputeReason { get; init; }

    /// <summary>How the dispute was resolved ("Refund"/"Extension"/"Closed"). Carried by <c>DisputeResolved.ResolutionType</c>.</summary>
    public string? ResolutionType { get; init; }

    /// <summary>The participant associated with the resolution. Carried by <c>DisputeResolved.ParticipantId</c> (optional on payload).</summary>
    public Guid? ResolutionParticipantId { get; init; }

    /// <summary>The winning bidder. Carried by <c>ObligationFulfilled.WinnerId</c> — only at fulfilment (W006 §4 finding); null before.</summary>
    public Guid? WinnerId { get; init; }

    /// <summary>The seller. Carried by <c>ObligationFulfilled.SellerId</c> — only at fulfilment; null before.</summary>
    public Guid? SellerId { get; init; }

    /// <summary>When the deadline escalation fired. Carried by <c>DeadlineEscalated.EscalatedAt</c>.</summary>
    public DateTimeOffset? EscalatedAt { get; init; }

    /// <summary>When the dispute was raised. Carried by <c>DisputeOpened.OpenedAt</c>.</summary>
    public DateTimeOffset? DisputeOpenedAt { get; init; }

    /// <summary>When the dispute was resolved. Carried by <c>DisputeResolved.ResolvedAt</c>.</summary>
    public DateTimeOffset? DisputeResolvedAt { get; init; }

    /// <summary>When the obligation fulfilled. Carried by <c>ObligationFulfilled.FulfilledAt</c>.</summary>
    public DateTimeOffset? FulfilledAt { get; init; }

    /// <summary>The derived queue-membership state (W006 §4). Drives escalation/open-dispute queue filters.</summary>
    public QueueState QueueState { get; init; }

    /// <summary>
    /// Marten document key — equals <see cref="ObligationId"/>. Expression-bodied to keep the
    /// storage shape identical to the <see cref="SettlementQueueView"/>/<see cref="LotBoardView"/>
    /// natural-key-as-id pattern; no <c>.Identity()</c> override is needed in the module.
    /// </summary>
    public Guid Id => ObligationId;
}
