using CritterBids.Contracts.Auctions;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// Relay's participant-facing consumer of <see cref="BidPlaced"/>. Pushes a
/// <see cref="BidPlacedNotification"/> to the <c>listing:{ListingId}</c> group so every participant
/// watching the listing sees the new bid in real time.
///
/// Pure consumer (ADR 023 path (b)): the handler returns <see cref="Task"/>, injects
/// <c>IHubContext&lt;BiddingHub&gt;</c>, and chooses the target group explicitly. It never returns
/// <c>OutgoingMessages</c>, never calls <c>IMessageBus</c>, and never publishes — its only output is
/// the SignalR push.
/// </summary>
public static class BidPlacedHandler
{
    public static async Task Handle(
        BidPlaced message,
        IHubContext<BiddingHub> hub,
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

        await history.AppendAsync(
            message.BidderId,
            nameof(BidPlaced),
            $"Bid {message.BidId} accepted at {message.Amount}.",
            message.PlacedAt,
            cancellationToken);
    }
}
