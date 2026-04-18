namespace CritterBids.Auctions;

/// <summary>
/// Internal audit event for rejected bids. Not in <c>CritterBids.Contracts.Auctions.*</c>
/// by design — rejections are an Auctions-internal audit concern, not an integration event
/// consumed by other BCs. No cross-BC consumer is anticipated in M3–M6.
///
/// W002-7 stream-placement decision: rejections land in a dedicated
/// <see cref="BidRejectionAudit"/> stream per listing — never on the listing's primary
/// bidding stream, never in a global audit stream. The stream key is derived via UUID v5
/// from the listing id so the mapping is deterministic without a lookup table.
///
/// Rejected events are tagged with <see cref="ListingStreamId"/> but are excluded from
/// the DCB <c>EventTagQuery.AndEventsOfType&lt;...&gt;</c> list by type, so
/// <see cref="BidConsistencyState"/> never sees them.
/// </summary>
public sealed record BidRejected(
    Guid ListingId,
    Guid? BidderId,
    decimal AttemptedAmount,
    decimal CurrentHighBid,
    string Reason,
    DateTimeOffset RejectedAt);

/// <summary>
/// Stream-type marker for the per-listing bid-rejection audit stream.
/// Stream key is UUID v5 of <c>"bid-rejection-audit:{listingId}"</c> — deterministic,
/// collision-free against the listing's primary stream. Not projected into a live aggregate
/// or registered with <c>LiveStreamAggregation</c>; it's a raw audit log.
/// </summary>
public class BidRejectionAudit
{
    public Guid Id { get; set; }
}
