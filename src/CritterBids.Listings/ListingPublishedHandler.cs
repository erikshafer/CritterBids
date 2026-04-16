using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Listings;

/// <summary>
/// Wolverine handler that consumes <see cref="ListingPublished"/> from the Selling BC
/// (via RabbitMQ queue "listings-selling-events") and writes a <see cref="CatalogListingView"/>
/// document to the Marten "listings" schema.
///
/// No [MartenStore] attribute — CritterBids uses a single primary IDocumentStore (ADR 009).
/// No SaveChangesAsync() — AutoApplyTransactions() (configured in Program.cs) commits after Handle() returns.
/// No OutgoingMessages or IMessageBus — this handler produces no downstream messages in M2.
/// </summary>
public static class ListingPublishedHandler
{
    public static void Handle(
        ListingPublished message,
        IDocumentSession session)
    {
        session.Store(new CatalogListingView
        {
            Id          = message.ListingId,
            SellerId    = message.SellerId,
            Title       = message.Title,
            Format      = message.Format,
            StartingBid = message.StartingBid,
            BuyItNow    = message.BuyItNow,
            Duration    = message.Duration,
            PublishedAt = message.PublishedAt
        });
    }
}
