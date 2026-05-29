namespace CritterBids.Contracts.Obligations;

/// <summary>
/// Integration event published by the Obligations BC when a dispute is raised against a
/// post-sale obligation. Emitted by the Post-Sale Coordination saga's <c>OpenDispute</c>
/// handler per workshop 005 slice 5.7. Opening a dispute does not terminate the saga —
/// the dispute sub-workflow runs to a <see cref="DisputeResolved"/> outcome.
/// Transported via RabbitMQ on the publisher-side queue <c>relay-obligations-events</c>
/// (Wolverine transactional outbox); route wired in M6-S4.
///
/// Consumed by:
/// - Relay BC (M6): Alerts Operations staff via the <c>ops:staff</c> OperationsHub group
///   so a dispute lands in the staff work queue.
/// - Operations BC (M7): Adds the dispute to the staff dispute-resolution board.
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier the dispute
///   belongs to. Carried for correlation and at-least-once dedup.
/// - <c>ListingId</c> — the sold listing under dispute.
/// - <c>DisputeId</c> — the dispute instance identifier. Distinct from <c>ObligationId</c>
///   so the resolution event (<see cref="DisputeResolved"/>) can be matched to the specific
///   dispute even though MVP allows one open dispute per obligation at a time.
/// - <c>RaisedBy</c> — the participant who raised the dispute. In MVP this is the winner
///   (<c>NonDelivery</c> / <c>ItemCondition</c>) or Operations staff escalating a missed
///   deadline (<c>MissedDeadline</c>) per W005-3.
/// - <c>Reason</c> — a string-valued enum carrying one of <c>"NonDelivery"</c>,
///   <c>"ItemCondition"</c>, or <c>"MissedDeadline"</c>. Stored as a string rather than an
///   int-valued enum so the wire contract stays decoupled from any enum type, matching the
///   <c>ListingPassed.Reason</c> precedent. Consumers pattern-match on the string constant.
/// - <c>OpenedAt</c> — handler-stamped timestamp when the dispute was raised.
/// </summary>
public sealed record DisputeOpened(
    Guid ObligationId,
    Guid ListingId,
    Guid DisputeId,
    Guid RaisedBy,
    string Reason,
    DateTimeOffset OpenedAt);
