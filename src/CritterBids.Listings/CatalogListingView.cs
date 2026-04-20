namespace CritterBids.Listings;

/// <summary>
/// Read model for the catalog listing view.
/// Populated by <see cref="ListingPublishedHandler"/> when a
/// <c>CritterBids.Contracts.Selling.ListingPublished</c> integration event arrives,
/// then extended by <see cref="AuctionStatusHandler"/> as auction integration events
/// (<c>BiddingOpened</c>, <c>BidPlaced</c>, <c>BiddingClosed</c>, <c>ListingSold</c>,
/// <c>ListingPassed</c>, <c>BuyItNowPurchased</c>) arrive over the
/// <c>listings-auctions-events</c> RabbitMQ queue.
/// Stored as a Marten document in the "listings" schema.
/// </summary>
public sealed record CatalogListingView
{
    // ─── M2 fields (byte-identical from M2-S7 close) ─────────────────────────
    public Guid Id { get; init; }                  // ListingId — Marten document identity
    public Guid SellerId { get; init; }
    public string Title { get; init; } = "";
    public string Format { get; init; } = "";      // "Flash" or "Timed" — string, not enum
    public decimal StartingBid { get; init; }
    public decimal? BuyItNow { get; init; }
    public TimeSpan? Duration { get; init; }
    public DateTimeOffset PublishedAt { get; init; }

    // ─── M3-S6 auction-status fields (additive) ──────────────────────────────
    // Status transitions: "Published" → "Open" → "Closed" → "Sold" / "Passed".
    // BIN path: "Published" → "Open" → "Sold" (no "Closed" intermediate).
    // String, not enum — symmetry with Format above (M2-S7 precedent, OQ2 Path A).
    public string Status { get; init; } = "Published";

    public DateTimeOffset? ScheduledCloseAt { get; init; }

    public decimal? CurrentHighBid { get; init; }

    // OQ5 Path C — included now, redact at endpoint layer in M6 auth pass.
    public Guid? CurrentHighBidderId { get; init; }

    // Set authoritatively from BidPlaced.BidCount (OQ6 Path (a)) — never incremented.
    // DCB at the source guarantees monotonicity; last-write-wins is self-correcting
    // under at-least-once redelivery.
    public int BidCount { get; init; }

    public decimal? HammerPrice { get; init; }

    public Guid? WinnerId { get; init; }

    // One of "NoBids" or "ReserveNotMet" (per ListingPassed.Reason).
    public string? PassedReason { get; init; }

    // Populated from ListingPassed.HighestBid; null when Reason = "NoBids".
    public decimal? FinalHighestBid { get; init; }

    // Populated from whichever terminal arrived: BiddingClosed, ListingSold,
    // ListingPassed, or BuyItNowPurchased.
    public DateTimeOffset? ClosedAt { get; init; }
}
