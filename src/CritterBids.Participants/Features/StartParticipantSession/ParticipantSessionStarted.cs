namespace CritterBids.Participants.Features.StartParticipantSession;

// ParticipantId is the aggregate ID and must be the first property per domain event conventions.
// No "Event" suffix on domain event type names per CLAUDE.md conventions.
public sealed record ParticipantSessionStarted(
    Guid ParticipantId,
    string DisplayName,
    string BidderId,
    decimal CreditCeiling,
    DateTimeOffset StartedAt);
