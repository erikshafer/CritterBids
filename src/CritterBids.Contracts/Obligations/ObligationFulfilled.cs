namespace CritterBids.Contracts.Obligations;

/// <summary>
/// Integration event published by the Obligations BC when a post-sale obligation reaches
/// its happy-path terminal state — the seller shipped and delivery was confirmed (auto-
/// confirmed after the configured window in MVP per workshop 005 slice 5.4). Emitted by the
/// Post-Sale Coordination saga's <c>ConfirmDelivery</c> handler immediately before the saga
/// calls <c>MarkCompleted()</c>; corresponds to narrative 006 Moment 4 ("the obligation
/// closes itself").
/// Transported via RabbitMQ on the publisher-side queue <c>relay-obligations-events</c>
/// (Wolverine transactional outbox); route wired in M6-S3.
///
/// Consumed by:
/// - Relay BC (M6): Pushes a completion notification to the winner's <c>bidder:{WinnerId}</c>
///   and the seller's <c>bidder:{SellerId}</c> BiddingHub groups.
/// - Operations BC (M7): Removes the obligation from the active-obligations board.
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier. Carried for
///   correlation and at-least-once dedup.
/// - <c>ListingId</c> — the sold listing this obligation closed.
/// - <c>WinnerId</c>, <c>SellerId</c> — both participant identifiers carried verbatim from
///   saga state so consumers route notifications to either party without a follow-up read.
/// - <c>FulfilledAt</c> — handler-stamped timestamp at the terminal phase; audit semantics
///   ("when did this obligation reach fulfilled state?").
/// </summary>
public sealed record ObligationFulfilled(
    Guid ObligationId,
    Guid ListingId,
    Guid WinnerId,
    Guid SellerId,
    DateTimeOffset FulfilledAt);
