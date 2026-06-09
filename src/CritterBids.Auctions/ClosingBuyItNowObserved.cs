namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal command emitted by <see cref="AuctionClosingDispatchHandler"/> when a
/// <see cref="CritterBids.Contracts.Auctions.BuyItNowPurchased"/> arrives — the saga's
/// "terminate without close evaluation" BIN path (workshop scenario 3.8). Saga correlation and
/// the dispatcher-bridge rationale are documented on <see cref="ClosingBidObserved"/>.
/// </summary>
public sealed record ClosingBuyItNowObserved(Guid ListingId);
