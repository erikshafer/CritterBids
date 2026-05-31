namespace CritterBids.Operations;

/// <summary>
/// The obligation queue-membership state surfaced on the operations staff board, derived from which
/// Obligations-family integration event last advanced the <see cref="OperationsObligationsView"/>
/// row (W006 §4 derivation rule). W006 freezes the <i>derivation</i> — <c>Escalated → Disputed →
/// (Resolved-or-back-to-active) → Fulfilled</c> — not the member spelling; these names are the
/// Operations-internal realisation (consistent with <see cref="LotBoardStatus"/> /
/// <see cref="SettlementQueueStatus"/>, no Contracts type).
///
/// <para><b>Derivation + queue membership.</b> <c>DeadlineEscalated</c> → <see cref="Escalated"/>
/// (the escalation queue is <c>QueueState == Escalated</c>); <c>DisputeOpened</c> →
/// <see cref="Disputed"/> (the open-dispute queue is <c>QueueState == Disputed</c>);
/// <c>DisputeResolved</c> branches on its <c>ResolutionType</c> — <c>"Extension"</c> →
/// <see cref="Active"/> (the sanctioned <i>backward</i> move out of both queues, <b>non-terminal</b>,
/// narrative-008 Moment 2), <c>"Refund"</c>/<c>"Closed"</c> → <see cref="Resolved"/> (terminal);
/// <c>ObligationFulfilled</c> → <see cref="Fulfilled"/> (terminal). Both queues are <i>derived</i>
/// query filters over this single per-obligation row — a resolve/fulfil flips the state, it does
/// not delete the document (W006 §4 "drop the row from every active queue" = filter out, not
/// remove).</para>
///
/// <para><b>Non-monotone state machine.</b> Unlike the lot board's monotone absorbing rank (S3),
/// this view has a deliberate backward transition (<see cref="Disputed"/> → <see cref="Active"/> on
/// <c>Extension</c>). Only the terminal states (<see cref="Fulfilled"/>; <see cref="Resolved"/> from
/// <c>Refund</c>/<c>Closed</c>) are absorbing — see <see cref="QueueStateRules"/>. A pure rank guard
/// does not apply here.</para>
/// </summary>
public enum QueueState
{
    /// <summary>
    /// Unseeded sentinel — the zero value a freshly constructed row carries before any event is
    /// applied. Never persisted in practice: a tolerant upsert always advances the row to a real
    /// state in the same write that stores it. Present so the <c>default</c> never masquerades as a
    /// real queue (contrast making <c>0</c> a real state, where a bug could surface a phantom
    /// <see cref="Escalated"/> row).
    /// </summary>
    None,

    /// <summary>A sold listing's ship-by deadline lapsed. Set by <c>DeadlineEscalated</c>; the escalation queue.</summary>
    Escalated,

    /// <summary>A dispute is open against the obligation. Set by <c>DisputeOpened</c>; the open-dispute queue.</summary>
    Disputed,

    /// <summary>
    /// The dispute was resolved with an <c>Extension</c> — a fresh deadline was granted and the
    /// obligation returns to the active set (out of both queues). Set by
    /// <c>DisputeResolved { Extension }</c>; <b>non-terminal</b> (the one backward transition;
    /// the obligation can still fulfil or, on a fresh cycle, re-escalate/re-dispute).
    /// </summary>
    Active,

    /// <summary>
    /// The dispute was resolved terminally (<c>Refund</c> or <c>Closed</c>). Set by
    /// <c>DisputeResolved { Refund | Closed }</c>; terminal — does not regress.
    /// </summary>
    Resolved,

    /// <summary>The obligation reached its happy-path terminal state. Set by <c>ObligationFulfilled</c>; terminal — does not regress.</summary>
    Fulfilled,
}

/// <summary>
/// Transition mechanics for the obligation queue state (W006 §4), used by
/// <see cref="OperationsObligationsHandler"/> so the terminal-absorbing rule is expressed once.
///
/// <para><b>Terminal-absorbing, but not a monotone rank.</b> The lot board (S3) uses a monotone
/// absorbing rank because every transition moves forward. This view cannot: <c>Extension</c> is a
/// sanctioned <see cref="QueueState.Disputed"/> → <see cref="QueueState.Active"/> backward move. So
/// the guard is split:
/// <list type="bullet">
/// <item><b>Terminal absorption</b> — once <see cref="QueueState.Fulfilled"/> or
/// <see cref="QueueState.Resolved"/>, no later event changes the row. A real obligation reaches
/// exactly one terminal outcome (the saga either resolves a dispute with <c>Refund</c>/<c>Closed</c>
/// <i>or</i> fulfils — they are mutually exclusive, both <c>MarkCompleted()</c> paths), so
/// first-terminal-wins is domain-correct, and it gives the mandated guard that a re-delivered
/// earlier event never regresses a terminal row.</item>
/// <item><b>Ordering guard</b> — a strictly-older event (by handler-stamped timestamp) never
/// advances a non-terminal row, so a late <c>DeadlineEscalated</c> cannot regress
/// <see cref="QueueState.Disputed"/> → <see cref="QueueState.Escalated"/>. The legitimate
/// <c>Extension</c> backward move is <i>not</i> older than the <c>DisputeOpened</c> it follows, so
/// it is never caught by this guard.</item>
/// </list>
/// Both are realised in the handler as an early-return "ignore" decision (see
/// <see cref="IsTerminal"/>): an absorbed or stale event mutates nothing — neither the state nor the
/// payload fields — so re-delivery is a true no-op.</para>
/// </summary>
internal static class QueueStateRules
{
    /// <summary>True when <paramref name="state"/> is one of the two absorbing terminal states.</summary>
    public static bool IsTerminal(QueueState state) =>
        state is QueueState.Fulfilled or QueueState.Resolved;
}
