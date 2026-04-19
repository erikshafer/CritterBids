using CritterBids.Contracts.Auctions;
using JasperFx.Events.Tags;
using Marten;

namespace CritterBids.Auctions;

/// <summary>
/// DCB handler for the <see cref="PlaceBid"/> command.
///
/// We do NOT use the canonical <c>[BoundaryModel]</c> auto-append shape — that shape
/// requires Marten to infer tags from an event property whose type exactly matches the
/// registered tag type. Our contract events carry <c>Guid ListingId</c>, not
/// <c>ListingStreamId ListingTag</c>, and we refuse to leak the tag wrapper into
/// <c>CritterBids.Contracts.Auctions.*</c>. So instead the handler:
///
/// 1. Calls <see cref="IEventStoreOperations.FetchForWritingByTags{T}"/> directly — this
///    returns the aggregate AND queues an <c>AssertDcbConsistency</c> operation on the
///    session that fires at <c>SaveChanges</c>, so the optimistic-concurrency guarantee
///    survives even though we're not going through <c>IEventBoundary.AppendMany</c>.
/// 2. On rejection, writes <see cref="BidRejected"/> to the dedicated
///    <see cref="BidRejectionAudit"/> stream and returns. The boundary's consistency
///    assertion still fires for the (empty) acceptance write set, which is fine — no
///    events to compete with.
/// 3. On acceptance, builds each event via <c>session.Events.BuildEvent</c>, tags it with
///    <see cref="ListingStreamId"/>, and appends to the listing's primary stream.
///
/// Covers all 15 scenarios in <c>docs/workshops/002-scenarios.md</c> §1. BuyNow (§2) is
/// a sibling DCB handler in M3-S4b and is out of scope here.
/// </summary>
public static class PlaceBidHandler
{
    public static async Task HandleAsync(
        PlaceBid command,
        IDocumentSession session,
        TimeProvider time)
    {
        var query = BuildQuery(command.ListingId);
        var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
        var state = boundary.Aggregate ?? new BidConsistencyState();

        var now = time.GetUtcNow();
        var reason = EvaluateRejection(command, state, now);

        if (reason is not null)
        {
            await AppendRejectionAudit(session, command, state, reason, now);
            return;
        }

        foreach (var evt in AcceptanceEvents(command, state, now))
        {
            var wrapped = session.Events.BuildEvent(evt);
            wrapped.AddTag(new ListingStreamId(command.ListingId));
            session.Events.Append(command.ListingId, wrapped);
        }
    }

    /// <summary>
    /// Pure acceptance-path projection, exposed for unit tests that exercise the
    /// decision logic without the bus. Named <c>Decide</c> (not <c>Handle</c>) to avoid
    /// matching Wolverine's handler-discovery convention.
    /// </summary>
    public static Events Decide(
        PlaceBid command,
        BidConsistencyState state,
        TimeProvider time)
    {
        var now = time.GetUtcNow();
        var reason = EvaluateRejection(command, state, now);
        if (reason is not null)
            return new Events();

        return AcceptanceEvents(command, state, now);
    }

    public static EventTagQuery BuildQuery(Guid listingId) =>
        EventTagQuery
            .For(new ListingStreamId(listingId))
            .AndEventsOfType<BiddingOpened, BidPlaced, BuyItNowOptionRemoved, ReserveMet, ExtendedBiddingTriggered>();

    private static async Task AppendRejectionAudit(
        IDocumentSession session,
        PlaceBid command,
        BidConsistencyState state,
        string reason,
        DateTimeOffset now)
    {
        var rejected = new BidRejected(
            ListingId: command.ListingId,
            BidderId: command.BidderId,
            AttemptedAmount: command.Amount,
            CurrentHighBid: state.CurrentHighBid,
            Reason: reason,
            RejectedAt: now);

        var auditKey = BidRejectionAudit.StreamKey(command.ListingId);
        var existing = await session.Events.FetchStreamStateAsync(auditKey);
        if (existing is null)
            session.Events.StartStream<BidRejectionAudit>(auditKey, rejected);
        else
            session.Events.Append(auditKey, rejected);
    }

