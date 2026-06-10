using CritterBids.Contracts.Selling;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Operations;

/// <summary>
/// Operations BC's <b>Selling-family</b> lot-board consumer — one of the two ADR-014 Path A
/// Sub-Option A sibling handlers that maintain <see cref="LotBoardView"/> (the other being
/// <see cref="LotBoardAuctionsHandler"/>). This sibling owns the Selling-source events:
/// <c>ListingPublished</c> (seeds the row in <see cref="LotBoardStatus.Draft"/> with the catalog
/// fields) and <c>ListingWithdrawn</c> (the terminal <see cref="LotBoardStatus.Withdrawn"/> path).
/// One sibling class per source BC; both upsert the same <see cref="LotBoardView.ListingId"/>-keyed
/// document. The handler returns <see cref="Task"/> and writes only via the injected Marten session
/// — Operations is a pure consumer, so there are <b>no</b> <c>OutgoingMessages</c> and <b>no</b>
/// <c>IMessageBus</c>.
///
/// <para><b>Tolerant upsert + load-and-preserve.</b> Each overload loads-or-constructs the row by
/// <see cref="LotBoardView.ListingId"/>, mutates via record <c>with</c>, and stores. Because
/// <c>with</c> only changes the listed fields, auction state already set by
/// <see cref="LotBoardAuctionsHandler"/> (e.g. <c>CurrentBid</c>, <c>BidCount</c>,
/// <c>ScheduledCloseAt</c>) survives a late-arriving <c>ListingPublished</c> seed untouched, and
/// <see cref="LotBoardStatusRules.Advance"/> prevents the seed from regressing
/// <see cref="LotBoardView.Status"/> back to <see cref="LotBoardStatus.Draft"/> (ADR 014 seed-handler
/// discipline).</para>
///
/// <para><b>METHOD-level sticky bindings (ADR 027, M8-S3c).</b> This class consumes from two
/// Operations-owned queues — <c>ListingPublished</c> rides <c>operations-selling-events</c> while
/// <c>ListingWithdrawn</c> rides <c>operations-auctions-events</c> per the M7 milestone §2 queue
/// table literal — so the bindings sit on the methods, not the class.</para>
/// </summary>
public static class LotBoardSellingHandler
{
    [StickyHandler("operations-selling-events")]
    public static async Task Handle(
        ListingPublished message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ListingId, cancellationToken);

        // Seed the catalog-shape fields. Status advances to Draft only if the row is still new —
        // Advance keeps an already-Open/terminal row (the seed-after-auction-events case). SellerId
        // is set-once via the Guid.Empty sentinel; an Auctions event may have set it first.
        session.Store(view with
        {
            SellerId      = view.SellerId == Guid.Empty ? message.SellerId : view.SellerId,
            Title         = message.Title,
            Format        = message.Format,
            StartingBid   = message.StartingBid,
            ReservePrice  = message.ReservePrice,
            BuyItNow      = message.BuyItNow,
            FeePercentage = message.FeePercentage,
            Status        = LotBoardStatusRules.Advance(view.Status, LotBoardStatus.Draft),
            LastUpdatedAt = Latest(view.LastUpdatedAt, message.PublishedAt),
        });
    }

    [StickyHandler("operations-auctions-events")]
    public static async Task Handle(
        ListingWithdrawn message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.ListingId, cancellationToken);

        // Withdrawal is a terminal status (absorbing). Advance keeps an existing terminal status if
        // one was somehow already set, but populates the withdrawal-specific fields regardless.
        session.Store(view with
        {
            WithdrawnBy      = message.WithdrawnBy,
            WithdrawalReason = message.Reason,
            Status           = LotBoardStatusRules.Advance(view.Status, LotBoardStatus.Withdrawn),
            LastUpdatedAt    = Latest(view.LastUpdatedAt, message.WithdrawnAt),
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
