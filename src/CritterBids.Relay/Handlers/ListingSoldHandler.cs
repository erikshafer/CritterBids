using CritterBids.Contracts.Auctions;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// Relay's participant-facing consumer of <see cref="ListingSold"/>. Pushes a
/// <see cref="ListingSoldNotification"/> to the <c>listing:{ListingId}</c> group so every watcher
/// sees the terminal outcome and final hammer price.
///
/// Pure consumer (ADR 023 path (b)): returns <see cref="Task"/>, injects
/// <c>IHubContext&lt;BiddingHub&gt;</c>, targets the group explicitly, and never publishes.
/// </summary>
public static class ListingSoldHandler
{
    public static async Task Handle(
        ListingSold message,
        IHubContext<BiddingHub> hub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var notification = new ListingSoldNotification(
            message.ListingId,
            message.WinnerId,
            message.HammerPrice,
            message.BidCount,
            message.SoldAt);

        await hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);

        await history.AppendAsync(
            message.WinnerId,
            nameof(ListingSold),
            $"Listing sold at {message.HammerPrice}.",
            message.SoldAt,
            cancellationToken);
    }
}
