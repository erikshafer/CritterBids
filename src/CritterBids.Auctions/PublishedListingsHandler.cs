using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Auctions;

/// <summary>
/// Wolverine handler that maintains the <see cref="PublishedListings"/> Auctions-side
/// cache from two Selling-source integration events on the <c>auctions-selling-events</c>
/// RabbitMQ queue (already wired at M3-S3 in <c>Program.cs</c>; the M4-S5 work adds the
/// handler, not the queue):
/// <list type="bullet">
///   <item><c>ListingPublished</c> — creates the row at
///     <see cref="PublishedListingsStatus.Published"/>; idempotent on re-delivery and
///     terminal-state preserving (a re-delivered <c>ListingPublished</c> on a Withdrawn
///     row preserves the terminal state per the M5-S3 <c>PendingSettlement</c> pattern).</item>
///   <item><c>ListingWithdrawn</c> — transitions
///     <see cref="PublishedListingsStatus.Published"/> to
///     <see cref="PublishedListingsStatus.Withdrawn"/> and stamps
///     <see cref="PublishedListings.WithdrawnAt"/>; idempotent on re-delivery.</item>
/// </list>
///
/// <para><b>Tolerant-upsert shape per the marten-projections skill.</b> <c>LoadAsync</c>
/// by <c>ListingId</c>; construct or mutate via record <c>with</c>; <c>session.Store</c>.
/// <c>AutoApplyTransactions()</c> commits after <c>Handle</c> returns. No
/// <c>OutgoingMessages</c>; no <c>IMessageBus</c>. Mirrors the M5-S3
/// <see cref="CritterBids.Settlement.PendingSettlementHandler"/> shape verbatim.</para>
///
/// <para><b>Multi-handler-on-<c>ListingPublished</c> within Auctions BC.</b> After M4-S5,
/// the Auctions BC has TWO handlers for <c>ListingPublished</c>:
/// <see cref="ListingPublishedHandler"/> (M3 — opens the Listing aggregate's primary
/// stream with <c>BiddingOpened</c>) and this handler (caches the projection row). Same
/// cross-cut as the M4-S3 <c>BidPlaced</c> two-handler topology. Under
/// <see cref="Wolverine.MultipleHandlerBehavior.Separated"/>, each handler runs on its
/// own endpoint — any in-test dispatch of <c>ListingPublished</c> via the bus must use
/// <c>SendMessageAndWaitAsync</c> per <c>wolverine-sagas.md</c> §"Multiple Handlers +
/// Separated". The existing M3 <c>BiddingOpenedConsumerTests</c> calls
/// <c>ListingPublishedHandler.Handle()</c> directly and is unaffected.</para>
/// </summary>
public static class PublishedListingsHandler
{
    public static async Task Handle(
        ListingPublished message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<PublishedListings>(
            message.ListingId, cancellationToken);

        // Terminal-state preservation: a re-delivered ListingPublished against a row
        // that already transitioned to Withdrawn preserves the Withdrawn status. The
        // upstream contract is immutable per ListingPublished's docstring, so the
        // remaining fields can safely be upserted from the message; only the Status
        // field is sticky.
        var status = existing?.Status ?? PublishedListingsStatus.Published;

        session.Store(new PublishedListings
        {
            Id                            = message.ListingId,
            SellerId                      = message.SellerId,
            StartingBid                   = message.StartingBid,
            ReservePrice                  = message.ReservePrice,
            BuyItNowPrice                 = message.BuyItNow,
            Duration                      = message.Duration,
            ExtendedBiddingEnabled        = message.ExtendedBiddingEnabled,
            ExtendedBiddingTriggerWindow  = message.ExtendedBiddingTriggerWindow,
            ExtendedBiddingExtension      = message.ExtendedBiddingExtension,
            PublishedAt                   = message.PublishedAt,
            WithdrawnAt                   = existing?.WithdrawnAt,
            Status                        = status,
        });
    }

    public static async Task Handle(
        ListingWithdrawn message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<PublishedListings>(
            message.ListingId, cancellationToken);

        // Re-delivery against an already-Withdrawn row: preserve the original WithdrawnAt
        // and the existing payload. Idempotent on re-delivery per the M5-S3 PendingSettlement
        // terminal-status pattern.
        if (existing is null)
        {
            // First arrival of ListingWithdrawn before ListingPublished — possible under
            // cross-queue race conditions (auctions-selling-events delivers both events;
            // re-ordering can in principle occur on retry). Create a minimal row at
            // Withdrawn status; downstream consumers see Status = Withdrawn and reject
            // attach attempts uniformly. Subsequent ListingPublished re-delivery (item
            // above) preserves Status = Withdrawn via the terminal-status guard.
            session.Store(new PublishedListings
            {
                Id          = message.ListingId,
                WithdrawnAt = message.WithdrawnAt,
                Status      = PublishedListingsStatus.Withdrawn,
            });
            return;
        }

        if (existing.Status == PublishedListingsStatus.Withdrawn) return;

        session.Store(existing with
        {
            Status      = PublishedListingsStatus.Withdrawn,
            WithdrawnAt = message.WithdrawnAt,
        });
    }
}
