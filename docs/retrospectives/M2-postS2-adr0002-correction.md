# M2 (post-S2): ADR 0002 Correction + Architecture Unwind — Retrospective

**Date:** 2026-04-14
**Milestone:** M2 — Listings Pipeline
**Slice:** Unscheduled — impromptu architectural correction between S2 and S3
**Agent:** Claude (interactive session, no Copilot agent)
**Prompt:** None — driven by Claude Code agent retrospective feedback from M2-S2

> **Session note:** This was not a prompted Copilot agent session. It began as a skills
> enhancement pass based on M2-S2 agent feedback, then escalated into an architectural
> correction after the named-store approach was identified as fundamentally incompatible
> with CritterBids' showcase objective. The retro captures both phases.

---

## Baseline

- `dotnet build`: 0 errors, 0 warnings (post-M2-S2 state).
- `dotnet test`: 9 passing — Participants 6, Selling 1, Api.Tests 1, Contracts.Tests 1.
- Architecture under ADR 0002: named Marten stores via `AddMartenStore<ISellingDocumentStore>()`.
- `IDocumentSession` injection, `AutoApplyTransactions`, `[Entity]`, `IStorageAction<T>` all unavailable in Selling BC handlers by design.
- `SellerRegistrationCompletedHandler` injecting `ISellingDocumentStore`, calling `LightweightSession()` manually, calling `SaveChangesAsync()` explicitly.

---

## Items completed

| Item | Description |
|------|-------------|
| Ph1a | Authored `marten-named-stores.md` skill — then archived at session close |
| Ph1b | Added Anti-Pattern #15 to `wolverine-message-handlers.md` — then removed at session close |
| Ph1c | Added cross-BC fixture isolation section to `critter-stack-testing-patterns.md` — then updated |
| Ph2a | Modular monolith architecture evaluated and confirmed — not migrating to microservices |
| Ph2b | ADR 0003 authored — supersedes ADR 0002 |
| Ph2c | ADR 0002 marked superseded |
| Ph2d | `ISellingDocumentStore.cs` deleted |
| Ph2e | `SellingModule.cs` rewritten — `ConfigureMarten()`, `IConfiguration` parameter removed |
| Ph2f | `SellerRegistrationCompletedHandler.cs` rewritten — `IDocumentSession` injection, `AutoApplyTransactions` commit |
| Ph2g | `SellerRegistrationService.cs` updated — `IDocumentStore` (not `ISellingDocumentStore`) |
| Ph2h | `Program.cs` rewritten — primary `AddMarten()`, null guards on both postgres and sqlserver |
| Ph2i | `CritterBids.Api.csproj` — `WolverineFx.Marten` package reference added |
| Ph2j | `SellingTestFixture.cs` rewritten — single PostgreSQL container, `AddMarten()` + `AddSellingModule()` in `ConfigureServices` |
| Ph2k | `ParticipantsTestFixture.cs` rewritten — `AddParticipantsModule()` directly in `ConfigureServices` |
| Ph2l | `SellingModuleTests.cs` updated — asserts `IDocumentStore` (not `ISellingDocumentStore`) |
| Ph2m | `marten-named-stores.md` archived with prominent warning header |
| Ph2n | Anti-Pattern #15 removed from `wolverine-message-handlers.md` |
| Ph2o | `critter-stack-testing-patterns.md` updated — Marten fixture pattern, Key Principles, ConfigureAppConfiguration caveat |
| Ph2p | `adding-bc-module.md` rewritten — `ConfigureMarten()` pattern throughout |
| Ph2q | `CLAUDE.md` updated — removed named-store callout |
| Ph2r | `docs/skills/README.md` updated — `marten-named-stores.md` archived |
| Ph2s | 13/13 tests passing at session close |

---

## Phase 1: Skills authoring (subsequently superseded)

The session opened with a Claude Code agent's post-M2-S2 feedback identifying three skill gaps. All three were authored and committed to disk before the architectural question surfaced and made them obsolete or reformulated.

**What was created:**
- `marten-named-stores.md` — comprehensive named-store constraint reference
- Anti-Pattern #15 in `wolverine-message-handlers.md` — `IDocumentSession` injection in named-store handlers
- Cross-BC fixture isolation section in `critter-stack-testing-patterns.md`

These were substantially correct descriptions of the named-store constraints. They became moot once ADR 0002 was superseded, but the cross-BC fixture isolation section survived in updated form (the `IWolverineExtension` handler exclusion pattern and stub local queue remain valid under ADR 0003).

