namespace CritterBids.Operations;

/// <summary>
/// Operations BC's participant-activity read model — a staff-facing row per participant session,
/// folded from the single Participants-family event <c>ParticipantSessionStarted</c> (W006 §5b).
/// Operations is a <b>pure consumer</b> (ADR-014 Path A, Sub-Option A): a single sibling handler
/// (<see cref="ParticipantActivityHandler"/>) upserts this <see cref="ParticipantId"/>-keyed
/// document. It appends to no local stream and publishes nothing.
///
/// <para><b>Lifecycle.</b> A tolerant upsert keyed by <see cref="ParticipantId"/>. All five fields
/// come from the one <c>ParticipantSessionStarted</c> payload (immutable for the participant's
/// lifetime — narrative 003 / W001 §0.2), so the handler needs only idempotent-upsert tolerance and
/// no status guard. The MVP carries <see cref="StartedAt"/> only — there is no participant
/// session-close event in the contract set (W006 §5b), so the board shows started-and-active
/// participants with no end timestamp.</para>
///
/// <para><b>BidderId is a <c>string</c>.</b> The participant identifier is the Participants-side
/// short display correlation (e.g. "<c>Bidder 4217</c>"), a <c>string</c> on the payload — not a
/// <see cref="System.Guid"/>, and never "paddle" (the project-wide ban).</para>
///
/// <para><b>Marten Id convention.</b> <see cref="ParticipantId"/> doubles as the Marten document key,
/// exposed via the <see cref="Id"/> expression-bodied alias — the natural-key-as-id idiom shared
/// with the other Operations views. No <c>.Identity()</c> override is needed in the module.</para>
/// </summary>
public sealed record ParticipantActivityView
{
    /// <summary>The participant this row tracks. Carried by <c>ParticipantSessionStarted.ParticipantId</c>; doubles as the Marten document key.</summary>
    public Guid ParticipantId { get; init; }

    /// <summary>The participant's display name. Carried by <c>ParticipantSessionStarted.DisplayName</c>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The short bidder correlation ("Bidder 4217"). Carried by <c>ParticipantSessionStarted.BidderId</c> — a <c>string</c>, never "paddle".</summary>
    public string? BidderId { get; init; }

    /// <summary>The initial per-bidder credit cap assigned at session start. Carried by <c>ParticipantSessionStarted.CreditCeiling</c>.</summary>
    public decimal CreditCeiling { get; init; }

    /// <summary>When the participant session started. Carried by <c>ParticipantSessionStarted.StartedAt</c>.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Marten document key — equals <see cref="ParticipantId"/>. Expression-bodied to keep the
    /// storage shape identical to the natural-key-as-id pattern shared with the other Operations
    /// views; no <c>.Identity()</c> override is needed in the module.
    /// </summary>
    public Guid Id => ParticipantId;
}
