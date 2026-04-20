namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a Flash-format Session aggregate is
/// created. Sessions are the container for simultaneous-close auctions: ops staff create a
/// session, attach already-published listings to it, and start it — at which point all
/// attached listings open for bidding through the
/// <see cref="SessionStarted"/> fan-out handler.
///
/// Session stream IDs are UUID v7 per M4-D2 (§8 of
/// <c>docs/milestones/M4-auctions-bc-completion.md</c>) — no natural business key exists
/// (<c>Title</c> is not unique; two Flash sessions can share a title).
///
/// Authored at M4-S1 as a vocabulary-lock stub; produced at M4-S5 by the Session aggregate
/// after the <c>CreateSession</c> command is accepted.
/// Transported via RabbitMQ queue <c>listings-auctions-events</c> (Wolverine transactional
/// outbox) — the same queue as <c>BiddingOpened</c> and the rest of the Auctions → Listings
/// traffic; no new queue introduced (M4 plan §2).
///
/// Consumed by:
/// - Listings BC (M4-S6): Project session-membership fields onto
///   <c>CatalogListingView</c>. <c>SessionCreated</c> alone does not set the fields on any
///   listing (no attachment yet), but the Listings BC may also maintain a lightweight
///   <c>SessionCatalog</c> view summarizing active sessions for ops tooling — shape decided
///   in M4-S6.
/// - Operations BC (post-M5): Live-board indicator — a new Flash session is available to
///   attach listings to. MVP supports one active Flash session at a time (W001 demo flow);
///   operations surfaces that constraint to the ops user.
/// - Relay BC (post-M5): Not a direct consumer — Relay pushes notifications on
///   <see cref="SessionStarted"/>, not session creation. The create step is ops-internal.
///
/// Full payload at first commit per integration-messaging.md L2 — <c>DurationMinutes</c>
/// is required by Operations's live-board countdown and by Listings's future
/// <c>SessionCatalog</c>, even though M4-S6 may only consume <c>SessionId</c> and
/// <c>Title</c> for the initial membership fields.
/// </summary>
public sealed record SessionCreated(
    Guid SessionId,
    string Title,
    int DurationMinutes,
    DateTimeOffset CreatedAt);
