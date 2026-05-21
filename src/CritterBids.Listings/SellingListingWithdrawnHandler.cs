using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Listings;

/// <summary>
/// Wolverine handler consuming <see cref="ListingWithdrawn"/> from the Selling BC
/// (via RabbitMQ queue "listings-selling-events") and landing the <c>"Withdrawn"</c>
/// status transition on <see cref="CatalogListingView"/> alongside the
/// <see cref="CatalogListingView.ClosedAt"/> stamp from
/// <see cref="ListingWithdrawn.WithdrawnAt"/>.
///
/// Third lived application of the M3-D2 Path A cross-BC read-model extension pattern
/// formalized by ADR-014; sibling to <see cref="AuctionStatusHandler"/> (M3-S6),
/// <see cref="ListingPublishedHandler"/> (M2-S7), and <see cref="SettlementStatusHandler"/>
/// (M5-S6). Sub-Option A resolution of the ADR-014 sub-question pinned at M4-S6 session
/// open: one handler class per source BC; this class is the Selling-source sibling.
/// <see cref="AuctionsSessionHandler"/> is the matching Auctions-source sibling landing
/// the session-membership fields.
///
/// Status-preservation guard (M4-S6 OQ4 resolution): only the <c>"Published"</c> →
/// <c>"Withdrawn"</c> and <c>"Open"</c> → <c>"Withdrawn"</c> transitions are legal.
/// Arrivals against <c>"Closed"</c>, <c>"Sold"</c>, <c>"Passed"</c>, or <c>"Settled"</c>
/// rows no-op without throwing — those terminals are absorbing and a late-arriving
/// <c>ListingWithdrawn</c> must not regress them. Mirrors the M5-S6
/// <see cref="SettlementStatusHandler"/> shape: load, guard, store-or-no-op.
///
/// Tolerant upsert per <c>marten-projections.md §"Handler-Driven Projections — Tolerant
/// Upsert"</c>: <c>LoadAsync</c>; if absent (a structurally near-impossible cross-queue
/// race where <c>ListingWithdrawn</c> arrives before <c>ListingPublished</c>), construct
/// a minimal row at <c>Status = "Withdrawn"</c>. The seed handler's M5-S6 named
/// field-preservation block is amended at M4-S6 so later <c>ListingPublished</c> delivery
/// preserves the Withdrawn terminal.
/// </summary>
public static class SellingListingWithdrawnHandler
{
    public static async Task Handle(
        ListingWithdrawn message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);

        if (existing is null)
        {
            // Structurally near-impossible cross-queue race: ListingWithdrawn arriving
            // before ListingPublished. Seed a minimal row at the terminal — the
            // ListingPublishedHandler M5-S6 amendment (M4-S6 extension) preserves it on
            // later arrival.
            session.Store(new CatalogListingView
            {
                Id       = message.ListingId,
                Status   = "Withdrawn",
                ClosedAt = message.WithdrawnAt,
            });
            return;
        }

        // Only "Published" and "Open" are legal pre-states. The four other vocabularies
        // ("Closed", "Sold", "Passed", "Settled") are absorbing terminals — a late
        // ListingWithdrawn must not regress them.
        if (existing.Status is not ("Published" or "Open")) return;

        session.Store(existing with
        {
            Status   = "Withdrawn",
            ClosedAt = message.WithdrawnAt,
        });
    }
}
