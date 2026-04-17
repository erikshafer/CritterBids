namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a bid placed inside the extended-bidding
/// trigger window causes the scheduled close time to shift forward (anti-snipe). Can chain —
/// each triggering bid produces one event; MaxDuration caps the total extension (scenarios
/// 1.14 / 1.15). Not emitted when extended bidding is disabled for the listing or the new close
/// time would exceed MaxDuration.
/// Transport queue: TBD (consumers are post-M3). Relay consumes in a later milestone — queue
/// name finalized when Relay is scaffolded.
///
/// Consumed by:
/// - Auctions BC internally: Auction Closing saga cancels its pending CloseAuction scheduled
///   message and reschedules it at NewCloseAt
/// - Relay BC (post-M5): Push "bidding extended" broadcast so UI updates countdown timers
/// - Operations BC (post-M5): Live-board extension-count indicator
///
/// PreviousCloseAt and NewCloseAt together let Relay animate the countdown shift; TriggeredBy-
/// BidderId lets support tooling trace which bidder caused an extension.
/// </summary>
public sealed record ExtendedBiddingTriggered(
    Guid ListingId,
    DateTimeOffset PreviousCloseAt,
    DateTimeOffset NewCloseAt,
    Guid TriggeredByBidderId,
    DateTimeOffset TriggeredAt);
