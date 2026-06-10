using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine;
using Wolverine.Attributes;

namespace CritterBids.Auctions;

/// <summary>
/// Starts the Auction Closing saga when a listing opens for bids. Runs as a separate static
/// class — per the Wolverine-sagas skill the Start pattern lives outside the saga type so
/// Wolverine can distinguish "create + persist" from "load existing and handle".
///
/// Sticky to the BC's own <c>auctions-auctions-events</c> queue (ADR 027, M8-S3c): exactly one
/// BiddingOpened delivery starts exactly one saga. Before the sticky binding the Separated
/// fan-out delivered one copy per consuming queue (3×) and the duplicate starts raced past the
/// LoadAsync guard into DocumentAlreadyExistsException dead letters on every flow — the
/// "Bug #3" noise class, eliminated by this binding.
///
/// Idempotency: if a saga already exists for this ListingId (broker re-delivery of
/// BiddingOpened), the handler returns null so Wolverine skips saga creation — kept as
/// at-least-once redelivery hygiene.
/// </summary>
[StickyHandler("auctions-auctions-events")]
public static class StartAuctionClosingSagaHandler
{
    public static async Task<AuctionClosingSaga?> Handle(
        BiddingOpened message,
        IMessageBus bus,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<AuctionClosingSaga>(message.ListingId, cancellationToken);
        if (existing is not null) return null;

        var saga = new AuctionClosingSaga
        {
            Id = message.ListingId,
            ListingId = message.ListingId,
            ExtendedBiddingEnabled = message.ExtendedBiddingEnabled,
            ScheduledCloseAt = message.ScheduledCloseAt,
            OriginalCloseAt = message.ScheduledCloseAt,
            Status = AuctionClosingStatus.AwaitingBids,
        };

        await bus.ScheduleAsync(
            new CloseAuction(message.ListingId, message.ScheduledCloseAt),
            message.ScheduledCloseAt);

        return saga;
    }
}
