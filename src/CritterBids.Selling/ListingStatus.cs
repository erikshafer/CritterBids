namespace CritterBids.Selling;

/// <summary>Lifecycle state of a <see cref="SellerListing"/> aggregate.</summary>
/// <remarks>
/// Only the Draft transition is implemented in S5. Submitted, Published, Rejected, and Withdrawn
/// transitions arrive with their respective handlers in S6+. All statuses are defined now so that
/// state-guard tests (1.4, 1.5) and the ListingStatus enum are complete at M2 close.
/// </remarks>
public enum ListingStatus
{
    Draft,
    Submitted,
    Published,
    Rejected,
    Withdrawn
}
