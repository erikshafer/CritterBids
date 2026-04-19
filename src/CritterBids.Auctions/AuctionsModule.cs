using CritterBids.Contracts.Auctions;
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

            // Event types register at first use (M2 key learning — registering ahead of
            // Apply() methods causes silent null returns from AggregateStreamAsync<T>).
            // BiddingOpened is produced by ListingPublishedHandler (S3); the bid-batch below
            // lands with S4. S5 adds BiddingClosed / ListingSold / ListingPassed.
            opts.Events.AddEventType<BiddingOpened>();
            opts.Events.AddEventType<BidPlaced>();
            opts.Events.AddEventType<BidRejected>();
            opts.Events.AddEventType<ReserveMet>();
            opts.Events.AddEventType<ExtendedBiddingTriggered>();
            opts.Events.AddEventType<BuyItNowOptionRemoved>();

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
