namespace CritterBids.Contracts.Obligations;

/// <summary>
/// Integration event published by the Obligations BC when the seller provides shipping
/// tracking information for a sold listing. Emitted by the Post-Sale Coordination saga's
/// <c>ProvideTracking</c> handler per workshop 005 slice 5.3; corresponds to narrative 006
/// Moment 3 ("GreyOwl12 enters the tracking number").
/// Providing tracking cancels the saga's pending shipping-reminder and deadline-escalation
/// scheduled messages and schedules the auto-confirm delivery timer.
/// Transported via RabbitMQ on the publisher-side queue <c>relay-obligations-events</c>
/// (Wolverine transactional outbox); route wired in M6-S3.
///
/// Consumed by:
/// - Relay BC (M6): Pushes a tracking-confirmation notification to the winner's
///   <c>bidder:{WinnerId}</c> BiddingHub group. The winner is not carried on this payload
///   (the seller is the actor); Relay reads the winner from its own notification context or
///   the <c>relay-obligations-events</c> envelope correlation. If winner routing requires
///   the id inline, that is an additive payload change per ADR 005 — flagged for M6-S6.
/// - Operations BC (M7): Clears the "awaiting shipment" indicator on the obligation board.
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier
///   (<c>UuidV5(ObligationsIdentityNamespaces.PostSaleCoordination, $"obligation:{ListingId}")</c>).
///   Carried so consumers can correlate the event to a specific obligation instance and so
///   at-least-once redelivery deduplicates against the same identifier.
/// - <c>ListingId</c> — the sold listing this obligation closes. The natural business key
///   the obligation id is derived from.
/// - <c>SellerId</c> — the participant who shipped. The actor on this event.
/// - <c>TrackingNumber</c> — the carrier tracking string the seller entered. Opaque to
///   the platform in MVP (no carrier-webhook validation per W005-1).
/// - <c>ProvidedAt</c> — handler-stamped timestamp when tracking was recorded.
/// </summary>
public sealed record TrackingInfoProvided(
    Guid ObligationId,
    Guid ListingId,
    Guid SellerId,
    string TrackingNumber,
    DateTimeOffset ProvidedAt,
    Guid? WinnerId = null);
