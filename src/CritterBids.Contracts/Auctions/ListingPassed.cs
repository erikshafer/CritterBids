namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a listing closes without a sale.
/// Emitted by the Auction Closing saga on the two non-sale close paths: reserve-not-met
/// (scenario 3.6) and no-bids (scenario 3.7). Not emitted on the ListingWithdrawn path —
/// withdrawal is a separate terminal event outside the Auctions BC vocabulary.
/// Transported via RabbitMQ queue "listings-auctions-events" (Wolverine transactional outbox).
///
/// Consumed by:
/// - Listings BC (M3): Update CatalogListingView Status="Passed" with Reason
/// - Relay BC (post-M5): Push "your listing didn't sell" notification to seller
/// - Operations BC (post-M5): Live-board passed-count indicator
/// - Auctions BC internally: Proxy Bid Manager saga (M4) terminates on ListingPassed
/// - Settlement BC does NOT consume ListingPassed — no money moves on a passed listing
///
/// Reason is a string-valued enum carrying one of:
///   "NoBids"         — no bids were placed before close
///   "ReserveNotMet"  — bids were placed but none reached the reserve threshold
/// Stored as string rather than int-valued enum so the wire contract stays decoupled from
/// any enum type. Consumers pattern-match on the string constant.
/// HighestBid is null when Reason is "NoBids" (there was no highest bid). When Reason is
/// "ReserveNotMet" it carries the highest-bid amount at close for operator diagnostics and
/// Listings display ("passed at $X, reserve not met").
/// </summary>
public sealed record ListingPassed(
    Guid ListingId,
    string Reason,
    decimal? HighestBid,
    int BidCount,
    DateTimeOffset PassedAt);