---

## Phase 2a: Architecture evaluation — modular monolith vs. microservices

The named-store problem surfaced a deeper question: whether CritterBids should remain a modular monolith. The evaluation considered:

**Why CritterSupply doesn't face this problem:** CritterSupply is microservices — separate `Program.cs` per BC, separate DI containers, each BC calls `AddMarten()` independently. The comparison is not applicable.

**Why microservices would genuinely help:** Each BC process gets its own `AddMarten()` with no multi-store conflict. But the tradeoff: conference demo complexity, no clean `dotnet run` single-command story, no compelling transport-swap demo, no `docker compose up` on a Hetzner VPS.

**Decision: keep the modular monolith.** The vision document (`docs/vision/overview.md`) is explicit: "CritterBids is not a microservices showcase. The modular monolith is a deliberate architectural choice." The transport-swap story (RabbitMQ → Azure Service Bus in a single `Program.cs` change) is uniquely compelling in the monolith form. CritterSupply already demonstrates the microservices path.

**Migration difficulty if ever needed:** Low. BC boundaries are already enforced by project structure and `CritterBids.Contracts`. Wolverine transport abstraction means handler code doesn't change. Migration is primarily a deployment concern. A future milestone could demonstrate extracting one BC (e.g., Relay or Operations) and introducing YARP as a reverse proxy.

---

## Phase 2b: ADR 0002 root cause

ADR 0002 correctly diagnosed the problem (two `AddMarten()` calls = competing `IDocumentStore` singletons) but chose the wrong solution (`AddMartenStore<T>()` named stores). The named-store API is Marten's "ancillary store" mechanism, designed for multi-server deployments where BCs must connect to separate PostgreSQL instances. CritterBids has one PostgreSQL server.

The consequence: `AddMartenStore<T>()` deliberately omits `SessionVariableSource`, `MartenPersistenceFrameProvider`, and `MartenOpPolicy` registrations. These are what enable Wolverine's code-generated handler middleware to inject `IDocumentSession`, apply `AutoApplyTransactions()`, honour `[Entity]`, and process `IStorageAction<T>`. ADR 0002 traded away the entire Critter Stack idiom stack to solve a constraint that did not actually apply.

**The correct solution (ADR 0003):** One primary `IDocumentStore` via `AddMarten()` in `Program.cs`. Each Marten BC contributes its types via `services.ConfigureMarten()` inside its `AddXyzModule()`. The `mt_events` and `mt_streams` tables are shared across all Marten BCs — distinguishable by aggregate type, not by physical schema. Document tables can still have per-BC schema names via per-type `DatabaseSchemaName` configuration.

---

## Phase 2c: Handler before and after

### Before (ADR 0002)

```csharp
[MartenStore(typeof(ISellingDocumentStore))]
public static class SellerRegistrationCompletedHandler
{
    public static async Task Handle(
        SellerRegistrationCompleted message,
        ISellingDocumentStore store)
    {
        await using var session = store.LightweightSession();
        session.Store(new RegisteredSeller { Id = message.ParticipantId });
        await session.SaveChangesAsync();
    }
}
```

### After (ADR 0003)

```csharp
public static class SellerRegistrationCompletedHandler
{
    public static void Handle(
        SellerRegistrationCompleted message,
        IDocumentSession session)
    {
        session.Store(new RegisteredSeller { Id = message.ParticipantId });
        // AutoApplyTransactions commits — no explicit SaveChangesAsync
    }
}
```

**Structural metrics:**

| Metric | Before (ADR 0002) | After (ADR 0003) |
|---|---|---|
| `[MartenStore]` attributes on handlers | 1 | 0 |
| `ISellingDocumentStore` injections | 1 | 0 |
| `IDocumentSession` injections | 0 | 1 |
| Manual `LightweightSession()` calls | 1 | 0 |
| Explicit `SaveChangesAsync()` calls | 1 | 0 |
| `IDocumentStore` registrations (prod) | 0 | 1 |
| `AddMartenStore<T>()` calls | 1 | 0 |
| `AddMarten()` calls | 0 | 1 |
| `ISellingDocumentStore.cs` files | 1 | 0 (deleted) |

---

## Phase 2d: `SellingModule.cs` before and after

