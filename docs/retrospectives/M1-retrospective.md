# M1 — Skeleton — Retrospective

**Date:** 2026-04-14
**Milestone:** M1 — Skeleton
**Sessions:** S1–S7 (7 sessions)
**Milestone doc:** `docs/milestones/M1-skeleton.md`

---

## Baseline vs. Exit State

### Before S1 (repo empty)

- Empty repository with no source code
- No solution file, no projects, no CI
- No infrastructure configuration, no BC structure

### After S7 (M1 close)

- Solution builds clean: 0 errors, 0 warnings
- 8 integration tests passing: 1 smoke (Contracts), 1 smoke (Api), 1 smoke (Participants), 2 StartSession integration, 3 RegisterAsSeller integration
- `POST /api/participants/session` and `POST /api/participants/{id}/register-seller` operational
- Participants BC fully event-sourced with Polecat/SQL Server
- `SellerRegistrationCompleted` integration event published via `OutgoingMessages` to Wolverine outbox
- `.NET Aspire` AppHost provisions Postgres + SQL Server + RabbitMQ and launches API with connection strings injected
- Skill library updated: `polecat-event-sourcing.md` ✅, `aspire.md` ✅, `wolverine-message-handlers.md` +AP#14, `critter-stack-testing-patterns.md` fixture corrected

**Solution layout at close:**

```
src/
  CritterBids.AppHost/          # .NET Aspire orchestration
  CritterBids.Api/              # API host, Program.cs
  CritterBids.Contracts/        # SellerRegistrationCompleted integration event
  CritterBids.Participants/     # Participants BC — event-sourced with Polecat
tests/
  CritterBids.Api.Tests/        # 1 smoke test
  CritterBids.Contracts.Tests/  # 1 smoke test
  CritterBids.Participants.Tests/ # 1 smoke + 5 integration tests
```

---

## Key Decisions Made

