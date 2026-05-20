using CritterBids.Contracts.Auctions;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritterBids.Auctions;

/// <summary>
/// Command to create a new Flash-format Session aggregate. Always creates — no
/// uniqueness check on <see cref="Title"/> (Workshop 002 §5.1; two Flash sessions can
/// share a title). M6 will add the HTTP surface; M5 invokes via <c>IMessageBus</c> only.
/// </summary>
public sealed record CreateSession(string Title, int DurationMinutes);

/// <summary>
/// Wolverine handler for <see cref="CreateSession"/>. Generates a UUID v7 stream id per
/// M4-D2 and opens the Session stream with a <see cref="SessionCreated"/> first event.
///
/// <para><b>Return shape — OQ4 resolution.</b> <c>(CreationResponse&lt;Guid&gt;, IStartStream)</c>
/// inherits the M2 Selling <c>CreateDraftListingHandler</c> aggregate-creation shape.
/// <c>CreationResponse&lt;Guid&gt;</c> echoes the new id back to the caller — useful for
/// dispatch tests today, drop-in for the M6 HTTP wiring later (no <c>[WolverinePost]</c>
/// in M5 per CLAUDE.md's "<c>[AllowAnonymous]</c> through M6" stance). <see cref="MartenOps.StartStream{T}(Guid, object[])"/>
/// is the correct pattern — direct <c>session.Events.StartStream()</c> silently discards
/// events per wolverine-message-handlers anti-pattern #9.</para>
///
/// <para>No aggregate load (no existing stream); no cross-projection validation. The
/// only invariants on creation are the command's own field invariants (Title non-empty,
/// DurationMinutes positive) — neither is enforced here yet per the M4 milestone doc's
/// "no input validation beyond what the workshop scenarios assert" posture.</para>
/// </summary>
public static class CreateSessionHandler
{
    public static (CreationResponse<Guid>, IStartStream) Handle(CreateSession cmd)
    {
        var sessionId = Guid.CreateVersion7();

        var created = new SessionCreated(
            SessionId: sessionId,
            Title: cmd.Title,
            DurationMinutes: cmd.DurationMinutes,
            CreatedAt: DateTimeOffset.UtcNow);

        var stream = MartenOps.StartStream<Session>(sessionId, created);

        return (new CreationResponse<Guid>($"/api/sessions/{sessionId}", sessionId), stream);
    }
}
