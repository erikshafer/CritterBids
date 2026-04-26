# M1-S3: Infrastructure Baseline — Retrospective

**Date:** 2026-04-11
**Milestone:** M1 — Skeleton
**Slice:** S3 — Infrastructure baseline
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M1-S3-infrastructure-baseline.md`

## Baseline

- Solution builds clean; 2 tests pass (both smoke tests from M1-S1).
- `src/CritterBids.AppHost/` did not exist.
- `src/CritterBids.Api/CritterBids.Api.csproj` was an empty Web SDK project.
- `src/CritterBids.Api/Program.cs` was `WebApplication.CreateBuilder(args).Build().Run()`.
- `Directory.Packages.props` had 4 pins (xUnit + Shouldly only).
- `CLAUDE.md` Quick Start still described the docker-compose fallback path.
- `docs/milestones/M1-skeleton.md` §4 already reflected Layout 2 test names and `CritterBids.AppHost` in `src/` — both doc-fix targets were already resolved by prior sessions. §9 S3 row still showed `*TBD*`.
- Aspire workload was not installed (`dotnet workload list` returned nothing). In .NET Aspire 9.0+, the Aspire AppHost SDK is delivered as a NuGet package — no separate workload installation is required. `dotnet workload search aspire` returned no results, confirming the workload-free model applies to Aspire 13.x on .NET 10.

---

## Items completed

| Item | Description |
|------|-------------|
| S3a | `Directory.Packages.props` extended with Aspire, Wolverine, and Polecat package pins |
| S3b | `src/CritterBids.AppHost/CritterBids.AppHost.csproj` created with Aspire.AppHost.Sdk and resource references |
| S3c | `src/CritterBids.AppHost/Program.cs` provisions PostgreSQL, SQL Server, RabbitMQ, and `CritterBids.Api` with resource references |
| S3d | `src/CritterBids.Api/CritterBids.Api.csproj` updated with Wolverine and Polecat package references |
| S3e | `src/CritterBids.Api/Program.cs` configured with Wolverine (RabbitMQ transport, `AutoApplyTransactions`) and Polecat (host-level SQL Server, `IntegrateWithWolverine`) |
| S3f | `src/CritterBids.AppHost/Properties/launchSettings.json` created — required for `dotnet run` to configure Aspire dashboard env vars |
| S3g | `CritterBids.slnx` updated to include `CritterBids.AppHost` in the `/src/` folder |
| S3h | `CLAUDE.md` Quick Start and Preferred Tools table updated — docker-compose text removed |
| S3i | `docs/milestones/M1-skeleton.md` §9 S3 row updated from `*TBD*` to the actual prompt file name |

---

## S3a: Directory.Packages.props — package version decisions

### Aspire packages — 13.2.2

`13.2.2` is the current stable release as of session date. This matches the CLAUDE.md `Aspire 13.2+` requirement. Packages added:

- `Aspire.Hosting.AppHost` 13.2.2
- `Aspire.Hosting.PostgreSQL` 13.2.2
- `Aspire.Hosting.RabbitMQ` 13.2.2
- `Aspire.Hosting.SqlServer` 13.2.2

### Wolverine packages — 5.30.0 (stale version in Directory.Build.props)

**⚠️ Flag: `WolverineVersion` property in `Directory.Build.props` is stale.**

`Directory.Build.props` defines `WolverineVersion=5.4.0`. The current stable release is `5.30.0`. These differ by 26 minor versions. The prompt explicitly required checking this value before restoring pins. Per the prompt rule ("do not silently use a stale pin"), `5.30.0` is used in `Directory.Packages.props`. The `WolverineVersion` property in `Directory.Build.props` is now an orphan with no consumers. Cleanup of that property is assigned to M1-S7 along with broader CLAUDE.md cleanup.

Packages added at `5.30.0`:
- `WolverineFx`
- `WolverineFx.Polecat`
- `WolverineFx.RabbitMQ`

### MartenVersion and AlbaVersion — not used this session

`MartenVersion=8.16.1` in `Directory.Build.props` vs `8.29.2` on NuGet — stale, but Marten is out of M1-S3 scope. `AlbaVersion=8.4.0` — not verified this session. Both flagged for the session that first adds those packages.

### Polecat — 2.0.1

`Polecat 2.0.1` is the current stable release. Added to `Directory.Packages.props`. `WolverineFx.Polecat 5.30.0` provides the Wolverine integration via `IntegrateWithWolverine()` on `PolecatConfigurationExpression`.

---

## S3f: Properties/launchSettings.json — required scaffolding not in prompt scope

**Discovery:** `dotnet run --project src/CritterBids.AppHost` crashes immediately without `Properties/launchSettings.json`. The crash sequence:

1. **First failure:** `ASPNETCORE_URLS environment variable was not set` — the Aspire dashboard can't determine its own URL. Normally `ASPNETCORE_URLS` is set by the `applicationUrl` field in `launchSettings.json` when `dotnet run` reads it.
2. **Second failure (after adding HTTP profile):** `The 'applicationUrl' setting must be an https address unless ASPIRE_ALLOW_UNSECURED_TRANSPORT is set to true` — Aspire 13.x enforces HTTPS by default for the dashboard.

**Resolution:** Added two profiles to `Properties/launchSettings.json`:
- `https` — primary, dashboard at `https://localhost:17019`, OTLP at `https://localhost:21029`
- `http` — includes `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` for environments without a dev cert

