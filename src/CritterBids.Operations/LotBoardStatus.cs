namespace CritterBids.Operations;

/// <summary>
/// The lot-board lifecycle status surfaced on the operations staff board, derived from which
/// source-family integration event last advanced the <see cref="LotBoardView"/> row (W006 §2
/// derivation rule). The derivation — not the member spelling — is frozen; these names are the
/// Operations-internal realisation (W006 leaves enum member names unfrozen).
///
/// <para><b>Derivation.</b> <c>ListingPublished</c> → <see cref="Draft"/>; <c>BiddingOpened</c>
/// (or a <c>BidPlaced</c> arriving before it) → <see cref="Open"/>; <c>ListingSold</c> →
/// <see cref="Sold"/>; <c>ListingPassed</c> → <see cref="Passed"/>; <c>ListingWithdrawn</c> →
/// <see cref="Withdrawn"/>. The mandated preservation guard: the three terminal states
/// (<see cref="Sold"/>/<see cref="Passed"/>/<see cref="Withdrawn"/>) must not regress to
/// <see cref="Open"/> on a late <c>BidPlaced</c> (W006 §2). <see cref="LotBoardStatusRules"/>
/// realises this as a monotone, terminal-absorbing advance.</para>
/// </summary>
public enum LotBoardStatus
{
    /// <summary>The listing is published and catalogued but bidding has not opened. Set by <c>ListingPublished</c>.</summary>
    Draft,

    /// <summary>Bidding is open. Set by <c>BiddingOpened</c> (or a <c>BidPlaced</c> arriving before it).</summary>
    Open,

    /// <summary>The listing closed with a winning, reserve-meeting bid. Set by <c>ListingSold</c>; terminal.</summary>
    Sold,

    /// <summary>The listing closed without a sale (no bids or reserve not met). Set by <c>ListingPassed</c>; terminal.</summary>
    Passed,

    /// <summary>The listing was withdrawn before close. Set by <c>ListingWithdrawn</c>; terminal.</summary>
    Withdrawn,
}

/// <summary>
/// Status-derivation mechanics for the lot board (W006 §2), shared by the two ADR-014
/// Sub-Option A sibling handlers (<see cref="LotBoardSellingHandler"/> and
/// <see cref="LotBoardAuctionsHandler"/>) so the monotone rule is expressed once.
///
/// <para><b>Monotone, terminal-absorbing advance.</b> Each status has a rank —
/// <see cref="LotBoardStatus.Draft"/> = 0, <see cref="LotBoardStatus.Open"/> = 1, and the three
/// terminal states = 2. <see cref="Advance"/> keeps the existing status whenever its rank is
/// greater than or equal to the candidate's, otherwise it takes the candidate. This yields two
/// invariants W006 §2 requires plus the load-and-preserve discipline ADR 014 mandates:
/// <list type="bullet">
/// <item>a late <c>BidPlaced</c> (candidate <see cref="LotBoardStatus.Open"/>) never regresses a
/// terminal row — the mandated guard;</item>
/// <item>a late-seeding <c>ListingPublished</c> (candidate <see cref="LotBoardStatus.Draft"/>)
/// never regresses an already-<see cref="LotBoardStatus.Open"/>/terminal row to
/// <see cref="LotBoardStatus.Draft"/> — the seed-after-auction-events case;</item>
/// <item>once terminal, a second (non-domain-realistic) terminal event does not flip the status —
/// terminal is absorbing, first-terminal-wins. A listing reaches exactly one terminal in the
/// domain, so terminal-vs-terminal ordering is outside W006's frozen scope; absorbing is the
/// safe default.</item>
/// </list></para>
/// </summary>
internal static class LotBoardStatusRules
{
    /// <summary>True when <paramref name="status"/> is one of the three absorbing terminal states.</summary>
    public static bool IsTerminal(LotBoardStatus status) =>
        status is LotBoardStatus.Sold or LotBoardStatus.Passed or LotBoardStatus.Withdrawn;

    /// <summary>
    /// Returns the surviving status when an event whose natural status is <paramref name="candidate"/>
    /// is applied to a row currently at <paramref name="current"/>. Monotone and terminal-absorbing:
    /// the existing status wins on a rank tie or when it already outranks the candidate.
    /// </summary>
    public static LotBoardStatus Advance(LotBoardStatus current, LotBoardStatus candidate) =>
        Rank(current) >= Rank(candidate) ? current : candidate;

    private static int Rank(LotBoardStatus status) => status switch
    {
        LotBoardStatus.Draft => 0,
        LotBoardStatus.Open  => 1,
        _                    => 2, // Sold / Passed / Withdrawn — all terminal, equal rank
    };
}
