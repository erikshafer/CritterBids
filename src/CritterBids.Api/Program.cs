using CritterBids.Api;
using CritterBids.Api.Auth;
using CritterBids.Auctions;
using CritterBids.Contracts;
using CritterBids.Listings;
using CritterBids.Obligations;
using CritterBids.Operations;
using CritterBids.Participants;
using CritterBids.Relay;
using CritterBids.Relay.Hubs;
using CritterBids.Selling;
using CritterBids.Settlement;
using JasperFx;
using JasperFx.Events;
using Marten;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.UseWolverine(opts =>
{
    // ── Modular monolith isolation settings ───────────────────────────
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

    // Wolverine's envelope tables live in the "wolverine" schema
    opts.Durability.MessageStorageSchemaName = "wolverine";

    // BC handler/endpoint discovery
    opts.Discovery.IncludeAssembly(typeof(Participant).Assembly);
    opts.Discovery.IncludeAssembly(typeof(SellerListing).Assembly);
    opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly);
    opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);
    opts.Discovery.IncludeAssembly(typeof(SettlementSaga).Assembly);
    opts.Discovery.IncludeAssembly(typeof(PostSaleCoordinationSaga).Assembly);
    opts.Discovery.IncludeAssembly(typeof(SettlementQueueView).Assembly);
    opts.Discovery.IncludeAssembly(typeof(BiddingHub).Assembly);

    // RabbitMQ transport — guarded so fixtures using DisableAllExternalWolverineTransports() are unaffected
    var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq");
    if (!string.IsNullOrEmpty(rabbitMqUri))
    {
        opts.UseRabbitMq(new Uri(rabbitMqUri))
            .AutoProvision(); // Declares queues/exchanges at startup if they don't exist

        opts.PublishMessage<SellerRegistrationCompleted>()
            .ToRabbitQueue("selling-participants-events");
        opts.ListenToRabbitQueue("selling-participants-events")
            .ProcessInline();
        opts.PublishMessage<CritterBids.Contracts.Participants.ParticipantSessionStarted>()
            .ToRabbitQueue("relay-participants-events");
        opts.PublishMessage<SellerRegistrationCompleted>()
            .ToRabbitQueue("relay-participants-events");
        opts.ListenToRabbitQueue("relay-participants-events");

        opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToRabbitQueue("listings-selling-events");
        opts.ListenToRabbitQueue("listings-selling-events");

        opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToRabbitQueue("auctions-selling-events");
        opts.ListenToRabbitQueue("auctions-selling-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToRabbitQueue("relay-selling-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingRevised>()
            .ToRabbitQueue("relay-selling-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingEndedEarly>()
            .ToRabbitQueue("relay-selling-events");
        opts.ListenToRabbitQueue("relay-selling-events");

        // M4-S2: Selling-side WithdrawListing producer landed; route the contract
        // event to the two consuming queues that previously received only the
        // M3-S5b test-fixture synthesis. settlement-selling-events is the third
        // consumer and was already pre-wired in the M5-S3 block below — do not
        // duplicate that route here.
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
            .ToRabbitQueue("listings-selling-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
            .ToRabbitQueue("auctions-selling-events");

        // M3-S6: Auctions BC publishes auction-status events to the Listings BC's
        // CatalogListingView projection. All six events share one queue so the
        // consumer endpoint binds once. BuyItNowPurchased is included per M3-S6
        // OQ3 Path (a) — terminal BIN path, no preceding BiddingClosed.
        opts.PublishMessage<CritterBids.Contracts.Auctions.BiddingOpened>()
            .ToRabbitQueue("listings-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BidPlaced>()
            .ToRabbitQueue("listings-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BiddingClosed>()
            .ToRabbitQueue("listings-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
            .ToRabbitQueue("listings-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingPassed>()
            .ToRabbitQueue("listings-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowPurchased>()
            .ToRabbitQueue("listings-auctions-events");
        // M9-S3: ExtendedBiddingTriggered now consumed by the Listings BC — advances
        // CatalogListingView.ScheduledCloseAt on extension (M8-S7 carry-forward).
        opts.PublishMessage<CritterBids.Contracts.Auctions.ExtendedBiddingTriggered>()
            .ToRabbitQueue("listings-auctions-events");

        // M4-S5: Auctions BC publishes the Session-aggregate events the Listings BC consumes
        // (AuctionsSessionHandler: ListingAttachedToSession + SessionStarted) to the same
        // listings-auctions-events queue. No new queue — the M3-S6 ListenTo below covers the
        // Auctions → Listings traffic for the entire BC.
        // M8-S3c: the SessionCreated route that also pointed here was REMOVED — Listings has no
        // SessionCreated consumer, and under ADR 027's sticky dispatch a delivery at a queue with
        // no sticky match throws NoHandlerForEndpointException (the fan-out previously absorbed
        // the consumer-less copy silently). Truthful topology: no consumer, no route.
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingAttachedToSession>()
            .ToRabbitQueue("listings-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.SessionStarted>()
            .ToRabbitQueue("listings-auctions-events");
        opts.ListenToRabbitQueue("listings-auctions-events");

        // M5-S3: Settlement BC subscribes to Selling-source events for the
        // PendingSettlement projection's lifecycle. ListingPublished seeds the
        // Pending row (workshop 003 §8.1); ListingWithdrawn transitions to
        // Expired (§8.5). ListingPublished now carries three publish routes
        // (listings-selling-events, auctions-selling-events, settlement-selling-
        // events); ListingWithdrawn has its first real publish route here —
        // Selling's own publisher is deferred per M3 §3, but the queue is in
        // place for when it lands.
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToRabbitQueue("settlement-selling-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
            .ToRabbitQueue("settlement-selling-events");
        opts.ListenToRabbitQueue("settlement-selling-events");

        // M5-S3: Settlement BC subscribes to Auctions-source events. ListingPassed
        // transitions PendingSettlement to Expired (§8.4); ListingSold and
        // BuyItNowPurchased trigger the Settlement saga in M5-S4 (queue topology
        // already accommodates them — only the ListingPassed handler fires in S3).
        // The milestone doc §2 lists ListingSold/BuyItNowPurchased only; ListingPassed
        // is a queue-payload extension confirmed at M5-S3 scoping (recorded in the
        // M5-S3 prompt's open questions for milestone-doc amendment in a future
        // doc-cleanup pass).
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
            .ToRabbitQueue("settlement-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowPurchased>()
            .ToRabbitQueue("settlement-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingPassed>()
            .ToRabbitQueue("settlement-auctions-events");
        opts.ListenToRabbitQueue("settlement-auctions-events");

        // M5-S5: Settlement BC subscribes to ParticipantSessionStarted from the
        // Participants BC. Seeds BidderCreditViewHandler's per-bidder credit row
        // at the assigned CreditCeiling per W003 Phase 1 Part 7. Mirrors the
        // settlement-auctions-events / settlement-selling-events route shapes.
        opts.PublishMessage<CritterBids.Contracts.Participants.ParticipantSessionStarted>()
            .ToRabbitQueue("settlement-participants-events");
        opts.ListenToRabbitQueue("settlement-participants-events");

        // M4-S4: Auctions BC subscribes to ParticipantSessionStarted from the
        // Participants BC. Seeds ParticipantCreditCeilingHandler's per-bidder ceiling
        // row consumed at saga-start by StartProxyBidManagerSagaHandler to populate
        // ProxyBidManagerSaga.BidderCreditCeiling. Second lived application of the
        // M4-D4 duplicate-projection pattern (first: Settlement.BidderCreditView at
        // M5-S5 above). Separate queue per BC preserves consumer isolation.
        opts.PublishMessage<CritterBids.Contracts.Participants.ParticipantSessionStarted>()
            .ToRabbitQueue("auctions-participants-events");
        opts.ListenToRabbitQueue("auctions-participants-events");

        // M5-S6: Settlement → Listings publish route for SettlementCompleted. The
        // Listings BC's SettlementStatusHandler transitions CatalogListingView.Status
        // from "Sold" to "Settled" and stamps SettledAt from the event's CompletedAt.
        // Second lived application of the M3-D2 Path A cross-BC read-model extension
        // pattern (ADR-014). Mirrors the listings-auctions-events route shape from M3-S6.
        opts.PublishMessage<CritterBids.Contracts.Settlement.SettlementCompleted>()
            .ToRabbitQueue("listings-settlement-events");
        opts.ListenToRabbitQueue("listings-settlement-events");

        // M5-S6: Settlement → Relay publish route for SellerPayoutIssued. Relay BC has
        // not shipped at M5 close; the route is wired structurally with no consumer
        // until Relay lands (post-M5). The publish rule is required for
        // Wolverine.Tracking's tracked.Sent assertions in SellerPayoutIssuedPublishRouteTests
        // per the wolverine-outbox-tracking memory; without it, the message lands in
        // tracked.NoRoutes instead of tracked.Sent.
        opts.PublishMessage<CritterBids.Contracts.Settlement.SellerPayoutIssued>()
            .ToRabbitQueue("relay-settlement-events");

        // M5-S6: Settlement → Operations publish route for PaymentFailed. M7-S2 lands the
        // Operations consumer: the PaymentFailed publish route (wired at M5-S6 for queue-topology
        // completeness) is now joined by the SettlementCompleted / SellerPayoutIssued publish
        // routes and the ListenToRabbitQueue that activates the SettlementQueueHandler. All three
        // Settlement-family events land on the single operations-settlement-events queue, which
        // the Operations BC owns per the modular-monolith consumer-isolation discipline.
        // AutoProvision() declares the queue at startup.
        opts.PublishMessage<CritterBids.Contracts.Settlement.PaymentFailed>()
            .ToRabbitQueue("operations-settlement-events");
        opts.PublishMessage<CritterBids.Contracts.Settlement.SettlementCompleted>()
            .ToRabbitQueue("operations-settlement-events");
        opts.PublishMessage<CritterBids.Contracts.Settlement.SellerPayoutIssued>()
            .ToRabbitQueue("operations-settlement-events");
        opts.ListenToRabbitQueue("operations-settlement-events");

        // M7-S3: Operations BC's lot board + bid-activity feed subscribe to the Auctions and
        // Selling integration-event families (W006 §2/§3). Two new dedicated Operations consumer
        // queues keep the modular-monolith consumer-isolation discipline intact (Operations reads
        // its own queues, not the listings-/settlement-* queues other BCs own).
        //
        // operations-auctions-events — the Auctions-source events the lot board (BiddingOpened /
        // BidPlaced / ListingSold / ListingPassed) and the bid-activity feed (BidPlaced) consume.
        // ListingWithdrawn is a Selling-published contract but rides THIS queue per the milestone
        // §2 queue table (the milestone is authoritative for scope); the handler grouping is still
        // by source BC (LotBoardSellingHandler owns ListingWithdrawn) — queue is transport only.
        // The session events (SessionCreated / SessionStarted / ListingAttachedToSession) that also
        // ride this queue per the milestone are S5 scope — no publish route or handler is added for
        // them here. AutoProvision() declares the queue at startup.
        opts.PublishMessage<CritterBids.Contracts.Auctions.BiddingOpened>()
            .ToRabbitQueue("operations-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BidPlaced>()
            .ToRabbitQueue("operations-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
            .ToRabbitQueue("operations-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingPassed>()
            .ToRabbitQueue("operations-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
            .ToRabbitQueue("operations-auctions-events");
        // M7-S5: the three Auctions session events ride the existing operations-auctions-events
        // queue per the milestone §2 queue table (S3 deliberately left them unrouted — see the
        // S3 comment above). They feed the session activity board (SessionActivityHandler); the
        // queue is transport only. No new auctions listener — the ListenToRabbitQueue below
        // (S3's) already activates the Operations consumers on this queue.
        opts.PublishMessage<CritterBids.Contracts.Auctions.SessionCreated>()
            .ToRabbitQueue("operations-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.SessionStarted>()
            .ToRabbitQueue("operations-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingAttachedToSession>()
            .ToRabbitQueue("operations-auctions-events");
        opts.ListenToRabbitQueue("operations-auctions-events");

        // operations-selling-events — the Selling-source event the lot board consumes to seed each
        // listing row in Draft (ListingPublished). ListingWithdrawn routes to the auctions queue
        // above per the milestone §2 literal, so this queue carries only ListingPublished.
        // AutoProvision() declares the queue at startup.
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToRabbitQueue("operations-selling-events");
        opts.ListenToRabbitQueue("operations-selling-events");

        // M6-S2: Settlement → Obligations publish route for SettlementCompleted. This is the
        // third publish route for SettlementCompleted, alongside listings-settlement-events
        // (M5-S6) and the financial event stream. The Obligations BC's SettlementCompletedHandler
        // starts the PostSaleCoordination saga on consumption. Obligations listens on its own
        // queue per the modular-monolith consumer-isolation discipline. AutoProvision() declares
        // the queue at startup.
        opts.PublishMessage<CritterBids.Contracts.Settlement.SettlementCompleted>()
            .ToRabbitQueue("obligations-settlement-events");
        opts.ListenToRabbitQueue("obligations-settlement-events");

        // M6-S3: Obligations → Relay publish routes for the post-sale coordination integration
        // events. The Relay BC has not shipped at M6-S3; these routes are wired publish-only with
        // no ListenTo — the Relay consumer lands in M6-S5–S7. Publish-only so the post-sale saga's
        // OutgoingMessages-emitted TrackingInfoProvided / ObligationFulfilled have a route (and so
        // Wolverine.Tracking's tracked.Sent assertions resolve once Relay listens). The frozen
        // contracts' docstrings name this route as wired in M6-S3.
        opts.PublishMessage<CritterBids.Contracts.Obligations.TrackingInfoProvided>()
            .ToRabbitQueue("relay-obligations-events");
        opts.PublishMessage<CritterBids.Contracts.Obligations.ObligationFulfilled>()
            .ToRabbitQueue("relay-obligations-events");

        // M6-S4: Obligations → Relay publish routes for the failure-path integration events.
        // DisputeOpened / DisputeResolved were frozen at M6-S1; both are append+emit on the
        // post-sale saga and gained their Relay consumers (ObligationsRelayHandler) at M6-S5–S7.
        // M8-S3c removed the DeadlineEscalated → relay-obligations-events route as consumer-less
        // ("restore the route together with the Relay handler if it ships"); M8-S6b ships that
        // handler — ObligationsRelayHandler now pushes DeadlineEscalated to the ops feed (the
        // escalation-arrival gap behind the dashboard's polling stopgap) — so the route is
        // restored. The S3c double-execution hazard does not return: the Relay handler is sticky
        // to this queue, and the Operations handler is sticky to operations-obligations-events.
        opts.PublishMessage<CritterBids.Contracts.Obligations.DeadlineEscalated>()
            .ToRabbitQueue("relay-obligations-events");
        opts.PublishMessage<CritterBids.Contracts.Obligations.DisputeOpened>()
            .ToRabbitQueue("relay-obligations-events");
        opts.PublishMessage<CritterBids.Contracts.Obligations.DisputeResolved>()
            .ToRabbitQueue("relay-obligations-events");
        opts.ListenToRabbitQueue("relay-obligations-events");

        // M7-S4: Operations BC's obligations view subscribes to the four Obligations integration
        // events (W006 §4) on its own dedicated consumer queue, keeping the modular-monolith
        // consumer-isolation discipline intact (Operations reads its own queue, not the
        // relay-obligations-events queue Relay owns). All four events are Obligations-published on
        // the same source family — there is no cross-queue asymmetry like S3's ListingWithdrawn, so
        // these are straight parallel publish routes alongside the relay-obligations-events routes
        // above (no upstream Obligations BC code change). AutoProvision() declares the queue at
        // startup.
        opts.PublishMessage<CritterBids.Contracts.Obligations.DeadlineEscalated>()
            .ToRabbitQueue("operations-obligations-events");
        opts.PublishMessage<CritterBids.Contracts.Obligations.DisputeOpened>()
            .ToRabbitQueue("operations-obligations-events");
        opts.PublishMessage<CritterBids.Contracts.Obligations.DisputeResolved>()
            .ToRabbitQueue("operations-obligations-events");
        opts.PublishMessage<CritterBids.Contracts.Obligations.ObligationFulfilled>()
            .ToRabbitQueue("operations-obligations-events");
        opts.ListenToRabbitQueue("operations-obligations-events");

        // M7-S5: Operations BC's participant activity board subscribes to the single Participants
        // integration event ParticipantSessionStarted (W006 §5b) on its own new dedicated consumer
        // queue, keeping the modular-monolith consumer-isolation discipline intact (Operations reads
        // its own queue, not the settlement-/auctions-participants-events queues other BCs own).
        // ParticipantSessionStarted already has publish routes to those other queues; this is a
        // parallel route addition with no upstream Participants BC code change. AutoProvision()
        // declares the queue at startup.
        opts.PublishMessage<CritterBids.Contracts.Participants.ParticipantSessionStarted>()
            .ToRabbitQueue("operations-participants-events");
        opts.ListenToRabbitQueue("operations-participants-events");

        // M6-S5: Relay BC's first reactive surface. Relay consumes three already-published events
        // and pushes them to BiddingHub participant groups. These are publish-route additions to
        // existing message types (no Auctions or Settlement BC code changes) plus the inbound
        // ListenTo calls.
        //
        // relay-auctions-events carries the Auctions-source participant feed. BidPlaced / ListingSold
        // already publish to listings-auctions-events (Listings consumer); this adds the Relay route.
        opts.PublishMessage<CritterBids.Contracts.Auctions.BidPlaced>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BiddingOpened>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BidRejected>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ReserveMet>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ExtendedBiddingTriggered>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingPassed>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ProxyBidExhausted>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowPurchased>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowOptionRemoved>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.SessionCreated>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.SessionStarted>()
            .ToRabbitQueue("relay-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingAttachedToSession>()
            .ToRabbitQueue("relay-auctions-events");
        opts.ListenToRabbitQueue("relay-auctions-events");

        // relay-settlement-events is a shared Settlement → Relay queue. It was pre-wired publish-only
        // for SellerPayoutIssued at M5-S6; M6-S5 adds the SettlementCompleted publish route (resolving
        // the milestone §2-vs-S5-row routing ambiguity in favour of the S5 row + exit criteria) and
        // the ListenTo. Relay handles SettlementCompleted this slice; the SellerPayoutIssued push
        // handler lands in M6-S6 — until then SellerPayoutIssued arrives on this queue with no Relay
        // handler (a known, accepted deferral; harmless in the test suite, which disables transports).
        // M8-S6b adds the PaymentFailed route together with its first Relay consumer
        // (SettlementOperationsHandler → ops feed): the settlement queue's staff-attention state
        // must reach the dashboard live, completing the ops-feed coverage of the settlement family.
        opts.PublishMessage<CritterBids.Contracts.Settlement.SettlementCompleted>()
            .ToRabbitQueue("relay-settlement-events");
        opts.PublishMessage<CritterBids.Contracts.Settlement.PaymentFailed>()
            .ToRabbitQueue("relay-settlement-events");
        opts.ListenToRabbitQueue("relay-settlement-events");

        opts.PublishMessage<CritterBids.Contracts.Listings.LotWatchAdded>()
            .ToRabbitQueue("relay-listings-events");
        opts.PublishMessage<CritterBids.Contracts.Listings.LotWatchRemoved>()
            .ToRabbitQueue("relay-listings-events");
        opts.ListenToRabbitQueue("relay-listings-events");

        // M8-S3c (ADR 027): auctions-auctions-events — the Auctions BC's broker self-consumption
        // queue. Under ADR 027 every BC's broker-fed handlers bind sticky to that BC's own queue,
        // and a sticky match at the receiving endpoint suppresses the Separated fan-out entirely —
        // so the Auctions-side consumers of Auctions-family events (the two dispatcher bridges,
        // the closing-saga start handler, and the Flash-session fan-out) lose the fan-out delivery
        // path they used to ride and get their own queue instead. Self-consumption through the
        // broker is consistent with the extraction story: an extracted Auctions service would
        // consume its own events the same way. The event list is the audit-confirmed consumer set:
        //   BiddingOpened            → StartAuctionClosingSagaHandler (saga start, exactly once —
        //                              this binding is what eliminates the Bug #3-class
        //                              DocumentAlreadyExistsException dead letters)
        //   BidPlaced                → ProxyBidDispatchHandler (emits ClosingBidObserved +
        //                              ProxyBidObserved×N; one sticky chain per endpoint, so the
        //                              two former dispatcher methods were consolidated)
        //   ReserveMet / ExtendedBiddingTriggered / BuyItNowPurchased
        //                            → AuctionClosingDispatchHandler
        //   ListingSold / ListingPassed → ProxyBidDispatchHandler
        //   SessionStarted           → SessionStartedHandler (the Flash-session fan-out is
        //                              broker-fed — live-verified at the S3c audit; it never had a
        //                              direct local forwarding path)
        // ListingWithdrawn (a Selling contract) also rides this queue for the dispatcher bridge —
        // the auctions-selling-events copy feeds PublishedListingsHandler, this copy feeds the
        // saga bridges. AutoProvision() declares the queue at startup.
        opts.PublishMessage<CritterBids.Contracts.Auctions.BiddingOpened>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BidPlaced>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ReserveMet>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ExtendedBiddingTriggered>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowPurchased>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.ListingPassed>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Auctions.SessionStarted>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
            .ToRabbitQueue("auctions-auctions-events");
        opts.ListenToRabbitQueue("auctions-auctions-events");

        // M8-S3c (ADR 027): settlement-settlement-events — the Settlement BC's broker
        // self-consumption queue, surfaced by the S3c consumer audit (the ADR named only the
        // Auctions one). Settlement's PendingSettlementHandler consumes the BC's OWN
        // SettlementCompleted / PaymentFailed (workshop 003 §8.6/§8.7) and was fed by the fan-out
        // from the queues other BCs own; with those consumers bound sticky the fan-out never
        // fires, so Settlement gets the same self-consumption queue the same ADR logic gives
        // Auctions. AutoProvision() declares the queue at startup.
        opts.PublishMessage<CritterBids.Contracts.Settlement.SettlementCompleted>()
            .ToRabbitQueue("settlement-settlement-events");
        opts.PublishMessage<CritterBids.Contracts.Settlement.PaymentFailed>()
            .ToRabbitQueue("settlement-settlement-events");
        opts.ListenToRabbitQueue("settlement-settlement-events");
    }

    opts.Policies.AutoApplyTransactions();

    // Local queues persist to Wolverine's envelope store — saga-scheduled CloseAuction
    // messages survive a node restart and are visible via IMessageStore.ScheduledMessages.
    // M3-S5 first-use requirement for the Auction Closing saga.
    opts.Policies.UseDurableLocalQueues();
});

// ── Primary Marten store + all BC modules (ADR 011: All-Marten pivot) ────────
// Null-guarded: test fixtures that do not provision PostgreSQL skip this block entirely.
// Without the guard, BC modules register services (e.g. ISellerRegistrationService)
// that depend on IDocumentStore — DI validation fails if IDocumentStore is absent.
// Test fixtures that need Marten register AddMarten() directly in ConfigureServices,
// which runs after Program.cs and wins for IDocumentStore resolution.
var postgresConnectionString = builder.Configuration.GetConnectionString("postgres");
if (!string.IsNullOrEmpty(postgresConnectionString))
{
    builder.Services.AddMarten(opts =>
    {
        opts.Connection(postgresConnectionString);
        opts.DatabaseSchemaName = "public";
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Events.UseMandatoryStreamTypeDeclaration = true;
        opts.DisableNpgsqlLogging = true;
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true);

    // All BC modules live here — each calls services.ConfigureMarten() to contribute
    // its event types and projections to the primary store above.
    // Adding a new BC = add its AddXyzModule() call here.
    builder.Services.AddParticipantsModule();
    builder.Services.AddSellingModule();
    builder.Services.AddListingsModule();
    builder.Services.AddAuctionsModule();
    builder.Services.AddSettlementModule();
    builder.Services.AddObligationsModule();
    builder.Services.AddOperationsModule();
}

// ── Relay BC (pure-consumer reactive module) ─────────────────────────────────
// Registered UNCONDITIONALLY, outside the PostgreSQL guard above: Relay owns no Marten document,
// and its AddSignalR() services must be present for the unconditional app.MapHub<...>() calls below
// to resolve — including in test hosts that skip the PostgreSQL-guarded module block. See
// RelayModule.AddRelayModule() and ADR 023.
builder.Services.AddRelayModule();

// ── ASP.NET / Wolverine HTTP ──────────────────────────────────────────────────
builder.Services.AddWolverineHttp();

// M8-S6b live-smoke finding: C# enums on the read-model wire (LotBoardStatus,
// SettlementQueueStatus, QueueState, SessionActivityStatus) serialized as STJ-default NUMBERS,
// while the documented wire contract (docs/skills/wolverine-http-frontend-contract §3) and the
// ops SPA's Zod schemas + status switches expect the string names. Invisible until the first
// real row reached a staff board — M8-S6's smoke saw only empty boards, and [] parses the same
// either way. Enum names on the wire restore the documented contract for every HTTP endpoint
// (Wolverine HTTP shares the Minimal-API JsonOptions this configures).
builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ── Staff authentication (ADR-024) ────────────────────────────────────────────
// The StaffToken scheme is the DEFAULT authenticate + challenge scheme — the fix for the
// no-DefaultChallengeScheme runtime trap (a guarded request would otherwise 500, not 401). The
// StaffOnly policy is the single MVP authorization policy. Both replace the former bare
// AddAuthentication()/AddAuthorization() calls; the UseAuthentication()/UseAuthorization() pipeline
// order below is unchanged. The configured staff token is bound from configuration
// (OperationsAuth:StaffToken) — never hard-coded — and validated at startup outside test/dev.
builder.Services.AddStaffTokenAuthentication();
builder.Services.AddStaffAuthorizationPolicy();
StaffAuthenticationExtensions.EnsureStaffTokenConfigured(builder.Configuration, builder.Environment);

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Commit-time optimistic-concurrency conflicts (DCB consistency check, per-stream appends, saga
// revisions) surface AFTER an endpoint method returns and Wolverine HTTP chains do not consume
// failure rules at 6.5.1 — map them to a graceful 409 here (M8-S3a deferred item; see the
// middleware's docstring and ADR 027's session).
app.UseMiddleware<ConcurrencyConflictMiddleware>();

app.MapWolverineEndpoints();

// ── Relay SignalR hubs ───────────────────────────────────────────────────────
// Mapped unconditionally (AddRelayModule registers AddSignalR unconditionally above).
// .DisableAntiforgery() is required on hub routes in ASP.NET Core 10+, or the WebSocket
// negotiation POST fails 400/403. OperationsHub is mapped now (host wiring done once) but its
// push handlers land in M6-S6.
app.MapHub<BiddingHub>("/hub/bidding").DisableAntiforgery();
// OperationsHub is StaffOnly-gated (ADR-024). SignalR's JS/WebSocket clients cannot set the
// X-Staff-Token header on the negotiate request, so the StaffToken handler reads the credential
// from the access_token query string for this path only. Two hardening notes: (1) no HTTP request
// logging middleware is registered in this pipeline, so the access_token is never written to logs;
// the StaffTokenAuthenticationHandler likewise never logs the token value. (2) Production must
// terminate TLS in front of this host (HTTPS-only) so the query-string credential is never sent in
// cleartext — this is host/ingress configuration, not application code.
app.MapHub<OperationsHub>("/hub/operations").DisableAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "CritterBids API"));
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

return await app.RunJasperFxCommands(args);

public partial class Program { }
