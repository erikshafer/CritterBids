namespace CritterBids.Settlement;

/// <summary>
/// Settlement BC's per-bidder credit projection per W003 Phase 1 Part 7. Surfaces the
/// bidder's remaining credit so Relay's post-M5 <c>SettlementCompleted</c> broadcast and any
/// future bidder-balance endpoint can render the running balance without a cross-BC read.
///
/// <para><b>Lifecycle.</b> Initialized at <c>ParticipantSessionStarted</c> arrival
/// (<see cref="BidderCreditViewHandler"/>) with <see cref="RemainingCredit"/> seeded to the
/// participant's assigned <c>CreditCeiling</c> and <see cref="LastChargedSettlementId"/>
/// null. Updated at every <c>WinnerCharged</c> arrival: debit <see cref="RemainingCredit"/>
/// by the charged amount, set <see cref="LastChargedSettlementId"/> to the saga's
/// <c>SettlementId</c>, advance <see cref="UpdatedAt"/>. Idempotency-by-
/// <see cref="LastChargedSettlementId"/>: re-delivery of the same <c>WinnerCharged</c> for
/// an already-debited settlement is a no-op per Part 7's contract.</para>
///
/// <para><b>Lazy-init posture.</b> If a <c>WinnerCharged</c> arrives for a <c>WinnerId</c>
/// with no prior <c>ParticipantSessionStarted</c>-seeded row (the contract-promotion
/// deferral case, or any cross-queue race where the charge event lands first), the handler
/// creates the row with <see cref="RemainingCredit"/> set to <c>-Amount</c> — a negative
/// sentinel that surfaces "no prior state" as data. Downstream consumers (Relay's broadcast)
/// read the value verbatim; the sentinel is not converted or coerced.</para>
///
/// <para><b>No DCB consumer.</b> Per W003 Phase 1 Part 4 Option A, the bidder-credit
/// projection is not a participant in any Dynamic Consistency Boundary query. Reads happen
/// via direct <c>LoadAsync&lt;BidderCreditView&gt;(BidderId)</c> calls only; no aggregation
/// or stream-tag indexing is required.</para>
///
/// <para><b>Marten Id convention.</b> The <see cref="BidderId"/> property is the Marten
/// document key (exposed via the <see cref="Id"/> expression-bodied alias). Mirrors the
/// <see cref="PendingSettlement.Id"/> = <c>ListingId</c> shape from M5-S3 — the natural
/// business key doubles as the document key, no separate id field.</para>
/// </summary>
public sealed record BidderCreditView
{
    /// <summary>The participant whose credit balance this row tracks. Doubles as the Marten document key.</summary>
    public Guid BidderId { get; init; }

    /// <summary>
    /// Remaining credit available to this bidder. Seeded at <c>CreditCeiling</c> on
    /// <c>ParticipantSessionStarted</c>; decremented by each <c>WinnerCharged.Amount</c>.
    /// Negative values are the lazy-init sentinel: a row created from <c>WinnerCharged</c>
    /// with no prior <c>ParticipantSessionStarted</c> seed carries <c>-Amount</c> to mark
    /// the absent prior state explicitly.
    /// </summary>
    public decimal RemainingCredit { get; init; }

    /// <summary>
    /// The <c>SettlementId</c> of the most recent <c>WinnerCharged</c> applied to this row.
    /// Null on a freshly-seeded row from <c>ParticipantSessionStarted</c> (never charged yet).
    /// Used as the idempotency key: re-delivery of <c>WinnerCharged</c> with a matching
    /// value is a no-op per W003 Phase 1 Part 7.
    /// </summary>
    public Guid? LastChargedSettlementId { get; init; }

    /// <summary>Handler-stamped timestamp at the most recent mutation (init or charge).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Marten document key — equals <see cref="BidderId"/>. Expression-bodied to keep the
    /// record's storage shape identical to <c>PendingSettlement</c>'s natural-key-as-id pattern.
    /// </summary>
    public Guid Id => BidderId;
}
