namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal command emitted by <see cref="AuctionClosingDispatchHandler"/> when a
/// <see cref="CritterBids.Contracts.Selling.ListingWithdrawn"/> arrives — the saga's
/// "terminate without evaluation" withdrawal path (workshop scenario 3.10). Saga correlation
/// and the dispatcher-bridge rationale are documented on <see cref="ClosingBidObserved"/>.
/// </summary>
public sealed record ClosingListingWithdrawnObserved(Guid ListingId);
