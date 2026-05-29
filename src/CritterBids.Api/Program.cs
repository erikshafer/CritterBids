using CritterBids.Auctions;
using CritterBids.Contracts;
using CritterBids.Listings;
using CritterBids.Obligations;
using CritterBids.Participants;
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

        opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToRabbitQueue("listings-selling-events");
        opts.ListenToRabbitQueue("listings-selling-events");

        opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToRabbitQueue("auctions-selling-events");
        opts.ListenToRabbitQueue("auctions-selling-events");

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

        // M4-S5: Auctions BC publishes the Session-aggregate trio to the same
        // listings-auctions-events queue. Listings consumes them at M4-S6 in the new
        // SessionMembershipHandler sibling class. No new queue — the M3-S6 ListenTo
        // below covers the Auctions → Listings traffic for the entire BC.
        opts.PublishMessage<CritterBids.Contracts.Auctions.SessionCreated>()
            .ToRabbitQueue("listings-auctions-events");
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

        // M5-S6: Settlement → Operations publish route for PaymentFailed. Operations BC
        // has not shipped at M5 close; the route is wired structurally for queue-topology
        // completeness per the M5-S5 retro §"What M5-S6 should know" item #1. No
        // ListenToRabbitQueue — the Operations consumer ships post-M5.
        opts.PublishMessage<CritterBids.Contracts.Settlement.PaymentFailed>()
            .ToRabbitQueue("operations-settlement-events");

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
}

// ── ASP.NET / Wolverine HTTP ──────────────────────────────────────────────────
builder.Services.AddWolverineHttp();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapWolverineEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "CritterBids API"));
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

return await app.RunJasperFxCommands(args);

public partial class Program { }
