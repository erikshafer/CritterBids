using CritterBids.Contracts.Auctions;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// Relay's consumer of <see cref="BidPlaced"/>. Pushes a <see cref="BidPlacedNotification"/> to the
/// <c>listing:{ListingId}</c> BiddingHub group so every participant watching the listing sees the
/// new bid in real time, and an <see cref="OperationsFeedNotification"/> to the OperationsHub.
///
/// Pure consumer (ADR 023 path (b)): the handler returns <see cref="Task"/>, injects the hub
/// contexts, and chooses the target groups explicitly. It never returns
/// <c>OutgoingMessages</c>, never calls <c>IMessageBus</c>, and never publishes — its only output is
/// the SignalR pushes.
///
/// <para><b>Single discovered <c>BidPlaced</c> handler for the BC (ADR 027, M8-S3c).</b> The
/// OperationsHub push lived in <c>AuctionsOperationsHandler</c> until S3c; sticky dispatch executes
/// at most one handler class per (message type, endpoint), so both pushes now ride the one
/// <c>relay-auctions-events</c> delivery from this class.</para>
/// </summary>
[StickyHandler("relay-auctions-events")]
public static class BidPlacedHandler
{
    public static async Task Handle(
        BidPlaced message,
        IHubContext<BiddingHub> hub,
        IHubContext<OperationsHub> operationsHub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var notification = new BidPlacedNotification(
            message.ListingId,
            message.BidId,
            message.BidderId,
            message.Amount,
            message.BidCount,
            message.PlacedAt);

        await hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);

        await operationsHub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    "BidPlacedOperations",
                    $"Bid placed at {message.Amount}.",
                    message.PlacedAt),
                cancellationToken);

        await history.AppendAsync(
            message.BidderId,
            nameof(BidPlaced),
            $"Bid {message.BidId} accepted at {message.Amount}.",
            message.PlacedAt,
            cancellationToken);
    }
}
