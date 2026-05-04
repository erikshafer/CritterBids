using CritterBids.Auctions;
using CritterBids.Contracts;
using CritterBids.Listings;
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
}

// ── ASP.NET / Wolverine HTTP ──────────────────────────────────────────────────
builder.Services.AddWolverineHttp();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapWolverineEndpoints();

return await app.RunJasperFxCommands(args);

public partial class Program { }
