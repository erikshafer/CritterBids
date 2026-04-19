namespace CritterBids.Auctions;

/// <summary>
/// Auctions BC command dispatched when a participant exercises Buy It Now on a listing.
/// Routed to <see cref="BuyNowHandler"/> via the Wolverine bus; consumed internally within
/// the Auctions BC — not cross-BC, not on a RabbitMQ queue. M3 dispatch is test-only per
/// the M2.5 dispatch-precedent — no HTTP endpoint until M6.
///
/// Unlike <see cref="PlaceBid"/>, the command does NOT carry an amount — the BIN price is
/// read from the boundary state (populated by <c>Apply(BiddingOpened)</c>). This matches
/// the workshop 002 §2 scenarios: the buyer commits to the listing's published BIN price,
/// not to an arbitrary amount.
///
/// Credit ceiling travels on the command in M3 for the same reason it does on
/// <see cref="PlaceBid"/> — Participants does not yet emit a <c>ParticipantSessionStarted</c>
/// event the Auctions boundary can load. M4's Session aggregate will carry the credit
/// ceiling in its own stream.
/// </summary>
public sealed record BuyNow(
    Guid ListingId,
    Guid BuyerId,
    decimal CreditCeiling);