    private static Events AcceptanceEvents(PlaceBid command, BidConsistencyState state, DateTimeOffset now)
    {
        var events = new Events();

        var newBidCount = state.BidCount + 1;
        events.Add(new BidPlaced(
            ListingId: command.ListingId,
            BidId: command.BidId,
            BidderId: command.BidderId,
            Amount: command.Amount,
            BidCount: newBidCount,
            IsProxy: false,
            PlacedAt: now));

        if (state.BuyItNowAvailable)
            events.Add(new BuyItNowOptionRemoved(command.ListingId, now));

        if (state.ReserveThreshold is { } reserve
            && !state.ReserveMet
            && command.Amount >= reserve)
        {
            events.Add(new ReserveMet(command.ListingId, command.Amount, now));
        }

        if (TryComputeExtension(state, now, out var newCloseAt))
        {
            events.Add(new ExtendedBiddingTriggered(
                ListingId: command.ListingId,
                PreviousCloseAt: state.ScheduledCloseAt,
                NewCloseAt: newCloseAt,
                TriggeredByBidderId: command.BidderId,
                TriggeredAt: now));
        }

        return events;
    }

    private static string? EvaluateRejection(PlaceBid command, BidConsistencyState state, DateTimeOffset now)
    {
        // Scenario 1.6 — no BiddingOpened applied, state.ListingId is default.
        if (state.ListingId == Guid.Empty)
            return "ListingNotOpen";

        // Scenario 1.7 — ScheduledCloseAt in the past. S4 derives closure from the scheduled
        // close time because BiddingClosed / ListingSold / ListingPassed are S5-scope events
        // and are not registered in AuctionsModule yet.
        if (now >= state.ScheduledCloseAt)
            return "ListingClosed";

        // Scenario 1.8 — seller cannot bid on their own listing.
        if (state.SellerId == command.BidderId)
            return "SellerCannotBid";

        // Scenario 1.5 — command-supplied credit ceiling (M3 scope choice; M4 reads from
        // ParticipantSessionStarted when Sessions land).
        if (command.Amount > command.CreditCeiling)
            return "ExceedsCreditCeiling";

        // Scenarios 1.3, 1.4 — first bid must be >= starting bid; subsequent bids must be
        // >= currentHighBid + increment where increment is $1 under $100, $5 at $100+.
        var minimum = state.BidCount == 0
            ? state.StartingBid
            : state.CurrentHighBid + Increment(state.CurrentHighBid);
        if (command.Amount < minimum)
            return "BelowMinimumBid";

        return null;
    }

    private static decimal Increment(decimal currentHighBid) =>
        currentHighBid >= 100m ? 5m : 1m;

    private static bool TryComputeExtension(
        BidConsistencyState state,
        DateTimeOffset now,
        out DateTimeOffset newCloseAt)
    {
        newCloseAt = default;

        if (!state.ExtendedBiddingEnabled)
            return false;
        if (state.ExtendedBiddingTriggerWindow is not { } window)
            return false;
        if (state.ExtendedBiddingExtension is not { } extension)
            return false;

        // Must be strictly before close (we've already rejected closed listings above) AND
        // within the trigger window of the close.
        var remaining = state.ScheduledCloseAt - now;
        if (remaining <= TimeSpan.Zero || remaining > window)
            return false;

        var candidate = now + extension;
        var maxClose = state.OriginalCloseAt + state.MaxDuration;
        if (candidate > maxClose)
            return false;

        newCloseAt = candidate;
        return true;
    }
}

/// <summary>
/// Collection shape for acceptance-path events returned by <see cref="PlaceBidHandler.Decide"/>.
/// Distinct from <c>Wolverine.Marten.Events</c> — we don't need the <c>IWolverineReturnType</c>
/// marker because the bus handler doesn't return events; it writes them directly via session.
/// </summary>
public sealed class Events : List<object>
{
    public Events() { }
    public Events(IEnumerable<object> collection) : base(collection) { }
}
