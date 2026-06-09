namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal command emitted by <see cref="AuctionClosingDispatchHandler"/> when an
/// <see cref="CritterBids.Contracts.Auctions.ExtendedBiddingTriggered"/> arrives. Carries the
/// rescheduled close time the saga needs to cancel-and-reschedule the pending
/// <see cref="CloseAuction"/>. Saga correlation and the dispatcher-bridge rationale are
/// documented on <see cref="ClosingBidObserved"/>.
/// </summary>
public sealed record ClosingExtendedBiddingObserved(
    Guid ListingId,
    DateTimeOffset NewCloseAt);
