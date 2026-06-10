using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Auctions;

/// <summary>
/// Wolverine handler that consumes <see cref="ListingPublished"/> from the Selling BC
/// (via RabbitMQ queue "auctions-selling-events") and opens a Listing event stream in the
/// Auctions BC by appending <see cref="BiddingOpened"/> as the first event on the stream
/// — for Timed-format listings only.
///
/// <para><b>Two-path topology after M4-S5.</b> Listing-open-for-bidding now has two
/// production paths:</para>
/// <list type="bullet">
///   <item><b>Timed path</b> (this handler) — <c>ListingPublished</c> with
///     <c>Duration</c> non-null opens the listing's stream immediately on Selling-side
///     publish. Inherits the M3 single-listing posture.</item>
///   <item><b>Flash path</b> (<see cref="SessionStartedHandler"/> at M4-S5) — Flash
///     listings (<c>Duration == null</c>) do NOT open here; they are skipped via the
///     guard below. The Session aggregate's <c>SessionStarted</c> event triggers
///     <see cref="SessionStartedHandler"/>, which fans out one
///     <see cref="BiddingOpened"/> per attached listing — the M4 milestone doc §6
///     Option B fan-out. Flash listings' Auctions-side stream is empty until session
///     start.</item>
/// </list>
///
/// Stream ID is <see cref="ListingPublished.ListingId"/> — the upstream UUID v7 flows
/// through from Selling as the cross-BC listing identity (ADR 007 stream-ID guidance).
///
/// At-least-once delivery is the transport contract (RabbitMQ + Wolverine's transactional
/// inbox is not configured for exactly-once). Idempotency is absorbed in the handler via a
/// stream-state check: a second delivery of the same ListingPublished finds the stream
/// already exists and no-ops silently, producing no duplicate BiddingOpened and no
/// exception. The canonical concurrency-throw-and-retry pattern in integration-messaging.md
/// does not satisfy the S3 acceptance criterion "no handler-level exception propagates";
/// the stream-state fallback named in the S3 prompt's Open Questions is what is applied here.
///
/// Handler shape follows the Listings BC ListingPublishedHandler precedent: session work is
/// direct (no MartenOps / IStartStream return), relying on Program.cs's
/// AutoApplyTransactions() policy to commit the session through the Wolverine pipeline in
/// production. The two S3 integration tests invoke this handler directly and call
/// SaveChangesAsync explicitly — same pattern as Listings' CatalogListingViewTests.
///
/// <para><b>Single discovered ListingPublished handler for the BC (ADR 027, M8-S3c).</b> Sticky
/// dispatch executes at most one handler class per (message type, endpoint), so the BC's two
/// ListingPublished jobs — the M4-S5 <see cref="PublishedListings"/> cache upsert and this
/// timed-open path — now run from this one discovered handler, sticky to
/// <c>auctions-selling-events</c>. The cache upsert runs FIRST and unconditionally (Flash
/// listings need their <see cref="PublishedListings"/> row; only the stream-open is
/// Duration-gated). The upsert logic stays in
/// <see cref="PublishedListingsHandler.UpsertPublishedListingAsync"/> — same class, no longer
/// separately discovered for this event.</para>
/// </summary>
[StickyHandler("auctions-selling-events")]
public static class ListingPublishedHandler
{
    public static async Task Handle(
        ListingPublished message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // ADR 027 consolidation: maintain the PublishedListings cache row for every published
        // listing (Flash included) before the Duration gate below.
        await PublishedListingsHandler.UpsertPublishedListingAsync(message, session, cancellationToken);

        // Flash-format guard (M4-S5 item 9). Flash listings (Duration == null) open
        // for bidding via SessionStartedHandler's fan-out, not this per-listing path.
        // Skipping here is additive and harmless when invoked directly with a non-null
        // Duration — the existing BiddingOpenedConsumerTests pass both their listings
        // with Duration set, so this guard is inert for those tests.
        if (message.Duration is null)
        {
            return;
        }

        var duration = message.Duration.Value;

        var opened = new BiddingOpened(
            ListingId: message.ListingId,
            SellerId: message.SellerId,
            StartingBid: message.StartingBid,
            ReserveThreshold: message.ReservePrice,
            BuyItNowPrice: message.BuyItNow,
            ScheduledCloseAt: message.PublishedAt.Add(duration),
            ExtendedBiddingEnabled: message.ExtendedBiddingEnabled,
            ExtendedBiddingTriggerWindow: message.ExtendedBiddingTriggerWindow,
            ExtendedBiddingExtension: message.ExtendedBiddingExtension,
            MaxDuration: duration,
            OpenedAt: DateTimeOffset.UtcNow);

        // Append a TAGGED BiddingOpened via the DCB boundary (idempotent). Replaces the broken
        // `FetchStreamStateAsync` guard + untagged `StartStream<Listing>` — in the shared store the
        // listingId stream already exists (Selling's SellerListing), so the old guard skipped
        // forever and the event was never tag-visible to the bid DCB. See OpenListingForBidding.
        await OpenListingForBidding.AppendIfNotOpenAsync(session, opened);
    }
}
