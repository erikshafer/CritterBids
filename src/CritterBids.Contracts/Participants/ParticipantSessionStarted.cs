namespace CritterBids.Contracts.Participants;

/// <summary>
/// Integration event published by the Participants BC when a new anonymous session is started
/// via <c>POST /api/participants/session</c>. Promoted from a Participants-internal record to a
/// cross-BC contract at M5-S5 so the Settlement BC's <c>BidderCreditViewHandler</c> can seed
/// the per-bidder credit projection with the assigned <see cref="CreditCeiling"/>.
///
/// <para><b>Origin and lifecycle.</b> Emitted by the <c>StartParticipantSessionHandler</c>
/// (Participants BC) into the participant's event stream at session-creation time. The
/// payload is immutable for the participant's lifetime — display name, bidder id, and
/// credit ceiling are all assigned once at session start per narrative 003 / W001 §0.2.</para>
///
/// <para><b>Consumed by:</b>
/// <list type="bullet">
///   <item>Settlement BC <c>BidderCreditViewHandler</c> (M5-S5): seeds the per-bidder credit
///         row with <c>RemainingCredit = CreditCeiling</c>, <c>LastChargedSettlementId = null</c>
///         per W003 Phase 1 Part 7. Idempotent under at-least-once redelivery — a row already
///         charged (<c>LastChargedSettlementId != null</c>) is preserved on re-delivery rather
///         than reset to ceiling.</item>
///   <item>Relay BC (post-M5): bidder-side push handler may correlate session-started
///         broadcasts with the live connection identity.</item>
/// </list>
/// </para>
///
/// <para><b>Field rationale:</b>
/// <list type="bullet">
///   <item><c>ParticipantId</c> — Participants-side aggregate id; doubles as the Marten event
///         stream key and the Settlement-side <c>BidderCreditView.BidderId</c> document key.</item>
///   <item><c>DisplayName</c> — derived from UUID v7 random bytes (per narrative 001 Finding 002);
///         carried for Relay / Operations rendering without a cross-BC participant read.</item>
///   <item><c>BidderId</c> — the Participants-side short display correlation
///         ("<c>Bidder 4217</c>") shown in auction UIs; distinct from <c>ParticipantId</c>.</item>
///   <item><c>CreditCeiling</c> — the initial per-bidder credit cap assigned at session start.
///         Drives the Settlement-side <c>BidderCreditView.RemainingCredit</c> seed value.</item>
///   <item><c>StartedAt</c> — handler-stamped timestamp at session-creation; used as the
///         initial <c>UpdatedAt</c> on the bidder-credit projection row.</item>
/// </list>
/// </para>
/// </summary>
public sealed record ParticipantSessionStarted(
    Guid ParticipantId,
    string DisplayName,
    string BidderId,
    decimal CreditCeiling,
    DateTimeOffset StartedAt);
