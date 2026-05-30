using CritterBids.Contracts;
using CritterBids.Contracts.Participants;
using CritterBids.Relay.Hubs;
using CritterBids.Relay.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Handlers;

public static class ParticipantsOperationsHandler
{
    public static Task Handle(ParticipantSessionStarted message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    null,
                    nameof(ParticipantSessionStarted),
                    $"Participant session started for {message.DisplayName}.",
                    message.StartedAt),
                cancellationToken);

    public static Task Handle(SellerRegistrationCompleted message, IHubContext<OperationsHub> hub, CancellationToken cancellationToken) =>
        hub.Clients
            .All
            .SendAsync(
                RelayHubMethods.ReceiveMessage,
                new OperationsFeedNotification(
                    null,
                    nameof(SellerRegistrationCompleted),
                    $"Seller registration completed for participant {message.ParticipantId}.",
                    message.CompletedAt),
                cancellationToken);
}
