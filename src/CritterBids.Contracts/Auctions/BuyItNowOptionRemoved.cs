namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when the Buy It Now option is no longer
/// available on a listing. Emitted atomically with the first BidPlaced — once any bid is
/// accepted, the listing completes via the bidding path and BIN short-circuit is closed.
/// Transport queue: TBD (consumers are post-M3). Listings and Relay consume in later
/// milestones — queue name finalized when the first consumer is wired.
///
/// Consumed by:
/// - Listings BC (post-M3): Update CatalogListingView to hide BIN price from participant catalog
/// - Relay BC (post-M5): Broadcast "BIN removed" to UI so the button disappears in real time
/// - Operations BC (post-M5): Live-board visibility
///
/// Not consumed by the Auction Closing saga — BIN removal doesn't change saga state; the saga
/// only cares about bids, extensions, BIN purchase (terminal), and withdrawal (terminal).
/// </summary>
public sealed record BuyItNowOptionRemoved(
    Guid ListingId,
    DateTimeOffset RemovedAt);
