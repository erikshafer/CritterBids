using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Listings;

/// <summary>
/// Wolverine handler that consumes the seven auction integration events the Auctions BC
/// publishes — <see cref="BiddingOpened"/>, <see cref="BidPlaced"/>,
/// <see cref="BiddingClosed"/>, <see cref="ListingSold"/>, <see cref="ListingPassed"/>,
/// <see cref="BuyItNowPurchased"/>, and <see cref="ExtendedBiddingTriggered"/> — over the
/// "listings-auctions-events" RabbitMQ queue, and writes the resulting state transitions
/// to <see cref="CatalogListingView"/> in the Marten "listings" schema.
///
/// Sibling to <see cref="ListingPublishedHandler"/> (M2-S7 OQ1 Path B from M3-S6) — kept
/// in its own static class so the M2 handler's Selling-sourced upsert stays byte-identical
/// and this file's name describes what it does.
///
/// Upsert shape per handler: LoadAsync the document by ListingId; if absent (cross-queue
/// race where an auction event arrived before ListingPublished — M3-S6 OQ4 Path II),
/// construct a minimal view with Id set and M2 fields at default; mutate the auction-status
/// fields via record `with`; session.Store. AutoApplyTransactions commits after Handle()
/// returns. No OutgoingMessages, no IMessageBus.
///
/// M4-S6 amendment: <see cref="Handle(BiddingOpened, IDocumentSession, CancellationToken)"/>
/// carries a Withdrawn-preservation guard. The Auctions-side fan-out emits
/// <c>BiddingOpened</c> for every listing in <c>SessionStarted.ListingIds</c> regardless
/// of <c>PublishedListings.Status</c>; the guard is the load-bearing edit that pins the
/// OQ3 Path α terminal state observed in the cross-BC composition test
/// (<see cref="CatalogListingView.Status"/> stays <c>"Withdrawn"</c>; the catalog is the
/// source of truth for the user-facing terminal). See ADR-014 §"Decision" §4 and the
/// M4-S6 retrospective. The other five Handle methods on this class do NOT carry the
/// guard — only <c>BiddingOpened</c> is on the fan-out's emission path.
/// </summary>
[StickyHandler("listings-auctions-events")]
public static class AuctionStatusHandler
{
    public static async Task Handle(
        BiddingOpened message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        // M4-S6: Withdrawn-preservation guard for OQ3 Path α (BiddingOpened arriving at
        // a withdrawn listing via the Session fan-out). The Auctions-side fan-out emits
        // BiddingOpened for every listing in SessionStarted.ListingIds regardless of
        // PublishedListings.Status (M4 milestone doc §3 — "Defensive pre-filtering at
        // StartSession time is post-MVP hardening"). The catalog handler is the source
        // of truth: Withdrawn is terminal and absorbing; the fan-out's emission no-ops.
        // ScheduledCloseAt is not advanced either — symmetric with the M5-S6
        // SettlementStatusHandler total-guard shape. See M4-S6 retrospective + ADR-014
        // amendment.
        if (view.Status == "Withdrawn") return;

        session.InsertOrStore(existing, view with
        {
            Status           = "Open",
            ScheduledCloseAt = message.ScheduledCloseAt
        });
    }

    public static async Task Handle(
        BidPlaced message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        // BidCount is set authoritatively from the message (M3-S6 OQ6 Path (a))
        // — never incremented. DCB monotonicity at the source plus last-write-wins
        // here makes this naturally idempotent under at-least-once redelivery.
        session.InsertOrStore(existing, view with
        {
            CurrentHighBid      = message.Amount,
            CurrentHighBidderId = message.BidderId,
            BidCount            = message.BidCount
        });
    }

    public static async Task Handle(
        BiddingClosed message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        // M8-S3c: Settled is absorbing — SettlementCompleted (listings-settlement-events) can
        // process before this queue's close signal under exactly-once delivery.
        if (view.Status == "Settled") return;

        // Mechanical close signal — followed by ListingSold or ListingPassed on the
        // timer paths (per S5b retro §"What M3-S6 should know" §3). Not emitted on
        // the BIN or Withdrawn terminal paths.
        session.InsertOrStore(existing, view with
        {
            Status   = "Closed",
            ClosedAt = message.ClosedAt
        });
    }

    public static async Task Handle(
        ListingSold message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        // Final outcome on the sold path — preceded by BiddingClosed on the
        // timer path. SoldAt overrides any prior ClosedAt set by BiddingClosed
        // so the catalog reflects the terminal time of sale.
        // M8-S3c: an already-Settled row keeps its terminal (SettlementCompleted on the other
        // queue can process first under exactly-once delivery); the sale payload still lands.
        session.InsertOrStore(existing, view with
        {
            Status      = view.Status == "Settled" ? "Settled" : "Sold",
            HammerPrice = message.HammerPrice,
            WinnerId    = message.WinnerId,
            BidCount    = message.BidCount,
            ClosedAt    = message.SoldAt
        });
    }

    public static async Task Handle(
        ListingPassed message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        // Final outcome on the no-sale paths. Reason is one of "NoBids" or
        // "ReserveNotMet"; HighestBid is null when Reason = "NoBids".
        // PassedAt overrides any prior ClosedAt set by BiddingClosed.
        session.InsertOrStore(existing, view with
        {
            Status          = "Passed",
            PassedReason    = message.Reason,
            FinalHighestBid = message.HighestBid,
            BidCount        = message.BidCount,
            ClosedAt        = message.PassedAt
        });
    }

    public static async Task Handle(
        ExtendedBiddingTriggered message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        if (view.Status == "Withdrawn") return;

        session.InsertOrStore(existing, view with
        {
            ScheduledCloseAt = message.NewCloseAt
        });
    }

    public static async Task Handle(
        BuyItNowPurchased message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        // BIN is its own terminal — no preceding BiddingClosed (S5b retro
        // §"What M3-S6 should know" §5). Status transitions directly from
        // any prior state ("Published" or "Open") to "Sold". HammerPrice
        // captures the BIN price; WinnerId is the buyer.
        // M8-S3c: an already-Settled row keeps its terminal (the BIN settlement pipeline can
        // outrun this queue's copy under exactly-once delivery); the sale payload still lands.
        session.InsertOrStore(existing, view with
        {
            Status      = view.Status == "Settled" ? "Settled" : "Sold",
            HammerPrice = message.Price,
            WinnerId    = message.BuyerId,
            ClosedAt    = message.PurchasedAt
        });
    }
}
