namespace CritterBids.Settlement;

/// <summary>
/// First event in the financial event stream — emitted by <see cref="StartSettlementSagaHandler"/>
/// when the saga starts on a successful auction outcome. Per workshop 003 scenario §1.1
/// (bidding source) and §1.2 (BIN source). Carries the eight-field W003-canonical payload
/// plus the handler-stamped <see cref="InitiatedAt"/> timestamp (per the M5-S1 F004 amendment
/// that normalized the §1.1 / §7.1 payload shape across decider output and evolver input).
///
/// <para><b>Stream-internal — not in <c>CritterBids.Contracts.Settlement.*</c>.</b> This event
/// lives entirely inside the Settlement BC's financial event stream as audit ground; no
/// cross-BC consumer subscribes to it. Per W003 §"Integration in/out", the integration-out
/// set is exactly <see cref="CritterBids.Contracts.Settlement.SettlementCompleted"/>,
/// <see cref="CritterBids.Contracts.Settlement.PaymentFailed"/>, and
/// <see cref="CritterBids.Contracts.Settlement.SellerPayoutIssued"/>.</para>
///
/// <para><b>Field name convention.</b> The <see cref="Price"/> field is source-agnostic at
/// initiation per the W003 Phase 1 Part 2 Field Name Convention (M5-S1 F002 amendment) —
/// <c>HammerPrice</c> for Bidding source, <c>BuyItNowPrice</c> for BIN source, both
/// flow into <see cref="Price"/> at the saga's start. Downstream events
/// (<see cref="WinnerCharged.Amount"/>, <see cref="FinalValueFeeCalculated.HammerPrice"/>,
/// <see cref="CritterBids.Contracts.Settlement.SettlementCompleted.HammerPrice"/>) use
/// the post-initiation <c>HammerPrice</c> name because the value is the hammer price by
/// definition once <see cref="Source"/> is committed.</para>
/// </summary>
public sealed record SettlementInitiated(
    Guid SettlementId,
    Guid ListingId,
    Guid WinnerId,
    Guid SellerId,
    decimal Price,
    SettlementSource Source,
    decimal? ReservePrice,
    decimal FeePercentage,
    DateTimeOffset InitiatedAt);
