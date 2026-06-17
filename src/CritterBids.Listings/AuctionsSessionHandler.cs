using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Listings;

/// <summary>
/// Wolverine handler consuming the Auctions BC's Session-aggregate trio that the
/// catalog cares about — <see cref="ListingAttachedToSession"/> and
/// <see cref="SessionStarted"/> — over the "listings-auctions-events" RabbitMQ queue.
/// Writes the two M4-S6 session-membership fields on <see cref="CatalogListingView"/>:
/// <see cref="CatalogListingView.SessionId"/> from <c>ListingAttachedToSession</c>
/// and <see cref="CatalogListingView.SessionStartedAt"/> from <c>SessionStarted</c>.
///
/// Third lived application of the M3-D2 Path A cross-BC read-model extension pattern
/// formalized by ADR-014; sibling to <see cref="AuctionStatusHandler"/> (M3-S6),
/// <see cref="ListingPublishedHandler"/> (M2-S7), and <see cref="SettlementStatusHandler"/>
/// (M5-S6). Sub-Option A resolution of the ADR-014 sub-question pinned at M4-S6 session
/// open: one handler class per source BC; this class is the Auctions-source sibling.
/// <see cref="SellingListingWithdrawnHandler"/> is the matching Selling-source sibling
/// landing the <c>"Withdrawn"</c> status transition.
///
/// <c>SessionCreated</c> has no per-listing catalog consequence — the catalog has no
/// per-session document — and is intentionally not handled. Operations BC may add a
/// SessionCatalog view post-M5 if ops tooling needs it; that is its own slice.
///
/// Tolerant upsert per <c>marten-projections.md §"Handler-Driven Projections — Tolerant
/// Upsert"</c>: <c>LoadAsync</c>; if absent (cross-queue race where the session event
/// arrives before <c>ListingPublished</c>), construct a minimal row at
/// <c>Status = "Published"</c>. The seed handler's M5-S6 named field-preservation block
/// is amended at M4-S6 to also preserve <c>SessionId</c> and <c>SessionStartedAt</c> so
/// later <c>ListingPublished</c> delivery does not regress them.
/// </summary>
[StickyHandler("listings-auctions-events")]
public static class AuctionsSessionHandler
{
    public static async Task Handle(
        ListingAttachedToSession message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken);
        var view = existing ?? new CatalogListingView { Id = message.ListingId };

        // M9-S7: Insert on first write so a concurrent creator on the selling/settlement queue
        // collides instead of silently overwriting; Store merges once the row exists.
        session.InsertOrStore(existing, view with { SessionId = message.SessionId });
    }

    public static async Task Handle(
        SessionStarted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Batch-load every listing in the session in one round-trip. Sessions
        // typically carry a small list (Workshop 001's demo flow is three);
        // LoadManyAsync still beats N sequential LoadAsync calls and matches
        // the M5-S6 single-listing tolerant-upsert primitive once the rows are
        // in hand.
        var existing = await session.Query<CatalogListingView>()
            .Where(v => message.ListingIds.Contains(v.Id))
            .ToListAsync(cancellationToken);

        var existingById = existing.ToDictionary(v => v.Id);

        foreach (var listingId in message.ListingIds)
        {
            existingById.TryGetValue(listingId, out var current);
            var view = current ?? new CatalogListingView { Id = listingId };

            // M9-S7: Insert any listing not yet in the catalog (cross-queue create race);
            // Store merges SessionStartedAt onto rows that already exist.
            session.InsertOrStore(current, view with { SessionStartedAt = message.StartedAt });
        }
    }
}
