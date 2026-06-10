using CritterBids.Contracts.Selling;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

[StickyHandler("relay-selling-events")]
public static class SellingOperationsHandler
{
    public static Task Handle(ListingPublished message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(ListingPublished),
                    $"Listing published: {message.Title}.",
                    message.PublishedAt),
                cancellationToken);

    public static Task Handle(ListingRevised message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(ListingRevised),
                    $"Listing revised: {message.Title}.",
                    message.RevisedAt),
                cancellationToken);

    public static Task Handle(ListingEndedEarly message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(ListingEndedEarly),
                    "Listing ended early.",
                    message.EndedAt),
                cancellationToken);
}
