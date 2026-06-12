using CritterBids.Contracts.Settlement;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;
using Wolverine.Attributes;

namespace CritterBids.Relay.Handlers;

/// <summary>
/// M8-S6b (ops-feed completion): Relay's first <see cref="PaymentFailed"/> consumer. A failed
/// payment is the settlement queue's staff-attention state (<c>Status == Failed</c> with a
/// <c>FailureReason</c>), so the push targets the ops feed alone — there is no participant-facing
/// BiddingHub notification for it in MVP (the winner-side recovery journey is a recorded
/// deferral), and no history entry. Rides the existing <c>relay-settlement-events</c> queue; the
/// publish route for this event is added in <c>Program.cs</c> alongside this handler. Sibling
/// settlement-family classes (<see cref="SettlementCompletedHandler"/>,
/// <see cref="SellerPayoutIssuedHandler"/>) keep their own message types — one sticky class per
/// (message type, endpoint).
/// </summary>
[StickyHandler("relay-settlement-events")]
public static class SettlementOperationsHandler
{
    public static Task Handle(
        PaymentFailed message,
        IHubContext<OperationsHub> operationsHub,
        CancellationToken cancellationToken) =>
        operationsHub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    message.ListingId,
                    nameof(PaymentFailed),
                    $"Payment failed ({message.Reason}).",
                    message.FailedAt),
                cancellationToken);
}
