using CritterBids.Relay.Hubs;

namespace CritterBids.Relay.Notifications;

/// <summary>
/// <see cref="BiddingHub"/> push delivered when a listing closes with a winning bidder.
/// Composed by <c>ListingSoldHandler</c> from <c>CritterBids.Contracts.Auctions.ListingSold</c>
/// and broadcast to the <c>listing:{ListingId}</c> group so every watcher sees the terminal
/// outcome and final hammer price.
/// </summary>
public sealed record ListingSoldNotification(
    Guid ListingId,
    Guid WinnerId,
    decimal HammerPrice,
    int BidCount,
    DateTimeOffset SoldAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;

    // Listing-wide outcome — broadcast to all watchers of the listing.
    Guid? IBiddingHubMessage.BidderId => null;
}
