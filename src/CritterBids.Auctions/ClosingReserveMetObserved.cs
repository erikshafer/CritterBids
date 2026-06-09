namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal command emitted by <see cref="AuctionClosingDispatchHandler"/> when a
/// <see cref="CritterBids.Contracts.Auctions.ReserveMet"/> arrives. Saga correlation and the
/// dispatcher-bridge rationale are documented on <see cref="ClosingBidObserved"/> (same M8
/// Bug #2 fix — the saga must not be a default handler on a multi-consumer contract event
/// under <c>MultipleHandlerBehavior.Separated</c>).
/// </summary>
public sealed record ClosingReserveMetObserved(Guid ListingId);
