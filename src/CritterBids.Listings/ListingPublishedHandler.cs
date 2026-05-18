using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Listings;

/// <summary>
/// Wolverine handler that consumes <see cref="ListingPublished"/> from the Selling BC
/// (via RabbitMQ queue "listings-selling-events") and writes a <see cref="CatalogListingView"/>
/// document to the Marten "listings" schema.
///
/// No [MartenStore] attribute — CritterBids uses a single primary IDocumentStore (ADR 009).
/// No SaveChangesAsync() — AutoApplyTransactions() (configured in Program.cs) commits after Handle() returns.
/// No OutgoingMessages or IMessageBus — this handler produces no downstream messages.
///
/// M5-S6 amendment: load-and-preserve pattern mirrors the M5-S3 PendingSettlementHandler
/// discipline. On re-delivery (or the structurally near-impossible cross-queue race where
/// <see cref="AuctionStatusHandler"/> or <see cref="SettlementStatusHandler"/> created a
/// minimal row first), preserve any downstream-handler state (Status, ClosedAt, SettledAt,
/// bid fields) so that re-delivery of ListingPublished never regresses an already-advanced
/// row. The M2-S7 unconditional-Store pattern is replaced because M5-S6's tolerant-upsert
/// posture on SettlementStatusHandler can produce a Status = "Settled" minimal row before
/// ListingPublished arrives in that edge case.
/// </summary>
public static class ListingPublishedHandler
{
    public static async Task Handle(
        ListingPublished message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);

        session.Store(new CatalogListingView
        {
            // M2 fields — always overwritten with the publish payload (these are the
            // contract's authoritative source).
            Id          = message.ListingId,
            SellerId    = message.SellerId,
            Title       = message.Title,
            Format      = message.Format,
            StartingBid = message.StartingBid,
            BuyItNow    = message.BuyItNow,
            Duration    = message.Duration,
            PublishedAt = message.PublishedAt,

            // Downstream-handler fields — preserved from the existing row if present,
            // default if first delivery. Prevents re-delivery from regressing an
            // already-advanced row's Status / timestamps / bid state.
            Status              = existing?.Status ?? "Published",
            ScheduledCloseAt    = existing?.ScheduledCloseAt,
            CurrentHighBid      = existing?.CurrentHighBid,
            CurrentHighBidderId = existing?.CurrentHighBidderId,
            BidCount            = existing?.BidCount ?? 0,
            HammerPrice         = existing?.HammerPrice,
            WinnerId            = existing?.WinnerId,
            PassedReason        = existing?.PassedReason,
            FinalHighestBid     = existing?.FinalHighestBid,
            ClosedAt            = existing?.ClosedAt,
            SettledAt           = existing?.SettledAt,
        });
    }
}
