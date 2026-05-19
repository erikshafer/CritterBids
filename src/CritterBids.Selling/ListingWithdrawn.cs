namespace CritterBids.Selling;

/// <summary>
/// Domain event appended to the <see cref="SellerListing"/> stream when a listing is withdrawn.
/// Replayed by <see cref="SellerListing.Apply(ListingWithdrawn)"/> to set <c>Status = Withdrawn</c>.
/// </summary>
/// <remarks>
/// Distinct CLR type from <c>CritterBids.Contracts.Selling.ListingWithdrawn</c> — the contract
/// carries the full cross-BC payload (<c>WithdrawnBy</c>, <c>Reason</c>, <c>WithdrawnAt</c>);
/// this internal event carries only what the aggregate needs to replay. The two-namespace
/// split mirrors the established <see cref="ListingPublished"/> domain-vs-contract pattern.
/// </remarks>
public sealed record ListingWithdrawn(
    Guid ListingId,
    DateTimeOffset WithdrawnAt);
