using CritterBids.Contracts.Auctions;
using Marten;

namespace CritterBids.Auctions;

/// <summary>
/// Wolverine handler for the Flash-session fan-out (Workshop 002 Phase 1 Option B). On
/// inbound <see cref="SessionStarted"/>, opens each attached listing for bidding by
/// appending <see cref="BiddingOpened"/> to its per-listing event stream. UseFastEventForwarding
/// forwards each appended event as a Wolverine message: locally to
/// <c>AuctionClosingSaga.Handle(BiddingOpened)</c> (start handler — creates the
/// per-listing closing saga + schedules <c>CloseAuction</c> at <c>ScheduledCloseAt</c>);
/// externally to the <c>listings-auctions-events</c> RabbitMQ queue (Listings BC's
/// <c>AuctionStatusHandler</c> consumer at M4-S6 — flips
/// <c>CatalogListingView.Status</c> to <c>"Open"</c>).
///
/// <para><b>First in-repo one-inbound-N-outbound fan-out handler.</b> Prior handlers all
/// emit at most one outbound per inbound. The shape lands at M4-S5 per the prompt's
/// "first-use surface" framing.</para>
///
/// <para><b>OQ1 Path A — full PublishedListings payload.</b> The per-listing
/// <see cref="BiddingOpened"/> payload (SellerId, StartingBid, ReservePrice, BuyItNowPrice,
/// extended-bidding fields) reads from the <see cref="PublishedListings"/> cache row
/// inline — no second cross-projection or aggregate stream load. The fan-out is also a
/// consumer of <see cref="PublishedListings"/>, not just the attach-time published-status
/// check. The richer field shape was pinned at session open by the user.</para>
///
/// <para><b>OQ5 Path B — DurationMinutes from the Session aggregate.</b>
/// <see cref="SessionStarted"/> carries <c>StartedAt</c> and <c>ListingIds</c> but not
/// <c>DurationMinutes</c> (that field is on <see cref="SessionCreated"/>, and modifying
/// the M4-S1 contract stub is out of scope). The handler loads the Session aggregate via
/// <see cref="IEventStore.AggregateStreamAsync"/> and reads <c>DurationMinutes</c> from
/// the rebuilt state to compute <c>ScheduledCloseAt = StartedAt + DurationMinutes</c>.
/// One extra Marten read per fan-out invocation; acceptable.</para>
///
/// <para><b>OQ3 Path α — no defensive pre-filter for withdrawn listings.</b> Per milestone
/// doc §3, the handler does not skip <see cref="PublishedListingsStatus.Withdrawn"/> rows.
/// A withdrawn listing in <c>ListingIds</c> still receives a <see cref="BiddingOpened"/>
/// append; termination happens reactively via the Auction Closing saga's downstream
/// <c>ListingWithdrawn</c> consumption. The lived terminal path is pinned in the M4-S5
/// retrospective. <b>However</b>, listings with a NULL <see cref="PublishedListings"/>
/// row are skipped — the handler cannot construct the per-listing
/// <see cref="BiddingOpened"/> payload without the projection. That's not defensive
/// pre-filtering; it's a data-availability constraint.</para>
///
/// <para><b>OQ2 — Idempotency mechanism (pre-query stream state).</b> The user pinned
/// "DCB-primary first, halt-and-consult on failure" at session open. At implementation
/// time, the milestone doc §6's "DCB-primary" framing turned out to conflate two distinct
/// mechanisms: <see cref="BidConsistencyState"/> DCB (PlaceBidHandler's bid-acceptance
/// tag-aggregate) and stream-existence idempotency (M3 <see cref="ListingPublishedHandler"/>'s
/// <see cref="IEventStore.FetchStreamStateAsync"/> + early-return). The latter is the M3
/// in-repo precedent for "skip if listing stream already opened". The fan-out here mirrors
/// that idiom verbatim — pre-query the listing stream's state; skip if already opened.
/// This is technically the OQ2 "fallback" shape, but the milestone doc's "primary"
/// framing turned out to be load-bearing only on the BidConsistencyState mechanism that
/// doesn't actually apply at stream-opening time. Pinned in the M4-S5 retrospective.</para>
///
/// <para><b>MaxDuration.</b> Workshop 002 platform default is 2x original duration. The
/// fan-out computes <c>MaxDuration = DurationMinutes * 2</c> and stamps it onto every
/// emitted <see cref="BiddingOpened"/>, same convention as the M3 path.</para>
/// </summary>
public static class SessionStartedHandler
{
    public static async Task Handle(
        SessionStarted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // OQ5 Path B: SessionStarted contract carries StartedAt + ListingIds only; load
        // the Session aggregate to read DurationMinutes.
        var sessionAggregate = await session.Events.AggregateStreamAsync<Session>(
            message.SessionId, token: cancellationToken);
        if (sessionAggregate is null)
        {
            // Defensive: SessionStarted should never arrive before SessionCreated is
            // appended to the same stream, but if it does, the handler can't compute
            // ScheduledCloseAt without DurationMinutes. Skip silently.
            return;
        }

        var duration = TimeSpan.FromMinutes(sessionAggregate.DurationMinutes);
        var scheduledCloseAt = message.StartedAt.Add(duration);
        // Workshop 002 platform default: MaxDuration = 2x original duration.
        var maxDuration = TimeSpan.FromMinutes(sessionAggregate.DurationMinutes * 2);
        var openedAt = DateTimeOffset.UtcNow;

        foreach (var listingId in message.ListingIds)
        {
            var published = await session.LoadAsync<PublishedListings>(
                listingId, cancellationToken);
            if (published is null) continue;

            // Idempotency: pre-query the listing stream's state. If a prior delivery of
            // SessionStarted already opened this listing, the stream exists — skip.
            // Mirrors the M3 ListingPublishedHandler idiom (file: ListingPublishedHandler.cs).
            var existing = await session.Events.FetchStreamStateAsync(listingId);
            if (existing is not null) continue;

            var bidding = new BiddingOpened(
                ListingId: listingId,
                SellerId: published.SellerId,
                StartingBid: published.StartingBid,
                ReserveThreshold: published.ReservePrice,
                BuyItNowPrice: published.BuyItNowPrice,
                ScheduledCloseAt: scheduledCloseAt,
                ExtendedBiddingEnabled: published.ExtendedBiddingEnabled,
                ExtendedBiddingTriggerWindow: published.ExtendedBiddingTriggerWindow,
                ExtendedBiddingExtension: published.ExtendedBiddingExtension,
                MaxDuration: maxDuration,
                OpenedAt: openedAt);

            session.Events.StartStream<Listing>(listingId, bidding);
        }

        // AutoApplyTransactions commits after Handle returns. UseFastEventForwarding then
        // forwards each newly-appended BiddingOpened as a Wolverine message to in-process
        // handlers (AuctionClosingSaga.Handle starts the per-listing closing saga) and
        // RabbitMQ external consumers (Listings BC's AuctionStatusHandler at M4-S6).
    }
}
