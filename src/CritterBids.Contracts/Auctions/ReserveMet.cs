namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC the first time a bid crosses the listing's
/// reserve threshold. Real-time UX signal only — Settlement performs the authoritative reserve
/// check at close (W001-5 resolution). Fires at most once per listing; subsequent bids above
/// reserve do not re-fire.
/// Transport queue: TBD (consumers are post-M3). Relay and Settlement consume in later
/// milestones — queue name finalized when the first consumer is wired.
///
/// Consumed by:
/// - Auctions BC internally: Auction Closing saga flips its ReserveHasBeenMet flag (used at
///   close to choose ListingSold vs ListingPassed)
/// - Relay BC (post-M5): Push "reserve met" broadcast so watchers see the indicator change
/// - Operations BC (post-M5): Live-board reserve indicator
/// - Settlement BC does NOT consume this event — Settlement uses its own cached reserve value
///   from PendingSettlement and performs an authoritative check on ListingSold / close events
///
/// Amount is the exact bid that crossed the threshold (useful for support tooling diagnostics
/// and live-board "reserve met at $X" messaging).
/// </summary>
public sealed record ReserveMet(
    Guid ListingId,
    decimal Amount,
    DateTimeOffset MetAt);
