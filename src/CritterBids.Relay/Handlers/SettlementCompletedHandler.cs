using CritterBids.Contracts.Settlement;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// Relay's consumer of <see cref="SettlementCompleted"/>. Pushes a
/// <see cref="SettlementCompletedNotification"/> to the <c>bidder:{WinnerId}</c> group — the
/// winning bidder's settlement confirmation, per the contract docstring — and an
/// <see cref="OperationsFeedNotification"/> to the OperationsHub (M8-S6b ops-feed completion:
/// the Operations settlement queue consumes this event, and the demo's gavel→charged beat
/// must move that board live, not on a poll).
///
/// Pure consumer (ADR 023 path (b)): returns <see cref="Task"/>, injects the hub contexts,
/// targets the groups explicitly, and never publishes.
/// </summary>
[StickyHandler("relay-settlement-events")]
public static class SettlementCompletedHandler
{
    public static async Task Handle(
        SettlementCompleted message,
        IHubContext<BiddingHub> hub,
        IHubContext<OperationsHub> operationsHub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var notification = new SettlementCompletedNotification(
            message.SettlementId,
            message.ListingId,
            message.WinnerId,
            message.HammerPrice,
            message.CompletedAt);

        await Task.WhenAll(
            hub.Clients
                .Group($"bidder:{message.WinnerId}")
                .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken),
            operationsHub.Clients
                .All
                .SendAsync(
                    RelayHubMethods.ReceiveMessage,
                    new OperationsFeedNotification(
                        message.ListingId,
                        nameof(SettlementCompleted),
                        $"Settlement completed at {message.HammerPrice}.",
                        message.CompletedAt),
                    cancellationToken));

        await history.AppendAsync(
            message.WinnerId,
            nameof(SettlementCompleted),
            $"Settlement {message.SettlementId} completed at {message.HammerPrice}.",
            message.CompletedAt,
            cancellationToken);
    }
}
