using CritterBids.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritterBids.Participants.Features.RegisterAsSeller;

/// <summary>
/// Command to register a participant as a seller.
/// ParticipantId is resolved from the {id} route segment via the [WriteAggregate] workflow:
/// FindIdentity tries "participantId" (camelCase of aggregate name + "Id") first, then falls
/// back to "id" (the route variable). Sending ParticipantId in the request body ensures the
/// first path is taken and the correct participant is loaded.
/// </summary>
public sealed record RegisterAsSeller(Guid ParticipantId);

public static class RegisterAsSellerHandler
{
    // Rejection scenario (a): participant has no active session.
    // NOTE: [WriteAggregate]'s OnMissing.Simple404 handles the "stream does not exist" case
    // (returns 404 before Before() is reached). The HasActiveSession check below handles the
    // theoretically impossible case of a stream with no session — retained for defence-in-depth
    // and to make the rejection intent explicit.
    //
    // Rejection scenario (b): participant is already registered as a seller — idempotency guard.
    public static ProblemDetails Before(RegisterAsSeller cmd, Participant participant)
    {
        if (!participant.HasActiveSession)
            return new ProblemDetails
            {
                Detail = "Participant has no active session.",
                Status = StatusCodes.Status400BadRequest
            };

        if (participant.IsRegisteredSeller)
            return new ProblemDetails
            {
                Detail = "Participant is already registered as a seller.",
                Status = StatusCodes.Status409Conflict
            };

        return WolverineContinue.NoProblems;
    }

    // M1 override: [AllowAnonymous] on all Participants endpoints during M1.
    // Real authentication is deferred to M6 (§3 and §6 of M1-skeleton.md).
    [WolverinePost("/api/participants/{id}/register-seller")]
    [AllowAnonymous]
    public static (IResult, SellerRegistered, OutgoingMessages) Handle(
        RegisterAsSeller cmd,
        [WriteAggregate] Participant participant)
    {
        var evt = new SellerRegistered(participant.Id, DateTimeOffset.UtcNow);

        // Integration event published via OutgoingMessages (transactional outbox).
        // Never use IMessageBus.PublishAsync() here — anti-pattern #11 in wolverine-message-handlers.md.
        var outgoing = new OutgoingMessages();
        outgoing.Add(new SellerRegistrationCompleted(participant.Id, evt.CompletedAt));

        // Returns 200 OK — appending to an existing resource, not creating a new one.
        return (Results.Ok(), evt, outgoing);
    }
}
