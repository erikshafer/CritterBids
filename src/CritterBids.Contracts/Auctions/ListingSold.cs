namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a listing closes with a winning bidder
/// whose bid met or exceeded the reserve threshold. Emitted by the Auction Closing saga after
/// BiddingClosed on the sold outcome path (scenarios 3.5, 3.11). Not emitted on the BuyItNow
/// path — BuyItNowPurchased is the BIN terminal event.
/// Transported via RabbitMQ queue "listings-auctions-events" (Wolverine transactional outbox).
///
/// Consumed by:
/// - Listings BC (M3): Update CatalogListingView Status="Sold" with HammerPrice
/// - Settlement BC (M5): Initiate winner charge and seller payout using HammerPrice,
///   SellerId, WinnerId
/// - Relay BC (post-M5): Push "you won" / "your item sold" notifications
/// - Operations BC (post-M5): Live-board terminal outcome
/// - Auctions BC internally: Proxy Bid Manager saga (M4) terminates on ListingSold
///
/// SellerId is carried to enable Settlement to drive payout without a follow-up lookup against
/// the Selling BC — payload completeness per integration-messaging.md L2.
/// BidCount is the final count at close, useful for Settlement fee-calculation audit and for
/// Listings UI "N bids" display.
/// </summary>
public sealed record ListingSold(
    Guid ListingId,
    Guid SellerId,
    Guid WinnerId,
    decimal HammerPrice,
    int BidCount,
    DateTimeOffset SoldAt);
