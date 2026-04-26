# M1-S5: Slice 0.2 — Start Participant Session — Retrospective

**Date:** 2026-04-13
**Milestone:** M1 — Skeleton
**Slice:** S5 — Slice 0.2: `StartParticipantSession`
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M1-S5-slice-0-2-start-participant-session.md`

## Baseline

- Solution builds clean; 2 tests pass (1 Api smoke, 1 Participants smoke — 3 if the prompt's
  S1/S3 smoke tests from `CritterBids.Api.Tests` are counted separately).
- `src/CritterBids.Participants/` contained only `Participant.cs`, `ParticipantsConstants.cs`,
  `ParticipantsModule.cs`, and the `.csproj`.
- `AddParticipantsModule()` used `ConfigurePolecat()` (S4 architectural choice); host-level
  `Program.cs` owned `AddPolecat().IntegrateWithWolverine()`.
- `docs/milestones/M1-skeleton.md` §9 S5 row already updated from S4.
- `Directory.Packages.props` had 12 package pins; `WolverineFx.Http`, `WolverineFx.Http.Polecat`,
  `Alba`, `Testcontainers.MsSql` were not yet pinned.

---

## Items completed

| Item | Description |
|------|-------------|
| S5a | `ParticipantSessionStarted` sealed record |
| S5b | `StartParticipantSession` command + handler + HTTP endpoint |
| S5c | `Participant.Apply(ParticipantSessionStarted)` |
| S5d | `Program.cs` — `MapWolverineEndpoints()`, auth middleware, conditional RabbitMQ |
| S5e | Package pins — `WolverineFx.Http`, `WolverineFx.Http.Polecat`, `Alba`, `Testcontainers.MsSql` |
| S5f | `ParticipantsTestFixture` + `ParticipantsTestCollection` |
| S5g | `StartParticipantSessionTests.cs` — 2 integration tests |
| S5h | Architecture revision: module owns `AddPolecat().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()` |
| S5i | `docs/decisions/0001-uuid-strategy.md` as Proposed |

---

## S5h: Module architecture revision — supersedes S4 `ConfigurePolecat()` decision

This was the most significant deviation from S4's plan and the root of the longest debugging path.

### What changed and why

S4 established: host calls `AddPolecat().IntegrateWithWolverine()`; module calls `ConfigurePolecat()`.

S5 ends with: module calls `AddPolecat().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()`;
host calls only `AddParticipantsModule()`.

**Root cause:** `CleanAllPolecatDataAsync()` in the test fixture issues raw SQL directly against
`participants.pc_events` without triggering Polecat's lazy `AutoCreate.CreateOrUpdate`. The test
calls cleanup in `InitializeAsync()` before any ORM operation, so the schema does not yet exist.

**Resolution:** `ApplyAllDatabaseChangesOnStartup()` forces eager schema creation at host startup,
before any test cleanup call can run.

**Why not inside the `StoreOptions` lambda?** `ApplyAllDatabaseChangesOnStartup()` lives on
`PolecatConfigurationExpression` — the builder returned by `AddPolecat()` — not inside the
`Action<StoreOptions>` lambda. It must be chained on the builder:

```csharp
services.AddPolecat(opts =>
{
    opts.ConnectionString = connectionString;
    opts.DatabaseSchemaName = "participants";
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
    opts.Events.StreamIdentity = StreamIdentity.AsGuid;
})
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();
```

**Why move `IntegrateWithWolverine()` to the module?** Once `AddPolecat()` moved to the module,
`.IntegrateWithWolverine()` must chain on the same builder. Having the host call a second
`IntegrateWithWolverine()` separately would require a second `AddPolecat()` call, creating competing
store registrations.

**Impact on the S4 `connectionString` parameter note:** The parameter is now load-bearing — it is
passed directly into `opts.ConnectionString`. The S4 note ("retained but not forwarded, will become
load-bearing at named stores") is now resolved ahead of schedule.

**S4-F2 multi-BC warning unchanged:** The single-store conflict on `DatabaseSchemaName` remains.
When Settlement or Operations land, named stores (`AddPolecatStore<T>()`) are still required. The
S5 change does not affect this warning.

### Structural metrics

| Metric | S4 | S5 |
|---|---|---|
| `AddPolecat()` call location | `Program.cs` | `AddParticipantsModule()` |
| `IntegrateWithWolverine()` location | `Program.cs` | `AddParticipantsModule()` |
| `ConfigurePolecat()` calls | 1 (module) | 0 |
| `ApplyAllDatabaseChangesOnStartup()` | absent | present (module) |
| `connectionString` param forwarded | No | Yes |

---

## S5f: Test fixture — `ConfigureAppConfiguration` vs `ConfigureServices`

**Root cause of `InvalidOperationException: connection string not found` at test host startup:**

`AlbaHost.For<Program>` uses `WebApplicationFactory<Program>`. Callbacks registered on the builder
via `ConfigureAppConfiguration` inject into `IConfiguration` — but `WebApplicationBuilder.Configuration`
is already built before `WebApplicationFactory` callbacks run. This means `builder.Configuration.GetConnectionString("sqlserver")` in `Program.cs` reads the original (empty) value, not the test override.

**Resolution:** `ConfigureServices` callbacks apply before the DI container is finalized. The fixture
override must use `services.ConfigurePolecat(opts => { opts.ConnectionString = testConnStr; })`,
not `UseSetting`. This correctly overrides the connection string set by `AddParticipantsModule()`.

```csharp
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        services.ConfigurePolecat(opts =>
        {
            opts.ConnectionString = connectionString;
        });
        services.DisableAllExternalWolverineTransports();
    });
});
```

**Connection string guard in `Program.cs`:** Any `?? throw` on connection strings in `Program.cs`
must be removed. At test host startup, the `IConfiguration` value is empty (Aspire hasn't provided
it yet); the test fixture's override arrives later via `ConfigureServices`. A `?? throw` fires before
the override can apply. Replace with `?? string.Empty`.

**`DisableAllExternalWolverineTransports()` namespace:** Requires `using Wolverine;` — the extension
is on `IServiceCollection`, not on any Wolverine configuration type.

---

## S5d: `Program.cs` changes

**Conditional RabbitMQ:** The original `opts.UseRabbitMq(...)` was unconditional. At test host
startup the RabbitMQ connection string is empty (no Aspire; transport disabled). Wrapped in:

```csharp
var rabbitMqUri = builder.Configuration.GetConnectionString("rabbitmq");
if (!string.IsNullOrEmpty(rabbitMqUri))
{
    opts.UseRabbitMq(new Uri(rabbitMqUri));
}
```

`DisableAllExternalWolverineTransports()` in the fixture disables the transport even when registered,
so this guard is defense-in-depth.

**Assembly discovery:** Endpoints live in `CritterBids.Participants` (a class library), not
`CritterBids.Api`. Wolverine's default scan covers only the host assembly. Add:

```csharp
opts.Discovery.IncludeAssembly(typeof(Participant).Assembly);
```

Without this, `Found 0 Wolverine HTTP endpoints` at test startup; tests reach 404.

**Auth middleware:** `app.UseAuthentication()` and `app.UseAuthorization()` added before
`MapWolverineEndpoints()`. Required for `[AllowAnonymous]` attribute resolution even when no real
auth scheme is configured. `AddAuthentication()` and `AddAuthorization()` added to `builder.Services`.

---

## S5b: Handler shape

```csharp
[WolverinePost("/api/participants/session")]
[AllowAnonymous]
public static (CreationResponse<Guid>, IStartStream) Handle(StartParticipantSession cmd)
{
    var participantId = Guid.CreateVersion7();
    // ... derive displayName, bidderId, creditCeiling from UUID bytes ...
    var evt = new ParticipantSessionStarted(participantId, displayName, bidderId, creditCeiling, DateTimeOffset.UtcNow);
    var stream = PolecatOps.StartStream<Participant>(participantId, evt);
    return (new CreationResponse<Guid>($"/api/participants/{participantId}", participantId), stream);
}
```

**Open question resolved — stream ID:** UUID v7 (`Guid.CreateVersion7()`). `StartParticipantSession`
has no natural business key; v5 determinism does not apply. Documented in
`docs/decisions/0001-uuid-strategy.md`.

**Open question resolved — display name algorithm:** UUID-derived from random bytes 8–11 of the v7
stream ID. Adjective + Animal + N (1–9999). Uniqueness guaranteed by stream ID uniqueness — no
projection or DB read required.

**Open question resolved — BidderId:** UUID-derived from bytes 12–13. Format: `"Bidder N"` (1–9999).
True sequential counters require mutable state outside the aggregate; deferred post-M1.

**CreditCeiling:** Derived from byte 14. Range 200–1000 in 100-unit steps (9 values).

---

## S5g: Test assertions — `CreationResponse<Guid>` response body

**Error:** `System.Text.Json.JsonException: Cannot get the value of a token type 'StartObject' as a string`

**Root cause:** `CreationResponse<Guid>` serializes the response body as a JSON object, not a plain
GUID string. `result.ReadAsJsonAsync<Guid>()` fails because the body is `{"value":"...", ...}`.

**Resolution:** The participant ID is also present in the `Location` header set by `CreationResponse`.
Parse it from there:

```csharp
var location = result.Context.Response.Headers.Location.ToString();
var participantId = Guid.Parse(location.Split('/').Last());
```

This is cleaner than deserializing the response body type: the Location header is part of
`CreationResponse`'s documented contract, and the ID is always recoverable from the URL.

---

## S5e: Package pin decisions

| Package | Version pinned | Notes |
|---|---|---|
| `WolverineFx.Http` | 5.30.0 | Same family as `WolverineFx` already pinned |
| `WolverineFx.Http.Polecat` | 5.30.0 | Same family |
| `Alba` | 8.5.2 | Current stable at session time (prompt estimated 8.4.0) |
| `Testcontainers.MsSql` | 4.11.0 | Current stable at session time |

`MsSqlBuilder` obsolete constructor API: `new MsSqlBuilder()` with separate `.WithImage()` is
obsolete in Testcontainers 4.11.0. Pass the image tag directly to the constructor:
`new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04")`.

