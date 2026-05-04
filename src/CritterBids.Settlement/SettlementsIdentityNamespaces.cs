namespace CritterBids.Settlement;

/// <summary>
/// UUID v5 namespace constants for deterministic identifier generation inside the Settlement BC.
///
/// Values are generated once and committed as hard-coded literals — changing any namespace
/// Guid would invalidate every existing deterministic identifier hashed against it.
///
/// This file is the Settlement-internal counterpart to
/// <see cref="CritterBids.Auctions.AuctionsIdentityNamespaces"/>. The W003 Phase 1 Part 6
/// reference to "AuctionsNamespace" was a workshop drift corrected at M5-S4 — Settlement
/// owns its own namespace per the BC-isolation discipline.
/// </summary>
internal static class SettlementsIdentityNamespaces
{
    /// <summary>
    /// Namespace Guid for the Settlement saga's deterministic <c>SettlementId</c> per
    /// W003 Phase 1 Part 6 (<c>UuidV5(SettlementsIdentityNamespaces.SettlementSaga, $"settlement:{ListingId}")</c>).
    /// First lived UUID v5 use in CritterBids — M5-S4's <see cref="StartSettlementSagaHandler"/>
    /// is the producing call site.
    /// </summary>
    public static readonly Guid SettlementSaga = new Guid("c5e1d04a-72b3-5e7f-a4d1-1d09f8e3a2c0");

    /// <summary>
    /// Derives the deterministic <c>SettlementId</c> for a listing's settlement. The
    /// produced value is stable across calls — a duplicate <c>ListingSold</c> consumption
    /// derives the same id, and the saga's existence check at
    /// <see cref="StartSettlementSagaHandler"/> rejects re-creation.
    /// </summary>
    public static Guid SettlementId(Guid listingId) =>
        UuidV5.Create(SettlementSaga, $"settlement:{listingId}");
}
