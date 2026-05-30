using CritterBids.Relay.Hubs;

namespace CritterBids.Relay.Notifications;

/// <summary>
/// <see cref="BiddingHub"/> push delivered to everyone watching a listing when a bid is accepted.
/// Composed by <c>BidPlacedHandler</c> from <c>CritterBids.Contracts.Auctions.BidPlaced</c> and
/// broadcast to the <c>listing:{ListingId}</c> group. <see cref="BidId"/> is carried so clients can
/// dedupe on retry. No reference-data enrichment in M6-S5 (Relay owns no read model yet).
/// </summary>
public sealed record BidPlacedNotification(
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    int BidCount,
    DateTimeOffset OccurredAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;

    // Listing-wide live feed — broadcast to all watchers of the listing, not a single bidder.
    Guid? IBiddingHubMessage.BidderId => null;
}
