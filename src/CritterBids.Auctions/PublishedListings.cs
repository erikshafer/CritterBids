namespace CritterBids.Auctions;

/// <summary>
/// Auctions-side cache of the listing data the BC needs at handler hot-paths without
/// crossing the Selling boundary. Two consumers inside Auctions read this row:
/// <list type="bullet">
///   <item><see cref="AttachListingToSession"/>'s handler — rejects when the row is
///     absent or <see cref="Status"/> is <see cref="PublishedListingsStatus.Withdrawn"/>
///     per Workshop 002 §5.3 (M4-D4 resolution at M4-S1).</item>
///   <item><see cref="SessionStartedHandler"/> — reads the per-listing
///     <c>BiddingOpened</c>-precursor payload to emit one
///     <see cref="Contracts.Auctions.BiddingOpened"/> per attached listing on session
///     start (the Workshop 002 Phase 1 Option B fan-out).</item>
/// </list>
///
/// <para><b>M4-D4 — duplicate-projection pattern, third lived application.</b> Settlement's
/// <see cref="CritterBids.Settlement.PendingSettlement"/> (M5-S3) and Auctions's own
/// <see cref="ParticipantCreditCeiling"/> (M4-S4) are the prior two. Each BC maintains its
/// own local copy of upstream seed data, consumed on a BC-specific RabbitMQ queue
/// (<c>auctions-selling-events</c> here, already wired at M3-S3). The duplication is
/// intentional per ADR 011 and the integration-messaging skill §L2 — saga / aggregate
/// hot-paths do not cross BC boundaries to read.</para>
///
/// <para><b>Field shape — OQ1 Path A.</b> Full <c>BiddingOpened</c>-precursor payload.
/// Picked at M4-S5 session open over Path B (minimal status-only) because the fan-out
/// handler ALSO consults this projection, not just the attach-time published-status
/// check. With Path A the fan-out reads the per-listing payload (SellerId, StartingBid,
/// ReservePrice, BuyItNowPrice, extended-bidding fields) inline; no second Marten
/// round-trip per emission. Path B would have forced the fan-out to load each Listing
/// aggregate's primary stream — but Flash listings are guarded out of
/// <see cref="ListingPublishedHandler"/> at M4-S5 (item 9), so their Auctions-side
/// stream is empty at fan-out time and Path B would have required a third lookup
/// mechanism. The M4 milestone doc §6's "no fields beyond what the handler needs"
/// framing extends to the fan-out handler as a consumer; the expansion is pinned in the
/// M4-S5 retrospective.</para>
///
/// <para><b>Field name note.</b> The upstream <c>ListingPublished.BuyItNow</c> field
/// renames to <see cref="BuyItNowPrice"/> here — matches Settlement's
/// <see cref="CritterBids.Settlement.PendingSettlement.BuyItNowPrice"/> rename and the
/// Workshop 002 scenario vocabulary. The Selling-side contract field name does not
/// change.</para>
///
/// <para><b>Marten Id convention.</b> The <see cref="Id"/> property is the Marten
/// document key; its value is the natural <c>ListingId</c>. Mirrors the M5-S3
/// <c>PendingSettlement</c> and M4-S4 <see cref="ParticipantCreditCeiling"/> Id
/// conventions (natural key as document key, no separate ListingId property).</para>
/// </summary>
public sealed record PublishedListings
{
    /// <summary>Marten document key; equals the listing's <c>ListingId</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>The listing's owning seller, carried verbatim from <c>ListingPublished</c>.</summary>
    public Guid SellerId { get; init; }

    /// <summary>Starting bid set at publish time. Workshop 002 default is $25.</summary>
    public decimal StartingBid { get; init; }

    /// <summary>Reserve price set at publish time. Nullable — listings without a reserve are valid.</summary>
    public decimal? ReservePrice { get; init; }

    /// <summary>Buy-It-Now price set at publish time. Nullable — listings without BIN are valid.
    /// Renamed from upstream <c>ListingPublished.BuyItNow</c> to match Workshop 002 vocabulary
    /// and Settlement's <c>PendingSettlement</c> rename.</summary>
    public decimal? BuyItNowPrice { get; init; }

    /// <summary>
    /// Listing duration from publish time. Nullable: Flash-format listings carry
    /// <c>null</c> per the Workshop 002 Flash-Session-on-start contract. The
    /// <see cref="SessionStartedHandler"/> reads <c>DurationMinutes</c> from the
    /// Session aggregate instead (OQ5 Path B) and ignores this field for Flash.
    /// Timed listings keep their Duration here so future Auctions-side consumers
    /// see the same payload shape.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Extended-bidding enabled flag at publish time.</summary>
    public bool ExtendedBiddingEnabled { get; init; }

    /// <summary>Extended-bidding trigger window (e.g. 30s); null when extended bidding is disabled.</summary>
    public TimeSpan? ExtendedBiddingTriggerWindow { get; init; }

    /// <summary>Extended-bidding extension duration (e.g. 15s); null when extended bidding is disabled.</summary>
    public TimeSpan? ExtendedBiddingExtension { get; init; }

    /// <summary>Publish-time timestamp, carried verbatim from <c>ListingPublished.PublishedAt</c>.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Withdrawn-time timestamp, stamped on transition to
    /// <see cref="PublishedListingsStatus.Withdrawn"/> from
    /// <c>ListingWithdrawn.WithdrawnAt</c>. Null while
    /// <see cref="Status"/> is <see cref="PublishedListingsStatus.Published"/>.</summary>
    public DateTimeOffset? WithdrawnAt { get; init; }

    /// <summary>Lifecycle status. <see cref="PublishedListingsStatus.Published"/> on first
    /// creation; <see cref="PublishedListingsStatus.Withdrawn"/> is terminal and absorbing
    /// — a re-delivered <c>ListingPublished</c> on a Withdrawn row preserves the terminal
    /// state per the M5-S3 PendingSettlement pattern.</summary>
    public PublishedListingsStatus Status { get; init; }
}
