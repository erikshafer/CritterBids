using CritterBids.Contracts.Auctions;

namespace CritterBids.Auctions;

/// <summary>
/// Event-sourced aggregate for a Flash-format Session. A Session owns the container
/// lifecycle for simultaneous-close auctions: ops staff create a session, attach
/// already-published listings to it, and start it — at which point all attached listings
/// open for bidding through the <see cref="SessionStartedHandler"/> fan-out per Workshop
/// 002 Phase 1 Option B.
///
/// <para><b>Stream identity.</b> UUID v7 per M4-D2 (resolved at M4-S1). No natural business
/// key exists — <c>Title</c> is not unique (two Flash sessions can share a title). Stream
/// ids are generated in <see cref="CreateSessionHandler"/> and returned via
/// <c>CreationResponse&lt;Guid&gt;</c> so the M6 HTTP wiring can echo them to the ops
/// frontend.</para>
///
/// <para><b>Live stream aggregation.</b> Registered as
/// <c>LiveStreamAggregation&lt;Session&gt;</c> in
/// <see cref="AuctionsModule.AddAuctionsModule"/>; state is rebuilt from the stream on
/// each load. The functional <c>Apply</c> shape (returns a new instance via record
/// <c>with</c>) is Marten 8's supported pattern for sealed-record aggregates. The first
/// in-Auctions non-<see cref="Listing"/> aggregate; the first in-Auctions
/// <c>[WriteAggregate]</c> shape lands here as well (M4-S5 OQ8 — halt-and-consult
/// discipline if codegen fails on the two non-create commands).</para>
///
/// <para><b>Lifecycle and invariants.</b></para>
/// <list type="bullet">
///   <item><see cref="SessionCreated"/> — creates the aggregate at the supplied
///     <c>Title</c> and <c>DurationMinutes</c>; <see cref="AttachedListingIds"/> is empty
///     and <see cref="StartedAt"/> is <c>null</c>.</item>
///   <item><see cref="ListingAttachedToSession"/> — appends to
///     <see cref="AttachedListingIds"/>. The attach command rejects when
///     <see cref="StartedAt"/> is non-null (Workshop 002 §5.4) or when the
///     <see cref="PublishedListings"/> row is absent or Withdrawn (§5.3).</item>
///   <item><see cref="SessionStarted"/> — stamps <see cref="StartedAt"/>. The start
///     command rejects when <see cref="AttachedListingIds"/> is empty (§5.6) or when
///     <see cref="StartedAt"/> is already non-null (§5.7). The
///     <see cref="SessionStartedHandler"/> reads <see cref="DurationMinutes"/> off the
///     aggregate to compute per-listing <c>ScheduledCloseAt</c> (OQ5 Path B — the
///     <c>SessionStarted</c> contract carries only <c>StartedAt</c>, not duration).</item>
/// </list>
/// </summary>
public sealed record Session
{
    /// <summary>Marten stream identity — UUID v7 per M4-D2.</summary>
    public Guid Id { get; init; }

    /// <summary>Display title of the session; not unique. Example: "Nebraska.Code() Live Auction".</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Session duration in minutes — assigned at <see cref="SessionCreated"/>
    /// and immutable thereafter. The fan-out handler uses this to compute per-listing
    /// <c>ScheduledCloseAt</c> on <see cref="SessionStarted"/>.</summary>
    public int DurationMinutes { get; init; }

    /// <summary>Listings attached to this session via <see cref="ListingAttachedToSession"/>,
    /// in attachment order. Initialized empty on creation; ordered carries forward to
    /// <see cref="SessionStarted.ListingIds"/> verbatim.</summary>
    public IReadOnlyList<Guid> AttachedListingIds { get; init; } = [];

    /// <summary>Set when <see cref="SessionStarted"/> is applied. Null while the session
    /// is in the pre-start <c>SessionCreated</c> + <c>ListingAttachedToSession</c> phase;
    /// non-null is terminal — sessions do not unstart, pause, or cancel after start.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Stream-creation apply for <see cref="SessionCreated"/>. Marten 8 recognizes
    /// the static <c>Create</c> convention for first-event aggregate creation.</summary>
    public static Session Create(SessionCreated created) => new()
    {
        Id              = created.SessionId,
        Title           = created.Title,
        DurationMinutes = created.DurationMinutes,
    };

    /// <summary>Append the attached listing to the in-order list. The handler
    /// (<see cref="AttachListingToSessionHandler"/>) is responsible for the rejection
    /// invariants (already-started, not-published); this method handles the state
    /// transition only.</summary>
    public Session Apply(ListingAttachedToSession attached) => this with
    {
        AttachedListingIds = [.. AttachedListingIds, attached.ListingId],
    };

    /// <summary>Mark the session started. Terminal state transition; the fan-out
    /// handler reads <see cref="StartedAt"/> + <see cref="DurationMinutes"/> to compute
    /// <c>ScheduledCloseAt</c> per attached listing.</summary>
    public Session Apply(SessionStarted started) => this with
    {
        StartedAt = started.StartedAt,
    };
}
