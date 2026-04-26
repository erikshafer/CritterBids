# M1-S7: Retrospective Skills and M1 Close — Retrospective

**Date:** 2026-04-14
**Milestone:** M1 — Skeleton
**Slice:** S7 — Retrospective skills, schema verification, M1 close
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M1-S7-retrospective-skills-m1-close.md`

## Baseline

- Solution builds clean from S6 close; 8 tests passing (5 existing + 3 `RegisterAsSellerTests`).
- `docs/skills/polecat-event-sourcing.md` at 🟡 placeholder status with unfilled implementation checklist.
- `docs/skills/wolverine-message-handlers.md` at 13 anti-patterns; routing rule requirement missing.
- `docs/skills/critter-stack-testing-patterns.md` Polecat fixture code differed from actual `ParticipantsTestFixture.cs`.
- `docs/skills/aspire.md` did not exist.
- `docs/milestones/M1-skeleton.md` §9 S7 row was `*TBD*`.
- S4-F4 schema verification still outstanding (deferred from S5 and S6).

---

## Items completed

| Item | Description |
|------|-------------|
| S7a | S4-F4 schema verification — Testcontainers diagnostic test confirmed schema separation |
| S7b | `docs/skills/polecat-event-sourcing.md` — checklist annotated; single-event tuple pattern; OnMissing; `UseSystemTextJsonForSerialization` absence; `ApplyAllDatabaseChangesOnStartup` placement |
| S7c | `docs/skills/wolverine-message-handlers.md` — Anti-Pattern #14: OutgoingMessages routing rule requirement |
| S7d | `docs/skills/critter-stack-testing-patterns.md` — outbox assertion prerequisite note; Polecat fixture corrected to match actual `ParticipantsTestFixture.cs` |
| S7e | `docs/skills/aspire.md` — new skill file authored retrospectively from M1 S1–S4 |
| S7f | `docs/skills/README.md` — `polecat-event-sourcing.md` 🟡 → ✅; `aspire.md` row added |
| S7g | `docs/retrospectives/M1-retrospective.md` — M1 milestone retrospective written |
| S7h | `docs/milestones/M1-skeleton.md` §9 S7 row updated from `*TBD*` to prompt filename |

---

## S7a: Schema Verification (S4-F4)

Added a temporary diagnostic test (`SchemaVerificationTests.cs`) that:
1. Creates a dedicated `MsSqlBuilder` Testcontainers SQL Server instance
2. Boots `AlbaHost.For<Program>` with `ConfigurePolecat` override — triggering `ApplyAllDatabaseChangesOnStartup()`
3. Queries `sys.tables` + `sys.schemas` filtered to `participants` and `wolverine`
4. Asserts table placement and outputs the full schema/table list

The diagnostic test was run to capture the result and then deleted. The result is documented in `docs/retrospectives/M1-retrospective.md` §S4-F4.

**Confirmed table placement:**

| Schema | Tables |
|---|---|
| `participants` | `pc_events`, `pc_streams`, `pc_event_progression` |
| `wolverine` | `wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`, `wolverine_dead_letters`, `wolverine_nodes`, `wolverine_node_assignments`, `wolverine_node_records`, `wolverine_control_queue`, `wolverine_agent_restrictions` |

Schema separation is complete. S4-F4 closed.

---

## S7b: `polecat-event-sourcing.md` Updates

### Corrections to existing content

- **`UseSystemTextJsonForSerialization`** — annotated as absent from Polecat 2.x API. Polecat uses System.Text.Json exclusively; there is no configuration method for serialization format. Previous skill content listed this call in the standard module pattern — now corrected with an inline `⚠️` comment.
- **`opts.Connection()` vs `opts.ConnectionString`** — confirmed from `ParticipantsModule.cs` that the property form (`opts.ConnectionString = connectionString`) is used. The `Connection()` method is listed as unverified; the property form is annotated as `✅ confirmed`.
- **`ApplyAllDatabaseChangesOnStartup()` placement** — annotated that it chains on `PolecatConfigurationExpression` (the builder), not inside the `StoreOptions` lambda. Module pattern updated with inline notes.

### New content added

- **Single-event return type** — documented the `Events()` wrapper build error (`CS1503`) and the correct pattern: return the domain event type directly in the tuple.
- **`OnMissing.Simple404` before `Before()`** — documented that when no event stream exists, Wolverine returns 404 before calling `Before()`. `Before()` therefore receives a guaranteed non-null aggregate.
- **Implementation checklist** — all items annotated: `✅ confirmed`, `N/A (M1)`, or `⚠️ gotcha` as appropriate.

---

## S7c: `wolverine-message-handlers.md` — Anti-Pattern #14

Added after Anti-Pattern #13 (the last in the sequence). Documents:

- Root cause: `PublishAsync` calls `Runtime.RoutingFor(type).RouteForPublish()`. With no route, returns empty array → `NoRoutesFor()` → returns `ValueTask.CompletedTask`. Message never reaches `_outstanding` or any `ISendingAgent`.
- Resolution: `opts.Publish(x => x.Message<T>().ToLocalQueue(...))` in `Program.cs` (or equivalent transport rule).
- M1 placeholder pattern: local queue with no handler is safe (Wolverine records `NoHandlers` + `MessageSucceeded`, no exception).
- Host configuration requirement, not a fixture concern.

---

## S7d: `critter-stack-testing-patterns.md` Updates

### Outbox assertion prerequisite note

Added a `⚠️` callout directly before the "Testing Integration Message Publishing" code example. Points to Anti-Pattern #14 in `wolverine-message-handlers.md`.

### Polecat BC TestFixture Pattern — corrections vs. actual implementation

Compared skill doc code with `tests/CritterBids.Participants.Tests/Fixtures/ParticipantsTestFixture.cs`. Corrections made:

| Aspect | Skill doc (before) | Actual (after correction) |
|---|---|---|
| `MsSqlBuilder` constructor | `new MsSqlBuilder().WithImage("image:tag")` | `new MsSqlBuilder("image:tag")` — constructor form; `.WithImage()` is obsolete (CS0618 in Testcontainers 4.x) |
| Connection override | `services.AddPolecat(opts => opts.Connection(...))` | `services.ConfigurePolecat(opts => { opts.ConnectionString = ...; })` — `AddPolecat` would create a competing store registration |
| Auth setup | Auth handler + AddDefaultAuthHeader() | Absent — M1 uses `[AllowAnonymous]` on all Participants endpoints |
| `DocumentStore()` | `Host.Services.GetRequiredService<IDocumentStore>()` | `Host.Services.DocumentStore()` — extension method on `IServiceProvider` |
| `CleanAllPolecatDataAsync()` | `Host.CleanAllPolecatDataAsync()` | `Host.Services.CleanAllPolecatDataAsync()` — on `IServiceProvider` |
| `TrackActivity()` + `ExecuteAndWaitAsync()` | `Host.TrackActivity(...)` | `Host.Services.TrackActivity(...)` — on `IServiceProvider` |
| `TrackedHttpCall()` | `Host.ExecuteAndWaitAsync(...)` | `Host.Services.ExecuteAndWaitAsync(...)` — on `IServiceProvider` |

Updated Polecat-specific testing helpers section to document `IServiceProvider` requirement consistently.

---

## S7e: `docs/skills/aspire.md` — New Skill

Authored retrospectively from M1 S1–S4 source inspection. Content:

- AppHost project structure and SDK reference
- Provisioning PostgreSQL, SQL Server, and RabbitMQ resources
- `WithReference()` / `WaitFor()` pattern and connection string key convention
- Connection string injection into `Program.cs` and BC modules
- Local dev workflow commands and dashboard URL
- Integration testing note: Testcontainers replaces Aspire; `ConfigureServices` (not `ConfigureAppConfiguration`) for overrides
- Schema creation timing gotcha: `AutoCreate.CreateOrUpdate` is lazy; `ApplyAllDatabaseChangesOnStartup()` is required for test fixtures
- Conditional RabbitMQ registration pattern
- Aspire MCP discovery note: no candidates observed in M1; deferred

---

## Test results

| Phase | Participants Tests | Solution Total | Result |
|---|---|---|---|
| Session open (S6 close) | 6 (1 smoke + 2 StartSession + 3 RegisterAsSeller) | 8 | All pass |
| S7 diagnostic (schema verification) | 1 (separate run, diagnostic only) | — | Pass — schema confirmed |
| S7 close (diagnostic removed) | 6 | 8 | All pass, 0 fail |

---

## Build state at session close

- Errors: 0
- Warnings: 0
- Files modified: `docs/skills/polecat-event-sourcing.md`, `docs/skills/wolverine-message-handlers.md`, `docs/skills/critter-stack-testing-patterns.md`, `docs/skills/README.md`, `docs/milestones/M1-skeleton.md`
- Files created: `docs/skills/aspire.md`, `docs/retrospectives/M1-retrospective.md`, `docs/retrospectives/M1-S7-retrospective-skills-m1-close.md` (this file)
- Files created and deleted: `tests/.../SchemaVerificationTests.cs` (diagnostic — not committed)
- No source code files modified

---

## Key learnings

1. **Polecat helper extensions are on `IServiceProvider`, not `IHost`.** `CleanAllPolecatDataAsync()`, `ResetAllPolecatDataAsync()`, `DocumentStore()`, `TrackActivity()`, and `ExecuteAndWaitAsync()` are all extension methods on `IServiceProvider`. Use `Host.Services.*` in fixture code, not `Host.*`. The skill doc placeholder had this wrong; corrected against the actual `ParticipantsTestFixture.cs`.

2. **`ConfigurePolecat` is the correct fixture override hook, not `AddPolecat`.** Using `services.AddPolecat(...)` in `ConfigureServices` creates a second, competing store registration. `services.ConfigurePolecat(...)` adds to the `IOptions<PolecatOptions>` chain and correctly overrides only the connection string while preserving all other module settings.

3. **`MsSqlBuilder` constructor form (`new MsSqlBuilder("image:tag")`) is required in Testcontainers 4.x.** The no-arg constructor with `.WithImage()` is marked `[Obsolete]` and produces CS0618. Pass the image tag to the constructor directly.

---

## Verification checklist

- [x] `docs/skills/polecat-event-sourcing.md` implementation checklist annotated (all reachable M1 items)
- [x] Single-event tuple return pattern documented (not wrapped in `Events()`)
- [x] `[WriteAggregate]` `OnMissing.Simple404` before `Before()` behavior documented
- [x] Anti-Pattern #14 added to `wolverine-message-handlers.md`
- [x] Outbox assertion prerequisite note added to `critter-stack-testing-patterns.md`
- [x] Polecat BC TestFixture Pattern corrected to match actual `ParticipantsTestFixture.cs`
- [x] `docs/skills/aspire.md` created and covers all required content areas
- [x] `docs/skills/README.md` updated: `polecat-event-sourcing.md` → ✅; `aspire.md` row added
- [x] S4-F4 verified: schema separation confirmed via Testcontainers diagnostic; result in M1 retrospective
- [x] `docs/retrospectives/M1-retrospective.md` written
- [x] `docs/milestones/M1-skeleton.md` §9 S7 row updated from `*TBD*` to prompt filename
- [x] `dotnet build` succeeds with 0 errors and 0 warnings
- [x] `dotnet test` reports 8 passing tests, 0 failing
- [x] No files modified outside `docs/skills/`, `docs/retrospectives/`, `docs/milestones/M1-skeleton.md`

---

## What remains / next session should verify

- **M2 planning** — Selling BC, `SellerRegistrationCompleted` consumer, `RegisteredSellers` projection. When Selling BC lands, replace `Program.cs` local queue rule with RabbitMQ exchange routing.
- **S4-F2** — Named Polecat stores (`AddPolecatStore<T>()`) when Settlement or Operations BC is scaffolded alongside Participants.
- **`[AllowAnonymous]` removal** — M2 override is in effect for M1 only; remove at M2 start.
- **`adding-bc-module.md` skill** — write when Selling BC scaffold session is prompted.
- **`domain-event-conventions.md` skill** — write when next domain events are authored.
