using CritterBids.Contracts.Settlement;
using CritterBids.Relay.History;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// Relay's participant-facing consumer of <see cref="SettlementCompleted"/>. Pushes a
/// <see cref="SettlementCompletedNotification"/> to the <c>bidder:{WinnerId}</c> group — the
/// winning bidder's settlement confirmation, per the contract docstring.
///
/// Pure consumer (ADR 023 path (b)): returns <see cref="Task"/>, injects
/// <c>IHubContext&lt;BiddingHub&gt;</c>, targets the winner group explicitly, and never publishes.
/// </summary>
[StickyHandler("relay-settlement-events")]
public static class SettlementCompletedHandler
{
    public static async Task Handle(
        SettlementCompleted message,
        IHubContext<BiddingHub> hub,
        INotificationHistoryWriter history,
        CancellationToken cancellationToken)
    {
        var notification = new SettlementCompletedNotification(
            message.SettlementId,
            message.ListingId,
            message.WinnerId,
            message.HammerPrice,
            message.CompletedAt);

        await hub.Clients
            .Group($"bidder:{message.WinnerId}")
            .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);

        await history.AppendAsync(
            message.WinnerId,
            nameof(SettlementCompleted),
            $"Settlement {message.SettlementId} completed at {message.HammerPrice}.",
            message.CompletedAt,
            cancellationToken);
    }
}
