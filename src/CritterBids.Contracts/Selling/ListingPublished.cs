namespace CritterBids.Contracts.Selling;

/// <summary>
/// Integration event published by the Selling BC when a listing is approved and published.
/// Transported via RabbitMQ queue "listings-selling-events" (Wolverine transactional outbox).
///
/// Consumed by:
/// - Listings BC (M2): Project CatalogListingView from ListingId, SellerId, Title, Format, StartingBid, PublishedAt
/// - Auctions BC (M3): Wire extended bidding via ExtendedBiddingEnabled, ExtendedBiddingTriggerWindow, ExtendedBiddingExtension, Duration
/// - Settlement BC (M5): Initiate fee calculation from ReservePrice, FeePercentage
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
