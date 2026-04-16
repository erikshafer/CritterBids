namespace CritterBids.Listings;

/// <summary>
/// Read model for the catalog listing view.
/// Populated by <see cref="ListingPublishedHandler"/> when a
/// <c>CritterBids.Contracts.Selling.ListingPublished</c> integration event arrives.
/// Stored as a Marten document in the "listings" schema.
/// </summary>
public sealed record CatalogListingView
{
    public Guid Id { get; init; }                  // ListingId — Marten document identity
    public Guid SellerId { get; init; }
    public string Title { get; init; } = "";
    public string Format { get; init; } = "";      // "Flash" or "Timed" — string, not enum
    public decimal StartingBid { get; init; }
    public decimal? BuyItNow { get; init; }
    public TimeSpan? Duration { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
}
