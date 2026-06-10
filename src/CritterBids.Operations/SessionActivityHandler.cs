using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Operations;

/// <summary>
/// Operations BC's <b>Auctions-family</b> session-activity consumer — the single ADR-014 Path A,
/// Sub-Option A sibling handler that maintains <see cref="SessionActivityView"/> (W006 §5a). Auctions
/// is the only source BC for the session board, so the three <c>Handle</c> overloads (one per session
/// event) live together here. The handler returns <see cref="Task"/> and writes only via the injected
/// Marten session — Operations is a pure consumer, so there are <b>no</b> <c>OutgoingMessages</c> and
/// <b>no</b> <c>IMessageBus</c> (it publishes nothing).
///
/// <para><b>Tolerant upsert.</b> Each overload loads-or-constructs the row by
/// <see cref="SessionActivityView.SessionId"/>, mutates via record <c>with</c>, and stores — the
/// lived shape from <see cref="LotBoardAuctionsHandler"/>/<see cref="OperationsObligationsHandler"/>.
/// Any of the three events tolerantly seeds the row, so an out-of-order <c>ListingAttachedToSession</c>
/// or <c>SessionStarted</c> arriving before <c>SessionCreated</c> still materialises a row.</para>
///
/// <para><b>Status (W006 §5a).</b> <see cref="SessionActivityStatusRules.Advance"/> keeps the status
/// monotone and forward-only on the <c>Created → Started</c> axis: a re-delivered <c>SessionCreated</c>
/// after <c>SessionStarted</c> cannot regress <see cref="SessionActivityStatus.Started"/> to
/// <see cref="SessionActivityStatus.Created"/> nor null <see cref="SessionActivityView.StartedAt"/>,
/// and a <c>ListingAttachedToSession</c> never moves the status at all.</para>
///
/// <para><b>Set-union accumulation (W006 §5a).</b> <see cref="SessionActivityView.AttachedListingIds"/>
/// is an additive set-union with dedupe — <c>ListingAttachedToSession</c> ids accumulate and
/// <c>SessionStarted.ListingIds</c> is unioned (not assigned over) the accumulated set via
/// <see cref="Union"/>.</para>
/// </summary>
[StickyHandler("operations-auctions-events")]
public static class SessionActivityHandler
{
    public static async Task Handle(
        SessionCreated message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.SessionId, cancellationToken);

        session.Store(view with
        {
            Title           = message.Title,
            DurationMinutes = message.DurationMinutes,
            CreatedAt       = message.CreatedAt,
            Status          = SessionActivityStatusRules.Advance(view.Status, SessionActivityStatus.Created),
        });
    }

    public static async Task Handle(
        SessionStarted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.SessionId, cancellationToken);

        session.Store(view with
        {
            AttachedListingIds = Union(view.AttachedListingIds, message.ListingIds),
            StartedAt          = message.StartedAt,
            Status             = SessionActivityStatusRules.Advance(view.Status, SessionActivityStatus.Started),
        });
    }

    public static async Task Handle(
        ListingAttachedToSession message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.SessionId, cancellationToken);

        // Set-union the single attached id; never touch Status/StartedAt — a late attachment that
        // arrives after SessionStarted (out-of-order) must add its id without regressing the row to
        // Created or nulling StartedAt (W006 §5a).
        session.Store(view with
        {
            AttachedListingIds = Union(view.AttachedListingIds, [message.ListingId]),
        });
    }

    private static async Task<SessionActivityView> LoadOrCreate(
        IDocumentSession session,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<SessionActivityView>(sessionId, cancellationToken);
        return existing ?? new SessionActivityView { SessionId = sessionId };
    }

    /// <summary>
    /// Additive set-union with dedupe (W006 §5a): folds <paramref name="incoming"/> ids into the
    /// accumulated <paramref name="existing"/> set, preserving first-seen order and dropping
    /// duplicates. Never a last-write replace.
    /// </summary>
    private static IReadOnlyList<Guid> Union(IReadOnlyList<Guid> existing, IReadOnlyList<Guid> incoming)
    {
        var set = new HashSet<Guid>(existing);
        var result = new List<Guid>(existing);
        foreach (var id in incoming)
        {
            if (set.Add(id))
                result.Add(id);
        }

        return result;
    }
}
