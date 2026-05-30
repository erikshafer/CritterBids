using Marten;

namespace CritterBids.Relay.History;

public sealed class MartenNotificationHistoryWriter : INotificationHistoryWriter
{
    private readonly IDocumentSession? _session;

    public MartenNotificationHistoryWriter(IDocumentSession? session = null)
    {
        _session = session;
    }

    public async Task AppendAsync(
        Guid bidderId,
        string eventType,
        string payload,
        DateTimeOffset deliveredAt,
        CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        var existing = await _session.LoadAsync<NotificationHistoryView>(bidderId, cancellationToken);
        var entry = new NotificationEntry(
            Guid.CreateVersion7(),
            eventType,
            payload,
            deliveredAt);

        var entries = existing?.Entries
            .Append(entry)
            .ToArray()
            ?? [entry];

        _session.Store(new NotificationHistoryView
        {
            Id = bidderId,
            Entries = entries,
            UpdatedAt = deliveredAt
        });
    }
}
