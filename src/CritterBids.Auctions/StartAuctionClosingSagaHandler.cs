using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine;

namespace CritterBids.Auctions;

/// <summary>
/// Starts the Auction Closing saga when a listing opens for bids. Runs as a separate static
/// class — per the Wolverine-sagas skill the Start pattern lives outside the saga type so
/// Wolverine can distinguish "create + persist" from "load existing and handle".
///
/// Idempotency: if a saga already exists for this ListingId (re-delivery of BiddingOpened),
/// the handler returns null so Wolverine skips saga creation. Note that in the post-DCB
/// pipeline, BiddingOpened fires once per listing from the Listings projection, so this
/// is a defensive guard for redelivery, not a frequent path.
/// </summary>
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
