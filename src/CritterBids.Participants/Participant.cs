using CritterBids.Participants.Features.StartParticipantSession;
using SellerRegistered = CritterBids.Participants.Features.RegisterAsSeller.SellerRegistered;

namespace CritterBids.Participants;

public class Participant
{
    public Guid Id { get; set; }

    // Set to true once ParticipantSessionStarted is applied.
    // Read by RegisterAsSellerHandler.Before() to verify an active session exists.
    public bool HasActiveSession { get; set; }

    // Set to true once SellerRegistered is applied.
    // Read by RegisterAsSellerHandler.Before() to enforce idempotency.
    public bool IsRegisteredSeller { get; set; }

    public void Apply(ParticipantSessionStarted @event)
    {
        Id = @event.ParticipantId;
        HasActiveSession = true;
    }

    public void Apply(SellerRegistered @event)
    {
        IsRegisteredSeller = true;
    }
}
