using CritterBids.Contracts;
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

    // RabbitMQ transport — guarded so fixtures using DisableAllExternalWolverineTransports() are unaffected
    var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq");
    if (!string.IsNullOrEmpty(rabbitMqUri))
    {
        opts.UseRabbitMq(new Uri(rabbitMqUri));

        opts.PublishMessage<SellerRegistrationCompleted>()
            .ToRabbitQueue("selling-participants-events");
        opts.ListenToRabbitQueue("selling-participants-events")
            .ProcessInline();
    }

    opts.Policies.AutoApplyTransactions();
});

// ── Primary Marten store + all Marten BC modules ──────────────────────────────
// Null-guarded: test fixtures that do not provision PostgreSQL skip this block entirely.
// Without the guard, Marten BC modules register services (e.g. ISellerRegistrationService)
// that depend on IDocumentStore — DI validation fails if IDocumentStore is absent.
// Test fixtures that need Marten provide ConnectionStrings:postgres via ConfigureAppConfiguration
// (or register AddMarten directly in ConfigureServices).
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

    // All Marten BC modules live here. Each calls services.ConfigureMarten() to
    // contribute its types to the primary store above, and registers services that
    // require IDocumentStore. Adding a new Marten BC = add its AddXyzModule() here.
    builder.Services.AddSellingModule();
}

// ── Polecat BC modules ────────────────────────────────────────────────────────
// Null-guarded: if SQL Server is absent, Polecat is not registered as a Wolverine
// message store. Having both Marten and Polecat registered as "main" message stores
// causes an InvalidWolverineStorageConfigurationException at startup.
// Test fixtures that need Participants provide ConnectionStrings:sqlserver via ConfigureAppConfiguration.
var sqlServerConnectionString = builder.Configuration.GetConnectionString("sqlserver");
if (!string.IsNullOrEmpty(sqlServerConnectionString))
{
    builder.Services.AddParticipantsModule(sqlServerConnectionString);
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
