namespace CritterBids.Contracts.Selling;

/// <summary>
/// Integration event signalling that a listing has been withdrawn before its scheduled close.
/// Authored in M3-S5b for the Auction Closing saga's terminal-without-evaluation path
/// (workshop scenario 3.10). The Selling-side publisher (a `WithdrawListing` command and the
/// corresponding handler that emits this event) is **deferred** per M3 milestone doc §3 —
/// the contract exists so the Auctions saga can subscribe today, and so future Selling-side
/// withdrawal work has a stable type to publish without contract churn (per
/// `integration-messaging.md` L2).
///
/// Until the Selling-side publisher lands, the only producer is the Auctions test fixture
/// (synthetic seed for scenario 3.10). No production code paths emit this event at M3 close.
///
/// Consumed by:
/// - Auctions BC (M3): Auction Closing saga transitions to Resolved without emitting any
///   outcome event (no BiddingClosed, no ListingSold, no ListingPassed) and cancels the
///   pending CloseAuction. Withdrawal skips reserve evaluation entirely — no money moves.
/// - Listings BC (post-M3): Update CatalogListingView Status="Withdrawn"
/// - Relay BC (post-M5): Push "listing withdrawn" notification to bidders
/// - Operations BC (post-M5): Live-board terminal-by-withdrawal indicator
///
/// Minimum-viable payload: ListingId only. A withdrawal-reason field would be useful for
/// operator audit (cf. workshop scenario 3.10's "WithdrawnBy: ops-staff") but is deferred
/// to the Selling-side authoring session — adding optional fields later is a non-breaking
/// contract change; renaming or restructuring would not be.
/// </summary>
public sealed record ListingWithdrawn(
    Guid ListingId);
