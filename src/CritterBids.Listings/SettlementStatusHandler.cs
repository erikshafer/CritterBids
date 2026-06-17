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
/// Status-transition guard (amended M8-S3c): <c>"Settled"</c> is reached from any
/// pre-terminal status — normally from <c>"Sold"</c>, but under ADR 027's exactly-once
/// delivery <c>SettlementCompleted</c> can process before <c>ListingSold</c> (they ride
/// different queues and the settlement pipeline completes in milliseconds), so earlier
/// statuses take the transition too. The no-sale terminals (<c>"Passed"</c>,
/// <c>"Withdrawn"</c>) never settle and absorb contradictory arrivals without throwing —
/// mirrors the M5-S3 <c>PendingSettlementHandler</c> status-preservation discipline.
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
            // arrival. M9-S7: Insert (not Store) so a concurrent creator on another queue
            // collides with DocumentAlreadyExistsException and is retried into the merge path.
            session.Insert(new CatalogListingView
            {
                Id        = message.ListingId,
                Status    = "Settled",
                SettledAt = message.CompletedAt,
            });
            return;
        }

        // "Settled" is absorbing (re-delivery no-ops) and the no-sale terminals ("Passed",
        // "Withdrawn") never produce SettlementCompleted — a contradictory arrival leaves them
        // untouched. Every pre-terminal status ("Published", "Open", "Closed", "Sold") takes the
        // transition: under ADR 027's exactly-once delivery, SettlementCompleted (on
        // listings-settlement-events) can legitimately process BEFORE ListingSold (on
        // listings-auctions-events) — the settlement pipeline completes in milliseconds — and the
        // former Sold-only guard left the row stuck at "Sold" forever once the trailing fan-out
        // duplicates that used to retry the transition disappeared (M8-S3c live verification).
        // AuctionStatusHandler's outcome handlers preserve an already-Settled row symmetrically.
        if (existing.Status is "Settled" or "Passed" or "Withdrawn") return;

        session.Store(existing with
        {
            Status    = "Settled",
            SettledAt = message.CompletedAt,
        });
    }
}
