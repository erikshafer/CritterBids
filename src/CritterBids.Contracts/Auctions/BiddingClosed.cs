namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC as a mechanical close signal — bidding is no
/// longer accepted on this listing. Emitted by the Auction Closing saga when the scheduled
/// close fires (and on the terminal path for reserve-met / reserve-not-met / no-bids).
/// Separate from the outcome events (ListingSold / ListingPassed / BuyItNowPurchased) so a
/// consumer that only cares about "bids no longer accepted" has a single type to subscribe to
/// without correlating three outcomes.
/// Transported via RabbitMQ queue "listings-auctions-events" (Wolverine transactional outbox).
///
/// Consumed by:
/// - Listings BC (M3): Update CatalogListingView Status="Closed" — preceded BiddingClosed
///   before the follow-up ListingSold or ListingPassed arrives
/// - Relay BC (post-M5): Broadcast "bidding ended" to UI so the bid button greys out
/// - Operations BC (post-M5): Live-board closed-count and countdown-zeroed display
///
/// Not emitted on the BuyItNow terminal path (scenario 3.8) — BIN is its own outcome and
/// skips the mechanical close signal. Consumers of BiddingClosed that also care about BIN
/// must subscribe to BuyItNowPurchased as well.
/// </summary>
public sealed record BiddingClosed(
    Guid ListingId,
    DateTimeOffset ClosedAt);
