namespace CritterBids.Operations;

/// <summary>
/// Operations BC's bid-activity feed row — one immutable entry per accepted bid (W006 §3). Unlike
/// the upsert views (<see cref="SettlementQueueView"/>, <see cref="LotBoardView"/>), this is an
/// <b>append/feed</b> shape: each <c>BidPlaced</c> carries a distinct <see cref="BidId"/> and yields
/// a new row, so a listing accrues many rows (one per bid) rather than one row mutated in place.
/// The feed has no status and no preservation guards; it is the raw, time-ordered bid stream a
/// staff member scrolls. Operations is a pure consumer (ADR-014 Path A): the row is folded from the
/// Auctions BC's <c>BidPlaced</c> integration event by <see cref="BidActivityHandler"/>; nothing is
/// published.
///
/// <para><b>Append, not upsert.</b> Rows are never mutated. Re-delivery of the same
/// <see cref="BidId"/> is an idempotent no-op (the handler skips the second insert), not a second
/// row — <see cref="BidId"/> being the document key makes the dedupe natural. Queries filter/group
/// by <see cref="ListingId"/> and sort by <see cref="PlacedAt"/> (the module indexes both).</para>
///
/// <para><b>Marten Id convention.</b> <see cref="BidId"/> doubles as the Marten document key via the
/// <see cref="Id"/> expression-bodied alias — the same natural-key-as-id idiom as the upsert views.</para>
/// </summary>
public sealed record BidActivityEntry
{
    /// <summary>The bid this row records (command-origin UUID v7, unique per <c>BidPlaced</c>). Doubles as the Marten document key.</summary>
    public Guid BidId { get; init; }

    /// <summary>The listing the bid was placed on. Feed filter / grouping key.</summary>
    public Guid ListingId { get; init; }

    /// <summary>The participant who placed the bid (never "paddle").</summary>
    public Guid BidderId { get; init; }

    /// <summary>The bid amount.</summary>
    public decimal Amount { get; init; }

    /// <summary>The bid sequence number at the time of this bid.</summary>
    public int BidCount { get; init; }

    /// <summary>Whether this was a proxy (auto) bid vs a direct bid.</summary>
    public bool IsProxy { get; init; }

    /// <summary>When the bid was placed. Feed sort key.</summary>
    public DateTimeOffset PlacedAt { get; init; }

    /// <summary>Marten document key — equals <see cref="BidId"/>. No <c>.Identity()</c> override needed.</summary>
    public Guid Id => BidId;
}
