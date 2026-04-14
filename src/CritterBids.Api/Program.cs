using CritterBids.Contracts;
using CritterBids.Participants;
using Wolverine;
using Wolverine.Http;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.UseWolverine(opts =>
{
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

app.Run();

// Required for WebApplicationFactory<Program> (used by AlbaHost.For<Program> in tests)
// to access the Program type from external test assemblies.
public partial class Program { }
