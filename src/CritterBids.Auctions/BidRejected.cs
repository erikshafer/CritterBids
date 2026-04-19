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
/// Stream-type marker for the per-listing bid-rejection audit stream. Required because
/// <c>UseMandatoryStreamTypeDeclaration = true</c> forces every new stream to declare its
/// type at <c>StartStream&lt;T&gt;</c>. Not projected into a live aggregate or registered
/// with <c>LiveStreamAggregation</c>; it's a raw audit log.
///
/// Stream key is derived deterministically from the listing id via <see cref="StreamKey"/> —
/// a non-SHA1 XOR scheme sufficient to guarantee the audit stream's Guid never collides
/// with the listing's primary stream Guid. A cryptographic UUID v5 would be overkill for a
/// single-domain, fixed-prefix derivation.
/// </summary>
public class BidRejectionAudit
{
    public Guid Id { get; set; }

    private static readonly Guid Namespace = new("b1d4a123-0000-0000-0000-000000000001");

    public static Guid StreamKey(Guid listingId)
    {
        Span<byte> listing = stackalloc byte[16];
        Span<byte> ns = stackalloc byte[16];
        listingId.TryWriteBytes(listing);
        Namespace.TryWriteBytes(ns);
        for (var i = 0; i < 16; i++) listing[i] ^= ns[i];
        return new Guid(listing);
    }
}
