# .NET Aspire — Local Orchestration

Patterns and conventions for using .NET Aspire in CritterBids for local development orchestration.
Authored retrospectively from M1 sessions 1–4 experience.

> **Status: Complete — authored from M1 (sessions S1–S4) experience (April 2026).**

---

## What Aspire Does in CritterBids

`CritterBids.AppHost` is the single local-dev orchestration entry point. Running it:

1. Provisions all infrastructure containers (PostgreSQL, SQL Server, RabbitMQ)
2. Injects connection strings into the API host via Aspire's resource reference system
3. Launches `CritterBids.Api` with all connection strings pre-wired
4. Serves the Aspire dashboard at `http://localhost:15237`

```bash
dotnet run --project src/CritterBids.AppHost --launch-profile http
```

For HTTPS, first trust the dev cert once: `dotnet dev-certs https --trust`.

---

## AppHost Project Structure

`CritterBids.AppHost` is a .NET 10 project using the Aspire AppHost SDK:

```xml
<Project Sdk="Aspire.AppHost.Sdk/9.3.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CritterBids.Api\CritterBids.Api.csproj" IsAspireProjectResource="true" />
  </ItemGroup>
</Project>
```

The `IsAspireProjectResource="true"` attribute marks the API project as an Aspire-managed resource —
this enables connection string injection and health monitoring from the dashboard.

---

## Provisioning Infrastructure Resources

`CritterBids.AppHost/Program.cs` provisions all three infrastructure resources and wires them to the API:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL — for Marten BCs (Selling, Auctions, Listings, etc.; not used in M1)
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine");

// SQL Server — for Polecat BCs (Participants, Settlement, Operations)
var sqlServer = builder.AddSqlServer("sqlserver");

// RabbitMQ — for Wolverine async messaging
var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithImageTag("3-management"); // management image includes RabbitMQ Management UI

// API host — receives all connection strings and waits for containers to be ready
builder.AddProject<Projects.CritterBids_Api>("critterbids-api")
    .WithReference(postgres)
    .WithReference(sqlServer)
    .WithReference(rabbitMq)
    .WaitFor(postgres)
    .WaitFor(sqlServer)
    .WaitFor(rabbitMq);

builder.Build().Run();
```

`WaitFor()` is load-bearing: without it, the API starts before the databases are ready and fails on
connection. Aspire handles readiness checking for each resource type automatically.

---

## Docker Grouping (Optional)

All infrastructure containers can be grouped under a Docker label to appear together in Docker Desktop:

```csharp
const string dockerProject = "critterbids";

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");
```

Three containers labelled `com.docker.compose.project=critterbids` appear grouped under **critterbids**
in Docker Desktop's Containers view. This is cosmetic — it has no effect on runtime behavior.

---

## Connection String Injection

`WithReference(resource)` causes Aspire to inject the resource's connection string into the referenced
project as an `IConfiguration` entry. The key format follows Aspire's convention:

| Resource | Configuration key |
|---|---|
| `AddSqlServer("sqlserver")` | `ConnectionStrings:sqlserver` |
| `AddPostgres("postgres")` | `ConnectionStrings:postgres` |
| `AddRabbitMQ("rabbitmq")` | `ConnectionStrings:rabbitmq` |

These keys match `builder.Configuration.GetConnectionString("sqlserver")` etc. in `Program.cs`.

### Consuming Connection Strings in `Program.cs`

```csharp
// SQL Server — injected by Aspire; empty string at test host startup (no Aspire in tests)
var sqlServerConnectionString = builder.Configuration.GetConnectionString("sqlserver") ?? string.Empty;
builder.Services.AddParticipantsModule(sqlServerConnectionString);

// RabbitMQ — guard required because connection string is empty at test host startup
var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq");
if (!string.IsNullOrEmpty(rabbitMqUri))
{
    opts.UseRabbitMq(new Uri(rabbitMqUri));
}
```

**Critical:** Never use `?? throw` on connection strings read at startup. At test host startup,
`IConfiguration` has not been populated by Aspire — the test fixture overrides connection strings
via `ConfigureServices` (which applies after `Program.cs` runs but before the DI container is
finalized). A `?? throw` fires before the override can apply and kills the test host.

### `WithReference()` vs Named Resources

`WithReference(resource)` uses the resource name (e.g., `"sqlserver"`) as the `ConnectionStrings` key.
When multiple BCs share a SQL Server instance (Settlement + Operations + Participants), they all receive
the same connection string — schema isolation inside SQL Server is then managed by each BC's
`opts.DatabaseSchemaName` setting. Named Polecat stores (`AddPolecatStore<T>()`) are the future
mitigation when store-per-BC isolation is required (S4-F2, deferred to M2+).

---

## Local Dev Workflow

1. `dotnet run --project src/CritterBids.AppHost --launch-profile http`
2. Aspire dashboard: `http://localhost:15237`
3. API base URL: typically `http://localhost:5XXX` (shown in dashboard under "critterbids-api" resource)
4. Three infrastructure containers appear in Docker Desktop under **critterbids**

