namespace CritterBids.Contracts.Obligations;

/// <summary>
/// Integration event published by the Obligations BC when Operations staff resolve an open
/// dispute. Emitted by the Post-Sale Coordination saga's <c>ResolveDispute</c> handler per
/// workshop 005 slice 5.8.
/// The <c>ResolutionType</c> drives the saga's terminal behavior (W005 Decision 5):
/// <c>Refund</c> and <c>Closed</c> are terminal and the saga calls <c>MarkCompleted()</c>;
/// <c>Extension</c> is the one deliberate non-terminal path — it reschedules a fresh
/// ship-by deadline and the saga continues in the awaiting-tracking state.
/// Transported via RabbitMQ on the publisher-side queue <c>relay-obligations-events</c>
/// (Wolverine transactional outbox); route wired in M6-S4.
///
/// Consumed by:
/// - Relay BC (M6): Notifies the relevant participants via their <c>bidder:{...}</c>
///   BiddingHub groups and updates the <c>ops:staff</c> OperationsHub dispute board.
/// - Operations BC (M7): Removes the dispute from the active dispute-resolution board
///   (terminal resolutions) or marks it extended.
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier. Carried for
///   correlation and at-least-once dedup.
/// - <c>ListingId</c> — the sold listing whose dispute was resolved.
/// - <c>DisputeId</c> — matches the <see cref="DisputeOpened"/> instance this resolves.
/// - <c>ResolutionType</c> — a string-valued enum carrying one of <c>"Refund"</c>,
///   <c>"Extension"</c>, or <c>"Closed"</c>. Stored as a string rather than an int-valued
///   enum so the wire contract stays decoupled from any enum type, matching the
///   <c>ListingPassed.Reason</c> precedent. Consumers pattern-match on the string constant;
///   the saga branches its terminal-vs-continue behavior on the same value.
/// - <c>ResolvedAt</c> — handler-stamped timestamp when staff resolved the dispute.
/// </summary>
public sealed record DisputeResolved(
    Guid ObligationId,
    Guid ListingId,
    Guid DisputeId,
    string ResolutionType,
    DateTimeOffset ResolvedAt,
    Guid? ParticipantId = null);
