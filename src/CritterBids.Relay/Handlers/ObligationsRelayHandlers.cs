using CritterBids.Contracts.Obligations;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

[StickyHandler("relay-obligations-events")]
public static class ObligationsRelayHandler
{
    /// <summary>
    /// M8-S6b (ops-feed completion): Relay's first <see cref="DeadlineEscalated"/> consumer — the
    /// handler whose absence made M8-S3c remove the <c>relay-obligations-events</c> publish route
    /// ("restore the route together with the Relay handler if it ships"; the route is restored in
    /// <c>Program.cs</c> alongside this method). Escalation is an operator-only fact: it lands in
    /// Morgan's escalation queue, so the push targets the ops feed alone — no participant-facing
    /// BiddingHub notification and no history entry (the seller's own vantage is narrative 007's
    /// reminder chain, not this alert).
    /// </summary>
    public static Task Handle(
        DeadlineEscalated message,
        IHubContext<OperationsHub> operationsHub,
        CancellationToken cancellationToken) =>
        operationsHub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(DeadlineEscalated),
                    "Ship-by deadline escalated without tracking.",
                    message.EscalatedAt),
                cancellationToken);

    public static async Task Handle(
        TrackingInfoProvided message,
        IHubContext<BiddingHub> biddingHub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var targetBidderId = message.WinnerId ?? message.SellerId;
        var notification = new BidderGroupNotification(
            targetBidderId,
            message.ListingId,
            nameof(TrackingInfoProvided),
            $"Tracking provided: {message.TrackingNumber}.",
            message.ProvidedAt);

        await biddingHub.Clients
            .Group($"bidder:{targetBidderId}")
            .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);

        await history.AppendAsync(
            targetBidderId,
            nameof(TrackingInfoProvided),
            $"Tracking provided: {message.TrackingNumber}.",
            message.ProvidedAt,
            cancellationToken);
    }

    public static async Task Handle(
        ObligationFulfilled message,
        IHubContext<BiddingHub> biddingHub,
        IHubContext<OperationsHub> operationsHub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var winnerNotification = new BidderGroupNotification(
            message.WinnerId,
            message.ListingId,
            nameof(ObligationFulfilled),
            "Obligation fulfilled.",
            message.FulfilledAt);
        var sellerNotification = new BidderGroupNotification(
            message.SellerId,
            message.ListingId,
            nameof(ObligationFulfilled),
            "Obligation fulfilled.",
            message.FulfilledAt);

        // M8-S6b (ops-feed completion): fulfilment is the obligation's true terminal — the
        // Operations view drops it from every active queue, so the ops feed must say so live.
        var operationsNotification = new OperationsFeedNotification(
            message.ListingId,
            nameof(ObligationFulfilled),
            "Obligation fulfilled.",
            message.FulfilledAt);

        await Task.WhenAll(
            biddingHub.Clients
                .Group($"bidder:{message.WinnerId}")
                .SendAsync(RelayHubMethods.ReceiveMessage, winnerNotification, cancellationToken),
            biddingHub.Clients
                .Group($"bidder:{message.SellerId}")
                .SendAsync(RelayHubMethods.ReceiveMessage, sellerNotification, cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(RelayHubMethods.ReceiveMessage, operationsNotification, cancellationToken));

        await Task.WhenAll(
            history.AppendAsync(
                message.WinnerId,
                nameof(ObligationFulfilled),
                "Obligation fulfilled.",
                message.FulfilledAt,
                cancellationToken),
            history.AppendAsync(
                message.SellerId,
                nameof(ObligationFulfilled),
                "Obligation fulfilled.",
                message.FulfilledAt,
                cancellationToken));
    }

    public static async Task Handle(
        DisputeOpened message,
        IHubContext<BiddingHub> biddingHub,
        IHubContext<OperationsHub> operationsHub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var bidderNotification = new BidderGroupNotification(
            message.RaisedBy,
            message.ListingId,
            nameof(DisputeOpened),
            $"Dispute opened ({message.Reason}).",
            message.OpenedAt);

        var operationsNotification = new OperationsFeedNotification(
            message.ListingId,
            nameof(DisputeOpened),
            $"Dispute opened ({message.Reason}).",
            message.OpenedAt);

        await Task.WhenAll(
            biddingHub.Clients
                .Group($"bidder:{message.RaisedBy}")
                .SendAsync(RelayHubMethods.ReceiveMessage, bidderNotification, cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(RelayHubMethods.ReceiveMessage, operationsNotification, cancellationToken));

        await history.AppendAsync(
            message.RaisedBy,
            nameof(DisputeOpened),
            $"Dispute opened ({message.Reason}).",
            message.OpenedAt,
            cancellationToken);
    }

    public static async Task Handle(
        DisputeResolved message,
        IHubContext<BiddingHub> biddingHub,
        IHubContext<OperationsHub> operationsHub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var opsNotification = new OperationsFeedNotification(
            message.ListingId,
            nameof(DisputeResolved),
            $"Dispute resolved ({message.ResolutionType}).",
            message.ResolvedAt);

        if (message.ParticipantId is { } participantId)
        {
            var bidderNotification = new BidderGroupNotification(
                participantId,
                message.ListingId,
                nameof(DisputeResolved),
                $"Dispute resolved ({message.ResolutionType}).",
                message.ResolvedAt);

            await Task.WhenAll(
                biddingHub.Clients
                    .Group($"bidder:{participantId}")
                    .SendAsync(RelayHubMethods.ReceiveMessage, bidderNotification, cancellationToken),
                operationsHub.Clients
                    .All
                    .SendAsync(RelayHubMethods.ReceiveMessage, opsNotification, cancellationToken));

            await history.AppendAsync(
                participantId,
                nameof(DisputeResolved),
                $"Dispute resolved ({message.ResolutionType}).",
                message.ResolvedAt,
                cancellationToken);
            return;
        }

        await Task.WhenAll(
            biddingHub.Clients
                .Group($"listing:{message.ListingId}")
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new ListingGroupNotification(
                        message.ListingId,
                        nameof(DisputeResolved),
                        $"Dispute resolved ({message.ResolutionType}).",
                        message.ResolvedAt),
                    cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(RelayHubMethods.ReceiveMessage, opsNotification, cancellationToken));
    }
}
