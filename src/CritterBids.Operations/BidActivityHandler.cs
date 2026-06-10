using CritterBids.Contracts.Auctions;
using Marten;

namespace CritterBids.Operations;

/// <summary>
/// Operations BC's bid-activity feed consumer — the Auctions-source handler that appends one
/// immutable <see cref="BidActivityEntry"/> per accepted bid (W006 §3). This is the single
/// feed-shaped Operations surface: unlike the upsert handlers it does not load-mutate a keyed row,
/// it appends a new row per distinct <c>BidPlaced.BidId</c>. The handler returns <see cref="Task"/>
/// and writes only via the injected Marten session — Operations is a pure consumer, so there are
/// <b>no</b> <c>OutgoingMessages</c> and <b>no</b> <c>IMessageBus</c>.
///
/// <para><b>Idempotent append.</b> The feed must yield exactly one row per bid, so a re-delivered
/// <c>BidPlaced</c> (same <see cref="BidActivityEntry.BidId"/>) is a no-op insert, not a second row.
/// The handler enforces this explicitly with a load-check-then-skip: if a row already exists for the
/// <see cref="BidActivityEntry.BidId"/>, it returns without storing. (A blind <c>Store</c> would also
/// not create a duplicate row — <c>BidId</c> is the key — but the explicit skip keeps the rows
/// genuinely immutable / append-only rather than silently re-writing on redelivery.)</para>
///
/// <para><b>No longer separately discovered (ADR 027, M8-S3c).</b> The M7 shape ran this append as
/// its own handler chain beside <see cref="LotBoardAuctionsHandler"/>'s <c>BidPlaced</c> upsert.
/// Sticky dispatch executes at most one handler class per (message type, endpoint), so the append
/// is now a plain function (deliberately not named <c>Handle</c>) invoked from
/// <see cref="LotBoardAuctionsHandler"/>'s discovered <c>BidPlaced</c> handler — same session,
/// one commit, same idempotent append semantics.</para>
/// </summary>
public static class BidActivityHandler
{
    public static async Task AppendActivityAsync(
        BidPlaced message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Append-only dedupe: one row per BidId. A re-delivered bid is skipped so the feed never
        // grows a duplicate and existing rows stay immutable.
        var existing = await session.LoadAsync<BidActivityEntry>(message.BidId, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        session.Store(new BidActivityEntry
        {
            BidId     = message.BidId,
            ListingId = message.ListingId,
            BidderId  = message.BidderId,
            Amount    = message.Amount,
            BidCount  = message.BidCount,
            IsProxy   = message.IsProxy,
            PlacedAt  = message.PlacedAt,
        });
    }
}
