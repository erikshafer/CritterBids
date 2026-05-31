using CritterBids.Contracts.Auctions;
using Marten;

namespace CritterBids.Operations;

/// <summary>
/// Operations BC's <b>Auctions-family</b> lot-board consumer — the second of the two ADR-014
/// Path A Sub-Option A sibling handlers that maintain <see cref="LotBoardView"/> (the other being
/// <see cref="LotBoardSellingHandler"/>). This sibling owns the Auctions-source events:
/// <c>BiddingOpened</c> (→ <see cref="LotBoardStatus.Open"/> + schedule), <c>BidPlaced</c>
/// (bid figures), <c>ListingSold</c> (→ <see cref="LotBoardStatus.Sold"/> outcome), and
/// <c>ListingPassed</c> (→ <see cref="LotBoardStatus.Passed"/> outcome). One sibling class per
/// source BC; both upsert the same <see cref="LotBoardView.ListingId"/>-keyed document. The handler
/// returns <see cref="Task"/> and writes only via the injected Marten session — Operations is a
/// pure consumer, so there are <b>no</b> <c>OutgoingMessages</c> and <b>no</b> <c>IMessageBus</c>.
///
/// <para><b>Status + guards (W006 §2).</b> <see cref="LotBoardStatusRules.Advance"/> keeps the
/// status monotone and terminal-absorbing: a late <c>BidPlaced</c> cannot regress a terminal row to
/// <see cref="LotBoardStatus.Open"/>. <see cref="LotBoardView.SellerId"/> is set-once across
/// <c>BiddingOpened</c>/<c>ListingSold</c> (and Selling's <c>ListingPublished</c>) via the
/// <see cref="System.Guid.Empty"/> sentinel — <c>ListingSold</c> populates it when it is the first
/// carrier. <see cref="LotBoardView.LastUpdatedAt"/> is latest-wins off each event's own timestamp.
/// <c>BidPlaced</c>'s figure write is guarded: it advances <see cref="LotBoardView.CurrentBid"/>/
/// <see cref="LotBoardView.BidCount"/> only when the row is non-terminal and the incoming
/// <c>BidCount</c> is not stale (monotone), so an out-of-order older bid never rewinds the figures
/// and a late bid after close never disturbs the final ones.</para>
/// </summary>
public static class LotBoardAuctionsHandler
{
    public static async Task Handle(
        BiddingOpened message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ListingId, cancellationToken);

        session.Store(view with
        {
            SellerId         = view.SellerId == Guid.Empty ? message.SellerId : view.SellerId,
            StartingBid      = message.StartingBid,
            ScheduledCloseAt = message.ScheduledCloseAt,
            Status           = LotBoardStatusRules.Advance(view.Status, LotBoardStatus.Open),
            LastUpdatedAt    = Latest(view.LastUpdatedAt, message.OpenedAt),
        });
    }

    public static async Task Handle(
        BidPlaced message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ListingId, cancellationToken);

        // Bid-figure regression guard: only advance CurrentBid/BidCount when the row is not yet
        // terminal AND the incoming BidCount is not stale. A late BidPlaced after a terminal close
        // leaves the final figures intact (status guard handles the status); an out-of-order older
        // bid (lower BidCount) before close does not rewind the figures (bids are monotone — W006
        // §2 CurrentBid "latest-wins (highest)"). LastUpdatedAt still advances latest-wins.
        var applyFigures = !LotBoardStatusRules.IsTerminal(view.Status)
                           && message.BidCount >= view.BidCount;

        session.Store(view with
        {
            CurrentBid    = applyFigures ? message.Amount : view.CurrentBid,
            BidCount      = applyFigures ? message.BidCount : view.BidCount,
            Status        = LotBoardStatusRules.Advance(view.Status, LotBoardStatus.Open),
            LastUpdatedAt = Latest(view.LastUpdatedAt, message.PlacedAt),
        });
    }

    public static async Task Handle(
        ListingSold message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ListingId, cancellationToken);

        // ListingSold is a set-once SellerId carrier — it must populate SellerId when it is the
        // first event to arrive for the listing (W006 §2: SellerId traces to three events).
        session.Store(view with
        {
            SellerId      = view.SellerId == Guid.Empty ? message.SellerId : view.SellerId,
            HammerPrice   = message.HammerPrice,
            WinnerId      = message.WinnerId,
            BidCount      = message.BidCount,
            Status        = LotBoardStatusRules.Advance(view.Status, LotBoardStatus.Sold),
            LastUpdatedAt = Latest(view.LastUpdatedAt, message.SoldAt),
        });
    }

    public static async Task Handle(
        ListingPassed message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ListingId, cancellationToken);

        session.Store(view with
        {
            PassReason    = message.Reason,
            BidCount      = message.BidCount,
            Status        = LotBoardStatusRules.Advance(view.Status, LotBoardStatus.Passed),
            LastUpdatedAt = Latest(view.LastUpdatedAt, message.PassedAt),
        });
    }

    private static async Task<LotBoardView> LoadOrCreate(
        IDocumentSession session,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<LotBoardView>(listingId, cancellationToken);
        return existing ?? new LotBoardView { ListingId = listingId };
    }

    private static DateTimeOffset Latest(DateTimeOffset existing, DateTimeOffset incoming) =>
        incoming > existing ? incoming : existing;
}
