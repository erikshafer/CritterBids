using CritterBids.Participants.Features.StartParticipantSession;

namespace CritterBids.Participants;

public class Participant
{
    public Guid Id { get; set; }

    // Set to true once ParticipantSessionStarted is applied.
    // Read by the M1-S6 RegisterAsSeller handler to verify an active session exists.
    public bool HasActiveSession { get; set; }

    // Set to true once SellerRegistrationCompleted is applied.
    // Read by the M1-S6 RegisterAsSeller handler to enforce idempotency.
    public bool IsRegisteredSeller { get; set; }

    public void Apply(ParticipantSessionStarted @event)
    {
        Id = @event.ParticipantId;
        HasActiveSession = true;
    }
}
