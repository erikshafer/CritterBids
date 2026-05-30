namespace CritterBids.Relay.History;

public interface INotificationHistoryWriter
{
    Task AppendAsync(
        Guid bidderId,
        string eventType,
        string payload,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken);
}
