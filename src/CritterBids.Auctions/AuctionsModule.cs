using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using JasperFx;
using JasperFx.Core;
using Marten;
using Marten.Events.Dcb;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.ErrorHandling;

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

            // Auction Closing saga document. Numeric revisions provide optimistic concurrency
            // for saga writes — ConcurrencyException on conflict is retried by the existing
            // AuctionsConcurrencyRetryPolicies below. The saga Id is the ListingId (see
            // AuctionClosingSaga.cs for the OQ1 Path A correlation decision).
            opts.Schema.For<AuctionClosingSaga>()
                .DatabaseSchemaName("auctions")
                .Identity(x => x.Id)
                .UseNumericRevisions(true);

            // Event types register at first use (M2 key learning — registering ahead of
            // Apply() methods causes silent null returns from AggregateStreamAsync<T>).
            // BiddingOpened is produced by ListingPublishedHandler (S3); the bid-batch below
            // lands with S4. BuyItNowPurchased lands with S4b as the terminal event of the
            // BIN short-circuit path. S5 adds BiddingClosed / ListingSold / ListingPassed.
            opts.Events.AddEventType<BiddingOpened>();
            opts.Events.AddEventType<BidPlaced>();
            opts.Events.AddEventType<BidRejected>();
            opts.Events.AddEventType<ReserveMet>();
            opts.Events.AddEventType<ExtendedBiddingTriggered>();
            opts.Events.AddEventType<BuyItNowOptionRemoved>();
            opts.Events.AddEventType<BuyItNowPurchased>();

            // ListingWithdrawn is authored as a Selling-BC contract (M3-S5b) but its only
            // M3 producer is the Auctions test fixture (synthetic seed for scenario 3.10).
            // The saga consumes it via [SagaIdentityFrom(nameof(ListingWithdrawn.ListingId))];
            // Marten requires the event type registered for stream replay / forwarding to
            // resolve the typed payload. Outcome events (BiddingClosed / ListingSold /
            // ListingPassed) intentionally NOT registered here — they are bus-only via
            // OutgoingMessages cascading from the saga (M3-S5b OQ5 Path ◦), not appended to
            // any Marten stream.
            opts.Events.AddEventType<ListingWithdrawn>();

            // DCB tag-type registration. ListingStreamId wraps Guid because .NET 10 added
            // Variant/Version public properties to Guid, which trips ValueTypeInfo's
            // "exactly one public gettable property" rule. See ListingStreamId.cs.
            //
            // PlaceBidHandler does not use the [BoundaryModel] auto-append path — the
            // automatic path relies on Marten inferring tags from an event property of
            // the registered tag type, and our contract events expose Guid, not
            // ListingStreamId. The handler instead tags events explicitly via
            // IEvent.AddTag(new ListingStreamId(...)) then calls IDocumentSession.Events.Append.
            opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>();

            // Listing aggregate uses live stream aggregation — state is rebuilt from events
            // on each load rather than snapshotted. S4 introduces the Apply() methods that
            // make this projection non-trivial.
            opts.Projections.LiveStreamAggregation<Listing>();
        });

        // DCB concurrency retry policies. DcbConcurrencyException (Marten.Events.Dcb,
        // subclass of MartenException) and ConcurrencyException (JasperFx) are siblings,
        // not parent/child — each needs its own OnException entry.
        services.AddSingleton<IWolverineExtension, AuctionsConcurrencyRetryPolicies>();

        return services;
    }
}

internal sealed class AuctionsConcurrencyRetryPolicies : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.OnException<ConcurrencyException>()
            .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());

        options.OnException<DcbConcurrencyException>()
            .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
    }
}
