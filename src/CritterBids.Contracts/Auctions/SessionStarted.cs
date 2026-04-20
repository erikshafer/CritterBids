namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a Flash session is kicked off.
/// Terminal command on the Session aggregate — sessions do not pause, unstart, or cancel
/// after starting (M4 non-goals; Workshop 002 §5 does not define those transitions).
///
/// This event drives the Option B fan-out pattern (Workshop 002 Phase 1,
/// <c>docs/workshops/002-auctions-bc-deep-dive.md</c> — "Session fan-out"): a dedicated
/// <c>SessionStartedHandler</c> inside the Auctions BC reacts to this event and produces
/// one <c>BiddingOpened</c> per listing in <c>ListingIds</c>. The Session aggregate itself
/// does not emit <c>BiddingOpened</c> — that separation keeps the aggregate focused on
/// membership and lifecycle, and keeps the per-listing bidding-state responsibility with
/// the DCB <c>BidConsistencyState</c> model.
///
/// Authored at M4-S1 as a vocabulary-lock stub; produced at M4-S5 by the Session
/// aggregate; the Auctions-internal fan-out handler is authored at M4-S5 (or S5b per the
/// split-slot trigger in §9).
/// Transported via RabbitMQ queue <c>listings-auctions-events</c> (Wolverine transactional
/// outbox).
///
/// Consumed by:
/// - Auctions BC internally (M4-S5): <c>SessionStartedHandler</c> fans out one
///   <c>BiddingOpened</c> per listing. Idempotency is enforced via the DCB
///   <c>BidConsistencyState</c> per-listing boundary (a second <c>BiddingOpened</c> append
///   to an already-open stream is rejected and treated as an expected no-op). Fallback
///   shape if the DCB composition does not hold — pre-query each listing's bidding state
///   before emission — is captured in M4 milestone doc §6.
/// - Listings BC (M4-S6): Set <c>CatalogListingView.SessionStartedAt</c> on every listing
///   in <c>ListingIds</c> via the <c>SessionMembershipHandler</c> sibling class.
/// - Operations BC (post-M5): Live-board transition — session is live, countdown begins.
/// - Relay BC (post-M5): Push "your watched listing is now live" notifications for each
///   listing in the session.
///
/// <c>ListingIds</c> is a full list rather than a reference to the session stream so every
/// consumer has the complete membership without a side lookup at event-handle time.
/// <c>StartedAt</c> is the aggregate-stamped timestamp, not the outbox-dispatch time, so
/// replayed events preserve the original ordering.
/// </summary>
public sealed record SessionStarted(
    Guid SessionId,
    IReadOnlyList<Guid> ListingIds,
    DateTimeOffset StartedAt);
