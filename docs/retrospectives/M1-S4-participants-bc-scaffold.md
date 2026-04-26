# M1-S4: Participants BC Scaffold — Retrospective

**Date:** 2026-04-12
**Milestone:** M1 — Skeleton
**Slice:** S4 — Participants BC scaffold
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M1-S4-participants-bc-scaffold.md`

## Baseline

- Solution builds clean; 2 tests pass (both smoke tests from M1-S1).
- `src/CritterBids.Participants/` did not exist.
- `tests/CritterBids.Participants.Tests/` did not exist.
- `CritterBids.slnx` listed 3 src projects and 2 test projects.
- `src/CritterBids.Api/Program.cs` called `AddPolecat()` + `.IntegrateWithWolverine()` with no schema, no stream registration, and a comment ("each BC sets its own schema in AddXyzModule()") that left M1-D4 open.
- `docs/milestones/M1-skeleton.md` §9 S4 row was `*TBD*`.
- `Directory.Packages.props` had 12 package pins. No new pins required this session.

---

## Items completed

| Item | Description |
|------|-------------|
| S4a | `src/CritterBids.Participants/CritterBids.Participants.csproj` created |
| S4b | `src/CritterBids.Participants/Participant.cs` — empty class-based aggregate |
| S4c | `src/CritterBids.Participants/ParticipantsConstants.cs` — UUID v5 namespace constant (resolves M1-D4) |
| S4d | `src/CritterBids.Participants/ParticipantsModule.cs` — `AddParticipantsModule()` extension |
| S4e | `tests/CritterBids.Participants.Tests/CritterBids.Participants.Tests.csproj` created |
| S4f | `tests/CritterBids.Participants.Tests/SmokeTests.cs` — smoke test |
| S4g | `CritterBids.slnx` updated — both new projects added to `/src/` and `/tests/` folders |
| S4h | `src/CritterBids.Api/CritterBids.Api.csproj` updated — project reference to `CritterBids.Participants` added |
| S4i | `src/CritterBids.Api/Program.cs` updated — `AddParticipantsModule()` called; SQL Server connection string extracted to a local variable |
| S4j | `docs/milestones/M1-skeleton.md` §9 S4 row updated from `*TBD*` to prompt filename |

---

## S4d: `AddParticipantsModule()` — architectural decision on Polecat extension pattern

### Open question resolved: `ConfigurePolecat()` is the correct BC module pattern

This was the primary architectural question carried from M1-S3 and the prompt's explicitly flagged decision gate. Three paths were stated:

1. Single store extended by BC modules via `ConfigurePolecat()`
2. Multi-store — named stores per Polecat BC
3. Host-level `AddPolecat()` removed, each BC owns its store fully

**Path chosen: single store, extended by BC modules via `ConfigurePolecat()`.**

**Evidence:** Assembly inspection of `Polecat.dll` 2.0.1 via the NuGet XML documentation confirmed:

```
T:Polecat.PolecatServiceConfigurationExtensions
M:Polecat.PolecatServiceConfigurationExtensions.ConfigurePolecat(
    Microsoft.Extensions.DependencyInjection.IServiceCollection,
    System.Action{Polecat.StoreOptions})
Summary: "Register a post-configuration action for StoreOptions."
```

Also confirmed:

```
T:Polecat.IConfigurePolecat
Summary: "Implement this interface and register in DI to apply additional
          configuration to StoreOptions after construction."
