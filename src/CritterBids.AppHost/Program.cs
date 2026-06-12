using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// All infrastructure containers are labelled with com.docker.compose.project=critterbids
// so Docker Desktop groups them under the "critterbids" stack in its Containers view.
const string dockerProject = "critterbids";

// PostgreSQL 18 — shared by all eight BCs (ADR 011: All-Marten pivot).
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

// SQL Server removed — Participants BC migrated to Marten (ADR 011 Participants migration session).
// WolverineFx.Http.Polecat + Polecat packages archived; see docs/decisions/011-all-marten-pivot.md.

// RabbitMQ 3 management image — includes the management UI, useful for the Aspire demo
// dashboard and local debugging. WithManagementPlugin publishes the 15672 UI endpoint to
// the Aspire dashboard so local smoke-tests and workshop demos can inspect queues without
// docker exec (M3-S7 operational-smoke gap; closed at M4-S1).
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithImageTag("3-management")
    .WithManagementPlugin()
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

// Aspire does not auto-propagate ASPNETCORE_ENVIRONMENT to child projects — without
// this, the API boots as "Production" even when the AppHost is in Development, which
// defeats any `app.Environment.IsDevelopment()` guards (e.g. the OpenAPI/SwaggerUI map).
var api = builder.AddProject<Projects.CritterBids_Api>("critterbids-api")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(postgres)
    .WithReference(rabbitMq)
    .WaitFor(postgres)
    .WaitFor(rabbitMq);

// Bidder and ops SPAs — launched as part of the Aspire orchestration so a single
// dotnet run starts the full stack. CRITTERBIDS_API_URL is injected from the API
// endpoint so the Vite dev-server proxy always targets the right host/port.
// Both vite configs fall back to http://localhost:5180 when run outside Aspire.
builder.AddViteApp("bidder", "../../client/bidder")
    .WithHttpEndpoint(port: 5173, name: "http")
    .WithEnvironment("CRITTERBIDS_API_URL", api.GetEndpoint("http"))
    .WaitFor(api);

builder.AddViteApp("ops", "../../client/ops")
    .WithHttpEndpoint(port: 5174, name: "http")
    .WithEnvironment("CRITTERBIDS_API_URL", api.GetEndpoint("http"))
    .WaitFor(api);

builder.Build().Run();
