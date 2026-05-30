namespace CritterBids.Relay.History;

public sealed record NotificationEntry(
    Guid NotificationId,
    string EventType,
    string Payload,
    DateTimeOffset DeliveredAt);