`IConfiguration` was removed as a parameter — `AddSellingModule()` no longer reads a connection string (the primary store's connection is owned by `Program.cs`).

```csharp
// After — entire module registration
public static IServiceCollection AddSellingModule(this IServiceCollection services)
{
    services.ConfigureMarten(opts =>
    {
        opts.Schema.For<RegisteredSeller>().DatabaseSchemaName("selling");
    });

    services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();
    return services;
}
```

---

## Phase 2e: Test failures encountered and resolved

### Failure 1 — `EventAppendMode` not found

**Error:** `CS0103: The name 'EventAppendMode' does not exist in the current context`

**Root cause:** `CritterBids.Api.csproj` had no `WolverineFx.Marten` (or `Marten`) package reference. `Program.cs` now calls `AddMarten()` but the namespace wasn't available.

**Resolution:** Added `<PackageReference Include="WolverineFx.Marten" />` to `CritterBids.Api.csproj`. Added `using JasperFx.Events;` for `EventAppendMode`.

---

### Failure 2 — Two main Wolverine message stores

**Error:** `InvalidWolverineStorageConfigurationException: There must be exactly one message store tagged as the 'main' store. Found multiples: wolverinedb://sqlserver/127.0.0.1/master/wolverine, wolverinedb://postgresql/127.0.0.1/postgres/wolverine`

**Root cause:** `AddParticipantsModule()` in Program.cs was called unconditionally:

```csharp
var sqlServerConnectionString = builder.Configuration.GetConnectionString("sqlserver") ?? string.Empty;
builder.Services.AddParticipantsModule(sqlServerConnectionString);
```

Even with an empty string, `AddParticipantsModule` called `AddPolecat().IntegrateWithWolverine()`, registering Polecat as a Wolverine "main" message store. The Selling test fixture simultaneously registered `AddMarten().IntegrateWithWolverine()`. Two main stores = conflict.

**Resolution:** Null-guarded `AddParticipantsModule` in `Program.cs`, mirroring the existing guard on `AddMarten()`:

```csharp
var sqlServerConnectionString = builder.Configuration.GetConnectionString("sqlserver");
if (!string.IsNullOrEmpty(sqlServerConnectionString))
{
    builder.Services.AddParticipantsModule(sqlServerConnectionString);
}
```

The Selling test fixture provides no `sqlserver` connection string → guard fails → Polecat not registered → only one main store.

---

### Failure 3 — `IDocumentStore` unresolvable in Selling tests

**Error:** `Unable to resolve service for type 'Marten.IDocumentStore' while attempting to activate 'CritterBids.Selling.SellerRegistrationService'`

**Root cause:** `ConfigureAppConfiguration` in `AlbaHost.For<Program>` does **not** propagate to `Program.cs`'s inline `builder.Configuration.GetConnectionString("postgres")` reads. The fixture's `ConfigureAppConfiguration` callback runs against the `WebApplicationFactory`'s host builder; `Program.cs`'s `WebApplicationBuilder` evaluates its configuration independently before those callbacks apply. The null guard on `AddMarten()` was never triggered, so `IDocumentStore` was never registered. DI validation then caught that `SellerRegistrationService` depended on the unregistered type.

This finding directly contradicts M2-S2's Key Learning #4, which stated `ConfigureAppConfiguration` was the correct injection point. That learning was wrong for inline `builder.Configuration` reads — it worked in M2-S2 only because that session's module guard used `IConfiguration` injected into the module's method signature (which is resolved later, not inline). The distinction:

- **Works via `ConfigureAppConfiguration`:** `configuration["ConnectionStrings:key"]` inside `AddXyzModule(IConfiguration config)` — evaluated lazily when DI builds the service
- **Does NOT work via `ConfigureAppConfiguration`:** `builder.Configuration.GetConnectionString("key")` in `Program.cs` inline code — evaluated immediately during builder setup

**Resolution:** Register `AddMarten()` and `AddSellingModule()` directly in the Selling test fixture's `ConfigureServices`:

```csharp
builder.ConfigureServices(services =>
{
    services.AddMarten(opts =>
    {
        opts.Connection(postgresConnectionString);
        // ...
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine();

    services.AddSellingModule(); // must also be registered — guarded in Program.cs
    services.RunWolverineInSoloMode();
    services.DisableAllExternalWolverineTransports();
});
```

---

### Failure 4 — `Polecat.IDocumentStore` not found in Participants tests

**Error:** `No service for type 'Polecat.IDocumentStore' has been registered` (during `CleanAllPolecatDataAsync` in `InitializeAsync`)

**Root cause:** Same as Failure 3. The Participants test fixture had used `ConfigureAppConfiguration` to inject the SQL Server connection string, expecting Program.cs's null guard on `AddParticipantsModule` to be satisfied. The guard was not triggered for the same reason — `ConfigureAppConfiguration` does not propagate to inline `Program.cs` reads.

**Resolution:** Register `AddParticipantsModule(connectionString)` directly in the Participants test fixture's `ConfigureServices`, removing the `ConfigureAppConfiguration` block and the `ConfigurePolecat` override:

```csharp
builder.ConfigureServices(services =>
{
    services.AddParticipantsModule(connectionString); // direct registration with Testcontainers string
    services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
    services.DisableAllExternalWolverineTransports();
});
```

---

### Failure 5 — `ISellerRegistrationService` not found in Participants tests

**Error:** `Unable to resolve service for type 'CritterBids.Selling.ISellerRegistrationService'`

**Root cause:** `AddSellingModule()` is now inside the postgres null guard in `Program.cs`. In the Participants fixture (no postgres), the guard fails → `AddSellingModule()` not called → `ISellerRegistrationService` not registered → DI validation fails.

**Resolution:** The Participants fixture doesn't need `ISellerRegistrationService`. The `SellingBcDiscoveryExclusion` `IWolverineExtension` already excludes Selling BC handlers from handler discovery. However, `ISellerRegistrationService` is registered as a transient service, and DI validates it even when not used. The fix: `SellerRegistrationService` depends on `IDocumentStore`, which is absent in the Participants fixture. Since Selling BC is fully excluded, the fixture must not register `AddSellingModule()` at all — and since `Program.cs`'s null guard now prevents it when postgres is absent, this resolves automatically once Failure 3's fix is in place.

---

## Test results

| Phase | Selling Tests | Participants Tests | All Tests | Result |
|-------|--------------|-------------------|-----------|--------|
| Session open (baseline) | 1 | 6 | 9 | Pass |
| After ADR 0003 code changes | 0 | 0 | — | Build fail (EventAppendMode) |
| After `WolverineFx.Marten` + `JasperFx.Events` using | 0 | 0 | — | Two-main-stores failure |
| After `AddParticipantsModule` null guard | 0 | 0 | — | `IDocumentStore` unresolvable |
| After `AddSellingModule` moved inside postgres guard | 0 | 0 | — | `ISellerRegistrationService` unresolvable |
| After `IConfiguration` param removed + direct `ConfigureServices` registration | 5 | 6 | 13 | **Pass** |

---

## Build state at session close

- Errors: 0
- Warnings: 0
- Tests: 13 passing (Selling 5, Participants 6, Api.Tests 1, Contracts.Tests 1)
- `AddMarten()` calls in production code: 1 (null-guarded in `Program.cs`)
- `AddMartenStore<T>()` calls in production code: 0
- `ISellingDocumentStore` files: 0 (deleted)
- `[MartenStore]` attribute placements: 0
- `IDocumentSession` injections in handlers: 1 (idiomatic)
- Manual `LightweightSession()` calls in handlers: 0
- Explicit `SaveChangesAsync()` calls in handlers: 0
- `ConfigureAppConfiguration` blocks in test fixtures: 0 (pattern retired for inline guards)
- `services.ConfigurePolecat()` overrides in test fixtures: 0 (replaced by direct module registration)
- `app.Run()` calls: 0
- `IMessageBus` usages outside `ScheduleAsync()`: 0

---

## Key learnings

1. **`ConfigureAppConfiguration` does NOT propagate to inline `builder.Configuration.GetConnectionString()` reads in `Program.cs`.** The `WebApplicationBuilder` evaluates its configuration before the `WebApplicationFactory`'s `ConfigureAppConfiguration` callbacks apply. Only module-method-signature `IConfiguration` parameters (resolved during DI build) see the injected values. M2-S2 Key Learning #4 was wrong for this pattern. The correct approach: register stores and modules directly in `ConfigureServices`, which always runs after `Program.cs`.

2. **Both stores calling `IntegrateWithWolverine()` in the same process causes `InvalidWolverineStorageConfigurationException`.** Wolverine requires exactly one "main" message store. In CritterBids' modular monolith, each test fixture must provision exactly one storage backend (Marten-only or Polecat-only). The multi-store production scenario (when all 8 BCs run together) is a deferred architectural concern — the Wolverine ancillary store API for Marten+Polecat is not well-documented for single-server mixed-backend deployments.

3. **The named-store API (`AddMartenStore<T>()`) is for multi-server deployments where BCs must connect to separate PostgreSQL instances.** For a single-server modular monolith, using named stores forfeits `IDocumentSession` injection, `AutoApplyTransactions`, `[Entity]`, `[WriteAggregate]`, and `IStorageAction<T>` — the entire showcase idiom stack. The correct CritterBids pattern is `AddMarten()` once in `Program.cs` + `ConfigureMarten()` contributions per BC module.

4. **Test fixtures must register their BC module directly in `ConfigureServices` even when `Program.cs` also registers it.** Program.cs's null guards prevent module registration when infrastructure is absent. `ConfigureServices` runs after Program.cs and must register the module independently to ensure services like `ISellerRegistrationService` are present. If Program.cs and the fixture both call `AddMarten()`, the last registration wins — no conflict.

5. **Each test fixture must provision exactly one storage backend.** Running both Marten (`AddMarten().IntegrateWithWolverine()`) and Polecat (`AddPolecat().IntegrateWithWolverine()`) in the same test host registers two main Wolverine message stores. The single-store-per-fixture rule prevents this. Marten BC tests: PostgreSQL only, no SQL Server. Polecat BC tests: SQL Server only, no PostgreSQL.

6. **`IWolverineExtension` exclusions are still required for cross-BC handler isolation**, even under ADR 0003. When a Marten BC's handlers are in scope (the assembly is always included via `Program.cs`) but Marten is not provisioned in the fixture, `SessionVariableSource` is absent and Wolverine cannot code-generate handlers that inject `IDocumentSession`. The `SellingBcDiscoveryExclusion` pattern in the Participants fixture remains the correct suppression mechanism.

7. **`AddSellingModule()` no longer needs an `IConfiguration` parameter.** Under ADR 0003, the module only calls `services.ConfigureMarten()` and registers BC-internal services. There is no connection string to read. Future Marten BC modules follow the same signature: `AddXyzModule(this IServiceCollection services)`, no `IConfiguration`.

8. **Modular monolith remains the correct architectural choice for CritterBids.** CritterSupply demonstrates microservices; CritterBids differentiates by showing the modular monolith story — transport-agnostic messaging, BC boundary enforcement without network overhead, `docker compose up` single-deployable story. Migration to separate services is low-effort if ever needed (Wolverine transport abstraction means handlers don't change), but the showcase value is in not doing it.

---

## Verification checklist

- [x] ADR 0003 authored, accepted, and consistent with the implemented code
- [x] ADR 0002 status updated to "Superseded by ADR 0003"
- [x] `ISellingDocumentStore.cs` deleted — no file exists
- [x] `SellingModule.cs` uses `services.ConfigureMarten()`, takes no `IConfiguration` parameter
- [x] `SellerRegistrationCompletedHandler.cs` injects `IDocumentSession`, carries no `[MartenStore]` attribute, contains no explicit `SaveChangesAsync()` call
- [x] `SellerRegistrationService.cs` injects `IDocumentStore`, not `ISellingDocumentStore`
- [x] `Program.cs` calls `AddMarten()` (null-guarded on postgres) and `AddParticipantsModule()` (null-guarded on sqlserver)
- [x] `Program.cs` places `AddSellingModule()` inside the postgres null guard
- [x] `CritterBids.Api.csproj` includes `WolverineFx.Marten`
- [x] `SellingTestFixture` provisions PostgreSQL only (no SQL Server container)
- [x] `SellingTestFixture` registers `AddMarten()` and `AddSellingModule()` in `ConfigureServices`
- [x] `SellingTestFixture.CleanAllMartenDataAsync()` uses non-generic `Host.CleanAllMartenDataAsync()`
- [x] `ParticipantsTestFixture` registers `AddParticipantsModule(connectionString)` directly in `ConfigureServices`
- [x] `ParticipantsTestFixture` contains no `ConfigureAppConfiguration` block
- [x] `ParticipantsTestFixture` contains no `ConfigurePolecat` override
- [x] `SellingModuleTests.cs` asserts `IDocumentStore` (not `ISellingDocumentStore`) is resolvable
- [x] `marten-named-stores.md` archived with warning header — not removed, retained as reference
- [x] Anti-Pattern #15 removed from `wolverine-message-handlers.md`
- [x] `critter-stack-testing-patterns.md` Marten BC fixture section updated to ADR 0003 pattern
- [x] `critter-stack-testing-patterns.md` ConfigureAppConfiguration caveat updated — clearly states it does NOT work for inline Program.cs guards
- [x] `critter-stack-testing-patterns.md` Key Principles updated — single-store-per-fixture rule added, ConfigureServices requirement documented
- [x] `adding-bc-module.md` rewritten — `ConfigureMarten()` pattern, correct fixture shape, updated checklist
- [x] `CLAUDE.md` Skill Invocation Guide updated — named-store rows removed
- [x] `docs/skills/README.md` updated — `marten-named-stores.md` archived, gap analysis log updated
- [x] `dotnet build`: 0 errors, 0 warnings
- [x] `dotnet test`: 13/13 passing

---

## Files changed

**Deleted**
- `src/CritterBids.Selling/ISellingDocumentStore.cs`

**Modified — source**
- `src/CritterBids.Api/Program.cs` — primary `AddMarten()` + null guards, `WolverineFx.Marten` + `JasperFx.Events` usings, both BC module registrations guarded
- `src/CritterBids.Api/CritterBids.Api.csproj` — `WolverineFx.Marten` reference added
- `src/CritterBids.Selling/SellingModule.cs` — `ConfigureMarten()` pattern, `IConfiguration` param removed
- `src/CritterBids.Selling/SellerRegistrationCompletedHandler.cs` — `IDocumentSession` injection, removed `[MartenStore]`, removed manual session management
- `src/CritterBids.Selling/SellerRegistrationService.cs` — `IDocumentStore` instead of `ISellingDocumentStore`

**Modified — tests**
- `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs` — single PostgreSQL container, `ConfigureServices`-only registration
- `tests/CritterBids.Selling.Tests/SellingModuleTests.cs` — asserts `IDocumentStore`
- `tests/CritterBids.Participants.Tests/Fixtures/ParticipantsTestFixture.cs` — `AddParticipantsModule()` direct, no `ConfigureAppConfiguration`

**Modified — docs**
- `docs/decisions/0002-marten-bc-isolation.md` — status updated to Superseded
- `docs/decisions/0003-shared-marten-store.md` — new ADR
- `docs/skills/marten-named-stores.md` — archived with warning header
- `docs/skills/wolverine-message-handlers.md` — Anti-Pattern #15 removed
- `docs/skills/critter-stack-testing-patterns.md` — Marten fixture pattern, ConfigureAppConfiguration caveat, Key Principles, Polecat fixture pattern, References
- `docs/skills/adding-bc-module.md` — full Marten BC section rewrite, updated checklist
- `docs/skills/CLAUDE.md` — Skill Invocation Guide updated
- `docs/skills/README.md` — status table updated, gap analysis log updated

---

## What remains / next session should verify

- **Production multi-store conflict is unresolved.** When both postgres and sqlserver connection strings are present (production, Aspire), both `AddMarten().IntegrateWithWolverine()` and `AddParticipantsModule()` → `AddPolecat().IntegrateWithWolverine()` run → two main Wolverine message stores → `InvalidWolverineStorageConfigurationException` at startup. This must be resolved before the full solution can be Aspire-run locally. Requires either: (a) marking Polecat as ancillary via `IntegrateWithWolverine(cfg => ...)` if Wolverine Polecat supports it, (b) a dedicated `opts.PersistMessagesWithPostgresql()` standalone message store, or (c) a conversation with Jeremy Miller. File a GitHub discussion or reach out directly.

- **M2-S2 Key Learning #4 is now incorrect** in the `M2-S2-selling-bc-scaffold-retrospective.md`. It states `ConfigureAppConfiguration` is the correct injection point for module null guards. It is not — it works only for module-method `IConfiguration` parameters, not for inline `Program.cs` reads. Future sessions reading that retro should be aware the pattern changed.

- **`dotnet run --project src/CritterBids.AppHost` will fail** until the two-main-stores production conflict is resolved. Test-only workflow is unblocked; local Aspire run is blocked.

- **`SellerListing` stream registration** — S3. Marten discovers event types at append time; no registration needed until events are introduced.

- **`SellerRegistrationCompleted` handler integration test** — already passing (`RegisteredSellersProjectionTests`), but should be reviewed at S3 since it is the first test that exercises the full `IDocumentSession` → `AutoApplyTransactions` → outbox pipeline under ADR 0003.

- **Future Marten BC modules** follow the pattern: `services.ConfigureMarten(...)` inside `AddXyzModule(this IServiceCollection services)`, no `IConfiguration` parameter, registered inside the postgres null guard block in `Program.cs`, and registered directly in `ConfigureServices` of their test fixture.
