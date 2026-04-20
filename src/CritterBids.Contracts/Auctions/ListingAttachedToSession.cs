namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a published listing is attached to
/// a Flash session. Session-membership is modelled as a stream of attach events on the
/// Session aggregate rather than a mutable collection on any single entity — replayability
/// and audit of which listings joined in what order fall out for free.
///
/// The <c>AttachListingToSession</c> command (M4-S5) rejects if the listing is not in
/// <c>Published</c> status (M4-D4 resolution: Auctions-side duplicate
/// <c>PublishedListings</c> projection, populated from <c>ListingPublished</c> /
/// <c>ListingWithdrawn</c> consumption) and if the session has already been started.
///
/// Authored at M4-S1 as a vocabulary-lock stub; produced at M4-S5 by the Session aggregate.
/// Transported via RabbitMQ queue <c>listings-auctions-events</c> (Wolverine transactional
/// outbox) — same queue as the rest of the Auctions → Listings traffic.
///
/// Consumed by:
/// - Listings BC (M4-S6): Set <c>CatalogListingView.SessionId</c> on the affected listing
///   row via the new <c>SessionMembershipHandler</c> sibling class (second lived
///   application of the M3-D2 Path A pattern; ADR 014 authored alongside).
/// - Operations BC (post-M5): Live-board attachment indicator; ops staff see the listing
///   join the session before start.
/// - Relay BC (post-M5): Not a direct consumer.
///
/// The payload is minimal by design — session membership is fully expressed by
/// <c>(SessionId, ListingId)</c>. Session-scoped metadata (<c>Title</c>,
/// <c>DurationMinutes</c>) lives on <see cref="SessionCreated"/>; listing-scoped metadata
/// lives on <c>ListingPublished</c>. Consumers that need both join on <c>SessionId</c> and
/// <c>ListingId</c> respectively; no denormalization onto the attachment event is required.
/// </summary>
public sealed record ListingAttachedToSession(
    Guid SessionId,
    Guid ListingId,
    DateTimeOffset AttachedAt);
