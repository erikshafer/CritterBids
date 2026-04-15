# M2-S4: North Star Alignment & Architecture Pivot

**Milestone:** M2 — Listings Pipeline  
**Slice:** S4 — Architecture pivot; north star alignment; skill + CLAUDE.md refresh  
**Agent:** @PSA  
**Estimated scope:** one PR, ~1 new ADR + CLAUDE.md update + 3 skill doc updates + 1 new session prompt  

---

## Background (read before everything else)

CritterBids is blocked. When both the PostgreSQL and SQL Server connection strings are present —
the production state under .NET Aspire — the host throws on startup:

```
InvalidWolverineStorageConfigurationException: There must be exactly one message store tagged as
the 'main' store. Found multiples:
  wolverinedb://sqlserver/127.0.0.1/master/wolverine,
  wolverinedb://postgresql/127.0.0.1/postgres/wolverine
```

ADR 010 (`docs/decisions/010-wolverine-dual-store-resolution.md`) documents the full investigation.
The short version: `AddMarten().IntegrateWithWolverine()` and `AddPolecat().IntegrateWithWolverine()`
both unconditionally register as the Wolverine "main" message store. Wolverine requires exactly one.
Polecat 5.30.0 has no ancillary-store API equivalent to Marten's. The blocker cannot be resolved
within current API constraints without modifying BC module files or waiting for a JasperFx framework
change.

**The project is not waiting for a JasperFx fix.**

This session makes the architecture decision that unblocks M2 and all subsequent work. The decision
is scoped by a second constraint: CritterBids must achieve the same uniformity of setup, configuration,
and handler patterns across all bounded contexts — the same level of consistency demonstrated in the
CritterStackSamples reference repository. That consistency is only achievable with a single primary
Marten store.

The new session prompt for M2-S5 (what was previously M2-S4: CreateDraftListing) is also a
deliverable of this session, authored against the corrected assumptions once the architecture
decision is committed.

---

## Goal

Produce four things in a single PR:

1. **ADR 011** — the architecture pivot decision (what changes from ADR 001's modular monolith
   stance, or confirmation that the monolith is preserved with a storage layer simplification)

2. **Updated CLAUDE.md** — reflect the new storage topology, canonical bootstrap pattern aligned
   with the north star, and any BC Quick Reference changes

3. **Three targeted skill doc updates** — propagate the most important north-star patterns from
   the CritterStackSamples analysis into the skill files that CritterBids agents load most often

4. **`docs/prompts/M2-S5-slice-1-1-create-draft-listing.md`** — the implementation session prompt
   for CreateDraftListing, authored against the corrected architecture

At session close: the production Aspire crash is architecturally resolved (though code changes for
Participants BC migration come in a subsequent session), CLAUDE.md and skill docs reflect the north
star, and M2-S5 is ready to execute.

---

## Context to load

Load these files in this order before doing any analysis or drafting:

1. **`C:\Code\JasperFx\critter-stack-samples-analysis.md`** — the CritterStackSamples north star
   analysis. This is the primary reference for this entire session. Read it fully before proceeding.
   Pay particular attention to sections 1 (canonical bootstrap), 2 (Marten configuration), 4
   (Wolverine HTTP patterns), 5 (validation patterns), 11 (Alba testing), and 14 (cross-cutting
   similarities). These sections define what "uniform" looks like across the reference samples.

2. **`docs/decisions/010-wolverine-dual-store-resolution.md`** — the blocking ADR. The research is
   complete; this session accepts its findings and makes a decision rather than re-investigating.

3. **`docs/decisions/001-modular-monolith.md`** — the original architecture decision this session
   may be updating or superseding.

4. **`docs/decisions/003-polecat-bcs.md`** — the decision that assigned Participants, Settlement,
   and Operations to Polecat/SQL Server. This session may supersede it.

5. **`docs/decisions/009-shared-marten-store.md`** — the ADR that established the shared primary
   Marten store pattern and the `ConfigureMarten()` per-BC contribution model. This is the
   foundational pattern this session builds on.

6. **`CLAUDE.md`** — current state; the BC Quick Reference table and bootstrap conventions are
   the primary sections to update.

7. **`docs/milestones/M2-listings-pipeline.md`** — specifically §9 (Session Breakdown) and the
   BC list; the session table needs updating to reflect S4 becoming this session and original S4
   through S7 shifting by one.

8. **`docs/skills/critter-stack-testing-patterns.md`** — first skill doc to update.

