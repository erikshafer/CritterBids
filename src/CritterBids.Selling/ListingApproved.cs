namespace CritterBids.Selling;

/// <summary>
/// Domain event appended to the <see cref="SellerListing"/> stream when a submitted listing
/// passes validation. Always followed by <see cref="ListingPublished"/> in the same transaction.
/// </summary>
public sealed record ListingApproved(
    Guid ListingId,
    DateTimeOffset ApprovedAt);
