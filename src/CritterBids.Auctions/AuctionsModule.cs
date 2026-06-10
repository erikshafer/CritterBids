using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using JasperFx;
using JasperFx.Core;
using Marten;
using JasperFx.Events;
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

            // Proxy Bid Manager saga document (M4-S3). Composite-key correlation: the
            // Id is the deterministic UUID v5 of (ListingId, BidderId) via
            // AuctionsIdentityHelpers.ProxyBidManagerSagaId. Numeric revisions guard
            // saga writes — the existing AuctionsConcurrencyRetryPolicies (ConcurrencyException
            // entry) retries conflicts uniformly across both saga document types.
            opts.Schema.For<ProxyBidManagerSaga>()
                .DatabaseSchemaName("auctions")
                .Identity(x => x.Id)
                .UseNumericRevisions(true);

            // ParticipantCreditCeiling (M4-S4) — Auctions-side projection of per-participant
            // credit ceilings sourced from ParticipantSessionStarted on the
            // auctions-participants-events queue. Second lived application of the M4-D4
            // duplicate-projection pattern (first: Settlement.BidderCreditView at M5-S5).
            // The Id => BidderId expression-bodied alias is resolved by Marten via the Id
            // property convention; no Identity override required (mirrors BidderCreditView's
            // shape from SettlementModule.cs). No UseNumericRevisions — idempotency lives in
            // the handler's existing-row guard, not optimistic concurrency.
            opts.Schema.For<ParticipantCreditCeiling>().DatabaseSchemaName("auctions");

            // PublishedListings (M4-S5) — Auctions-side cache of Selling's ListingPublished /
            // ListingWithdrawn payload, sourced from the existing auctions-selling-events queue
            // (wired at M3-S3). Third lived application of the M4-D4 duplicate-projection
            // pattern (first: Settlement.BidderCreditView at M5-S5; second:
            // ParticipantCreditCeiling above at M4-S4). Two consumers within Auctions read
            // this projection: AttachListingToSession's handler (Workshop 002 §5.3 reject-
            // not-published check) and SessionStartedHandler (per-listing BiddingOpened
            // payload for the fan-out). Field shape is OQ1 Path A (full BiddingOpened-
            // precursor payload) per the M4-S5 session-open resolution. No AddEventType
            // for ListingPublished / ListingWithdrawn — handler-consumed integration events
            // route by Wolverine independently of Marten event-type registration per M4-S4
            // OQ8. The two events ARE registered above (line 74 ListingWithdrawn for the
            // M3 fixture-synthesis path; ListingPublished is not currently registered
            // because the M3 ListingPublishedHandler does not append to a Marten stream —
            // it starts one).
            opts.Schema.For<PublishedListings>().DatabaseSchemaName("auctions");

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
            opts.Events.AddEventType<CritterBids.Contracts.Selling.ListingWithdrawn>();

            // M4-S5: Session aggregate event types. All three are appended to the Session
            // stream (SessionCreated via StartStream<Session>; ListingAttachedToSession and
            // SessionStarted via [WriteAggregate]). UseFastEventForwarding forwards them as
            // in-process Wolverine messages — SessionStartedHandler consumes SessionStarted
            // locally. The same three events are also published to the listings-auctions-
            // events RabbitMQ queue for the M4-S6 Listings consumer (route added in
            // Program.cs).
            opts.Events.AddEventType<SessionCreated>();
            opts.Events.AddEventType<ListingAttachedToSession>();
            opts.Events.AddEventType<SessionStarted>();

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

            // M4-S5: Session aggregate also live-aggregated. Sealed-record functional Apply
            // shape (returns new instance via `with`); static Create method handles the
            // SessionCreated first event. First in-Auctions [WriteAggregate]-routed
            // aggregate (Listing uses DCB, not [WriteAggregate]); OQ8 names the halt-and-
            // consult discipline if codegen fails on AttachListingToSession / StartSession.
            opts.Projections.LiveStreamAggregation<Session>();
        });

        // DCB concurrency retry policies for the MESSAGE-BUS path. As of JasperFx.Events 2.8.2,
        // DcbConcurrencyException DERIVES FROM JasperFx.ConcurrencyException (verified by
        // reflection at the M8 Bug #2 follow-ups session — the original "siblings, not
        // parent/child" comment described an older package), so the second OnException entry
        // below is redundant-but-harmless and kept for explicitness. These rules do NOT reach
        // Wolverine.HTTP chains (no failure-rule wiring in Wolverine.Http at 6.5.1) — the HTTP
        // path maps commit-time conflicts to 409 via ConcurrencyConflictMiddleware in the Api host.
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

        // M4-S4: ParticipantCreditCeiling projection-lag retry. Mirrors the M5-S4
        // SettlementsConcurrencyRetryPolicies shape (PendingSettlementNotFoundException
        // with the same three-step cooldown ladder). Re-queues RegisterProxyBid when the
        // auctions-participants-events projection is a few milliseconds behind; the
        // triggering command stays in the queue until the projection catches up.
        options.OnException<ParticipantCreditCeilingNotFoundException>()
            .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds(), 500.Milliseconds());
    }
}
