namespace CritterBids.Contracts.Selling;

/// <summary>
/// Integration event published when a seller ends a listing before its scheduled close.
/// </summary>
public sealed record ListingEndedEarly(
    Guid ListingId,
    Guid SellerId,
    string? Reason,
    DateTimeOffset EndedAt);
