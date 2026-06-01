namespace CritterBids.Operations;

/// <summary>
/// Operations BC's session-activity read model — a staff-facing row tracking one Flash session's
/// lineup and lifecycle, folded from the Auctions family (W006 §5a): <c>SessionCreated</c>,
/// <c>SessionStarted</c>, and <c>ListingAttachedToSession</c>. Operations is a <b>pure consumer</b>
/// (ADR-014 Path A, Sub-Option A): a single sibling handler family
/// (<see cref="SessionActivityHandler"/>) — Auctions is the only source BC for this view — upserts
/// this <see cref="SessionId"/>-keyed document. It appends to no local stream and publishes nothing.
///
/// <para><b>Lifecycle.</b> Maintained as a tolerant upsert keyed by <see cref="SessionId"/>.
/// <see cref="Status"/> is derived from which event arrived and advanced monotonically by
/// <see cref="SessionActivityStatusRules"/>: <c>SessionCreated</c> →
/// <see cref="SessionActivityStatus.Created"/>; <c>SessionStarted</c> →
/// <see cref="SessionActivityStatus.Started"/> (forward-only — a re-delivered <c>SessionCreated</c>
/// after start does not regress it or null <see cref="StartedAt"/>). <c>ListingAttachedToSession</c>
/// does not move the status.</para>
///
/// <para><b>Set-union accumulation.</b> <see cref="AttachedListingIds"/> is an additive set-union
/// with dedupe (W006 §5a) — never a last-write replace: <c>ListingAttachedToSession</c> ids
/// accumulate across deliveries, and <c>SessionStarted.ListingIds</c> is unioned (not assigned over)
/// the accumulated set. A <c>ListingAttachedToSession</c> arriving after <c>SessionStarted</c> adds
/// its id without regressing the status.</para>
///
/// <para><b>Marten Id convention.</b> <see cref="SessionId"/> doubles as the Marten document key,
/// exposed via the <see cref="Id"/> expression-bodied alias — the natural-key-as-id idiom shared
/// with <see cref="LotBoardView"/>/<see cref="OperationsObligationsView"/>. No <c>.Identity()</c>
/// override is needed in the module.</para>
/// </summary>
public sealed record SessionActivityView
{
    /// <summary>The Flash session this row tracks (all source events carry it). Doubles as the Marten document key.</summary>
    public Guid SessionId { get; init; }

    /// <summary>Session title. Carried by <c>SessionCreated</c>.</summary>
    public string? Title { get; init; }

    /// <summary>Configured session duration in minutes. Carried by <c>SessionCreated</c>.</summary>
    public int DurationMinutes { get; init; }

    /// <summary>
    /// The session's listing lineup — an additive set-union (dedupe) across
    /// <c>ListingAttachedToSession.ListingId</c> and <c>SessionStarted.ListingIds</c> (W006 §5a).
    /// <see cref="IReadOnlyList{T}"/> per the global record convention.
    /// </summary>
    public IReadOnlyList<Guid> AttachedListingIds { get; init; } = [];

    /// <summary>The lifecycle status, derived from event type and advanced monotonically (W006 §5a).</summary>
    public SessionActivityStatus Status { get; init; }

    /// <summary>When the session was created. Carried by <c>SessionCreated.CreatedAt</c>.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the session started. Carried by <c>SessionStarted.StartedAt</c>; null until started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Marten document key — equals <see cref="SessionId"/>. Expression-bodied to keep the storage
    /// shape identical to the natural-key-as-id pattern shared with the other Operations views; no
    /// <c>.Identity()</c> override is needed in the module.
    /// </summary>
    public Guid Id => SessionId;
}
