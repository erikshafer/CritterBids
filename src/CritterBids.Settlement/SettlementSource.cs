namespace CritterBids.Settlement;

/// <summary>
/// The triggering source of a settlement workflow per W003 Phase 1 Part 5. Carried on
/// <see cref="SettlementInitiated"/> so the evolver branches the initial state correctly:
/// <see cref="Bidding"/> initiates at <c>SettlementState.Initiated</c> (reserve check
/// pending); <see cref="BuyItNow"/> initiates directly at <c>SettlementState.ReserveChecked(WasMet: true)</c>
/// per W003 §1.2 (BIN purchases skip reserve verification by definition).
///
/// M5-S4 produces only <see cref="Bidding"/>; M5-S5's BIN-source path lands the
/// <see cref="BuyItNow"/> producer.
/// </summary>
public enum SettlementSource
{
    /// <summary>The settlement was triggered by <c>ListingSold</c> from the Auctions BC's bidding-source close path.</summary>
    Bidding,

    /// <summary>The settlement was triggered by <c>BuyItNowPurchased</c> from the Auctions BC's BIN-source short-circuit path.</summary>
    BuyItNow
}
