namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a Proxy Bid Manager saga terminates
/// because the proxy can no longer outbid a competing bid within its constraints. Produced
/// when <c>min(competingBid + increment, MaxAmount, BidderCreditCeiling) &lt;= competingBid</c>
/// (the next defensive bid would not strictly exceed the competitor). Terminal for the
/// saga — emitted once, then the saga calls <c>MarkCompleted()</c>.
///
/// Promoted from W001 Parked #3 (resolved in Workshop 002 Phase 1): the exhaustion case
/// earns a distinct notification path from the generic outbid broadcast because the user-
/// facing message is semantically different ("your proxy has been exceeded" vs "you have
/// been outbid on a manual bid").
///
/// Authored at M4-S1 as a vocabulary-lock stub; produced at M4-S4 by the saga's
/// competing-bid reactive handler.
///
/// Transport queue: TBD (consumers are post-M5). Relay (post-M5) is the primary consumer;
/// queue name finalized when Relay subscribes.
///
/// Consumed by:
/// - Relay BC (post-M5): Push the distinct "your proxy has been exceeded" notification to
///   the bidder's participant session. Must not be merged into the generic outbid
///   notification — that was the W001 Parked #3 design call (Workshop 002 Phase 1).
/// - Operations BC (post-M5): Live-board indicator decrementing the active-proxy count and
///   flagging the exhaustion for the demo audience.
/// - Settlement BC (post-M5): Not a consumer — exhaustion is a saga state transition, not
///   a financial event. Settlement reacts to the listing's terminal outcome
///   (<c>ListingSold</c> / <c>ListingPassed</c> / <c>BuyItNowPurchased</c>), not to proxy
///   state.
/// - Auctions BC internally: Not consumed — the saga emits this itself and terminates. No
///   internal subscriber.
///
/// <c>MaxAmount</c> is carried on the event (not just the saga state) so Relay can render
/// the user-facing notification text without a follow-up lookup. Full payload at first
/// commit per integration-messaging.md L2.
/// </summary>
public sealed record ProxyBidExhausted(
    Guid ListingId,
    Guid BidderId,
    decimal MaxAmount,
    DateTimeOffset ExhaustedAt);
