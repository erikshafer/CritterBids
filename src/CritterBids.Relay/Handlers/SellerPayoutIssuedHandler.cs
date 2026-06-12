using CritterBids.Contracts.Settlement;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

// M8-S6b (ops-feed completion): SellerPayoutIssued gained its OperationsFeedNotification push —
// the Operations settlement queue consumes it (Completed → PaidOut is the queue's terminal
// transition, which must move live). The event carries no ListingId, so the feed entry's
// listingId is null (the SessionCreated / ParticipantSessionStarted precedent).
[StickyHandler("relay-settlement-events")]
public static class SellerPayoutIssuedHandler
{
    public static async Task Handle(
        SellerPayoutIssued message,
        IHubContext<BiddingHub> hub,
        IHubContext<OperationsHub> operationsHub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var notification = new BidderGroupNotification(
            message.SellerId,
            null,
            nameof(SellerPayoutIssued),
            $"Payout issued: {message.PayoutAmount}.",
            message.IssuedAt);

        await Task.WhenAll(
            hub.Clients
                .Group($"bidder:{message.SellerId}")
                .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new OperationsFeedNotification(
                        null,
                        nameof(SellerPayoutIssued),
                        $"Payout issued: {message.PayoutAmount}.",
                        message.IssuedAt),
                    cancellationToken));

        await history.AppendAsync(
            message.SellerId,
            nameof(SellerPayoutIssued),
            $"Payout issued: {message.PayoutAmount}.",
            message.IssuedAt,
            cancellationToken);
    }
}
