using CritterBids.Contracts.Auctions;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Handlers;

public static class AuctionsOperationsHandler
{
    public static Task Handle(BidPlaced message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    "BidPlacedOperations",
                    $"Bid placed at {message.Amount}.",
                    message.PlacedAt),
                cancellationToken);

    public static Task Handle(ListingSold message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    "ListingSoldOperations",
                    $"Listing sold at {message.HammerPrice}.",
                    message.SoldAt),
                cancellationToken);

    public static Task Handle(SessionCreated message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    null,
                    nameof(SessionCreated),
                    $"Session created: {message.Title}.",
                    message.CreatedAt),
                cancellationToken);

    public static Task Handle(SessionStarted message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    null,
                    nameof(SessionStarted),
                    $"Session started with {message.ListingIds.Count} listings.",
                    message.StartedAt),
                cancellationToken);

    public static Task Handle(ListingAttachedToSession message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(ListingAttachedToSession),
                    $"Listing attached to session {message.SessionId}.",
                    message.AttachedAt),
                cancellationToken);
}
