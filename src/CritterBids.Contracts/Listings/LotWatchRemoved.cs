namespace CritterBids.Contracts.Listings;

/// <summary>
/// Integration event published when a participant removes a listing from their watchlist.
/// </summary>
public sealed record LotWatchRemoved(
    Guid ListingId,
    Guid ParticipantId,
    DateTimeOffset RemovedAt);
