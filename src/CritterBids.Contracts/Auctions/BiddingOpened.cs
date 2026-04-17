namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a listing opens for bids, immediately
/// after a ListingPublished is consumed from Selling and the Auctions-side Listing aggregate
/// is initialized.
/// Transported via RabbitMQ queue "listings-auctions-events" (Wolverine transactional outbox).
///
/// Consumed by:
/// - Listings BC (M3): Extend CatalogListingView with Status="Open", ScheduledCloseAt
/// - Relay BC (post-M5): Push "bidding started" notification to watchers
/// - Operations BC (post-M5): Update live-board with opened listings
/// - Settlement BC subscribes to ListingPublished directly (M5), not BiddingOpened — listed
///   here only to document that Settlement is NOT a consumer of this event
///
/// W002-9 (payload completeness) — resolved M3-S1: this contract carries the full extended-
/// bidding configuration (enabled flag, trigger window, extension duration, max duration cap)
/// rather than requiring the Auction Closing saga to load from stream on each reaction. Saga
/// is self-contained from the BiddingOpened event alone — no event-store lookup needed for
/// extension logic, which simplifies replay semantics and keeps the saga's only dependency
/// on prior events its own saga state. Full payload required at first commit — all future
/// consumer fields present even though only Listings subscribes in M3
/// (integration-messaging.md L2).
/// </summary>
public sealed record BiddingOpened(
    Guid ListingId,
    Guid SellerId,
    decimal StartingBid,
    decimal? ReserveThreshold,
    decimal? BuyItNowPrice,
    DateTimeOffset ScheduledCloseAt,
    bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow,
    TimeSpan? ExtendedBiddingExtension,
    TimeSpan MaxDuration,
    DateTimeOffset OpenedAt);
