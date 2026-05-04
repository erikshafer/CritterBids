namespace CritterBids.Settlement;

/// <summary>
/// Lifecycle states for the <see cref="PendingSettlement"/> projection per W003 Phase 1
/// Part 1 plus the Phase 2 amendment that introduced the <see cref="Failed"/> state to
/// distinguish "settlement attempted and failed" from "no settlement will ever run"
/// (<see cref="Expired"/>). State transitions are absorbing — once a row reaches a
/// terminal status (any value other than <see cref="Pending"/>), subsequent events
/// preserve the existing status rather than regressing to a different terminal value.
/// </summary>
public enum PendingSettlementStatus
{
    /// <summary>Initial state on <c>ListingPublished</c>; the listing is awaiting a sale outcome.</summary>
    Pending,

    /// <summary>The Settlement saga ran to completion against this listing (per <c>SettlementCompleted</c>).</summary>
    Consumed,

    /// <summary>The listing did not sell (<c>ListingPassed</c>) or was withdrawn (<c>ListingWithdrawn</c>); no settlement will ever run.</summary>
    Expired,

    /// <summary>A settlement attempt failed (per <c>PaymentFailed</c>). Distinct from <see cref="Expired"/>; the listing did sell, but the financial workflow could not complete.</summary>
    Failed
}
