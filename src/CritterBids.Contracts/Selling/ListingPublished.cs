namespace CritterBids.Contracts.Selling;

/// <summary>
/// Integration event published by the Selling BC when a listing is approved and published.
/// Transported via RabbitMQ on three queues (each consumer reads from its own dedicated queue
/// to keep MultipleHandlerBehavior.Separated routing clean):
/// - "listings-selling-events" — Listings BC.
/// - "auctions-selling-events" — Auctions BC.
/// - "settlement-selling-events" — Settlement BC (added at M5-S3).
///
/// Consumed by:
/// - Listings BC (M2): Project CatalogListingView from ListingId, SellerId, Title, Format, StartingBid, PublishedAt
/// - Auctions BC (M3): Wire extended bidding via ExtendedBiddingEnabled, ExtendedBiddingTriggerWindow, ExtendedBiddingExtension, Duration
/// - Settlement BC (M5-S3): Seed the PendingSettlement projection (Status: Pending) per
///   workshop 003 scenario §8.1, capturing ReservePrice, BuyItNow → BuyItNowPrice, FeePercentage,
///   SellerId, and PublishedAt for the saga to read at workflow-start time without crossing
///   the BC boundary (W003 Phase 1 Part 1).
///
/// Full payload required at first commit — all future consumer fields present even though
/// only Listings BC subscribes in M2 (integration-messaging.md L2).
/// </summary>
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    string Format,
    decimal StartingBid,
    decimal? ReservePrice,
    decimal? BuyItNow,
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow,
    TimeSpan? ExtendedBiddingExtension,
    decimal FeePercentage,
    DateTimeOffset PublishedAt);
