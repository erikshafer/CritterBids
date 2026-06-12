using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

// M8-S6b (ops-feed completion): BiddingOpened / ListingPassed / ListingWithdrawn gained their
// OperationsFeedNotification pushes here — the Operations lot board consumes all three, and the
// topology invariant (OperationsFeedTopologyTests) requires every Operations-consumed event to
// reach the ops feed. The ops push rides the same handler as the BiddingHub push because sticky
// dispatch executes at most one handler class per (message type, endpoint) — the
// BidPlacedHandler / ListingSoldHandler dual-push template.
[StickyHandler("relay-auctions-events")]
public static class AuctionsBiddingHandler
{
    public static Task Handle(
        BiddingOpened message,
        IHubContext<BiddingHub> hub,
        IHubContext<OperationsHub> operationsHub,
        CancellationToken cancellationToken) =>
        Task.WhenAll(
            hub.Clients
                .Group($"listing:{message.ListingId}")
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new ListingGroupNotification(
                        message.ListingId,
                        nameof(BiddingOpened),
                        $"Bidding opened at starting bid {message.StartingBid}.",
                        message.OpenedAt),
                    cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new OperationsFeedNotification(
                        message.ListingId,
                        nameof(BiddingOpened),
                        $"Bidding opened at starting bid {message.StartingBid}.",
                        message.OpenedAt),
                    cancellationToken));

    public static async Task Handle(
        BidRejected message,
        IHubContext<BiddingHub> hub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        await hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new ListingGroupNotification(
                    message.ListingId,
                    nameof(BidRejected),
                    $"Bid rejected: {message.Reason}.",
                    message.RejectedAt),
                cancellationToken);

        if (message.BidderId is { } bidderId)
        {
            await history.AppendAsync(
                bidderId,
                nameof(BidRejected),
                $"Bid rejected: {message.Reason}.",
                message.RejectedAt,
                cancellationToken);
        }
    }

    public static Task Handle(ReserveMet message, IHubContext<BiddingHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new ListingGroupNotification(
                    message.ListingId,
                    nameof(ReserveMet),
                    $"Reserve met at {message.Amount}.",
                    message.MetAt),
                cancellationToken);

    public static Task Handle(ExtendedBiddingTriggered message, IHubContext<BiddingHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new ListingGroupNotification(
                    message.ListingId,
                    nameof(ExtendedBiddingTriggered),
                    $"Close moved from {message.PreviousCloseAt:O} to {message.NewCloseAt:O}.",
                    message.TriggeredAt),
                cancellationToken);

    public static Task Handle(
        ListingPassed message,
        IHubContext<BiddingHub> hub,
        IHubContext<OperationsHub> operationsHub,
        CancellationToken cancellationToken) =>
        Task.WhenAll(
            hub.Clients
                .Group($"listing:{message.ListingId}")
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new ListingGroupNotification(
                        message.ListingId,
                        nameof(ListingPassed),
                        $"Listing passed ({message.Reason}).",
                        message.PassedAt),
                    cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new OperationsFeedNotification(
                        message.ListingId,
                        nameof(ListingPassed),
                        $"Listing passed ({message.Reason}).",
                        message.PassedAt),
                    cancellationToken));

    public static Task Handle(
        ListingWithdrawn message,
        IHubContext<BiddingHub> hub,
        IHubContext<OperationsHub> operationsHub,
        CancellationToken cancellationToken) =>
        Task.WhenAll(
            hub.Clients
                .Group($"listing:{message.ListingId}")
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new ListingGroupNotification(
                        message.ListingId,
                        nameof(ListingWithdrawn),
                        "Listing withdrawn.",
                        message.WithdrawnAt),
                    cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new OperationsFeedNotification(
                        message.ListingId,
                        nameof(ListingWithdrawn),
                        "Listing withdrawn.",
                        message.WithdrawnAt),
                    cancellationToken));

    public static async Task Handle(
        ProxyBidExhausted message,
        IHubContext<BiddingHub> hub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        await hub.Clients
            .Group($"bidder:{message.BidderId}")
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new BidderGroupNotification(
                    message.BidderId,
                    message.ListingId,
                    nameof(ProxyBidExhausted),
                    $"Proxy exhausted at max {message.MaxAmount}.",
                    message.ExhaustedAt),
                cancellationToken);

        await history.AppendAsync(
            message.BidderId,
            nameof(ProxyBidExhausted),
            $"Proxy exhausted at max {message.MaxAmount}.",
            message.ExhaustedAt,
            cancellationToken);
    }

    public static Task Handle(BuyItNowPurchased message, IHubContext<BiddingHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new ListingGroupNotification(
                    message.ListingId,
                    nameof(BuyItNowPurchased),
                    $"Buy It Now purchased for {message.Price}.",
                    message.PurchasedAt),
                cancellationToken);

    public static Task Handle(BuyItNowOptionRemoved message, IHubContext<BiddingHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .Group($"listing:{message.ListingId}")
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new ListingGroupNotification(
                    message.ListingId,
                    nameof(BuyItNowOptionRemoved),
                    "Buy It Now option removed.",
                    message.RemovedAt),
                cancellationToken);
}
