namespace CritterBids.Contracts.Settlement;

/// <summary>
/// Integration event published by the Settlement BC when the seven-phase financial
/// workflow reaches its terminal happy-path state. Emitted by the Settlement Saga's
/// <c>CompleteSettlement</c> handler from the <c>PayoutIssued</c> state per workshop
/// 003 scenario §6.1; corresponds to narrative 002 Moment 5.
/// Transported via RabbitMQ on the publisher-side queue routes wired in M5-S2.
/// Inbound queue route for the M5 consumer (Listings): <c>listings-settlement-events</c>.
///
/// Consumed by:
/// - Listings BC (M5): Update <c>CatalogListingView.Status</c> to <c>"Settled"</c>
///   (or analogous; exact field name decided at M5-S6's Listings extension). Marks
///   the listing as financially closed in addition to bidding-closed.
/// - Relay BC (post-M5): Broadcasts <c>{ type: "SettlementCompleted", listingId,
///   hammerPrice, remainingCredit }</c> over BiddingHub to the winning bidder's live
///   SignalR connection. The <c>remainingCredit</c> field is composed by the broadcast
///   handler reading Settlement's <c>BidderCreditView</c> projection (W003 Phase 1 Part 7).
/// - Obligations BC (post-M5): Triggers post-sale coordination per workshop 003
///   scenario §9.1's integration-event-publishing table.
/// - Operations BC (post-M5): Live-board "settled" indicator for the auction operator's
///   dashboard.
///
/// Field rationale:
/// - <c>SettlementId</c> — the deterministic UUID v5 stream identifier per W003 Phase 1
///   Part 6 (<c>UuidV5(SettlementsIdentityNamespaces.SettlementSaga, $"settlement:{ListingId}")</c>). Carried so
///   downstream consumers can attribute the event to a specific settlement instance and
///   so any retry or replay deduplicates against the same identifier.
/// - <c>ListingId</c> — the source listing whose sale this settlement closes. Listings
///   BC keys <c>CatalogListingView</c> updates by this field.
/// - <c>WinnerId</c>, <c>SellerId</c> — participant identifiers carried verbatim from
///   the saga's state. Relay's broadcast handler routes the bidder-side push by
///   <c>WinnerId</c>; future seller-side broadcast routes by <c>SellerId</c>.
/// - <c>HammerPrice</c> — the final accepted price (named <c>HammerPrice</c> rather
///   than <c>Price</c> per the W003 Phase 1 Part 2 Field Name Convention: post-initiation
///   the value is the hammer price by definition once <c>Source</c> is committed).
/// - <c>FeeAmount</c>, <c>SellerPayout</c> — the platform fee carved from the hammer
///   price and the resulting seller payout. Carried so consumers do not re-derive the
///   math; rounding is the single source of truth from <c>FinalValueFeeCalculated</c>.
/// - <c>CompletedAt</c> — handler-stamped timestamp at the terminal phase. Distinct
///   from the workflow's other phase timestamps; carries audit semantics ("when did this
///   settlement reach terminal state?").
/// </summary>
public sealed record SettlementCompleted(
    Guid SettlementId,
    Guid ListingId,
    Guid WinnerId,
    Guid SellerId,
    decimal HammerPrice,
    decimal FeeAmount,
    decimal SellerPayout,
    DateTimeOffset CompletedAt);
