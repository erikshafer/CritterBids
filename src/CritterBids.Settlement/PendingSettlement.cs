namespace CritterBids.Settlement;

/// <summary>
/// Settlement BC's local cache of the listing data the saga needs at workflow-start time —
/// reserve price, BIN price, fee percentage, seller identity — without crossing the Settlement /
/// Selling boundary. Seeded at <c>ListingPublished</c> arrival per W003 Phase 1 Part 1; lifecycle
/// transitions on <c>ListingPassed</c> / <c>ListingWithdrawn</c> (<see cref="PendingSettlementStatus.Expired"/>),
/// <c>SettlementCompleted</c> (<see cref="PendingSettlementStatus.Consumed"/>), and <c>PaymentFailed</c>
/// (<see cref="PendingSettlementStatus.Failed"/>) per workshop 003 scenarios §8.1 / §8.4 / §8.5 /
/// §8.6 / §8.7 / §8.8.
///
/// Per W003 Phase 1 Part 1's @Architect rationale: this is a derived read model, not an event-sourced
/// aggregate. A Marten document projection is the right primitive — there are no domain events the
/// projection produces, only the cross-BC integration events it consumes.
///
/// <para><b>Field name note.</b> The source <c>ListingPublished.BuyItNow</c> contract field renames
/// to <c>BuyItNowPrice</c> on this projection — matches the W003 Phase 1 Part 1 schema sketch and the
/// §8 scenario vocabulary. The rename is carried in the projection handler's <c>with</c> expression;
/// the Selling-side contract field name does not change.</para>
///
/// <para><b>Marten Id convention.</b> The <see cref="Id"/> property is the Marten document key; its
/// value is the natural <c>ListingId</c> for the listing this row caches. <see cref="LoadAsync"/>
/// callers index by <c>ListingId</c>; no separate <c>ListingId</c> property is authored.</para>
/// </summary>
public sealed record PendingSettlement
{
    /// <summary>Marten document key; equals the listing's <c>ListingId</c>.</summary>
    public Guid Id { get; init; }

    /// <summary>The listing's owning seller, carried verbatim from <c>ListingPublished</c>.</summary>
    public Guid SellerId { get; init; }

    /// <summary>Reserve price set at publish time. Nullable — listings without a reserve are valid.</summary>
    public decimal? ReservePrice { get; init; }

    /// <summary>Buy-It-Now price set at publish time. Nullable — listings without BIN are valid.</summary>
    public decimal? BuyItNowPrice { get; init; }

    /// <summary>
    /// Platform fee percentage frozen at publish time per W003 Phase 1 Part 1 / scenario §8.2's
    /// "FeePercentage is immutable after creation" rule. The Selling-side contract carries this
    /// field as a constant 0.10m placeholder per narrative 004 Finding 001; a future fee-engine
    /// boundary can move the source without changing this projection's contract.
    /// </summary>
    public decimal FeePercentage { get; init; }

    /// <summary>The listing's published-at timestamp, carried verbatim from <c>ListingPublished</c>.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Lifecycle status. <see cref="PendingSettlementStatus.Pending"/> on creation.</summary>
    public PendingSettlementStatus Status { get; init; }
}
