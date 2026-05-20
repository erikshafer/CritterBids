namespace CritterBids.Auctions;

/// <summary>
/// Lifecycle status of the <see cref="PublishedListings"/> Auctions-side cache row.
/// Two terminal values matched to the upstream <c>ListingPublished</c> /
/// <c>ListingWithdrawn</c> Selling contract pair. Withdrawn is absorbing — re-delivery
/// of <c>ListingPublished</c> against a Withdrawn row preserves the terminal state per
/// the M5-S3 <c>PendingSettlement</c> terminal-status preservation pattern.
/// </summary>
public enum PublishedListingsStatus
{
    Published,
    Withdrawn,
}