9. **`docs/skills/wolverine-message-handlers.md`** — second skill doc to update.

10. **`docs/decisions/README.md`** — ADR index; verify the next available ADR number before
    creating ADR 011.

---

## Architecture decision required

Before drafting anything, evaluate these three options against the CritterStackSamples north star
and the project's stated purpose (conference demo vehicle; reference architecture; Critter Stack
showcase). The evaluation must be explicit in ADR 011 — not just the decision but the reasoning
for rejecting the alternatives.

### Option A — All-Marten: migrate Participants, Settlement, Operations to Marten/PostgreSQL

Drop the Polecat/SQL Server storage layer entirely. All eight BCs use Marten + PostgreSQL. Each BC
contributes its document types, projections, and event registrations via `services.ConfigureMarten()`
inside its own `AddXyzModule()` extension method. `Program.cs` contains exactly one
`AddMarten().IntegrateWithWolverine()` chain.

The dual-store problem ceases to exist. The CritterStackSamples north star applies uniformly to
every BC. Every idiomatic Critter Stack pattern (`AutoApplyTransactions`, `[Entity]`,
`IDocumentSession` injection, `[AggregateHandler]`, `ValidateAsync`/`Validate`, `[WolverinePost]`,
`CleanAllMartenDataAsync()` in tests) works out of the box for every BC without exception.

Participants BC currently has a working Polecat event-sourced aggregate. Migrating it requires
replacing `AddPolecat()` + Polecat-specific patterns with their Marten equivalents. The Participants
BC has 6 passing integration tests that would need to pass against the Marten implementation — these
are the concrete acceptance criteria for a subsequent Participants migration session.

### Option B — Microservices split: separate deployable hosts by storage backend

Split CritterBids into two deployable processes:
- `CritterBids.MartenApi`: Selling, Auctions, Listings, Obligations, Relay (PostgreSQL / Marten)
- `CritterBids.PolecatApi`: Participants, Settlement, Operations (SQL Server / Polecat)

Each host has exactly one store and therefore no dual-store conflict. Each host can demonstrate
the uniform Critter Stack patterns within its own process.

Tradeoffs to evaluate explicitly: inter-service communication (RabbitMQ or HTTP); Aspire host
complexity (two API processes + three infra containers); demonstration clarity (is a split host
a useful showcase or a confusing one?); whether the Participants BC seam (session management,
seller registration, bidder identity) is harder to consume from a separate process than from a
shared in-process module.

### Option C — Defer Polecat BCs; develop Marten BCs only until JasperFx resolves ancillary API

Proceed with Marten BCs only (Selling, Listings, Auctions, Obligations, Relay). Guard-clause the
Polecat guard block in `Program.cs` so the host starts without SQL Server. Participants BC runs
in a degraded or test-only mode. Resume Polecat BC development when the ancillary API exists.

This option is already ruled out: the project is not waiting for a JasperFx fix. Document this
rejection explicitly in ADR 011 so the decision record is complete.

---

## In scope

### 1. Architecture decision evaluation (prerequisite to all other work)

Read and internalize the CritterStackSamples north star analysis before forming any opinion.
Evaluate Options A, B, and C using the criteria above. Document the decision in ADR 011.

**If Option A is chosen** (the expected outcome given the project's purpose and the north star
evidence): ADR 011 supersedes ADR 003 (Polecat BCs). ADR 001 (modular monolith) is unaffected —
the monolith structure is preserved, only the storage layer changes.

**If Option B is chosen**: ADR 011 partially supersedes ADR 001 (modular monolith). Document the
new host topology, communication contracts, and Aspire wiring changes required. Note the
implications for M2 session scoping.

### 2. ADR 011 — `docs/decisions/011-all-marten-pivot.md`

New file. Verify the correct ADR number via `docs/decisions/README.md` before creating.

