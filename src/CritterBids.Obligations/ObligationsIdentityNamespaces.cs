namespace CritterBids.Obligations;

/// <summary>
/// UUID v5 namespace constants for deterministic identifier generation inside the Obligations BC.
///
/// Values are generated once and committed as hard-coded literals — changing the namespace
/// Guid would invalidate every existing deterministic <c>ObligationId</c> hashed against it.
///
/// This file is the Obligations-internal counterpart to
/// <see cref="CritterBids.Settlement"/>'s <c>SettlementsIdentityNamespaces</c>. Per the
/// BC-isolation discipline, Obligations owns its own namespace constant rather than sharing one.
/// </summary>
internal static class ObligationsIdentityNamespaces
{
    /// <summary>
    /// Namespace Guid for the post-sale coordination saga's deterministic <c>ObligationId</c>.
    /// Derived as <c>UuidV5(PostSaleCoordination, $"obligation:{ListingId}")</c>, mirroring
    /// W003's <c>SettlementId</c> strategy. The Obligations BC's first lived UUID v5 use is
    /// <see cref="SettlementCompletedHandler"/> at M6-S2.
    /// </summary>
    public static readonly Guid PostSaleCoordination = new Guid("a3f8c1e7-6b24-5d9a-bf03-7e1c2d4a8b65");

    /// <summary>
    /// Derives the deterministic <c>ObligationId</c> for a sold listing's obligation. The
    /// produced value is stable across calls — a duplicate <c>SettlementCompleted</c>
    /// consumption derives the same id, and the saga-start existence check at
    /// <see cref="SettlementCompletedHandler"/> rejects re-creation (one obligation per listing).
    /// </summary>
    public static Guid ObligationId(Guid listingId) =>
        UuidV5.Create(PostSaleCoordination, $"obligation:{listingId}");
}