The dashboard shows:
- Resource health (running / starting / unhealthy)
- Structured logs from all services
- Traces (OpenTelemetry)
- Configuration / environment variables injected into each resource

---

## Integration Testing Note

**Aspire is not involved in integration tests.** `CritterBids.Participants.Tests` (and all BC test
projects) use **Testcontainers** to spin up isolated Docker containers per test run. The AppHost is
never launched during `dotnet test`.

Test fixtures override connection strings via `builder.ConfigureServices` in `AlbaHost.For<Program>`.
This works because `ConfigureServices` callbacks apply before the DI container is finalized — later
than `Program.cs` runs but in time to override what the BC modules registered:

```csharp
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        // Overrides the connection string AddParticipantsModule set
        services.ConfigurePolecat(opts => { opts.ConnectionString = testContainerConnectionString; });
        services.DisableAllExternalWolverineTransports();
    });
});
```

Do not use `ConfigureAppConfiguration` for connection string overrides — it injects into `IConfiguration`
after `WebApplicationBuilder.Configuration` is already built, so `Program.cs` reads the original
(empty) value rather than the test override. `ConfigureServices` is the correct hook.

---

## Gotchas Discovered in M1

### Schema Creation Timing

`AddPolecat()` with `AutoCreate.CreateOrUpdate` creates schemas **lazily** — on first ORM operation.
Test fixtures calling `CleanAllPolecatDataAsync()` in `InitializeAsync()` before any ORM operation
fail with `Invalid object name 'participants.pc_events'` because the schema hasn't been created yet.

**Resolution:** Call `.ApplyAllDatabaseChangesOnStartup()` on the `PolecatConfigurationExpression`
builder (not inside the `StoreOptions` lambda). This forces eager schema creation at host startup,
before any test cleanup call can run. This is why the BC module (not the test fixture) owns
`AddPolecat().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()`.

### Connection String Key Conventions

Aspire's key for SQL Server is `ConnectionStrings:sqlserver` where `"sqlserver"` matches the resource
name passed to `AddSqlServer("sqlserver")`. The Polecat connection string is read as:

```csharp
builder.Configuration.GetConnectionString("sqlserver")
```

If you rename the Aspire resource, you must update the key everywhere it is consumed in `Program.cs`.

### Conditional RabbitMQ Registration

The RabbitMQ transport must be registered conditionally — the connection string is empty at test host
startup because Aspire has not injected it:

```csharp
var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq");
if (!string.IsNullOrEmpty(rabbitMqUri))
{
    opts.UseRabbitMq(new Uri(rabbitMqUri));
}
```

`DisableAllExternalWolverineTransports()` in the fixture disables the transport even if it were
registered, so this guard is defense-in-depth.

---

## Aspire MCP Discovery Note

No Aspire MCP server candidates were integrated or observed in M1. The `mcp__next-devtools__*` MCP
tools are not related to Aspire. Aspire MCP tooling discovery is deferred — if a future Aspire MCP
server becomes available, it should be evaluated for:
- Dashboard interaction (querying resource health, structured logs)
- Connection string discovery without running the AppHost locally
- Resource provisioning scripting

This is a candidate for future tooling in M2+ when additional BCs require Aspire wiring changes.

---

## References

- [.NET Aspire documentation](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Aspire AppHost resource model](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/app-host-overview)
- [Aspire + SQL Server](https://learn.microsoft.com/en-us/dotnet/aspire/database/sql-server-component)
- [Aspire + PostgreSQL](https://learn.microsoft.com/en-us/dotnet/aspire/database/postgresql-component)
- [Aspire + RabbitMQ](https://learn.microsoft.com/en-us/dotnet/aspire/messaging/rabbitmq-client-component)
- `docs/decisions/006-infrastructure-orchestration.md` — ADR for choosing Aspire over Docker Compose
- `docs/skills/polecat-event-sourcing.md` — Polecat configuration and `ApplyAllDatabaseChangesOnStartup()`
- `docs/skills/critter-stack-testing-patterns.md` — Testcontainers fixture pattern (replaces Aspire in tests)
