using CritterBids.Relay.Hubs;

namespace CritterBids.Relay.Notifications;

public sealed record ListingGroupNotification(
    Guid ListingId,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;

    Guid? IBiddingHubMessage.BidderId => null;
}
