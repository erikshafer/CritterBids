namespace CritterBids.Contracts.Settlement;

/// <summary>
/// Integration event published by the Settlement BC when the workflow's
/// <c>IssueSellerPayout</c> phase commits. Emitted by the Settlement Saga from the
/// <c>FeeCalculated</c> state per workshop 003 scenario §5.1; corresponds to the
/// second paragraph of narrative 002 Moment 4's <c>Response.</c> block.
/// MVP scope: virtual seller-side credit ledger entry, no real banking integration
/// per W003 §"Seller Payout".
/// Transported via RabbitMQ on the publisher-side queue routes wired in M5-S6.
/// Inbound queue routes are post-M5 (Relay BC has not shipped at M5 close); slice 6.3
/// implementation lands when Relay ships.
///
/// Consumed by:
/// - Relay BC (post-M5): Broadcasts a seller-payout notification to the seller's live
///   connection (slice 6.3 P1 territory). Carries the payout amount and the deducted fee
///   so the seller's view can render both.
/// - Operations BC (post-M5): Live-board seller-payout indicator for ops staff.
/// - Future seller-balance read model (post-MVP): Updates a seller-side running balance
///   analogous to <c>BidderCreditView</c> but for seller credits. The seller-side
///   projection's name and shape are not defined in M5; this contract is shaped so the
///   future projection can consume it without payload extension.
///
/// Field rationale:
/// - <c>SettlementId</c> — the deterministic UUID v5 stream identifier. Carried so
///   consumers can correlate the payout with the specific settlement instance.
/// - <c>SellerId</c> — the participant receiving the payout. Carried so Relay's
///   broadcast handler can route the seller-side push without a follow-up read.
/// - <c>PayoutAmount</c> — the seller's net receipt (<c>HammerPrice - FeeAmount</c>).
///   Carried for direct rendering by seller-side notification UIs.
/// - <c>FeeDeducted</c> — the platform fee carved from the hammer price. Carried so
///   the seller's view can render the gross-vs-net breakdown without re-deriving the
///   math.
/// - <c>IssuedAt</c> — handler-stamped timestamp at the payout-emission phase.
/// </summary>
public sealed record SellerPayoutIssued(
    Guid SettlementId,
    Guid SellerId,
    decimal PayoutAmount,
    decimal FeeDeducted,
    DateTimeOffset IssuedAt);
