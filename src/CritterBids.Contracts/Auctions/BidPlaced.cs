namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a bid is accepted by the DCB PlaceBid
/// handler. Emitted atomically with any companion signals (BuyItNowOptionRemoved on first bid,
/// ReserveMet on reserve-crossing bid, ExtendedBiddingTriggered on trigger-window bid).
/// Transported via RabbitMQ queue "listings-auctions-events" (Wolverine transactional outbox).
///
/// Consumed by:
/// - Listings BC (M3): Update CatalogListingView CurrentHighBid, BidCount
/// - Auctions BC internally: Auction Closing saga tracks incremental high-bid state; Proxy
///   Bid Manager saga (M4) reacts to competing bids
/// - Relay BC (post-M5): Push outbid notification, current-high-bid broadcast
/// - Operations BC (post-M5): Live-board bid-count and leader updates
///
/// IsProxy flag is hard-coded to false by the M3 PlaceBid handler (no proxy path exists in
/// M3). M4 wires the Proxy Bid Manager saga to set IsProxy=true on auto-bids. The contract
/// shape is stable across M3 and M4 — field is present now to avoid contract churn.
///
/// BidId is an externally-assigned unique identifier for the bid (command-origin, UUID v7).
/// Allows idempotency on the Listings projection and per-bid references from support tooling.
/// </summary>
public sealed record BidPlaced(
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    int BidCount,
    bool IsProxy,
    DateTimeOffset PlacedAt);
