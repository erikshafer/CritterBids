namespace CritterBids.Selling;

/// <summary>
/// Domain event appended to the <see cref="SellerListing"/> stream when a seller updates
/// a listing that is still in Draft state. Only changed fields carry non-null values.
/// </summary>
public sealed record DraftListingUpdated(
    Guid ListingId,
    string? Title,
    decimal? ReservePrice,
    decimal? BuyItNowPrice,
    DateTimeOffset UpdatedAt);
