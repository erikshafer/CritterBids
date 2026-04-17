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

            // Listing aggregate uses live stream aggregation — state is rebuilt from events
            // on each load rather than snapshotted. S4 introduces the Apply() methods that
            // make this projection non-trivial. Registering the projection on an empty
            // aggregate is safe: live aggregation against zero events returns a default-
            // constructed aggregate.
            //
            // No opts.Events.AddEventType<T>() calls in S2 — event type registrations land
            // with their first use (BiddingOpened in S3, bid-and-friends in S4). Registering
            // event types ahead of their Apply() methods causes silent null returns from
            // AggregateStreamAsync<T> (M2 key learning).
            opts.Projections.LiveStreamAggregation<Listing>();
        });

        return services;
    }
}
