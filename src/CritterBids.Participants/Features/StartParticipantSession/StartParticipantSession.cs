using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;
using Wolverine.Polecat;

namespace CritterBids.Participants.Features.StartParticipantSession;

public sealed record StartParticipantSession;

public static class StartParticipantSessionHandler
{
    // Word lists for display name generation.
    // Names are derived from UUID v7 random bytes — uniqueness is guaranteed by stream ID uniqueness.
    // See docs/decisions/007-uuid-strategy.md for rationale on UUID v7 for Participants.
    private static readonly string[] Adjectives =
    [
        "Bold", "Swift", "Fierce", "Calm", "Bright", "Nimble", "Keen",
        "Brave", "Clever", "Daring", "Epic", "Grand", "Hardy", "Iron",
        "Jolly", "Lively", "Mighty", "Noble", "Proud", "Quick",
        "Royal", "Sharp", "Tough", "Vivid", "Wild"
    ];

    private static readonly string[] Animals =
    [
        "Ferret", "Penguin", "Otter", "Falcon", "Lynx", "Badger",
        "Crane", "Dingo", "Eagle", "Fox", "Gecko", "Heron", "Ibis",
        "Jaguar", "Koala", "Lemur", "Mink", "Narwhal", "Ocelot",
        "Parrot", "Quail", "Raven", "Stoat", "Tapir", "Viper",
        "Weasel", "Xerus", "Yak", "Zebra"
    ];

    // M1 override: [AllowAnonymous] on all Participants endpoints during M1.
    // Real authentication is deferred to M6 (see §3 and §6 of M1-skeleton.md).
    // The global [Authorize] convention in CLAUDE.md is explicitly overridden here.
    [WolverinePost("/api/participants/session")]
    [AllowAnonymous]
    public static (CreationResponse<Guid>, IStartStream) Handle(StartParticipantSession cmd)
    {
        // Stream ID is UUID v7: StartParticipantSession has no natural business key,
        // so UUID v5 determinism does not apply. UUID v7 is correct for anonymous participants.
        // See docs/decisions/007-uuid-strategy.md.
        var participantId = Guid.CreateVersion7();

        var bytes = participantId.ToByteArray();

        // Bytes 8–11 are in the random portion of UUID v7 (ToByteArray() Data4 section — big-endian).
        // UUID v7 uses time-ordered high bytes and independently randomized low bytes.
        // Two UUIDs created in the same millisecond share only the timestamp prefix (bytes 0–7);
        // bytes 8–15 are independently random, ensuring display name uniqueness.
        var adjIdx = bytes[8] % Adjectives.Length;
        var aniIdx = bytes[9] % Animals.Length;
        var num = ((bytes[10] << 8) | bytes[11]) % 9999 + 1;
        var displayName = $"{Adjectives[adjIdx]}{Animals[aniIdx]}{num}";

        // BidderId: UUID-derived short identifier in "Bidder N" format (1–9999).
        // Sequential counters require mutable state outside the aggregate — deferred post-M1.
        var bidderNum = ((bytes[12] << 8) | bytes[13]) % 9999 + 1;
        var bidderId = $"Bidder {bidderNum}";

        // CreditCeiling: randomly assigned in range 200–1000 (nine discrete values, 100-unit steps).
        // Derived deterministically from the stream ID. Hidden from participants; never returned
        // in HTTP responses. See 001-scenarios.md §0.2.
        var creditCeiling = 200m + (bytes[14] % 9) * 100m;

        var evt = new ParticipantSessionStarted(
            participantId,
            displayName,
            bidderId,
            creditCeiling,
            DateTimeOffset.UtcNow);

        // PolecatOps.StartStream<T> is the correct pattern — direct session.Events.StartStream()
        // silently discards events (anti-pattern #9 in wolverine-message-handlers.md).
        var stream = PolecatOps.StartStream<Participant>(participantId, evt);

        // HTTP response type must be first in the tuple (anti-pattern #3 in wolverine-message-handlers.md).
        return (new CreationResponse<Guid>($"/api/participants/{participantId}", participantId), stream);
    }
}
