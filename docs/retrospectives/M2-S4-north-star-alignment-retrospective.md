# M2-S4: North Star Alignment & Architecture Pivot — Retrospective

**Date:** 2026-04-15
**Milestone:** M2 — Listings Pipeline
**Slice:** S4 — Architecture pivot; north star alignment; skill + CLAUDE.md refresh
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M2-S4-north-star-alignment.md`

---

## Baseline

- 13 tests passing across four projects: Contracts (1), Api (1), Selling (5), Participants (6)
- `dotnet build` succeeds with 0 errors
- Production Aspire startup throws `InvalidWolverineStorageConfigurationException` — both `AddMarten().IntegrateWithWolverine()` and `AddPolecat().IntegrateWithWolverine()` claim the Wolverine "main" store
- ADR 010 (Wolverine Dual-Store Resolution) in Proposed state, pending JasperFx input
- Participants BC fully implemented with Polecat/SQL Server; six integration tests passing
- Selling BC scaffold in place (ADR 009 shared Marten store pattern)

---

## Items completed

| Item | Description |
|------|-------------|
| S4a | Architecture decision: Option A (All-Marten) chosen; ADR 011 authored |
| S4b | `docs/decisions/README.md` updated — ADR 011 row added, superseded chain updated, next ADR pointer advanced to 012 |
| S4c | `CLAUDE.md` — eight targeted updates reflecting All-Marten topology and canonical bootstrap |
| S4d | `docs/skills/critter-stack-testing-patterns.md` — North Star Test Class Lifecycle section added |
| S4e | `docs/skills/wolverine-message-handlers.md` — `[Entity]` batch-query pattern + `ValidateAsync`/`Validate` railway programming model added |
| S4f | `docs/skills/marten-event-sourcing.md` — `ConfigureMarten()` per-BC note expanded; `AutoApplyTransactions()` placement clarified with north star evidence |
| S4g | `docs/skills/README.md` — `polecat-event-sourcing.md` marked archived/inactive (ADR 011) |
| S4h | `docs/milestones/M2-listings-pipeline.md` §9 — session table updated: S4 is this session; S5–S8 renumbered |
| S4i | `docs/prompts/implementations/M2-S5-slice-1-1-create-draft-listing.md` — new implementation prompt authored |

---

## S4a: Architecture Decision — Option A (All-Marten)

**Decision:** All eight CritterBids BCs use PostgreSQL via Marten (ADR 011). Polecat/SQL Server is removed. ADR 003 (Polecat BCs) is superseded.

**Why Option A was chosen:** The CritterStackSamples north star analysis examined 12 reference projects. Every single sample uses one `AddMarten(...).IntegrateWithWolverine().UseLightweightSessions()` chain — zero exceptions. Zero samples use mixed storage backends. All idiomatic Critter Stack patterns (`IDocumentSession` injection, `AutoApplyTransactions()`, `[Entity]`, `[AggregateHandler]`, `CleanAllMartenDataAsync()`) depend on the primary store being Marten. CritterBids exists to demonstrate these patterns uniformly. With Polecat in three BCs, those BCs cannot use any of these patterns. Option A removes that constraint entirely.

**Why Option B was rejected:** Splitting into two deployable hosts partially supersedes ADR 001 (Modular Monolith), which chose the single-host model specifically to avoid microservices operational overhead for a conference demo on a single VPS. Zero CritterStackSamples microservices examples use mixed storage — the microservices sample is also Marten-only. The `ISellerRegistrationService` in-process seam becomes a network call under Option B with no corresponding benefit at this project phase.

**Why Option C was rejected:** The project is not waiting for a JasperFx fix. This was already decided before this session; it is recorded in ADR 011 for completeness of the decision record.

**ADR status changes:**

| ADR | Before | After |
|-----|--------|-------|
| ADR 003 (Polecat BCs) | Accepted | Superseded by ADR 011 |
| ADR 010 (Dual-Store Resolution) | Proposed — Pending JasperFx | Resolved — superseded by ADR 011 |
| ADR 011 (All-Marten Pivot) | — | Accepted |

---

## S4c: CLAUDE.md Updates

Eight targeted changes — no sections rewritten, only what the architecture decision required:

| Change | Location | Rationale |
|--------|----------|-----------|
| "Wolverine + Marten + Polecat" → "Wolverine + Marten + PostgreSQL" | Intro line | Polecat no longer in use |
| "PostgreSQL (Marten) for auction-core BCs; SQL Server (Polecat) for…" → "All eight BCs use PostgreSQL via Marten (ADR 011)" | Quick Start §1 bullet 3 | Reflects new topology |
| Removed "SQL Server" from Aspire provisioning description | Quick Start §2 | SQL Server container no longer provisioned |
| Removed "Event sourcing (SQL Server) \| Polecat 2+" row | Preferred Tools table | Not currently in use |
| `AutoApplyTransactions()` wording updated | Core Conventions | See S4f below |
| Added canonical bootstrap callout | After Core Conventions | North star alignment |
| Updated Participants, Settlement, Operations storage | BC Module Quick Reference | All → PostgreSQL / Marten |
| Removed "Event-sourced aggregate (Polecat)" row | Skill Invocation Guide | Polecat skill archived |

**Participants row note:** The table notes "migration pending — ADR 011" for Participants because the Polecat code still exists in `src/CritterBids.Participants/` and will be replaced in a subsequent session.

---

## S4d–S4f: Skill Doc Updates

### `critter-stack-testing-patterns.md`

New section "North Star Test Class Lifecycle" added before the existing "Marten BC TestFixture Pattern" section. Content confirmed from CritterStackSamples §11:

- `IAsyncLifetime` on every test class — identical pattern across all 12 samples
- `CleanAllMartenDataAsync()` in `InitializeAsync()` — the canonical cleanup call; called before the class runs, not after
- `_host.DocumentStore().LightweightSession()` for direct Marten access
- `AppFixture` + `ICollectionFixture<AppFixture>` — already present in the existing fixture sections; noted as north star–confirmed
- `DisableAllExternalWolverineTransports()` — already in the fixture; noted as required in the lifecycle

What was already correct: the `SellingTestFixture` shape, the collection fixture pattern, `ExecuteAndWaitAsync`, `TrackedHttpCall`, `CleanAllMartenDataAsync()` vs `ResetAllMartenDataAsync()` distinction. These received "confirmed by north star analysis" context in the new section.

### `wolverine-message-handlers.md`

`[Entity]` section expanded beyond the brief "Marten BC" entry that existed. Added:

- Batch-query behaviour: multiple `[Entity]` parameters trigger one Marten batch query, not N sequential `LoadAsync` calls
- `OnMissing = OnMissing.ProblemDetailsWith400`: distinguishes bad client input (400) from missing resource (404) — appropriate when the ID comes from the client payload
- How `[Entity]`-loaded entities flow into synchronous `Validate` without an async DB call

New subsection `ValidateAsync` and `Validate` — Railway Programming Pre-Handler Pattern added:

- `ValidateAsync` for async uniqueness/existence queries
- `Validate` (sync) for business rules against already-loaded aggregate/entity state
- `WolverineContinue.NoProblems` as the continue sentinel
- `Handle()` is always the happy path — no conditionals, no error paths
- Wolverine call order: FluentValidation → `Validate`/`ValidateAsync` → `Handle()`

What was already correct: `Before/ValidateAsync` under "Async External Validation", `ProblemDetails` return type, `WolverineContinue.NoProblems` usage. The new section provides the railway programming framing and batch-load specifics that the existing entry lacked.

### `marten-event-sourcing.md`

`AutoApplyTransactions()` placement clarified explicitly:

- It belongs in `UseWolverine()` globally in `Program.cs` — confirmed by all 12 CritterStackSamples
- It must NOT be inside a BC's `services.ConfigureMarten()` call
- The M2 milestone doc §6 (pre-ADR 011) showed it in the `AddMarten()` lambda per-BC — that placement was incorrect
- The existing `SellingTestFixture.cs` does not include it in the test's `AddMarten()` precisely because the host's `UseWolverine()` covers it

Standard BC Module Pattern note expanded to name:
- `opts.Schema.For<T>().DatabaseSchemaName("bc-name")` — BC schema assignment
- `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` — snapshot registration placement

---

## S4g: Skills README — Polecat Archived

`polecat-event-sourcing.md` status changed from `✅ Complete` to `⚠️ Archived — inactive (ADR 011)`.
Removed from the "Skills by Task" implementation table. The file itself is retained — its content
documents valid Polecat patterns that remain useful reference material for any future
Polecat-showcase project.

`marten-named-stores.md` was already archived (superseded by ADR 009) — no change needed.

---

## S4h: M2 Milestone Session Table Renumbering

| # | Old slot | New slot | Notes |
|---|---------|---------|-------|
| S4 | `M2-S4-slice-1-1-create-draft-listing.md` | `M2-S4-north-star-alignment.md` | This session |
| S5 | `M2-S5-slice-1-2-submit-listing.md` | `M2-S5-slice-1-1-create-draft-listing.md` | Renumbered |
| S6 | `M2-S6-listings-bc-and-read-paths.md` | `M2-S6-slice-1-2-submit-listing.md` | Renumbered |
| S7 | `M2-S7-retrospective-skills-m2-close.md` | `M2-S7-listings-bc-and-read-paths.md` | Renumbered |
| S8 | — | `M2-S8-retrospective-skills-m2-close.md` | New slot for M2 close |

M2 total session count: 7 → 8. Dependency graph updated accordingly.

The S2 scope note for `AutoApplyTransactions()` in the session table was updated to remove the
inaccurate per-BC mention (consequence of the S4f clarification).

---

## S4i: M2-S5 Prompt Authored

`docs/prompts/implementations/M2-S5-slice-1-1-create-draft-listing.md` authored against the corrected all-Marten
architecture. Key elements:

- Context references ADR 011, ADR 009, `marten-event-sourcing.md`, `wolverine-message-handlers.md`
- Scope: `DraftListingCreated`, `SellerListing.Apply(DraftListingCreated)`, `ListingValidator` (14 rules), `POST /api/listings/draft`, `ISellerRegistrationService` gate
- `[AllowAnonymous]` on the endpoint per M2 milestone §6 stance
- `ConfigureMarten()` contribution for `SellerListing` stream type in `AddSellingModule()`
- `AutoApplyTransactions()` explicitly noted as already in `UseWolverine()` — do not duplicate in `ConfigureMarten()`
- Acceptance criteria: 5 aggregate + 14 pure-function + 2 API gateway tests; total 34 passing
- Open question: Participants BC Polecat migration is a separate session and not a dependency for S5

---

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Baseline (before session) | 13 / 13 | ✅ All passing |
| After all documentation changes | 13 / 13 | ✅ Unchanged — no code changes in scope |

---

## Build state at session close

- `dotnet build` exits with 0 errors, 0 warnings
- No `.cs`, `.csproj`, `.slnx`, or `.props` files created or modified — session is documentation-only
- `session.Events.Append()` calls: 0 (no code changed)
- `AddPolecat()` calls in production code: 1 (unchanged — Participants BC migration is S5-adjacent, separate session)
- `PolecatOps.StartStream()` calls: 1 (unchanged — same)
- Production Aspire startup crash: **resolved by architecture decision** — the dual-store scenario is eliminated once the Participants BC migration lands. This session commits the decision; the code fix follows in the migration session.

---

## Key learnings

1. `AutoApplyTransactions()` belongs in `UseWolverine()` globally, not in per-BC `ConfigureMarten()` calls. Every CritterStackSample confirms this. The M2 milestone doc §6 showed it in the wrong place; that document was written before ADR 009 resolved the single-store pattern. The skill doc now explicitly corrects this discrepancy.

2. `[Entity]` with multiple parameters in the same signature is a batch-query optimisation — one Marten round-trip, not N. `OnMissing.ProblemDetailsWith400` is the correct variant when the referenced entity's ID comes from the client: a missing entity means bad input, not a missing resource.

3. Zero of the 12 CritterStackSamples use mixed storage backends. The uniformity finding is absolute. This is the decisive evidence for Option A — not an argument from simplicity but from the reference implementation's own consistent practice.

4. Option A preserves ADR 001 (Modular Monolith) entirely. Only the storage registration changes; all BC project isolation, `CritterBids.Contracts` integration events, and `AddXyzModule()` patterns are unaffected.

5. The "Polecat ↔ Marten swap" stretch goal from ADR 003 is fulfilled by this session itself — storage engine migration is a registration-level concern, not a business logic refactor.

---

## Verification checklist

- [x] `docs/decisions/011-all-marten-pivot.md` exists with status Accepted
- [x] ADR 011 explicitly evaluates all three options with rejection rationale for B and C
- [x] ADR 011 names which prior ADRs it supersedes (ADR 003; ADR 010 resolved)
- [x] `docs/decisions/README.md` includes the ADR 011 row
- [x] `CLAUDE.md` BC Quick Reference table reflects all-Marten (no SQL Server / Polecat entries)
- [x] `CLAUDE.md` contains canonical three-call bootstrap sequence and north star analysis file path
- [x] `docs/skills/critter-stack-testing-patterns.md` contains North Star Test Class Lifecycle section with `IAsyncLifetime`, `CleanAllMartenDataAsync()`, and `DisableAllExternalWolverineTransports()`
- [x] `docs/skills/wolverine-message-handlers.md` documents `[Entity]` batch-loading pattern and `ValidateAsync`/`Validate` railway programming model
- [x] `docs/skills/marten-event-sourcing.md` confirms `ConfigureMarten()` per-BC pattern and `AutoApplyTransactions()` placement (with explicit correction of the M2 milestone doc discrepancy)
- [x] `docs/skills/README.md` updated — `polecat-event-sourcing.md` marked archived/inactive
- [x] `docs/milestones/M2-listings-pipeline.md` §9 session table updated with S4 (this session) and S5–S8 renumbering
- [x] `docs/prompts/implementations/M2-S5-slice-1-1-create-draft-listing.md` exists, follows the prompt template, references ADR 011 and updated skill docs
- [x] `dotnet build` passes with 0 errors, 0 warnings
- [x] `dotnet test` passes with 13/13
- [x] No `.csproj`, `.cs`, `.slnx`, `.props`, or source files created or modified

---

## What remains / next session should verify

- **Participants BC migration** — `src/CritterBids.Participants/` still uses Polecat. The production Aspire crash is resolved by architecture decision; the code change lands in a dedicated migration session prompted separately from S5. S5 (`CreateDraftListing`) does not depend on the migration — `ISellerRegistrationService` is a Selling BC Marten concern.
- **`docs/milestones/M2-listings-pipeline.md` §5 Infrastructure section** — still references the M2-D1 decision note about ADR 008 named stores. That section was written before ADR 009. A future pass (M2-S8 retro/skills close) should update §5's module pattern example to match the ADR 009 + ADR 011 shape.
- **`docs/milestones/M2-listings-pipeline.md` open question M1-deferred: S4-F2** — "Named Polecat stores: Still deferred." This is now resolved (ADR 011); the milestone doc open-questions table can be updated in M2-S8.
