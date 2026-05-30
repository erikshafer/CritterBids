namespace CritterBids.Contracts.Selling;

/// <summary>
/// Integration event published when a seller revises mutable listing fields after publication.
/// </summary>
public sealed record ListingRevised(
    Guid ListingId,
    Guid SellerId,
    string Title,
    string Description,
    string ShippingTerms,
    DateTimeOffset RevisedAt);