---

## S4-F4: Schema verification — partial

`ApplyAllDatabaseChangesOnStartup()` runs at host startup and creates both the Polecat event store
schema (`participants`) and the Wolverine inbox/outbox schema. Tests pass, confirming both schemas
are created before any test cleanup call.

**Not verified:** Direct SQL query to confirm Wolverine inbox/outbox tables land in `wolverine`
schema (not `participants`). Polecat's `IntegrateWithWolverine()` documentation states a default
`wolverine` schema, and behavior in tests is consistent with schema separation (no conflicts observed).
Full schema-level verification deferred to Aspire boot verification in M1-S6 or M1-S7.

---

## S4-F5: Source generator verification

`Participant.Apply(ParticipantSessionStarted)` was added this session. Build succeeds with 0 errors
and 0 warnings, including from the Polecat source generator. No generator-related issues observed.

---

## Build errors resolved

| Error | Root cause | Fix |
|---|---|---|
| `CS0618: MsSqlBuilder()` | Testcontainers 4.x obsoleted no-arg constructor | `new MsSqlBuilder("image:tag")` |
| `CS0103: JasperFxEnvironment not found` | Wrong namespace | `using JasperFx.CommandLine` not `JasperFx` |
| `CS1061: DisableAllExternalWolverineTransports not found` | Missing using | `using Wolverine;` |
| `CS0118: StartParticipantSession used as type` | Namespace/type name collision | Type aliases via `using X = Full.Name.X` |
| `CS0121: Ambiguous ExecuteAndWaitAsync` | Two overloads match | Explicit `(Func<IMessageContext, Task>)` cast |
| `CS0266: Cannot convert IAlbaHost to AlbaHost` | `AlbaHost.For<>` returns `IAlbaHost` | Change `AlbaHost Host` to `IAlbaHost Host` |
| Runtime: connection string InvalidOperationException | `ConfigureAppConfiguration` not visible to Program.cs | Remove `?? throw`; use `?? string.Empty` |
| Runtime: 0 endpoints found | Participants assembly not in Wolverine scan | `IncludeAssembly(typeof(Participant).Assembly)` |
| Runtime: `Invalid object name 'participants.pc_events'` | Lazy `AutoCreate` bypassed by raw-SQL cleanup | `ApplyAllDatabaseChangesOnStartup()` on builder |
| Runtime: JsonException reading Guid | `CreationResponse<Guid>` serializes as object | Parse ID from `Location` header instead |

