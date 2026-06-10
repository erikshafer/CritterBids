using CritterBids.Contracts.Auctions;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// Relay's consumer of <see cref="ListingSold"/>. Pushes a <see cref="ListingSoldNotification"/>
/// to the <c>listing:{ListingId}</c> BiddingHub group so every watcher sees the terminal outcome
/// and final hammer price, and an <see cref="OperationsFeedNotification"/> to the OperationsHub.
///
/// Pure consumer (ADR 023 path (b)): returns <see cref="Task"/>, injects the hub contexts,
/// targets the groups explicitly, and never publishes.
///
/// <para><b>Single discovered <c>ListingSold</c> handler for the BC (ADR 027, M8-S3c).</b> The
/// OperationsHub push lived in <c>AuctionsOperationsHandler</c> until S3c; sticky dispatch executes
/// at most one handler class per (message type, endpoint), so both pushes now ride the one
/// <c>relay-auctions-events</c> delivery from this class.</para>
/// </summary>
[StickyHandler("relay-auctions-events")]
public static class ListingSoldHandler
{
    public static async Task Handle(
        ListingSold message,
        IHubContext<BiddingHub> hub,
        IHubContext<OperationsHub> operationsHub,
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

        await operationsHub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    "ListingSoldOperations",
                    $"Listing sold at {message.HammerPrice}.",
                    message.SoldAt),
                cancellationToken);

        await history.AppendAsync(
            message.WinnerId,
            nameof(ListingSold),
            $"Listing sold at {message.HammerPrice}.",
            message.SoldAt,
            cancellationToken);
    }
}
