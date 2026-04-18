using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Auctions;

/// <summary>
/// Wolverine handler that consumes <see cref="ListingPublished"/> from the Selling BC
/// (via RabbitMQ queue "auctions-selling-events") and opens a Listing event stream in the
/// Auctions BC by appending <see cref="BiddingOpened"/> as the first event on the stream.
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
/// </summary>
public static class ListingPublishedHandler
{
    public static async Task Handle(
        ListingPublished message,
        IDocumentSession session)
    {
        var existing = await session.Events.FetchStreamStateAsync(message.ListingId);
        if (existing is not null)
        {
            return;
        }

        // Duration is nullable on ListingPublished (Flash listings carry null). M3 is
        // Timed-listings-only per docs/milestones/M3-auctions-bc.md §3; the Flash path
        // belongs to the M4 Session aggregate. Unwrapping here is safe for every M3
        // production and test flow — not an invented default, but an explicit contract
        // with the Timed-only M3 scope.
        var duration = message.Duration!.Value;

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

        session.Events.StartStream<Listing>(message.ListingId, opened);
    }
}
