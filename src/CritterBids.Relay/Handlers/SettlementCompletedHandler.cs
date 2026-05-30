using CritterBids.Contracts.Settlement;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// Relay's participant-facing consumer of <see cref="SettlementCompleted"/>. Pushes a
/// <see cref="SettlementCompletedNotification"/> to the <c>bidder:{WinnerId}</c> group — the
/// winning bidder's settlement confirmation, per the contract docstring.
///
/// Pure consumer (ADR 023 path (b)): returns <see cref="Task"/>, injects
/// <c>IHubContext&lt;BiddingHub&gt;</c>, targets the winner group explicitly, and never publishes.
/// </summary>
public static class SettlementCompletedHandler
{
    public static Task Handle(
        SettlementCompleted message,
        IHubContext<BiddingHub> hub,
        CancellationToken cancellationToken)
    {
        var notification = new SettlementCompletedNotification(
            message.SettlementId,
            message.ListingId,
            message.WinnerId,
            message.HammerPrice,
            message.CompletedAt);

        return hub.Clients
            .Group($"bidder:{message.WinnerId}")
            .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);
    }
}
