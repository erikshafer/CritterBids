namespace CritterBids.Auctions;

/// <summary>
/// Auctions BC command dispatched when a participant submits a bid on a listing. Routed to
/// <see cref="PlaceBidHandler"/> via the Wolverine bus; consumed internally within the
/// Auctions BC — not cross-BC, not on a RabbitMQ queue. M3 dispatch is test-only per the
/// M2.5 dispatch-precedent — no HTTP endpoint until M6.
///
/// Credit ceiling travels on the command in M3 because Participants does not yet emit a
/// <c>ParticipantSessionStarted</c> event the Auctions boundary can load. M4's Session
/// aggregate will carry the credit ceiling in its own stream, at which point this field
/// drops off the command shape and is read from <see cref="BidConsistencyState"/>.
///
/// BidId is externally assigned (UUID v7) at command construction — allows idempotent
/// projections downstream and stable per-bid identity for support tooling.
/// </summary>
public sealed record PlaceBid(
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    decimal CreditCeiling);
