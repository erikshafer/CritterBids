namespace CritterBids.Contracts.Listings;

/// <summary>
/// Integration event published when a participant adds a listing to their watchlist.
/// </summary>
public sealed record LotWatchAdded(
    Guid ListingId,
    Guid ParticipantId,
    DateTimeOffset AddedAt);
