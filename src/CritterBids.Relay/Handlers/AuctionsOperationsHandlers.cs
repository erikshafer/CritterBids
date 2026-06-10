using CritterBids.Contracts.Auctions;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

// M8-S3c (ADR 027): the BidPlaced / ListingSold OperationsHub pushes moved into
// BidPlacedHandler / ListingSoldHandler — sticky dispatch executes at most one handler class per
// (message type, endpoint), and those types' relay-auctions-events slots belong to the
// BiddingHub-push classes. This class keeps the session trio, which only it consumes.
[StickyHandler("relay-auctions-events")]
public static class AuctionsOperationsHandler
{
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
