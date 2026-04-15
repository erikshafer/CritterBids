namespace CritterBids.Selling;

/// <summary>
/// Domain event appended to the <see cref="SellerListing"/> stream when a submitted listing
/// fails validation. Carries the reason for rejection. The listing can be resubmitted from
/// <see cref="ListingStatus.Rejected"/> state after correction (scenario 2.3).
/// </summary>
public sealed record ListingRejected(
    Guid ListingId,
    string RejectionReason,
    DateTimeOffset RejectedAt);
