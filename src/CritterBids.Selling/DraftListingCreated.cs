namespace CritterBids.Selling;

/// <summary>
/// Domain event appended to the <see cref="SellerListing"/> stream when a registered seller
/// creates a new draft listing via <c>POST /api/listings/draft</c>.
/// </summary>
/// <remarks>
/// Stream ID is <c>ListingId</c> — a UUID v7 generated at creation time (ADR 007, M2 §6).
/// <c>Apply(DraftListingCreated)</c> in <see cref="SellerListing"/> sets Status to Draft.
/// </remarks>
public sealed record DraftListingCreated(
    Guid ListingId,
    Guid SellerId,
    string Title,
    ListingFormat Format,
    decimal StartingBid,
    decimal? ReservePrice,
    decimal? BuyItNowPrice,
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow,
    TimeSpan? ExtendedBiddingExtension,
    DateTimeOffset CreatedAt);
