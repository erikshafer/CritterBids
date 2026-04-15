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
//
// ⚠️ DUAL-STORE CONFLICT — see ADR 010 (docs/decisions/010-wolverine-dual-store-resolution.md)
// When both postgres and sqlserver connection strings are present, both AddMarten().IntegrateWithWolverine()
// (below) and AddParticipantsModule() → AddPolecat().IntegrateWithWolverine() (further below) register as
// Wolverine "main" message stores. Wolverine requires exactly one. This causes:
//   InvalidWolverineStorageConfigurationException: There must be exactly one message store tagged as the
//   'main' store. Found multiples: wolverinedb://sqlserver/.../wolverine, wolverinedb://postgresql/.../wolverine
// Pending JasperFx resolution of the Polecat ancillary-store API gap documented in ADR 010.
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
//
// ⚠️ DUAL-STORE CONFLICT — see ADR 010 (docs/decisions/010-wolverine-dual-store-resolution.md)
// AddParticipantsModule() calls AddPolecat().IntegrateWithWolverine(), which unconditionally
// registers Polecat as a Wolverine "main" message store. No API exists in WolverineFx.Polecat 5.30.0
// to configure Polecat as an ancillary store. When sqlserver is also set, the host will throw
// InvalidWolverineStorageConfigurationException at startup. Pending JasperFx input — ADR 010 records
// what was investigated and why neither Option A nor Option B resolves this within current constraints.
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