---

## Test results

| Phase | Api Tests | Participants Tests | Result |
|---|---|---|---|
| Session open (baseline) | 1 | 1 smoke | 2 pass |
| After all changes | 1 | 1 smoke + 2 integration | 4 pass, 0 fail |

---

## Build state at session close

- Errors: 0
- Warnings: 0
- `Version=` on `<PackageReference>`: 0 (Aspire AppHost SDK version in `<Sdk>` element is not a `<PackageReference>`)
- `AddPolecat()` calls: 1 (inside `AddParticipantsModule()`)
- `ConfigurePolecat()` calls: 1 (inside `ParticipantsTestFixture` for test override only)
- `Apply()` methods on `Participant`: 1
- `PolecatOps.StartStream<>` calls: 1
- `session.Events.Append()` calls: 0 (anti-pattern #9 not present)
- `IMessageBus` usage in handlers: 0
- Endpoints in `CritterBids.Participants`: 1 (`POST /api/participants/session`)
- Commands: 1 (`StartParticipantSession`)
- Domain events: 1 (`ParticipantSessionStarted`)
- `[AllowAnonymous]` endpoints: 1 (M1 override in effect)
- `[Authorize]` endpoints: 0

---

## API research tooling — retrospective note

This session added MCP servers to the Claude Code configuration after M1-S5 completed. Mapping them
against the actual debugging detours reveals which would have had the most impact:

| MCP | Detour it would have prevented |
|---|---|
| `critter-stack-docs` | `ApplyAllDatabaseChangesOnStartup()` API discovery (4 wrong attempts before finding it in NuGet XML); `CreationResponse<T>` response body shape |
| `github` (JasperFx repos) | Same as above; also `MsSqlBuilder` obsolete constructor API change |
| `files` (CritterSupply) | Cross-project pattern lookup — CLAUDE.md states CritterSupply carries the same Critter Stack patterns; should be consulted before trial-and-error |
| `playwright` | Could close S4-F4 (schema verification) via Aspire dashboard inspection after local boot |
| `fetch` | Superseded by `critter-stack-docs` for Critter Stack API surface questions |
| `next-devtools` | Not applicable — no Next.js in M1 |

**For future sessions:** Before trial-and-error on an unfamiliar Polecat or Wolverine API surface,
query `critter-stack-docs` first. The pattern "try a plausible method name, get CS1061, try the next
one" is avoidable. Also consult CritterSupply via `files` — if the pattern exists there, it's solved.

**One open correction:** The retro's S5g section states the `CreationResponse<Guid>` body is
`{"value":"...", ...}`. This was inferred from the `StartObject` JSON error — the body was never
directly inspected. The Location header fix is correct regardless of body shape, but the body claim
is unverified. Use `critter-stack-docs` or `github` to confirm in a future session.

---

## Key learnings

1. **`ApplyAllDatabaseChangesOnStartup()` is on `PolecatConfigurationExpression`, not `StoreOptions`.**
   It must be chained on the builder returned by `AddPolecat()`. This makes the module the correct
   owner of the full `AddPolecat().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()` chain.
   Using it ensures `CleanAllPolecatDataAsync()` in test fixtures can safely call raw SQL cleanup
   before any ORM operation.

2. **`WebApplicationFactory`'s `ConfigureAppConfiguration` does not reach `WebApplicationBuilder.Configuration`.**
   Any connection string read by `Program.cs` at startup is already fixed before test overrides apply.
   Test overrides must use `ConfigureServices` (which applies before DI container finalization) not
   `ConfigureAppConfiguration`. Remove all `?? throw` guards on connection strings in `Program.cs`.

3. **`CreationResponse<Guid>` writes a JSON object to the response body, not a bare scalar.**
   Tests that need the participant ID from a 201 response should parse the `Location` header rather
   than `ReadAsJsonAsync<Guid>()`. The ID is always embedded in the URL path.

4. **Wolverine endpoint discovery does not auto-scan class library BCs.**
   `opts.Discovery.IncludeAssembly(assembly)` must be called for each BC assembly. Without it,
   Wolverine finds 0 endpoints in the host assembly and all HTTP requests return 404.

5. **`MsSqlBuilder` in Testcontainers 4.x requires the image tag in the constructor.**
   `new MsSqlBuilder("image:tag")` — not `new MsSqlBuilder().WithImage("image:tag")`. The no-arg
   form is marked obsolete.

6. **Namespace/type name collision with `StartParticipantSession`.** When the namespace and the
   command type share a name, `using` the namespace causes `CS0118`. Resolution: type aliases
   (`using X = Full.Namespace.X`) or omit the `using` and use fully qualified names selectively.

7. **Query `critter-stack-docs` before probing API surface by trial-and-error.** The two longest
   debugging paths in this session (finding `ApplyAllDatabaseChangesOnStartup()` and understanding
   `CreationResponse<T>`) both stemmed from not knowing where a method lived. `critter-stack-docs`
   indexes `wolverinefx.net/llms-full.txt` and `polecat.netlify.app/llms-full.txt` — querying it
   directly is faster than trying plausible method names until CS1061 stops. Also consult CritterSupply
   via the `files` MCP: if a pattern is already solved there, it doesn't need to be re-discovered here.

---

## Verification checklist

- [x] `ParticipantSessionStarted` sealed record exists with `ParticipantId` as the first property
- [x] `StartParticipantSession` command sealed record exists with no fields
- [x] `[WolverinePost("/api/participants/session")]` handler exists, returns `(CreationResponse<Guid>, IStartStream)`, uses `PolecatOps.StartStream<Participant>`
- [x] `[AllowAnonymous]` attribute is present on the endpoint
- [x] `Participant.Apply(ParticipantSessionStarted)` method exists and updates aggregate state
- [x] `app.MapWolverineEndpoints()` is called in `Program.cs`
- [x] `WolverineFx.Http` is referenced by `CritterBids.Api.csproj`
- [x] `WolverineFx.Http.Polecat` is referenced by `CritterBids.Participants.csproj`
- [x] `Directory.Packages.props` contains pins for `WolverineFx.Http`, `WolverineFx.Http.Polecat`, `Alba`, and `Testcontainers.MsSql`; no `Version=` on any `<PackageReference>`
- [x] `ParticipantsTestFixture` exists, boots `AlbaHost.For<Program>`, uses Testcontainers SQL Server, calls `DisableAllExternalWolverineTransports()`
- [x] `ParticipantsTestCollection` defines the xUnit collection fixture
- [x] `StartParticipantSessionTests.cs` exists with both test methods from §7 of the milestone doc
- [x] `dotnet test` reports 4 passing tests (2 existing + 2 new integration), zero failing
- [x] `dotnet build` succeeds with zero errors and zero warnings from new or modified projects
- [x] `POST /api/participants/session` returns 201 (verified via integration test)
- [~] S4-F4 verified: schema separation observed (tests pass without conflict); direct SQL-level table verification not performed — deferred to Aspire boot check in M1-S6/S7
- [x] S4-F5 verified: Polecat source generator activated; no generator-related compile errors
- [x] `docs/decisions/0001-uuid-strategy.md` exists as Proposed
- [x] `docs/milestones/M1-skeleton.md` §9 S5 row updated (already present from S4)
- [x] No files created or modified outside the allowed set
- [x] No commands, events, or endpoints for Slice 0.3 introduced

---

## What remains / next session should verify

- **M1-S6** — Slice 0.3: `RegisterAsSeller` command, `SellerRegistrationCompleted` integration event
  (3 scenarios: happy path, no-session rejection, already-registered rejection). `SellerRegistrationCompleted`
  published via `OutgoingMessages` with no M1 consumer.
- **S4-F4 full verification** — direct SQL query against Aspire-provisioned SQL Server confirming
  Wolverine inbox/outbox tables in `wolverine` schema, Polecat tables in `participants` schema.
  Run `dotnet run --project src/CritterBids.AppHost` and inspect via SSMS, `sqlcmd`, or the
  `playwright` MCP against the Aspire dashboard.
- **S4-F2 multi-BC named stores** — still deferred to M2 planning. When the next Polecat BC
  (Settlement or Operations) is scaffolded, migrate both BCs to `AddPolecatStore<T>()`.
- **`polecat-event-sourcing.md` skill doc** — `UseSystemTextJsonForSerialization` correction (S4-F1)
  and fixture pattern update (`ConfigureServices` override, `Location` header assertion pattern).
  M1-S7.
