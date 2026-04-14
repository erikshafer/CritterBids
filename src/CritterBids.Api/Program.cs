using CritterBids.Contracts;
using CritterBids.Participants;
using CritterBids.Selling;
using JasperFx;
using Wolverine;
using Wolverine.Http;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.UseWolverine(opts =>
{
    // ── Modular monolith isolation settings ───────────────────────────────────
    // Each BC handler for the same message type gets its own dedicated queue with
    // its own transaction and retry policy. Without Separated, multiple BC handlers
    // for the same integration event (e.g. ListingPublished) are merged into one queue,
    // breaking BC isolation.
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

    // Prevents the durable inbox from deduplicating messages fanned out to multiple
    // BC queues that share the same message ID. Without this, only one BC handler
    // fires when the same message ID arrives on multiple queues (fanout dedup bug).
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

    // Routes all named Marten stores' envelope rows to a shared schema.
    // Without this, each named store creates its own duplicate envelope tables.
    opts.Durability.MessageStorageSchemaName = "wolverine";

    // BC handler/endpoint discovery — add each BC assembly so Wolverine HTTP finds
    // [WolverinePost]/[WolverineGet] endpoints defined in BC class libraries.
    opts.Discovery.IncludeAssembly(typeof(Participant).Assembly);

    // RabbitMQ transport — connection string injected by Aspire via WithReference(rabbitMq).
    // Format: amqp://username:password@host:port
    // Guard is conditional: in test hosts the transport is disabled via
    // DisableAllExternalWolverineTransports() before the transport is ever used.
    var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq");
    if (!string.IsNullOrEmpty(rabbitMqUri))
    {
        opts.UseRabbitMq(new Uri(rabbitMqUri));
    }

    // Wrap all Wolverine message handlers in Polecat transactions automatically.
    // Established here at host level; applies to all BCs registered via AddXyzModule().
    opts.Policies.AutoApplyTransactions();

    // Integration event routing — local queues for M1 (no RabbitMQ topology yet).
    // Replace with exchange publish rules when each consuming BC is implemented.
    opts.Publish(x => x.Message<SellerRegistrationCompleted>()
        .ToLocalQueue("participants-integration-events"));
});

// SQL Server — connection string injected by Aspire via WithReference(sqlServer) in production.
// An empty string is acceptable here because the test fixture overrides it via ConfigurePolecat()
// in ConfigureServices (applied after Program.cs runs but before the DI container is finalized).
var sqlServerConnectionString = builder.Configuration.GetConnectionString("sqlserver") ?? string.Empty;

// Participants BC — owns its own AddPolecat().IntegrateWithWolverine() call.
// Each BC module is self-contained; Program.cs only invokes AddXyzModule().
builder.Services.AddParticipantsModule(sqlServerConnectionString);

// Selling BC — first Marten-backed BC. Registers ISellingDocumentStore (named Marten store)
// with schema isolation ("selling" schema), AutoApplyTransactions, and Wolverine outbox.
builder.Services.AddSellingModule(builder.Configuration);

// Wolverine HTTP — registers the endpoint discovery and route generation services
// required by app.MapWolverineEndpoints() below.
builder.Services.AddWolverineHttp();

// Auth services — required for [AllowAnonymous] attribute resolution on Wolverine HTTP endpoints.
// M1 uses [AllowAnonymous] on all endpoints (§3 non-goal: no real auth scheme in M1).
// Real authentication arrives in M6; this wires the middleware pipeline correctly now.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var app = builder.Build();

// Auth middleware must precede MapWolverineEndpoints() for [AllowAnonymous] / [Authorize]
// attributes to be resolved correctly by the ASP.NET Core authorization system.
app.UseAuthentication();
app.UseAuthorization();

app.MapWolverineEndpoints();

// RunJasperFxCommands enables the JasperFx CLI verbs: db-apply, db-assert, db-dump, codegen.
// Falls back to normal app.Run() behavior when no CLI verb is passed.
return await app.RunJasperFxCommands(args);

// Required for WebApplicationFactory<Program> (used by AlbaHost.For<Program> in tests)
// to access the Program type from external test assemblies.
public partial class Program { }
