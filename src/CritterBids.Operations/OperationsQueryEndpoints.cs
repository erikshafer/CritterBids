using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace CritterBids.Operations;

/// <summary>
/// The staff-facing read surface over the Operations BC's six operator views (M7-S6, ADR-024). Every
/// endpoint is <c>[Authorize(Policy = "StaffOnly")]</c>-gated and read-only: it queries the shared
/// Marten store through <see cref="IQuerySession"/> and returns the view rows verbatim. None is
/// <c>[AllowAnonymous]</c>; none defers to a later slice.
///
/// <para><b>HTTP contract (ADR-024 / M7-S6 open-question resolution).</b> One <see cref="WolverineGetAttribute"/>
/// per view under <c>/api/operations/*</c>, returning the view record as an
/// <see cref="IReadOnlyList{T}"/> — an empty array when no rows exist, never a 404 (the read-path
/// precedent set by <c>CatalogEndpoints.GetCatalog</c>). No pagination in the MVP. The
/// <see cref="OperationsObligationsView"/> is surfaced as two endpoints — the escalation queue
/// (<c>QueueState == Escalated</c>) and the open-dispute queue (<c>QueueState == Disputed</c>) — the
/// two derived active queues W006 §4 defines; terminal/Active rows are filtered out of both.</para>
///
/// <para><b>Deterministic ordering.</b> Each query carries an explicit order (the view's natural
/// recency field descending, then the document key as a stable tiebreak) so a staff member sees a
/// stable, newest-first board rather than Postgres's physical row order.</para>
///
/// <para><b>Policy string.</b> The literal <c>"StaffOnly"</c> is used rather than the
/// <c>StaffAuthConstants.PolicyName</c> constant: the Operations BC cannot reference the host
/// (<c>CritterBids.Api</c>) where that constant lives, and adding a Contracts type for it is barred
/// by the slice's acceptance criteria. The literal is the ADR-024-pinned policy name.</para>
/// </summary>
public static class OperationsQueryEndpoints
{
    /// <summary>Settlement-queue board — every settlement row, newest activity first.</summary>
    [Authorize(Policy = "StaffOnly")]
    [WolverineGet("/api/operations/settlement-queue")]
    public static async Task<IReadOnlyList<SettlementQueueView>> GetSettlementQueue(IQuerySession session) =>
        await session.Query<SettlementQueueView>()
            .OrderByDescending(x => x.LastUpdatedAt)
            .ThenBy(x => x.SettlementId)
            .ToListAsync();

    /// <summary>Lot board — every tracked listing, newest activity first.</summary>
    [Authorize(Policy = "StaffOnly")]
    [WolverineGet("/api/operations/lot-board")]
    public static async Task<IReadOnlyList<LotBoardView>> GetLotBoard(IQuerySession session) =>
        await session.Query<LotBoardView>()
            .OrderByDescending(x => x.LastUpdatedAt)
            .ThenBy(x => x.ListingId)
            .ToListAsync();

    /// <summary>Bid-activity feed — every accepted bid, newest first (the time-ordered bid stream).</summary>
    [Authorize(Policy = "StaffOnly")]
    [WolverineGet("/api/operations/bid-activity")]
    public static async Task<IReadOnlyList<BidActivityEntry>> GetBidActivity(IQuerySession session) =>
        await session.Query<BidActivityEntry>()
            .OrderByDescending(x => x.PlacedAt)
            .ThenBy(x => x.BidId)
            .ToListAsync();

    /// <summary>
    /// Escalation queue — obligations whose ship-by deadline lapsed (<c>QueueState == Escalated</c>,
    /// W006 §4). Terminal/Active/Disputed rows are excluded; newest escalation first.
    /// </summary>
    [Authorize(Policy = "StaffOnly")]
    [WolverineGet("/api/operations/obligations/escalations")]
    public static async Task<IReadOnlyList<OperationsObligationsView>> GetEscalations(IQuerySession session) =>
        await session.Query<OperationsObligationsView>()
            .Where(x => x.QueueState == QueueState.Escalated)
            .OrderByDescending(x => x.EscalatedAt)
            .ThenBy(x => x.ObligationId)
            .ToListAsync();

    /// <summary>
    /// Open-dispute queue — obligations with an open dispute (<c>QueueState == Disputed</c>,
    /// W006 §4). Terminal/Active/Escalated rows are excluded; newest dispute first.
    /// </summary>
    [Authorize(Policy = "StaffOnly")]
    [WolverineGet("/api/operations/obligations/disputes")]
    public static async Task<IReadOnlyList<OperationsObligationsView>> GetDisputes(IQuerySession session) =>
        await session.Query<OperationsObligationsView>()
            .Where(x => x.QueueState == QueueState.Disputed)
            .OrderByDescending(x => x.DisputeOpenedAt)
            .ThenBy(x => x.ObligationId)
            .ToListAsync();

    /// <summary>Session-activity board — every Flash session, newest created first.</summary>
    [Authorize(Policy = "StaffOnly")]
    [WolverineGet("/api/operations/sessions")]
    public static async Task<IReadOnlyList<SessionActivityView>> GetSessions(IQuerySession session) =>
        await session.Query<SessionActivityView>()
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.SessionId)
            .ToListAsync();

    /// <summary>Participant-activity board — every started participant session, newest first.</summary>
    [Authorize(Policy = "StaffOnly")]
    [WolverineGet("/api/operations/participants")]
    public static async Task<IReadOnlyList<ParticipantActivityView>> GetParticipants(IQuerySession session) =>
        await session.Query<ParticipantActivityView>()
            .OrderByDescending(x => x.StartedAt)
            .ThenBy(x => x.ParticipantId)
            .ToListAsync();
}