```

`ConfigurePolecat()` is Polecat's equivalent of Marten's `ConfigureMarten()`. It appends a post-configuration action that is applied when `IDocumentStore` is first resolved. Order relative to `AddPolecat()` at DI registration time is irrelevant — the action executes at build time.

**Why not path 2 (named stores)?** `AddPolecatStore<T>()` exists in `Polecat.PolecatStoreServiceCollectionExtensions` and has a corresponding `ConfigurePolecat<T>()` overload. Named stores would be correct for multi-Polecat-BC isolation once Settlement and Operations BCs land (M2+). For M1 with one Polecat BC, the default store with `ConfigurePolecat()` is sufficient. See "Future: multi-BC schema isolation" below.

**Why not path 3 (host removes `AddPolecat()`)?** The host-level call establishes the connection string and `.IntegrateWithWolverine()` once. Removing it would require each BC module to call `.IntegrateWithWolverine()`, creating competing registrations of the Wolverine SQL Server message store.

### Resulting module shape

```csharp
public static IServiceCollection AddParticipantsModule(
    this IServiceCollection services,
    string connectionString)
{
    services.ConfigurePolecat(opts =>
    {
        opts.DatabaseSchemaName = "participants";
        opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
    });

    return services;
}
```

### `connectionString` parameter — accepted but not forwarded

The prompt required the parameter for API consistency with future `AddXyzModule()` extensions. With `ConfigurePolecat()`, the connection string is already established by the host-level `AddPolecat()`. The parameter is retained in the signature but not passed inside `ConfigurePolecat()`. A comment in the method body documents this.

**Future-session note:** When named stores are introduced (M2+ for Settlement / Operations), the BC module pattern will change to `AddPolecatStore<T>()` with the connection string forwarded. The `connectionString` parameter will become load-bearing at that point. This is the primary reason to keep it in the signature now.

### `.IntegrateWithWolverine()` acceptance criterion — superseded

The prompt's acceptance criterion "[ ] `AddParticipantsModule()` chains `.IntegrateWithWolverine()`" was written under architectural uncertainty. It assumed BC modules would call `AddPolecat()` directly and chain `.IntegrateWithWolverine()`. With `ConfigurePolecat()`, there is nothing to chain — Wolverine integration is already wired at the host level. The criterion is superseded by the architectural decision. It will not appear as failing in the verification checklist; instead it is annotated as N/A with this explanation.

---

## S4c: UUID v5 namespace constant — resolves M1-D4

```csharp
public static readonly Guid ParticipantsNamespace =
    new Guid("f2f3dcf5-9e37-4f4c-b794-4e7bbeb2373c");
```

Generated fresh via `[System.Guid]::NewGuid()` in PowerShell during session. Not `Guid.Empty`, not a sentinel. The constant lives in `CritterBids.Participants.ParticipantsConstants` and is accessible to all future Participants handlers without a circular reference.

M1-D4 is closed.

---

## S4a: `CritterBids.Participants.csproj` — package reference decision

The `CritterBids.Participants` project references only `Polecat`. `JasperFx` and `JasperFx.Events` — which provide `AutoCreate` and `StreamIdentity` respectively — are transitive dependencies of `Polecat 2.0.1` (confirmed via `polecat.nuspec`). No additional explicit references needed for the using directives below:

```csharp
using JasperFx;          // AutoCreate
using JasperFx.Events;   // StreamIdentity
using Polecat;           // ConfigurePolecat, StoreOptions
```

`WolverineFx.Polecat` is explicitly absent. It provides `PolecatOps`, `[WriteAggregate]`, `[AggregateHandler]`, and similar handler-time types — none of which are needed in the scaffold. It will be added to the project in M1-S5 when the first Participants command handler is introduced.

---

## S4b: `Participant` aggregate — class vs record distinction

```csharp
public class Participant
{
    public Guid Id { get; set; }
}
```

Class (not `sealed record`). Polecat source generators emit `Apply()` method bodies that mutate the aggregate instance directly. `sealed record` aggregates with `with` expressions are a Marten convention; with Polecat's source generators the mutable class model is canonical.

`{ get; set; }` not `{ get; init; }` — the source generator needs a setter when applying events (M1-S5+). Using `init` would work today with no events, but switching to `set` post-events is a trivial no-op change with no current reason to defer. Better to be correct from the start.

No `Apply()` methods, no status enum, no other members. Structured so `PolecatOps.StartStream<Participant>(id, ...)` in M1-S5 can reference the type by generic parameter without modification.

---

## S4d: `UseSystemTextJsonForSerialization` — confirmed absent from Polecat 2.x

The prompt open question asked whether the `UseSystemTextJsonForSerialization(EnumStorage.AsString)` call was required or implicit. Confirmed via assembly inspection: **the method does not exist on `StoreOptions` in Polecat 2.0.1.** Polecat exposes `ConfigureSerialization(EnumStorage, Casing, CollectionStorage, NonPublicMembersStorage, Action<JsonSerializerOptions>)` instead.

Since Polecat is System.Text.Json–only, no serializer selection call is needed. The skill doc at `docs/skills/polecat-event-sourcing.md` incorrectly shows `UseSystemTextJsonForSerialization(EnumStorage.AsString)` — this is a Marten API that does not transfer. **Update the skill doc in M1-S7.**

For now, `ConfigureSerialization` is omitted. If `EnumStorage.AsString` becomes necessary (e.g., when status enums appear on events in M1-S5+), use:

```csharp
opts.ConfigureSerialization(Polecat.Serialization.EnumStorage.AsString);
```

---

## S4i: Program.cs refactor — connection string extraction

The existing `AddPolecat()` lambda used `builder.Configuration.GetConnectionString("sqlserver")` inline. Calling `AddParticipantsModule(...)` required the same value. Rather than a second `GetConnectionString()` call or an `!` throw inline, the string was extracted to a local variable:

```csharp
var sqlServerConnectionString = builder.Configuration.GetConnectionString("sqlserver")
    ?? throw new InvalidOperationException("...");