| ID | Decision | Status |
|---|---|---|
| M1-D1 | Auth scheme | **Deferred.** `[AllowAnonymous]` on all M1 endpoints. Real auth in M6. |
| M1-D3 | UUID v7 for event row IDs | **Leaning adopt; ADR Proposed.** `docs/decisions/0001-uuid-strategy.md`. v5 stays for stream IDs; v7 under consideration for high-write tables. Acceptance gates unmet — deferred to M3. |
| M1-D4 | Polecat namespace GUID for stream IDs | **Resolved (S4):** `StartParticipantSession` uses UUID v7 (no natural business key; v5 determinism doesn't apply). |
| — | Infrastructure orchestration | **ADR 006:** Aspire over Docker Compose. `docs/decisions/006-infrastructure-orchestration.md`. |
| — | Module architecture | Module owns `AddPolecat().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()` (not host). Established S5. |
| — | Domain event vs. integration event naming | `{Noun}ed` for BC aggregate event; `{Noun}Completed` for outgoing contract. Established S6. |

**ADRs authored in M1:** `docs/decisions/006-infrastructure-orchestration.md`, `docs/decisions/0001-uuid-strategy.md`.

---

## Session Timeline

| Session | Prompt | Deliverable |
|---|---|---|
| S1 | `M1-S1-solution-baseline.md` | Solution file, project scaffolding, Layout 2 test structure, `Directory.Packages.props` |
| S2 | `M1-S2-infrastructure-orchestration-adr.md` | ADR 006: Aspire over Docker Compose (docs only) |
| S3 | `M1-S3-infrastructure-baseline.md` | `CritterBids.AppHost` with Postgres + SQL Server + RabbitMQ; Wolverine + Polecat host config |
| S4 | `M1-S4-participants-bc-scaffold.md` | Participants BC scaffold — empty `Participant` aggregate, `AddParticipantsModule()`, UUID v5 namespace constant |
| S5 | `M1-S5-slice-0-2-start-participant-session.md` | Slice 0.2 — `StartParticipantSession`, 2 integration tests, module architecture revision |
| S6 | `M1-S6-slice-0-3-register-as-seller.md` | Slice 0.3 — `RegisterAsSeller`, 3 integration tests, `SellerRegistrationCompleted` via outbox |
| S7 | `M1-S7-retrospective-skills-m1-close.md` | Skill library updates, S4-F4 schema verification, M1 retrospective (this document) |

---

## Key Learnings

Three findings from M1 with the highest carry-forward value for M2+:

### 1. `OutgoingMessages` requires a host-level routing rule before outbox assertions work

`tracked.Sent.MessagesOf<T>()` in integration tests will always return 0 unless `opts.Publish(x => x.Message<T>()...)` is configured in the host's Wolverine options (`Program.cs`). Without a routing rule, `PublishAsync` calls `NoRoutesFor()` and returns immediately — the message never reaches any `ISendingAgent`. This is a host configuration requirement, not a test-fixture concern. Every session that introduces a new integration event type must include `Program.cs` in its allowed-file set.

Reference: `docs/skills/wolverine-message-handlers.md` Anti-Pattern #14.

### 2. Single domain events from `[WriteAggregate]` handlers are returned directly — no `Events()` wrapper

`Wolverine.Polecat.Events` constructor takes `IEnumerable<object>`. Passing a single domain event to `new Events(evt)` causes `CS1503: cannot convert 'TEvent' to 'IEnumerable<object>'`. Return the domain event type directly in the tuple: `(IResult, SellerRegistered, OutgoingMessages)`. Wolverine/Polecat recognizes the domain event type and appends it automatically. The `Events` collection is only needed for multi-event handlers.

Reference: `docs/skills/polecat-event-sourcing.md` §Single-Event Return Type.

### 3. Module owns the full Polecat chain; `ConfigureServices` is the correct test override hook

The BC module (`AddParticipantsModule()`) owns `AddPolecat().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()`. `ApplyAllDatabaseChangesOnStartup()` is on `PolecatConfigurationExpression` (the builder), not inside `StoreOptions`. Tests override the connection string via `services.ConfigurePolecat(opts => { opts.ConnectionString = ... })` in `ConfigureServices` — never `ConfigureAppConfiguration`, which arrives too late.

Reference: `docs/skills/critter-stack-testing-patterns.md` §Polecat BC TestFixture Pattern, `docs/skills/polecat-event-sourcing.md` §Standard BC Module Pattern.

---

## S4-F4: Schema Verification (M1-S7)

Verified via Testcontainers integration test (diagnostic, not committed). SQL Server schema query confirmed:

**`participants` schema (Polecat event store):**
- `pc_events` — event log
- `pc_streams` — stream metadata
- `pc_event_progression` — async daemon progress marker

**`wolverine` schema (Wolverine inbox/outbox):**
- `wolverine_incoming_envelopes`
- `wolverine_outgoing_envelopes`
- `wolverine_dead_letters`
- `wolverine_nodes`, `wolverine_node_assignments`, `wolverine_node_records`
- `wolverine_control_queue`, `wolverine_agent_restrictions`

Schema separation is complete. Polecat tables never appear in the `wolverine` schema; Wolverine tables never appear in the `participants` schema. The `IntegrateWithWolverine()` default schema name is `"wolverine"` and is confirmed operative.

---

## Deferred Items

| Item | Tracking | When to address |
|---|---|---|
| S4-F2: Named Polecat stores | M2 planning | When Settlement or Operations BC is scaffolded alongside Participants |
| `0001-uuid-strategy.md` promotion gates | ADR stays Proposed | When Marten 8/Polecat 2 capability check + JasperFx team input complete (M3, Auctions BC) |
| `Directory.Build.props` orphaned `WolverineVersion`/`MartenVersion`/`AlbaVersion` | Maintenance PR | Low priority; no runtime impact |
| `[AllowAnonymous]` override removal | M2 | Remove from all Participants endpoints; add real auth scheme |
| `adding-bc-module.md` skill | Write when first needed | When second BC (Selling, M2) is scaffolded |
| `domain-event-conventions.md` skill | Write when first needed | Any M2 session authoring new domain events |

---

## M1 Exit Criteria Verification

From `docs/milestones/M1-skeleton.md` §1:

- [x] Solution builds clean with `dotnet build` — 0 errors, 0 warnings
- [x] `dotnet run --project src/CritterBids.AppHost` boots API + Postgres + SQL Server + RabbitMQ — verified by Aspire AppHost construction; not re-run at S7 close but confirmed operational at S6
- [x] Participants BC implemented: `StartParticipantSession` and `RegisterAsSeller` commands, events, HTTP endpoints, Polecat storage
- [x] `SellerRegistrationCompleted` published via `OutgoingMessages` (no M1 consumer)
- [x] All 5 integration tests from §7 pass against real Polecat + SQL Server via Testcontainers
- [x] `docs/skills/aspire.md` authored — ✅ this session
- [x] `docs/skills/polecat-event-sourcing.md` 🟡 → ✅ — ✅ this session
- [x] Aspire MCP discovery note committed — noted in `aspire.md` §Aspire MCP Discovery Note (no candidates observed in M1; deferred)
- [x] `docs/decisions/0001-uuid-strategy.md` ADR authored as Proposed — ✅ S5
- [x] M1 retrospective doc written — ✅ this document

All exit criteria met. M1 is closed.

---

## Session Retrospective References

For per-session detail (build errors resolved, structural metrics, test phases), see:

- `docs/retrospectives/M1-S1-solution-baseline.md`
- `docs/retrospectives/M1-S2-infrastructure-orchestration-adr.md`
- `docs/retrospectives/M1-S3-infrastructure-baseline.md`
- `docs/retrospectives/M1-S4-participants-bc-scaffold.md`
- `docs/retrospectives/M1-S5-slice-0-2-start-participant-session.md`
- `docs/retrospectives/M1-S6-slice-0-3-register-as-seller.md`
- `docs/retrospectives/M1-S7-retrospective-skills-m1-close.md` (session retro for this session)
