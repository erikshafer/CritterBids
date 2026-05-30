using CritterBids.Contracts.Settlement;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Handlers;

public static class SellerPayoutIssuedHandler
{
    public static async Task Handle(
        SellerPayoutIssued message,
        IHubContext<BiddingHub> hub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var notification = new BidderGroupNotification(
            message.SellerId,
            null,
            nameof(SellerPayoutIssued),
            $"Payout issued: {message.PayoutAmount}.",
            message.IssuedAt);

        await hub.Clients
            .Group($"bidder:{message.SellerId}")
            .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);

        await history.AppendAsync(
            message.SellerId,
            nameof(SellerPayoutIssued),
            $"Payout issued: {message.PayoutAmount}.",
            message.IssuedAt,
            cancellationToken);
    }
}
