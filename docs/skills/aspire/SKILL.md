---
name: aspire
description: "CritterBids Aspire orchestration: AppHost provisions PostgreSQL and RabbitMQ, launch profiles, dashboard, Docker labels, env propagation, and test boundary. Use when running or wiring local dev infra."
cluster: aspire
tags: [aspire, apphost, local-dev, postgres, rabbitmq, testcontainers]
---

# .NET Aspire — Local Orchestration

> CritterBids local-development orchestration with Aspire AppHost.
> Generic Aspire testing mechanics live in ai-skills `wolverine-testing-with-aspire`; **this skill documents only CritterBids-specific AppHost resources and conventions.**

## When to apply this skill

Use this skill when:

- Running CritterBids locally through Aspire.
- Editing `src/CritterBids.AppHost` resource wiring.
- Debugging missing connection strings from local AppHost launches.
- Explaining why integration tests use Testcontainers instead of Aspire.
- Checking Docker Desktop container grouping for the demo stack.

Do NOT use this skill for: generic Wolverine/Marten integration testing (see `critter-stack-testing-patterns`) or Docker deployment packaging (see `docker`).

## Read upstream first

Generic mechanics are covered upstream. Read this ai-skill (license required; install via `npx skills add`) when testing through Aspire:

1. `wolverine-testing-with-aspire` — generic Aspire-backed testing and Wolverine host patterns.

That covers generic Aspire mechanics. This skill picks up at CritterBids AppHost posture.

## CritterBids AppHost posture

`CritterBids.AppHost` is the only local orchestration entry point:

```powershell
dotnet run --project src\CritterBids.AppHost --launch-profile http
```

Dashboard: `http://localhost:15237`.

For HTTPS profile, trust dev cert once:

```powershell
dotnet dev-certs https --trust
```

Current AppHost provisions:

- PostgreSQL 18 Alpine named `postgres` — shared by all eight Marten BCs (ADR 011).
- RabbitMQ 3 management image named `rabbitmq` — async messaging and visible management UI.
- `CritterBids.Api` project resource named `critterbids-api`.

SQL Server/Polecat is intentionally absent. Older docs mentioning SQL Server were pre-ADR-011 and are stale.

## Resource wiring

```csharp
var builder = DistributedApplication.CreateBuilder(args);

const string dockerProject = "critterbids";

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18-alpine")
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

var rabbitMq = builder.AddRabbitMQ("rabbitmq")
    .WithImageTag("3-management")
    .WithManagementPlugin()
    .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={dockerProject}");

builder.AddProject<Projects.CritterBids_Api>("critterbids-api")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithReference(postgres)
    .WithReference(rabbitMq)
    .WaitFor(postgres)
    .WaitFor(rabbitMq);

builder.Build().Run();
```

`WaitFor()` is load-bearing: API startup waits for infra readiness. `WithEnvironment("ASPNETCORE_ENVIRONMENT", ...)` is also load-bearing because Aspire does not automatically propagate AppHost environment to child projects.

## Connection strings

`WithReference(resource)` injects connection strings into the API host:

| Resource | Key read by API |
|---|---|
| `AddPostgres("postgres")` | `ConnectionStrings:postgres` |
| `AddRabbitMQ("rabbitmq")` | `ConnectionStrings:rabbitmq` |

`Program.cs` guards transport/store setup for non-Aspire hosts:

```csharp
var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq");
if (!string.IsNullOrEmpty(rabbitMqUri))
{
    opts.UseRabbitMq(new Uri(rabbitMqUri)).AutoProvision();
}
```

Do not use `?? throw` on Aspire-provided connection strings in `Program.cs`; test hosts and diagnostic code paths can start without AppHost injection.

## Docker grouping

All infrastructure containers carry:

```text
com.docker.compose.project=critterbids
```

Docker Desktop groups them under **critterbids**. This is cosmetic; runtime behavior does not depend on the label.

## Local loop

1. Start AppHost: `dotnet run --project src\CritterBids.AppHost --launch-profile http`.
2. Open Aspire dashboard: `http://localhost:15237`.
3. Find API URL on `critterbids-api` resource.
4. Inspect PostgreSQL/RabbitMQ health, logs, traces, env vars, and RabbitMQ management UI from dashboard links.
5. Stop AppHost to stop managed resources.

## Integration test boundary

Aspire is not used by `dotnet test`. CritterBids BC tests use Alba + Testcontainers PostgreSQL for deterministic isolation. Fixture overrides go through `ConfigureServices`, not `ConfigureAppConfiguration`, because Program.cs inline guards read configuration before Alba config callbacks can change it.

```csharp
builder.ConfigureServices(services =>
{
    services.AddMarten(opts => opts.Connection(postgresConnectionString))
        .UseLightweightSessions()
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

    services.DisableAllExternalWolverineTransports();
});
```

## Common pitfalls

- **Starting `CritterBids.Api` directly for local dev and expecting infra.** Use AppHost so PostgreSQL/RabbitMQ and connection strings exist.
- **Forgetting `WaitFor()`.** API can start before containers are ready.
- **Forgetting env propagation.** Child API may boot as Production without explicit `ASPNETCORE_ENVIRONMENT`.
- **Adding SQL Server back.** ADR 011 removed Polecat/SQL Server from current CritterBids.
- **Using Aspire in integration tests.** Tests use Testcontainers; AppHost is for local orchestration/demo loop.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-testing-with-aspire` — generic Aspire testing patterns.

**Prerequisites:**

- `adding-bc-module` — how API host consumes module/store wiring.

**Downstream:**

- `critter-stack-testing-patterns` — Testcontainers fixture pattern that replaces Aspire in tests.
- `diagnostics` — CLI commands to run against the API host.

**External:**

- ADR 006 (infrastructure orchestration), ADR 011 (All-Marten Pivot) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Quick Start and Canonical Bootstrap Sequence.
