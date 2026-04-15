namespace CritterBids.Selling;

/// <summary>
/// Domain event appended to the <see cref="SellerListing"/> stream when a seller submits
/// a draft listing for publication. Always the first event in a submit sequence.
/// </summary>
public sealed record ListingSubmitted(
    Guid ListingId,
    Guid SellerId,
    DateTimeOffset SubmittedAt);
