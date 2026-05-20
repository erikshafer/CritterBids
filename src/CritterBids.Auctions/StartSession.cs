using CritterBids.Contracts.Auctions;
using JasperFx.Events;
using Wolverine.Marten;

namespace CritterBids.Auctions;

/// <summary>
/// Command to start a Flash-format Session. Terminal command — sessions do not unstart,
/// pause, or cancel after starting (M4 non-goals). Handled by
/// <see cref="StartSessionHandler"/>.
/// </summary>
public sealed record StartSession(Guid SessionId);

/// <summary>
/// Wolverine handler for <see cref="StartSession"/>. Emits
/// <see cref="Contracts.Auctions.SessionStarted"/> carrying the full
/// <see cref="Session.AttachedListingIds"/> list verbatim in attachment order. The
/// downstream <see cref="SessionStartedHandler"/> consumes <c>SessionStarted</c> in the
/// same BC (Auctions-internal fan-out per Workshop 002 Phase 1 Option B).
///
/// <para><b>Rejection invariants.</b></para>
/// <list type="bullet">
///   <item>Workshop 002 §5.6 — no attached listings:
///     <see cref="SessionHasNoListingsException"/>.</item>
///   <item>Workshop 002 §5.7 — already started:
///     <see cref="SessionAlreadyStartedException"/>.</item>
/// </list>
///
/// <para><b>No defensive pre-filtering of withdrawn listings — OQ3 Path α.</b> Per the
/// M4 milestone doc §3: if a listing is attached to a session and then withdrawn via
/// Selling's <c>WithdrawListing</c> before start, <c>StartSession</c> still emits
/// <c>SessionStarted</c> with the full <c>ListingIds[]</c>. The fan-out handler emits
/// <c>BiddingOpened</c> for the withdrawn listing; termination happens reactively via
/// either DCB rejection or the Auction Closing saga's earlier <c>ListingWithdrawn</c>
/// consumption. The retrospective pins the lived terminal path observed at S5.</para>
/// </summary>
public static class StartSessionHandler
{
    public static Events Handle(
        StartSession cmd,
        [WriteAggregate(nameof(StartSession.SessionId))] Session session)
    {
        if (session.StartedAt is not null)
            throw new SessionAlreadyStartedException(cmd.SessionId);

        if (session.AttachedListingIds.Count == 0)
            throw new SessionHasNoListingsException(cmd.SessionId);

        return new Events
        {
            new SessionStarted(
                SessionId: cmd.SessionId,
                ListingIds: session.AttachedListingIds,
                StartedAt: DateTimeOffset.UtcNow),
        };
    }
}

/// <summary>Thrown when <see cref="StartSession"/> arrives for a session whose
/// <see cref="Session.AttachedListingIds"/> is empty (Workshop 002 §5.6).</summary>
public sealed class SessionHasNoListingsException : Exception
{
    public Guid SessionId { get; }

    public SessionHasNoListingsException(Guid sessionId)
        : base($"Session {sessionId} has no attached listings; cannot be started.")
    {
        SessionId = sessionId;
    }
}
