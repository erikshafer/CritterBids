namespace CritterBids.Operations;

/// <summary>
/// The session-activity lifecycle status surfaced on the operations staff board, derived from which
/// Auctions-family integration event last advanced the <see cref="SessionActivityView"/> row (W006
/// §5a derivation rule). The derivation — <c>Created → Started</c> — is frozen; these member names
/// are the Operations-internal realisation (W006 leaves enum member names unfrozen, consistent with
/// <see cref="LotBoardStatus"/>/<see cref="QueueState"/>).
///
/// <para><b>Derivation.</b> <c>SessionCreated</c> → <see cref="Created"/>; <c>SessionStarted</c> →
/// <see cref="Started"/>; <c>ListingAttachedToSession</c> does <b>not</b> change the status (it only
/// unions an id into <see cref="SessionActivityView.AttachedListingIds"/>). The mandated preservation
/// guard: a re-delivered <c>SessionCreated</c> after <c>SessionStarted</c> must not regress
/// <see cref="Started"/> back to <see cref="Created"/>. <see cref="SessionActivityStatusRules"/>
/// realises this as a monotone, forward-only advance (the S3 absorbing-rank pattern applies cleanly —
/// there is no sanctioned backward transition).</para>
/// </summary>
public enum SessionActivityStatus
{
    /// <summary>The Flash session is created and accepting listing attachments, but has not started. Set by <c>SessionCreated</c>.</summary>
    Created,

    /// <summary>The Flash session has started and its listings are open for bidding. Set by <c>SessionStarted</c>; terminal on this axis.</summary>
    Started,
}

/// <summary>
/// Status-derivation mechanics for the session board (W006 §5a), kept alongside the enum so the
/// monotone rule is expressed once for <see cref="SessionActivityHandler"/>. Mirrors
/// <see cref="LotBoardStatusRules"/> (M7-S3): each status has a rank —
/// <see cref="SessionActivityStatus.Created"/> = 0, <see cref="SessionActivityStatus.Started"/> = 1 —
/// and <see cref="Advance"/> keeps the existing status whenever its rank is greater than or equal to
/// the candidate's, otherwise takes the candidate. This guarantees the W006 §5a invariant: a
/// re-delivered <c>SessionCreated</c> (candidate <see cref="SessionActivityStatus.Created"/>) never
/// regresses an already-<see cref="SessionActivityStatus.Started"/> row.
/// </summary>
internal static class SessionActivityStatusRules
{
    /// <summary>
    /// Returns the surviving status when an event whose natural status is <paramref name="candidate"/>
    /// is applied to a row currently at <paramref name="current"/>. Monotone and forward-only: the
    /// existing status wins on a rank tie or when it already outranks the candidate.
    /// </summary>
    public static SessionActivityStatus Advance(SessionActivityStatus current, SessionActivityStatus candidate) =>
        Rank(current) >= Rank(candidate) ? current : candidate;

    private static int Rank(SessionActivityStatus status) => status switch
    {
        SessionActivityStatus.Created => 0,
        _                             => 1, // Started
    };
}
