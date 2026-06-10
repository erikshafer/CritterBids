using CritterBids.Contracts.Settlement;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Listings;

/// <summary>
/// Wolverine handler that consumes <see cref="SettlementCompleted"/> from the Settlement BC
/// (via RabbitMQ queue "listings-settlement-events") and transitions
/// <see cref="CatalogListingView.Status"/> from <c>"Sold"</c> to <c>"Settled"</c>,
/// stamping <see cref="CatalogListingView.SettledAt"/> from the event's <c>CompletedAt</c>.
///
/// Sibling to <see cref="AuctionStatusHandler"/> (M3-S6) and
/// <see cref="ListingPublishedHandler"/> (M2-S7) — one sibling class per source BC,
/// single-source per sibling. Second lived application of the M3-D2 Path A cross-BC
/// read-model extension pattern formalized by ADR-014 (M5-S6).
///
/// Status-transition guard: only <c>"Sold"</c> → <c>"Settled"</c> is legal.
/// <c>"Passed"</c> listings never settle (the financial workflow only runs on the sold
/// paths). Any non-"Sold" arrival state no-ops without throwing — mirrors the M5-S3
/// <c>PendingSettlementHandler</c> status-preservation discipline.
///
/// Tolerant upsert per <c>marten-projections.md §"Handler-Driven Projections — Tolerant
/// Upsert"</c>: <c>LoadAsync</c>; if absent (a structurally near-impossible cross-queue
/// race where SettlementCompleted arrives before ListingPublished), construct a minimal
/// row at <c>Status = "Settled"</c>. The M5-S6 amendment to
/// <see cref="ListingPublishedHandler"/> ensures that the row's Settled state is preserved
/// when ListingPublished later arrives and fills in the M2 fields.
/// </summary>
[StickyHandler("listings-settlement-events")]
public static class SettlementStatusHandler
{
    public static async Task Handle(
        SettlementCompleted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);

        if (existing is null)
        {
            // Tolerant upsert on the rare cross-queue race. ListingPublishedHandler's
            // M5-S6 amendment will preserve Status = "Settled" and SettledAt on later
            // arrival.
            session.Store(new CatalogListingView
            {
                Id        = message.ListingId,
                Status    = "Settled",
                SettledAt = message.CompletedAt,
            });
            return;
        }

        // Only the "Sold" → "Settled" transition is legal. "Passed" listings never
        // produce SettlementCompleted (the failure path emits PaymentFailed instead),
        // so this guard primarily defends against earlier-phase arrivals from re-delivery
        // or unexpected queue interleaving.
        if (existing.Status != "Sold") return;

        session.Store(existing with
        {
            Status    = "Settled",
            SettledAt = message.CompletedAt,
        });
    }
}
