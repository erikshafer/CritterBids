using CritterBids.Contracts.Listings;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

// Sticky binding is inert at 6.5.1 for LotWatchAdded/LotWatchRemoved (single-handler chains skip
// sticky grouping) but documents queue ownership and self-heals if a second consumer ever appears.
[StickyHandler("relay-listings-events")]
public static class ListingsOperationsHandler
{
    public static Task Handle(LotWatchAdded message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(LotWatchAdded),
                    $"Watch added by {message.ParticipantId}.",
                    message.AddedAt),
                cancellationToken);

    public static Task Handle(LotWatchRemoved message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(LotWatchRemoved),
                    $"Watch removed by {message.ParticipantId}.",
                    message.RemovedAt),
                cancellationToken);
}