Required sections: **Context** (the dual-store blocker, the CritterStackSamples north star finding,
the project's purpose as a showcase of idiomatic Critter Stack patterns), **Options Considered**
(A, B, C — each with explicit evaluation and rejection rationale where applicable), **Decision**
(one sentence), **Consequences** (what changes in `Program.cs` and BC modules, which ADRs are
superseded, what the Participants BC migration session must deliver, whether existing tests need
to change before the migration PR, the restored uniform bootstrap pattern).

Update `docs/decisions/README.md` — add the ADR 011 row.

### 3. CLAUDE.md updates

Targeted changes only. Do not rewrite sections that are correct. Update:

- **BC Module Quick Reference table** — reflect the new storage topology based on the architecture
  decision. If all-Marten: remove SQL Server / Polecat storage entries; all BCs become
  PostgreSQL / Marten. If microservices: note the host each BC belongs to.

- **Canonical bootstrap note** — add a brief sentence or callout after the "Core Conventions"
  section (or at the top of the relevant section) noting that the three-call sequence
  `AddMarten(...).IntegrateWithWolverine().UseLightweightSessions()` +
  `UseWolverine(opts => { opts.Policies.AutoApplyTransactions(); })` + `AddWolverineHttp()` +
  `MapWolverineEndpoints()` is the uniform bootstrap, consistent across all BCs, and confirmed
  by the CritterStackSamples reference analysis at `C:\Code\JasperFx\critter-stack-samples-analysis.md`.

- **Preferred Tools table** — if Option A: remove SQL Server and Polecat entries (or mark as
  not currently in use). If Option B: add a note about which tool serves which host.

- **The `[AllowAnonymous]` through M5 stance** — do NOT change this; it is correct.

- Do not update the Session Workflow section, ADR conventions, or the skill invocation guide
  unless the architecture decision requires it.

### 4. Three skill doc updates

#### `docs/skills/critter-stack-testing-patterns.md`

Add a new section (or integrate into the existing structure — match the file's existing section
format) covering the **standard test class lifecycle** as demonstrated across all CritterStackSamples:

- `IAsyncLifetime` on every test class
- `InitializeAsync`: `_host = await AlbaHost.For<Program>()` + `await _host.CleanAllMartenDataAsync()`
- `DisposeAsync`: `await _host.DisposeAsync()`
- Why `CleanAllMartenDataAsync()` is the canonical cleanup call (not a custom helper)
- The `_host.DocumentStore().LightweightSession()` pattern for direct Marten access in tests
  (both for setup seeding and for post-request assertions)
- The shared `AppFixture` + `ICollectionFixture<AppFixture>` pattern for host-once-per-collection
  scenarios (note: already partially present — confirm and expand if the north star analysis
  adds clarity)
- `services.DisableAllExternalWolverineTransports()` in every `AlbaHost.For<Program>()` override
  that uses external transports (RabbitMQ) — confirm this is documented

#### `docs/wolverine-message-handlers.md`

Add or expand coverage of these two patterns from the north star analysis. Check whether either
is already documented before adding; supplement rather than duplicate:

**`[Entity]` declarative loading — batch-query pattern**

When multiple related entities must be loaded to fulfil a request, `[Entity]` parameters on the
same method signature trigger a single Marten batch query rather than sequential `LoadAsync` calls.
The `OnMissing = OnMissing.ProblemDetailsWith400` variant treats a missing referenced entity as
bad client input (400) rather than a 404. This is appropriate when the ID comes from the client
and a missing entity means an invalid request.

Example pattern (do NOT include C# code blocks — describe in prose per prompt rule 8):
- Two `[Entity(Required = true)]` parameters on the same handler method load both documents in
  a single round-trip
- `OnMissing = OnMissing.ProblemDetailsWith400` on each entity parameter eliminates a separate
  `ValidateAsync` existence check
- The `Validate` or `ValidateAsync` method receives the already-loaded entities, so business rule
  checks against them are synchronous

**`ValidateAsync` and `Validate` — railway programming pre-handler pattern**

Wolverine calls a `ValidateAsync(command, IQuerySession)` method before the main handler. Returning
a populated `ProblemDetails` short-circuits with that status; returning `WolverineContinue.NoProblems`
proceeds. Describe:
- `ValidateAsync` for async database checks (uniqueness, existence queries)
- `Validate` (synchronous) for business rule checks against already-loaded aggregate state or
  `[Entity]` parameters — the aggregate or entity is already loaded, so no async call is needed
- The main handler method is always the happy path — no conditional returns, no error checks
- `WolverineContinue.NoProblems` is the continue sentinel in both variants

#### `docs/skills/marten-event-sourcing.md`

Add or confirm the **canonical BC module pattern** as established by ADR 009 and the north star:

- `services.ConfigureMarten(opts => { ... })` is how each Marten BC contributes its document types,
  projections, aggregates, and schema assignments from inside `AddXyzModule()`
- `opts.Schema.For<T>().DatabaseSchemaName("bc-name")` assigns a document type to its BC's schema
- `opts.Policies.AutoApplyTransactions()` is required in every BC's `ConfigureMarten()` call (not
  in the root `AddMarten()` call in `Program.cs` — the policy must be registered per-BC so it
  applies to each BC's handler context correctly)
- `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` for aggregates that need fast read access
  without stream replay
- Confirm whether the CritterStackSamples north star analysis changes or reinforces anything already
  documented here; update if reinforcement or gap-filling is needed

### 5. `docs/milestones/M2-listings-pipeline.md` — session table update

§9 Session Breakdown: update the table to reflect S4 (this session) and renumber what was
previously S4–S7 to S5–S8. The original S4 content (CreateDraftListing) becomes S5. Update each
row's prompt file reference accordingly.

### 6. `docs/prompts/M2-S5-slice-1-1-create-draft-listing.md` — new prompt

Author the implementation session prompt for what was previously M2-S4 (CreateDraftListing),
now renumbered S5. Use the established CritterBids prompt template from `docs/prompts/README.md`.

This prompt should be authored **after** ADR 011 is committed, so it reflects the correct
architecture. Key elements to include:
- Context: ADR 011 (architecture decision), ADR 009 (shared Marten store, `ConfigureMarten`
  pattern), `docs/skills/marten-event-sourcing.md`, `docs/skills/wolverine-message-handlers.md`
  (for `[Entity]`, `ValidateAsync`, `[AggregateHandler]` patterns)
- Scope: `DraftListingCreated` event, `SellerListing.Apply(DraftListingCreated)`, `ListingValidator`
  pure-function rules (14 tests), `POST /api/listings/draft` endpoint, `ISellerRegistrationService`
  gate (7.1/7.2 API gateway scenarios)
- The `POST /api/listings/draft` endpoint must use `[AllowAnonymous]` (M2 convention through M5)
- `ConfigureMarten()` contribution in `AddSellingModule()` to register the `SellerListing`
  event stream type once `DraftListingCreated` is introduced
- Acceptance criteria: 5 aggregate tests + 14 pure-function tests + 2 API gateway tests,
  `dotnet test` reports 13 + 21 = 34 passing

The full scope of S5 is specified in `docs/milestones/M2-listings-pipeline.md` §9 (now renumbered)
and §7 acceptance tests — defer to those for the complete test method list rather than restating it
inline.

### 7. Write the M2-S4 retrospective

`docs/retrospectives/M2-S4-north-star-alignment-retrospective.md` — new file.

Include: baseline state (13 tests passing, production Aspire crash still present, ADR 010 in
Proposed state), architecture decision made and rationale, ADR 011 summary, CLAUDE.md changes
made, skill doc updates (what was added, what was already correct), M2 session table renumbering,
M2-S5 prompt authored. Standard retrospective format per `docs/retrospectives/README.md`.

---

## Explicitly out of scope

- **No code changes of any kind.** No `.csproj`, `.cs`, `.slnx`, or `.props` files. The
  architecture decision is a documentation commitment; implementation follows in subsequent sessions.
- **Participants BC migration** — the code change that removes Polecat from Participants is a
  separate session. This session establishes what that session must deliver, not the delivery itself.
- **M2-S6 through S8 prompts** — only M2-S5 (CreateDraftListing) is authored in this session.
  Remaining session prompts follow in their respective sessions.
- **New packages** — no `Directory.Packages.props` changes.
- **`docs/skills/polecat-event-sourcing.md`** — if Option A is chosen, this skill becomes
  archived/inactive. Mark it as such in `docs/skills/README.md` but do not delete the file;
  its content may be valuable for future reference or a Polecat-only showcase project.
- **`docs/skills/integration-messaging.md`** — the RabbitMQ and local queue patterns in this file
  are correct for CritterBids' inter-BC messaging regardless of the storage decision; leave it
  unchanged unless the north star analysis reveals a direct conflict.
- **Frontend, CI workflows, Aspire orchestration changes** — not in this session.
- **Filing a JasperFx GitHub issue about the Polecat ancillary API** — the project is moving on;
  whether to file as informational feedback is Erik's call, not the agent's.

---

## Conventions to follow

- ADR format matches existing ADRs in `docs/decisions/` — same section headings, same tone.
  Read `docs/decisions/009-shared-marten-store.md` as the format reference (it is the most
  recent accepted ADR and covers a closely related topic).
- ADR 011 status is **Accepted** at session close — not Proposed.
- No C# code blocks in ADRs or skill doc prose descriptions (per prompt rule 8). Patterns are
  described in prose; concrete examples live in code, not in documentation.
- Skill doc updates supplement existing content — do not rewrite sections that are already correct.
  If a pattern is already documented accurately, add a "confirmed by CritterStackSamples north star
  analysis" note rather than restating it.
- The M2-S5 prompt obeys all ten rules in `docs/prompts/README.md`. Rule 8 (no code in prompts)
  and rule 5 (explicit non-goals) are particularly important.
- `docs/milestones/M2-listings-pipeline.md` is the scope authority for M2. The session table
  update is a factual renumbering, not a scope change.

---

## Acceptance criteria

- [ ] `docs/decisions/011-all-marten-pivot.md` (or equivalent name) exists with status **Accepted**
- [ ] ADR 011 explicitly evaluates all three options and states the rejection rationale for each
      non-chosen option
- [ ] ADR 011 names which prior ADRs it supersedes (at minimum: ADR 003 if Option A is chosen)
- [ ] `docs/decisions/README.md` includes the ADR 011 row
- [ ] `CLAUDE.md` BC Quick Reference table reflects the new storage topology (no SQL Server /
      Polecat entries if Option A; new host column if Option B)
- [ ] `CLAUDE.md` contains a reference to the canonical three-call bootstrap sequence and the
      north star analysis file path
- [ ] `docs/skills/critter-stack-testing-patterns.md` contains the standard `IAsyncLifetime`
      test class lifecycle and `CleanAllMartenDataAsync()` as the canonical cleanup call
- [ ] `docs/skills/wolverine-message-handlers.md` documents the `[Entity]` batch-loading pattern
      and the `ValidateAsync`/`Validate` railway programming model
- [ ] `docs/skills/marten-event-sourcing.md` confirms or adds the `ConfigureMarten()` per-BC
      contribution pattern and `AutoApplyTransactions()` placement
- [ ] `docs/skills/README.md` updated — `polecat-event-sourcing.md` marked as archived/inactive
      if Option A is chosen
- [ ] `docs/milestones/M2-listings-pipeline.md` §9 session table updated with S4 (this session)
      and S5–S8 renumbering
- [ ] `docs/prompts/M2-S5-slice-1-1-create-draft-listing.md` exists, follows the prompt template,
      and references ADR 011 and the updated skill docs in its context-load section
- [ ] `docs/retrospectives/M2-S4-north-star-alignment-retrospective.md` exists and covers all
      deliverables
- [ ] `dotnet build` passes with 0 errors, 0 warnings (no code was changed; this is a sanity check)
- [ ] `dotnet test` passes with 13/13 (no code was changed; this is a sanity check)
- [ ] No `.csproj`, `.cs`, `.slnx`, `.props`, or source files created or modified

---

## Open questions

**Option A vs B — additional consideration.** If, after reading the CritterStackSamples north star
analysis, the agent believes Option B (microservices split) better serves the project's goals as a
showcase — specifically, if demonstrating Polecat/SQL Server alongside Marten/PostgreSQL is judged
to have significant showcase value that outweighs the complexity cost — document this reasoning in
ADR 011 and choose Option B. Do not default to Option A merely because it is listed first. The
evaluation must be honest. That said: the north star analysis shows that zero of the CritterStack
reference samples use a mixed storage backend, and the analysis explicitly notes that all idiomatic
patterns depend on a single primary Marten store. These are substantial evidence for Option A.

**M2-S5 prompt scope for Participants BC migration.** If Option A is chosen, the Participants BC
currently has a working Polecat implementation with 6 passing tests. The migration from Polecat to
Marten is not part of M2-S5 (CreateDraftListing) — it is a separate session. The M2-S5 prompt
does not need to address Participants migration scope; that will be captured in a dedicated session
prompt. Flag this as an open dependency in the M2-S5 prompt's Open Questions section.

**`AutoApplyTransactions()` placement.** ADR 009 shows `AutoApplyTransactions()` inside the root
`AddMarten()` in `Program.cs`. The CritterStackSamples north star shows it in every BC's
`UseWolverine()` block via `opts.Policies.AutoApplyTransactions()`. Confirm whether CritterBids'
current pattern (in the root `AddMarten()` call) or the samples' pattern (in `UseWolverine()`)
is correct, and ensure the skill doc update reflects the verified placement. If there is a
discrepancy between the two, document it explicitly in the retrospective.
