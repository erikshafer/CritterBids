using CritterBids.Contracts.Auctions;
using Marten;

namespace CritterBids.Listings;

/// <summary>
/// Wolverine handler that consumes the six auction integration events the Auctions BC
/// publishes — <see cref="BiddingOpened"/>, <see cref="BidPlaced"/>,
/// <see cref="BiddingClosed"/>, <see cref="ListingSold"/>, <see cref="ListingPassed"/>,
/// and <see cref="BuyItNowPurchased"/> — over the "listings-auctions-events" RabbitMQ
/// queue, and writes the resulting state transitions to <see cref="CatalogListingView"/>
/// in the Marten "listings" schema.
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
/// </summary>
public static class AuctionStatusHandler
{
    public static async Task Handle(
        BiddingOpened message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken)
            ?? new CatalogListingView { Id = message.ListingId };

        session.Store(view with
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
        var view = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken)
            ?? new CatalogListingView { Id = message.ListingId };

        // BidCount is set authoritatively from the message (M3-S6 OQ6 Path (a))
        // — never incremented. DCB monotonicity at the source plus last-write-wins
        // here makes this naturally idempotent under at-least-once redelivery.
        session.Store(view with
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
        var view = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken)
            ?? new CatalogListingView { Id = message.ListingId };

        // Mechanical close signal — followed by ListingSold or ListingPassed on the
        // timer paths (per S5b retro §"What M3-S6 should know" §3). Not emitted on
        // the BIN or Withdrawn terminal paths.
        session.Store(view with
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
        var view = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken)
            ?? new CatalogListingView { Id = message.ListingId };

        // Final outcome on the sold path — preceded by BiddingClosed on the
        // timer path. SoldAt overrides any prior ClosedAt set by BiddingClosed
        // so the catalog reflects the terminal time of sale.
        session.Store(view with
        {
            Status      = "Sold",
            HammerPrice = message.HammerPrice,
            WinnerId    = message.WinnerId,
            BidCount    = message.BidCount,
            ClosedAt    = message.SoldAt
        });
    }
}