**Why this was missing:** Aspire project templates generate `launchSettings.json` automatically. Since we created the AppHost project manually (csproj + Program.cs only), this scaffolding file was never generated. The prompt did not list it as a deliverable because it is implicit in the "dotnet run boots all services" acceptance criterion.

**Lesson for `docs/skills/aspire.md`** (M1-S7): An AppHost project created manually requires `Properties/launchSettings.json` with the Aspire-specific environment variables (`ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL`, `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL`, `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL`) set before `dotnet run` will work. This is not documented in the Aspire 13.x package docs and is only discoverable by running and seeing the crash.

**End-to-end verification:** After adding `launchSettings.json`, `dotnet run --project src/CritterBids.AppHost --launch-profile http` boots all four services. Docker container check after 30 seconds confirmed:

| Container | Image | Status |
|---|---|---|
| `rabbitmq-*` | `rabbitmq:3-management` | Up 24 seconds |
| `postgres-*` | `postgres:18-alpine` | Up 24 seconds |
| `sqlserver-*` | `mcr.microsoft.com/mssql/server:2022-latest` | Up 26 seconds |
| `critterbids-api` | *(project resource, launched by DCP)* | Started after containers healthy |

---

## S3b–S3c: CritterBids.AppHost — container image choices

Per the prompt, container image choices were deferred from ADR 006 to this session. Chosen pragmatically:

