using CritterBids.Contracts.Auctions;
using JasperFx.Events.Tags;
using Marten;

namespace CritterBids.Auctions;

/// <summary>
/// DCB handler for the <see cref="BuyNow"/> command. Mirrors <see cref="PlaceBidHandler"/>'s
/// manual-tag, manual-append shape exactly — the canonical <c>[BoundaryModel]</c>
/// auto-append path was ruled out in M3-S4 because our contract events carry
/// <c>Guid ListingId</c>, not <c>ListingStreamId ListingTag</c>, and we refuse to leak the
/// Marten tag wrapper into <c>CritterBids.Contracts.Auctions.*</c>. See the S4 retro
/// (<c>docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md</c> §6) for the full
/// analysis; S4b is a clean re-application of that precedent.
///
/// <para>Covers the 4 scenarios in <c>docs/workshops/002-scenarios.md</c> §2:</para>
/// <list type="bullet">
/// <item><description>2.1 — BIN still available, credit sufficient → <see cref="BuyItNowPurchased"/></description></item>
/// <item><description>2.2 — BIN option removed (prior bid landed) → <see cref="BidRejected"/> (Reason: BuyItNowNotAvailable)</description></item>
/// <item><description>2.3 — BIN price exceeds credit ceiling → <see cref="BidRejected"/> (Reason: ExceedsCreditCeiling)</description></item>
/// <item><description>2.4 — listing closed (ScheduledCloseAt in the past) → <see cref="BidRejected"/> (Reason: ListingClosed)</description></item>
/// </list>
///
/// <para>Rejections reuse <see cref="BidRejected"/> and the <see cref="BidRejectionAudit"/>
/// stream — per the S4 retro's "What M3-S4b should know" guidance. The reason string
/// discriminates the BuyNow path; no separate <c>BuyNowRejected</c> type is introduced.</para>
///
/// <para>The tag query includes <see cref="BuyItNowPurchased"/> so a second BuyNow attempt
/// on a terminal listing loads the <c>Apply(BuyItNowPurchased)</c> projection and rejects via
/// <c>BuyItNowAvailable = false</c>.</para>
/// </summary>
public static class BuyNowHandler
{
    public static async Task HandleAsync(
        BuyNow command,
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

        // Rejection chain above guarantees BuyItNowAvailable is true, which in turn
        // guarantees BuyItNowPrice is set (Apply(BiddingOpened) couples them).
        var price = state.BuyItNowPrice!.Value;
        var purchased = new BuyItNowPurchased(
            ListingId: command.ListingId,
            BuyerId: command.BuyerId,
            Price: price,
            PurchasedAt: now);

        var wrapped = session.Events.BuildEvent(purchased);
        wrapped.AddTag(new ListingStreamId(command.ListingId));
        session.Events.Append(command.ListingId, wrapped);
    }

    /// <summary>
    /// Pure acceptance-path projection, exposed for unit tests that exercise the decision
    /// logic without the bus. Mirrors <see cref="PlaceBidHandler.Decide"/> — named
    /// <c>Decide</c> (not <c>Handle</c>) to avoid matching Wolverine's handler-discovery
    /// convention when a second static method would otherwise be discovered as a sibling.
    /// Returns an empty <see cref="Events"/> collection on any rejection path.
    /// </summary>
    public static Events Decide(
        BuyNow command,
        BidConsistencyState state,
        TimeProvider time)
    {
        var now = time.GetUtcNow();
        var reason = EvaluateRejection(command, state, now);
        if (reason is not null)
            return new Events();

        var price = state.BuyItNowPrice!.Value;
        return new Events
        {
            new BuyItNowPurchased(
                ListingId: command.ListingId,
                BuyerId: command.BuyerId,
                Price: price,
                PurchasedAt: now)
        };
    }

    public static EventTagQuery BuildQuery(Guid listingId) =>
        EventTagQuery
            .For(new ListingStreamId(listingId))
            .AndEventsOfType<BiddingOpened, BidPlaced, BuyItNowOptionRemoved, ReserveMet, ExtendedBiddingTriggered, BuyItNowPurchased>();

    private static async Task AppendRejectionAudit(
        IDocumentSession session,
        BuyNow command,
        BidConsistencyState state,
        string reason,
        DateTimeOffset now)
    {
        // AttemptedAmount captures the BIN price the buyer would have paid. If BuyItNowPrice
        // is null the scenario is scenario 2.2 territory (option removed / never set) — fall
        // back to 0 so the audit record carries a numeric amount.
        var rejected = new BidRejected(
            ListingId: command.ListingId,
            BidderId: command.BuyerId,
            AttemptedAmount: state.BuyItNowPrice ?? 0m,
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

    private static string? EvaluateRejection(BuyNow command, BidConsistencyState state, DateTimeOffset now)
    {
        // Scenario 2.4's "no BiddingOpened" twin — a BuyNow on a never-opened listing
        // reaches this handler if the command is dispatched in error. Guarded the same way
        // PlaceBidHandler guards scenario 1.6.
        if (state.ListingId == Guid.Empty)
            return "ListingNotOpen";

        // Scenario 2.4 — ScheduledCloseAt in the past. BiddingClosed / ListingPassed land in
        // S5; until then, closure is derived from timing the same way PlaceBidHandler
        // scenario 1.7 handles it. Apply(BuyItNowPurchased) also sets IsOpen = false, so a
        // terminal listing rejects here via the IsOpen check below.
        if (now >= state.ScheduledCloseAt)
            return "ListingClosed";

        if (!state.IsOpen)
            return "ListingClosed";

        // Scenario 2.2 — option removed (first bid landed) or never set on the listing.
        if (!state.BuyItNowAvailable || state.BuyItNowPrice is not { } price)
            return "BuyItNowNotAvailable";

        // Scenario 2.3 — BIN price exceeds the credit ceiling carried on the command.
        if (price > command.CreditCeiling)
            return "ExceedsCreditCeiling";

        return null;
    }
}
