using CritterBids.Contracts.Participants;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Operations;

/// <summary>
/// Operations BC's <b>Participants-family</b> participant-activity consumer — the single ADR-014
/// Path A, Sub-Option A sibling handler that maintains <see cref="ParticipantActivityView"/> (W006
/// §5b). Participants is the only source BC and <c>ParticipantSessionStarted</c> the only event, so
/// this handler has one <c>Handle</c> overload. It returns <see cref="Task"/> and writes only via the
/// injected Marten session — Operations is a pure consumer, so there are <b>no</b>
/// <c>OutgoingMessages</c> and <b>no</b> <c>IMessageBus</c> (it publishes nothing).
///
/// <para><b>Tolerant upsert.</b> Loads-or-constructs the row by
/// <see cref="ParticipantActivityView.ParticipantId"/>, populates all five W006 §5b fields, and
/// stores. The payload is immutable for the participant's lifetime, so a re-delivery harmlessly
/// rewrites identical data — no status guard is needed (single event, no lifecycle axis). The MVP
/// carries <c>StartedAt</c> only; there is no participant session-close event in the contract set.</para>
/// </summary>
[StickyHandler("operations-participants-events")]
public static class ParticipantActivityHandler
{
    public static async Task Handle(
        ParticipantSessionStarted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<ParticipantActivityView>(message.ParticipantId, cancellationToken);
        var view = existing ?? new ParticipantActivityView { ParticipantId = message.ParticipantId };

        session.Store(view with
        {
            DisplayName   = message.DisplayName,
            BidderId      = message.BidderId,
            CreditCeiling = message.CreditCeiling,
            StartedAt     = message.StartedAt,
        });
    }
}