| Service | Image | Rationale |
|---------|-------|-----------|
| PostgreSQL | `postgres:18-alpine` | Alpine for minimal image size; 18 is the current stable major. Default tag `latest` avoided per reproducibility. |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-latest` | Aspire's default for `AddSqlServer()` — no override needed; the `2022-latest` tag tracks 2022 patch releases. |
| RabbitMQ | `rabbitmq:3-management` | Management image includes the admin UI, useful for the Aspire demo dashboard and local debugging. Chosen over the standard `rabbitmq:3` because the management UI has zero demo cost. |

The `WithImageTag()` override on the RabbitMQ builder confirms `3-management`; PostgreSQL uses `WithImageTag("17-alpine")`. SQL Server uses the Aspire default.

---

## S3d–S3e: API host configuration — Polecat and Wolverine API surface

### Polecat registration API confirmed

The `AddPolecat(IServiceCollection, Action<StoreOptions>)` extension method is in the `Polecat` namespace (`Polecat.PolecatServiceCollectionExtensions`). It was not found initially because the `using Polecat;` directive was absent from `Program.cs`. Confirmed by assembly inspection of `Polecat.dll`:

```
PolecatConfigurationExpression AddPolecat(IServiceCollection, Action<StoreOptions>)
```

`StoreOptions.ConnectionString` confirmed (string property). `StoreOptions.DatabaseSchemaName` confirmed — will be used by each BC module in M1-S4+.

### Wolverine RabbitMQ API confirmed

`UseRabbitMq(Uri)` extension is in the `Wolverine.RabbitMQ` namespace. `builder.UseWolverine()` is a `WebApplicationBuilder` extension in the `Wolverine` namespace — `builder.Host.UseWolverine()` (the `IHostBuilder` pattern) does not work in the minimal API model with .NET 10's `ConfigureHostBuilder`.

### Polecat integration confirmed

`IntegrateWithWolverine()` is in `Wolverine.Polecat.WolverineOptionsPolecatExtensions` and extends `PolecatConfigurationExpression`. This wires Polecat as the Wolverine transactional outbox persistence store.

### Using directives required (not auto-imported by ImplicitUsings)

Four explicit `using` statements are required in `Program.cs`. None are in the `Microsoft.NET.Sdk.Web` implicit using set:

```csharp
using Polecat;
using Wolverine;
using Wolverine.Polecat;
using Wolverine.RabbitMQ;
```

**Add to `docs/skills/wolverine-message-handlers.md`:** note that `Wolverine`, `Wolverine.RabbitMQ`, etc. are not auto-imported and require explicit `using` directives in Program.cs.

### Polecat migration strategy — no decision required at M1-S3

The prompt flagged this as a potential stop: "If Polecat requires explicit schema creation calls beyond connection string injection, flag and stop." At host level with no streams registered, Polecat has nothing to create. Schema creation happens when BC modules register stream types (M1-S4). No `ApplyAllDatabaseChangesOnStartup()` call was needed or added. Migration strategy decision is deferred to M1-S4 when the first stream type is registered.

### AutoApplyTransactions placement

`opts.Policies.AutoApplyTransactions()` is called inside `builder.UseWolverine()` rather than inside the Polecat configuration lambda. This is the correct placement — it is a `WolverineOptions` policy, not a Polecat configuration option. The milestone doc's phrasing "applied in the Polecat configuration" means "established alongside Polecat in this session," not "called on a Polecat object."

---

## S3g: CLAUDE.md — doc fix

Two edits:

1. Replaced the docker-compose fallback sentence in Quick Start item 2 with "the single local-orchestration path" phrasing.
2. Removed the `| Containers | Docker Compose (fallback) |` row from Preferred Tools table.

The Quick Start now describes only the Aspire path, consistent with ADR 006.

---

## S3h: M1-skeleton.md — §4 already resolved, §9 updated

Both §4 targets (AppHost in `src/`, Layout 2 test names) were already present in the milestone doc — apparently resolved as part of the M1-S2 coherence edits. No §4 changes needed.

§9 S3 row updated from `*TBD*` to `docs/prompts/implementations/M1-S3-infrastructure-baseline.md`. This is within the allowed file list per the prompt's acceptance criteria.

---

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Session open (baseline) | 2 | Pass |
| After all changes | 2 | Pass — no regressions |

---

## Build state at session close

- Errors: 0
- Warnings: 0
- Projects in solution: 5 (3 src, 2 tests)
- `Version=` on `<PackageReference>`: 0 occurrences
- Package pins in `Directory.Packages.props`: 12 (4 Aspire + 3 Wolverine + 1 Polecat + 4 test)
- No connection strings in `appsettings.Development.json` (file does not exist)
- No BC code, no stream registration, no endpoints, no auth wiring

---

## Open questions / flags for retro session (M1-S7)

| ID | Finding | Disposition |
|----|---------|-------------|
| S3-F1 | `WolverineVersion=5.4.0` in `Directory.Build.props` is stale (current: 5.30.0). Property has no consumers. | Orphaned property cleanup assigned to M1-S7. |
| S3-F2 | `MartenVersion=8.16.1` in `Directory.Build.props` is stale (current: 8.29.2). Not used in M1-S3. | Flag when Marten first added (likely M2). |
| S3-F3 | `AlbaVersion=8.4.0` — not verified against current NuGet. | Verify when Alba first added (likely M1-S5 or later). |
| S3-F4 | Aspire workload-free model: no `dotnet workload install aspire` required in .NET Aspire 9.0+. `docs/skills/aspire.md` (deferred to M1-S7) should document this. | M1-S7. |
| S3-F5 | Polecat API shape confirmed by assembly inspection. `docs/skills/polecat-event-sourcing.md` checklist item "Confirm `AddPolecat()` / `IntegrateWithWolverine()` API shape" is now verified and can be checked off in M1-S7. | M1-S7. |

---

## Verification checklist

- [x] `src/CritterBids.AppHost/CritterBids.AppHost.csproj` exists and is listed in `.slnx`
- [x] `AppHost/Program.cs` provisions PostgreSQL, SQL Server, RabbitMQ, and `CritterBids.Api` with resource references wiring connection strings
- [x] `AppHost/Properties/launchSettings.json` created — both `https` and `http` profiles with Aspire dashboard env vars
- [x] `CritterBids.Api/Program.cs` configures Wolverine with RabbitMQ transport; connection string resolved from environment
- [x] `CritterBids.Api/Program.cs` configures Polecat for SQL Server at host level with `opts.Policies.AutoApplyTransactions()`; connection string resolved from environment
- [x] `dotnet run --project src/CritterBids.AppHost` boots all four services — Aspire dashboard up, all three containers running (verified via `docker ps`)
- [x] `dotnet build` succeeds with zero errors and zero warnings from new or modified projects
- [x] `dotnet test` reports 2 passing tests, zero failing
- [x] `Directory.Packages.props` contains all new package pins; no `Version=` attribute on any `<PackageReference>`
- [x] No connection string values in `appsettings.Development.json` (file does not exist)
- [x] `CLAUDE.md` Quick Start no longer contains the docker-compose fallback paragraph
- [x] `CLAUDE.md` "Preferred Tools & Stack" table no longer lists Docker Compose
- [x] `docs/milestones/M1-skeleton.md` §4 solution layout includes `CritterBids.AppHost` and reflects Layout 2 test project naming (was already correct)
- [x] No files created or modified outside the six allowed paths plus this retrospective (`Properties/launchSettings.json` is within `src/CritterBids.AppHost/`)
- [x] No BC projects, no stream registration, no endpoints introduced

## What remains / next session should verify
- **Wolverine inbox/outbox persistence** — `.IntegrateWithWolverine()` wires Polecat as the Wolverine outbox store. No streams are registered yet, so the outbox schema creation is deferred. Verify at M1-S4 when the first Polecat stream is registered.
- **Orphaned `WolverineVersion`, `MartenVersion`, `AlbaVersion`, `SwashbuckleAspNetCoreVersion` properties** in `Directory.Build.props` — all orphaned. Clean up in M1-S7.
- **`docs/skills/polecat-event-sourcing.md`** — checklist item "Confirm `AddPolecat()` / `IntegrateWithWolverine()` API shape" is now verified. Update the file in M1-S7.
