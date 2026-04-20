namespace CritterBids.Contracts.Selling;

/// <summary>
/// Integration event signalling that a listing has been withdrawn before its scheduled
/// close. Originally authored in M3-S5b as a single-field stub
/// (<c>Guid ListingId</c>) for the Auction Closing saga's terminal-without-evaluation
/// path (workshop scenario 3.10). Extended at M4-S1 to the full M4 payload ahead of the
/// Selling-side producer authoring in M4-S2.
///
/// ADR 005 additive-versioning — the extension appends three new fields
/// (<c>WithdrawnBy</c>, <c>Reason</c>, <c>WithdrawnAt</c>) to the existing single-field
/// shape. Any deserializer compiled against the M3-era record continues to round-trip
/// the extended payload without a code change; renaming or re-typing <c>ListingId</c>
/// would be breaking, and is explicitly not done here. The <c>ListingId</c> field is
/// preserved verbatim for contract-versioning hygiene.
///
/// Publisher:
/// - Selling BC's <c>WithdrawListing</c> command handler (authored at M4-S2). Replaces
///   the M3 test-fixture synthesis that stood in as a production-path placeholder at
///   M3-S5b close.
/// Transported via RabbitMQ on two queues (publisher-side routing lands at M4-S2):
/// - <c>auctions-selling-events</c> — existing queue, consumed by Auctions BC.
/// - <c>listings-selling-events</c> — existing queue, consumed by Listings BC.
///
/// Consumed by:
/// - Auctions BC (M3): Auction Closing saga transitions to Resolved without emitting any
///   outcome event (no <c>BiddingClosed</c>, no <c>ListingSold</c>, no
///   <c>ListingPassed</c>) and cancels the pending <c>CloseAuction</c>. Withdrawal skips
///   reserve evaluation entirely — no money moves.
/// - Auctions BC (M4-S4): Proxy Bid Manager saga terminal handler — any active proxies
///   on the listing transition to <c>ListingClosed</c> and call <c>MarkCompleted()</c>
///   (Workshop 002 scenario 4.8).
/// - Listings BC (M4-S6): Update <c>CatalogListingView.Status</c> via the new
///   <c>SessionMembershipHandler</c> sibling class. Exact status field shape
///   (<c>Withdrawn</c> as a new enum value vs <c>ClosedReason = "Withdrawn"</c>) is
///   M4-D5, decided in M4-S6.
/// - Relay BC (post-M5): Push "listing withdrawn" notification to bidders and watchers.
///   <c>Reason</c> may be surfaced in the notification if present; otherwise a generic
///   "listing withdrawn by seller" message is shown.
/// - Operations BC (post-M5): Live-board terminal-by-withdrawal indicator. <c>WithdrawnBy</c>
///   distinguishes seller-initiated withdrawal (M4 scope) from ops-staff-initiated
///   withdrawal (post-M4 scope — see M4 non-goals).
///
/// Field rationale:
/// - <c>WithdrawnBy</c> — participant or ops-staff identifier of the initiator. M4 only
///   produces seller-initiated withdrawal, but the field is present up-front so the post-
///   M4 ops-staff-withdrawal path adds no contract change. Matches Workshop 002 §3.10's
///   "WithdrawnBy: ops-staff" placeholder.
/// - <c>Reason</c> — optional free-text audit string. Null for seller withdrawal via the
///   MVP command (no UI capture yet); future ops-staff withdrawal and fraud/abuse paths
///   populate it.
/// - <c>WithdrawnAt</c> — handler-stamped timestamp, not outbox-dispatch time.
/// </summary>
public sealed record ListingWithdrawn(
    Guid ListingId,
    Guid WithdrawnBy,
    string? Reason,
    DateTimeOffset WithdrawnAt);
