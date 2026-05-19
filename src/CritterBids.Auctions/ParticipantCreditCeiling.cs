namespace CritterBids.Auctions;

/// <summary>
/// Auctions-side projection of per-participant credit ceilings, maintained by
/// <see cref="ParticipantCreditCeilingHandler"/> from <c>ParticipantSessionStarted</c>
/// integration events. The Proxy Bid Manager saga's start handler
/// (<see cref="StartProxyBidManagerSagaHandler"/>) reads this document at saga-start time
/// to populate <see cref="ProxyBidManagerSaga.BidderCreditCeiling"/> — the value then caps
/// the saga's defensive auto-bids per Workshop 002 §4.9 / scenario
/// <c>CompetingBidAtCeiling_ProducesProxyBidExhausted</c>.
///
/// <para><b>M4-D4 — duplicate-projection pattern, second lived application.</b> The
/// Settlement BC's <see cref="CritterBids.Settlement.BidderCreditView"/> (M5-S5) is the
/// first lived application — same source contract (<c>ParticipantSessionStarted</c>),
/// same upsert shape, same idempotent on re-delivery posture. Each BC maintains its own
/// local copy of the seed data the source BC publishes, consumed on a BC-specific RabbitMQ
/// queue (<c>auctions-participants-events</c> here, <c>settlement-participants-events</c>
/// in Settlement). The duplication is intentional — it preserves BC isolation by removing
/// cross-BC reads from saga-start hot paths per ADR 011 and the integration-messaging
/// skill §L2 ("publish the full payload at first commit").</para>
///
/// <para><b>Lifecycle.</b> Created at first <c>ParticipantSessionStarted</c> arrival with
/// <see cref="CreditCeiling"/> = <c>message.CreditCeiling</c> and <see cref="RegisteredAt"/>
/// = <c>message.StartedAt</c>. On re-delivery, the existing row is preserved verbatim — no
/// regression of <see cref="RegisteredAt"/>, no overwrite of <see cref="CreditCeiling"/>.
/// Unlike Settlement's <c>BidderCreditView</c>, this projection has no mutating downstream
/// event (no per-bidder debit on the Auctions side); the row is immutable after first
/// creation in the lived M4 model.</para>
///
/// <para><b>Identity convention.</b> <see cref="BidderId"/> = <c>ParticipantId</c> (the
/// Participants-side aggregate id; both <see cref="Guid"/>). The display-string
/// <c>BidderId</c> on the upstream contract ("<c>Bidder 4217</c>") is a separate concept
/// and is intentionally not stored here — the saga's lookup is by Guid. The
/// <see cref="Id"/> expression-bodied alias mirrors <c>BidderCreditView.Id</c>'s
/// natural-key-as-document-key shape from M5-S5.</para>
/// </summary>
public sealed record ParticipantCreditCeiling
{
    /// <summary>The participant whose credit ceiling this row records. Doubles as the Marten document key.</summary>
    public Guid BidderId { get; init; }

    /// <summary>
    /// Maximum cumulative bid amount the participant may commit across all listings —
    /// assigned at participant-session creation and immutable thereafter per the M5-S5
    /// W003 Phase 1 Part 7 contract on <c>ParticipantSessionStarted</c>.
    /// </summary>
    public decimal CreditCeiling { get; init; }

    /// <summary>
    /// Handler-stamped timestamp at first delivery (mirrors the upstream
    /// <c>ParticipantSessionStarted.StartedAt</c>). Preserved on re-delivery — the
    /// projection never re-stamps this field.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; }

    /// <summary>
    /// Marten document key — equals <see cref="BidderId"/>. Expression-bodied to keep the
    /// record's storage shape identical to <see cref="CritterBids.Settlement.BidderCreditView.Id"/>'s
    /// natural-key-as-id pattern.
    /// </summary>
    public Guid Id => BidderId;
}