builder.Services.AddPolecat(opts =>
{
    opts.ConnectionString = sqlServerConnectionString;
})
.IntegrateWithWolverine();

builder.Services.AddParticipantsModule(sqlServerConnectionString);
```

This is a minimal, zero-risk change that keeps the null check in one place. The stale inline comment ("No stream registration here — that is M1-S4's job") was replaced with the accurate comment ("No schema name set here — each BC sets its own schema via ConfigurePolecat() in AddXyzModule()").

---

## Future: multi-BC schema isolation with Polecat named stores

With a single default Polecat store, multiple BC modules calling `ConfigurePolecat()` with different `DatabaseSchemaName` values would conflict — the last registration wins. M1 is safe because Participants is the only Polecat BC. When Settlement and Operations BCs land (M2+), the pattern will need to change to named stores:

```csharp
// Future pattern for Settlement BC
services.AddPolecatStore<ISettlementStore>(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "settlement";
    // ...
});
```

`AddPolecatStore<T>()` and `ConfigurePolecat<T>()` are confirmed present in `Polecat.PolecatStoreServiceCollectionExtensions` (seen in the XML docs). The `connectionString` parameter on `AddXyzModule()` extensions will become load-bearing when this migration happens — it was retained in the signature specifically to avoid a breaking API change at that point.

**Flag for M2 planning:** When the first post-Participants Polecat BC is scaffolded, evaluate whether to:
1. Migrate the Participants BC to a named store at the same time (keeping all Polecat BCs on the same pattern), or
2. Leave Participants on the default store and Settlement/Operations on named stores (two patterns, potential confusion).

Option 1 is cleaner and worth a single-file refactor. Track in M2 planning docs.

---

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Session open (baseline) | 2 | Pass |
| After all changes | 3 | Pass — 1 new Participants smoke test added, 0 regressions |

---

## Build state at session close

- Errors: 0
- Warnings: 0
- Projects in solution: 7 (4 src, 3 tests)
- `Version=` on `<PackageReference>`: 0 occurrences
- Package pins in `Directory.Packages.props`: 12 (unchanged — `Polecat` was already pinned)
- `WolverineFx.Polecat` references in `CritterBids.Participants.csproj`: 0
- `AddPolecat()` calls in solution: 1 (host only)
- `ConfigurePolecat()` calls in solution: 1 (`AddParticipantsModule`)
- `Apply()` methods in `Participant`: 0
- Endpoints in `CritterBids.Participants`: 0
- Commands, events, projections: 0

---

## Key learnings

1. **`ConfigurePolecat()` is Polecat's equivalent of `ConfigureMarten()`** — it exists in `Polecat.PolecatServiceConfigurationExtensions` and appends a post-configuration action to the default store. BC modules should use this, not a second `AddPolecat()` call.

2. **`UseSystemTextJsonForSerialization` does not exist in Polecat 2.x** — Polecat is STJ-only; the method is a Marten API. References to it in skill docs transferred from the Marten skill are incorrect and should be replaced with `ConfigureSerialization(EnumStorage, ...)` when enum storage configuration is actually needed.

3. **Polecat's transitive dependency chain covers the namespaces needed in BC module code** — `Polecat` → `JasperFx` (provides `AutoCreate`) + `JasperFx.Events` (provides `StreamIdentity`). A BC project referencing only `Polecat` can use these enums without additional `<PackageReference>` entries.

4. **Named stores (`AddPolecatStore<T>()`) are the correct multi-BC Polecat pattern** — with a single default store, `ConfigurePolecat()` conflicts on `DatabaseSchemaName` when multiple BCs apply it. M1 avoids the problem (one Polecat BC), but the migration to named stores should be scoped into M2 planning before the second Polecat BC lands.

5. **`connectionString` parameters on `AddXyzModule()` extensions should be retained even when not immediately forwarded** — the parameter becomes load-bearing when named stores are introduced. Dropping it now would require a breaking API change later.

---

## Verification checklist

- [x] `src/CritterBids.Participants/CritterBids.Participants.csproj` exists and is listed in `.slnx`
- [x] `tests/CritterBids.Participants.Tests/CritterBids.Participants.Tests.csproj` exists and is listed in `.slnx`
- [x] A `Participant` aggregate class exists with a public `Guid Id` property and no other members
- [x] A UUID v5 namespace `Guid` constant exists in the Participants project with a clear name, a non-empty, non-sentinel literal value, and an inline comment
- [x] `AddParticipantsModule()` exists as an `IServiceCollection` extension method
- [x] `AddParticipantsModule()` configures Polecat with schema `"participants"`, `AutoCreate.CreateOrUpdate`, and `StreamIdentity.AsGuid`
- [N/A] `AddParticipantsModule()` chains `.IntegrateWithWolverine()` — superseded by the `ConfigurePolecat()` architectural decision; Wolverine integration is at the host level, not the BC module level (see S4d)
- [x] `CritterBids.Api/Program.cs` calls `AddParticipantsModule()` with the SQL Server connection string
- [x] `dotnet build` succeeds with zero errors and zero warnings from new or modified projects
- [x] `dotnet test` reports 3 passing tests (2 existing + 1 new Participants smoke test), zero failing
- [ ] `dotnet run --project src/CritterBids.AppHost --launch-profile http` still boots all four services without error — not verified this session (no infrastructure changes; boot verification deferred; risk assessed as low)
- [x] No `Version=` attribute on any `<PackageReference>` in any `.csproj`
- [x] `docs/milestones/M1-skeleton.md` §9 S4 row updated from `*TBD*` to the prompt filename
- [x] No files created or modified outside the allowed set
- [x] No commands, events, projections, or HTTP endpoints introduced

---

## Open questions / flags for retro session (M1-S7)

| ID | Finding | Disposition |
|----|---------|-------------|
| S4-F1 | `polecat-event-sourcing.md` skill doc incorrectly shows `UseSystemTextJsonForSerialization(EnumStorage.AsString)` — this is a Marten API not present in Polecat 2.x. Replace with `ConfigureSerialization(EnumStorage.AsString)` note and clarify STJ-only status. | M1-S7 skill update. |
| S4-F2 | Multi-BC Polecat schema isolation requires named stores (`AddPolecatStore<T>()`). Single default store conflicts on `DatabaseSchemaName` when multiple BC modules call `ConfigurePolecat()`. Must be resolved before the second Polecat BC (Settlement or Operations) is scaffolded. | M2 planning. Flag in `docs/milestones/M2-*.md` when created. |
| S4-F3 | `AutoCreate.All` does not exist in Polecat 2.x (confirmed by JasperFx XML docs — only `CreateOrUpdate`, `CreateOnly`, `None`). The skill doc already captures this. No action needed unless a future reader expects `AutoCreate.All` from a Marten background. | Already documented in skill file; no additional action. |
| S4-F4 | `Wolverine inbox/outbox schema creation` — `.IntegrateWithWolverine()` wires Polecat as the Wolverine outbox store. With `ConfigurePolecat()` now setting `DatabaseSchemaName = "participants"`, verify at M1-S5 that the Wolverine inbox/outbox tables still land in the `wolverine` schema (not `participants`). The `IntegrateWithWolverine()` docs show a `MessageStorageSchemaName = "wolverine"` default — this should be unaffected, but confirm at first boot with actual schema creation. | M1-S5 boot verification. |
| S4-F5 | `Polecat source generator behavior` — the `CritterBids.Participants` project includes `Polecat` which ships source generators. These generators are activated when `Apply()` methods and event types exist. With no events yet, the generators produce nothing. Verify generator output at M1-S5 when the first event and `Apply()` method appear. | M1-S5. |

## What remains / next session should verify

- **M1-S5** — Slice 0.2: `StartParticipantSession` command, event, handler, endpoint, and tests. Adds `WolverineFx.Polecat` to `CritterBids.Participants.csproj`. Adds first `Apply()` method to `Participant`. Adds `[AllowAnonymous]` per the M1 override convention.
- **S4-F4 boot verification** — Wolverine inbox/outbox schema lands in `wolverine` schema, not `participants`. Confirm at M1-S5 when SQL Server schema creation first runs.
- **`docs/decisions/0001-uuid-strategy.md`** — UUID strategy ADR (Proposed). Deferred to M1-S5 per prompt scope.
