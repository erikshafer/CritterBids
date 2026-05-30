namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration contract for an auction-side bid rejection.
/// Reserved for downstream consumers (Relay/Operations) that surface rejection outcomes.
/// </summary>
public sealed record BidRejected(
    Guid ListingId,
    Guid? BidderId,
    decimal AttemptedAmount,
    decimal CurrentHighBid,
    string Reason,
    DateTimeOffset RejectedAt);
