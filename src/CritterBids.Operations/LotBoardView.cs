namespace CritterBids.Operations;

/// <summary>
/// Operations BC's lot-board read model — a staff-facing row tracking the full lifecycle of one
/// listing, folded from <b>two</b> source families (W006 §2): the Selling family
/// (<c>ListingPublished</c>, <c>ListingWithdrawn</c>) and the Auctions family (<c>BiddingOpened</c>,
/// <c>BidPlaced</c>, <c>ListingSold</c>, <c>ListingPassed</c>). Operations is a <b>pure consumer</b>
/// (ADR-014 Path A, Sub-Option A): each source BC has its own sibling handler
/// (<see cref="LotBoardSellingHandler"/> / <see cref="LotBoardAuctionsHandler"/>), both upserting
/// this single <see cref="ListingId"/>-keyed document. It appends to no local stream and publishes
/// nothing.
///
/// <para><b>Lifecycle.</b> Maintained as a tolerant upsert keyed by <see cref="ListingId"/>.
/// <see cref="Status"/> is derived from which event arrived and advanced monotonically by
/// <see cref="LotBoardStatusRules"/>: <c>ListingPublished</c> → <see cref="LotBoardStatus.Draft"/>;
/// <c>BiddingOpened</c> (or a <c>BidPlaced</c> arriving first) → <see cref="LotBoardStatus.Open"/>;
/// the close events → <see cref="LotBoardStatus.Sold"/>/<see cref="LotBoardStatus.Passed"/>/
/// <see cref="LotBoardStatus.Withdrawn"/> (terminal, absorbing). The mandated guard: terminal does
/// not regress to <see cref="LotBoardStatus.Open"/> on a late <c>BidPlaced</c> (W006 §2).</para>
///
/// <para><b>Set-once + latest-wins.</b> <see cref="SellerId"/> is set-once across the three events
/// that carry it (<c>ListingPublished</c>/<c>BiddingOpened</c>/<c>ListingSold</c>) via the
/// <see cref="System.Guid.Empty"/> sentinel — whichever arrives first fixes it, including
/// <c>ListingSold</c> when it is the first carrier. <see cref="LastUpdatedAt"/> is latest-wins off
/// each event's own timestamp, so an out-of-order older event never rewinds it.
/// <see cref="CurrentBid"/>/<see cref="BidCount"/> only advance on a <c>BidPlaced</c> whose
/// <c>BidCount</c> is not stale (monotone), and not at all once terminal — the figures stay at
/// their close values.</para>
///
/// <para><b>Marten Id convention.</b> <see cref="ListingId"/> doubles as the Marten document key,
/// exposed via the <see cref="Id"/> expression-bodied alias — the natural-key-as-id idiom shared
/// with <see cref="SettlementQueueView"/> (M7-S2). No <c>.Identity()</c> override is needed in the
/// module.</para>
///
/// <para><b>Confidentiality.</b> <see cref="ReservePrice"/> is the confidential reserve; this is a
/// staff-only board (W006 §2). No bidder-facing surface reads this view (auth gating lands in
/// M7-S6).</para>
/// </summary>
public sealed record LotBoardView
{
    /// <summary>The listing this row tracks (all source events carry it). Doubles as the Marten document key.</summary>
    public Guid ListingId { get; init; }

    /// <summary>
    /// The seller. Carried by <c>ListingPublished</c>/<c>BiddingOpened</c>/<c>ListingSold</c>;
    /// set-once via the <see cref="System.Guid.Empty"/> sentinel (W006 §2). Remains
    /// <see cref="System.Guid.Empty"/> only until the first carrier arrives.
    /// </summary>
    public Guid SellerId { get; init; }

    /// <summary>Listing title. Carried by <c>ListingPublished</c>.</summary>
    public string? Title { get; init; }

    /// <summary>Auction format. Carried by <c>ListingPublished</c>.</summary>
    public string? Format { get; init; }

    /// <summary>Minimum first bid. Carried by <c>ListingPublished</c> and reconciled by <c>BiddingOpened</c> (same value).</summary>
    public decimal StartingBid { get; init; }

    /// <summary>Confidential reserve (staff-only). Carried by <c>ListingPublished</c> (≡ <c>BiddingOpened.ReserveThreshold</c>); null when none.</summary>
    public decimal? ReservePrice { get; init; }

    /// <summary>Buy-It-Now price. Carried by <c>ListingPublished</c> (≡ <c>BiddingOpened.BuyItNowPrice</c>); null when none.</summary>
    public decimal? BuyItNow { get; init; }

    /// <summary>Platform final-value-fee percentage. Carried by <c>ListingPublished</c>.</summary>
    public decimal FeePercentage { get; init; }

    /// <summary>Scheduled close time. Carried by <c>BiddingOpened</c>; null until bidding opens.</summary>
    public DateTimeOffset? ScheduledCloseAt { get; init; }

    /// <summary>Latest (highest, bids monotone) accepted bid. Carried by <c>BidPlaced.Amount</c>; null until the first bid.</summary>
    public decimal? CurrentBid { get; init; }

    /// <summary>Number of bids placed. Carried by <c>BidPlaced</c>/<c>ListingSold</c>/<c>ListingPassed</c>; latest-wins, monotone.</summary>
    public int BidCount { get; init; }

    /// <summary>Final accepted price at close. Carried by <c>ListingSold</c>.</summary>
    public decimal? HammerPrice { get; init; }

    /// <summary>The winning bidder. Carried by <c>ListingSold</c>; null until sold.</summary>
    public Guid? WinnerId { get; init; }

    /// <summary>Why the listing passed ("NoBids"/"ReserveNotMet"). Carried by <c>ListingPassed.Reason</c>.</summary>
    public string? PassReason { get; init; }

    /// <summary>Who withdrew the listing. Carried by <c>ListingWithdrawn.WithdrawnBy</c>.</summary>
    public Guid? WithdrawnBy { get; init; }

    /// <summary>Optional withdrawal audit reason. Carried by <c>ListingWithdrawn.Reason</c> (nullable on payload).</summary>
    public string? WithdrawalReason { get; init; }

    /// <summary>The lifecycle status, derived and advanced monotonically per W006 §2.</summary>
    public LotBoardStatus Status { get; init; }

    /// <summary>Latest-wins timestamp of the most recent event applied to this row.</summary>
    public DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>
    /// Marten document key — equals <see cref="ListingId"/>. Expression-bodied to keep the storage
    /// shape identical to the <see cref="SettlementQueueView"/> natural-key-as-id pattern; no
    /// <c>.Identity()</c> override is needed in the module.
    /// </summary>
    public Guid Id => ListingId;
}
