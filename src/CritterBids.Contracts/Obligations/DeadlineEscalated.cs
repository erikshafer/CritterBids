namespace CritterBids.Contracts.Obligations;

/// <summary>
/// Integration event published by the Obligations BC when a sold listing's ship-by deadline
/// passes with no tracking provided. Emitted by the Post-Sale Coordination saga's
/// <c>SendDeadlineEscalation</c> handler per workshop 005 slice 5.5 (narrative 007 Moment 1 —
/// "the deadline lapses"). Escalation does <b>not</b> terminate the saga: it advances to the
/// non-terminal <c>Escalated</c> state and a later tracking submission still recovers the
/// happy path (narrative 007 Moment 2).
/// Transported via RabbitMQ on the publisher-side queue <c>relay-obligations-events</c>
/// (Wolverine transactional outbox); route wired publish-only in M6-S4.
///
/// <para><b>The fifth Obligations integration event (ADR 005 additive).</b> The four contracts
/// frozen at M6-S1 (<see cref="TrackingInfoProvided"/>, <see cref="ObligationFulfilled"/>,
/// <see cref="DisputeOpened"/>, <see cref="DisputeResolved"/>) omitted escalation, treating it as
/// internal. M6-S4 promotes it to a published contract so real-time operator alerting can cross
/// the broker — the milestone's escalation-path prose ("Operations is notified via
/// <c>DeadlineEscalated</c> on <c>relay-obligations-events</c>") needs the fact to travel, not
/// merely sit in an Obligations-internal read model. Appended to the obligation stream
/// <b>and</b> emitted via <c>OutgoingMessages</c>, matching the <see cref="TrackingInfoProvided"/>
/// append+emit taxonomy.</para>
///
/// Consumed by:
/// - Relay BC (M6): Alerts Operations staff via the <c>ops:staff</c> OperationsHub group so the
///   escalation lands in the staff work queue.
/// - Operations BC (M7): Adds the obligation to the staff escalation board
///   (<c>OperationsObligationsView</c>).
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier. Carried for
///   correlation and at-least-once dedup.
/// - <c>ListingId</c> — the sold listing whose deadline lapsed.
/// - <c>EscalatedAt</c> — handler-stamped timestamp when the deadline escalation fired.
/// </summary>
public sealed record DeadlineEscalated(
    Guid ObligationId,
    Guid ListingId,
    DateTimeOffset EscalatedAt);
