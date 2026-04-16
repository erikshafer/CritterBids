using CritterBids.Contracts;
using CritterBids.Listings;
using CritterBids.Participants;
using CritterBids.Selling;
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
    // ── Modular monolith isolation settings ───────────────────────────────────
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

    // Wolverine's envelope tables live in the "wolverine" schema.
    opts.Durability.MessageStorageSchemaName = "wolverine";

    // BC handler/endpoint discovery
    opts.Discovery.IncludeAssembly(typeof(Participant).Assembly);
    opts.Discovery.IncludeAssembly(typeof(SellerListing).Assembly);
    opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly);

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
    }

    opts.Policies.AutoApplyTransactions();
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
    .IntegrateWithWolverine();

    // All BC modules live here — each calls services.ConfigureMarten() to contribute
    // its event types and projections to the primary store above.
    // Adding a new BC = add its AddXyzModule() call here.
    builder.Services.AddParticipantsModule();
    builder.Services.AddSellingModule();
    builder.Services.AddListingsModule();
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
