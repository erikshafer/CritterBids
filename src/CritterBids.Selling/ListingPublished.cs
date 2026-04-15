namespace CritterBids.Selling;

/// <summary>
/// Domain event appended to the <see cref="SellerListing"/> stream when a listing is published.
/// Always follows <see cref="ListingApproved"/> in the same transaction.
/// </summary>
/// <remarks>
/// This is the Selling BC's internal domain event. The corresponding integration contract
/// published to downstream BCs via RabbitMQ is <c>CritterBids.Contracts.Selling.ListingPublished</c>.
/// </remarks>
public sealed record ListingPublished(
    Guid ListingId,
    DateTimeOffset PublishedAt);
