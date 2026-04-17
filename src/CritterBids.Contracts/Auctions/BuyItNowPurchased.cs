namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a listing is purchased via the Buy It
/// Now path. Terminal for both the listing and the Auction Closing saga — no BiddingClosed,
/// no ListingSold follow-up. Only emitted when BIN is still available (no prior BidPlaced on
/// the listing); once any bid is accepted, BuyItNowOptionRemoved fires and the BIN path is
/// closed.
/// Transport queue: TBD (consumers are post-M3). Settlement (M5) and Relay (post-M5) consume
/// in later milestones — queue name finalized when Settlement subscribes.
///
/// Consumed by:
/// - Auctions BC internally: Auction Closing saga transitions to Resolved with
///   BuyItNowExercised=true and calls MarkCompleted
/// - Settlement BC (M5): Initiate charge and payout — BIN path starts in ReserveChecked
///   (WasMet: true) since BIN skips the reserve check entirely (W003-P1-6)
/// - Listings BC (post-M3): Update CatalogListingView Status="Sold" with the BIN price
/// - Relay BC (post-M5): Push "sold via Buy It Now" notification
/// - Operations BC (post-M5): Live-board terminal outcome
/// </summary>
public sealed record BuyItNowPurchased(
    Guid ListingId,
    Guid BuyerId,
    decimal Price,
    DateTimeOffset PurchasedAt);
