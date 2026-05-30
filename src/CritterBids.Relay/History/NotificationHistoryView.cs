namespace CritterBids.Relay.History;

public sealed record NotificationHistoryView
{
    public Guid Id { get; init; }

    public Guid BidderId => Id;

    public IReadOnlyList<NotificationEntry> Entries { get; init; } = Array.Empty<NotificationEntry>();

    public DateTimeOffset UpdatedAt { get; init; }
}
