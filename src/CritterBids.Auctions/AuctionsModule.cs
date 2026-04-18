using CritterBids.Contracts.Auctions;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Auctions;

public static class AuctionsModule
{
    public static IServiceCollection AddAuctionsModule(this IServiceCollection services)
    {
        // Contribute Auctions BC's document types to the primary IDocumentStore registered
        // in Program.cs (see ADR 009). The connection string is owned by Program.cs;
        // this module only declares what Auctions owns within the shared store.
        services.ConfigureMarten(opts =>
        {
            opts.Schema.For<Listing>().DatabaseSchemaName("auctions");

            // Event types register at first use (M2 key learning — registering ahead of
            // Apply() methods causes silent null returns from AggregateStreamAsync<T>).
            // BiddingOpened is the first produced here by ListingPublishedHandler (S3);
            // the bid-and-friends batch registers in S4, closing-outcome batch in S5.
            opts.Events.AddEventType<BiddingOpened>();

            // Listing aggregate uses live stream aggregation — state is rebuilt from events
            // on each load rather than snapshotted. S4 introduces the Apply() methods that
            // make this projection non-trivial.
            opts.Projections.LiveStreamAggregation<Listing>();
        });

        return services;
    }
}
