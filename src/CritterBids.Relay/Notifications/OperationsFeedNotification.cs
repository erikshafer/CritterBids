using CritterBids.Relay.Hubs;

namespace CritterBids.Relay.Notifications;

public sealed record OperationsFeedNotification(
    Guid? ListingId,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt) : IOperationsHubMessage;
