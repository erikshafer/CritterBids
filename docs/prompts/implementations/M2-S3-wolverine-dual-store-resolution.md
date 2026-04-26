# M2-S3: Wolverine Dual-Store Resolution + ADR 010

**Milestone:** M2 — Listings Pipeline
**Slice:** S3 — Production Aspire unblock; ADR 010; S3 close
**Agent:** @PSA
**Estimated scope:** one PR, ~1 new decision doc + 2 new retrospective/prompt docs + 1 Program.cs fix

## Goal

The deliverables originally assigned to S3 (RegisteredSellers handler, projection,
`ISellerRegistrationService`, `Program.cs` routing, 4 projection tests) were completed during
the unscheduled post-S2 architectural correction. The correction retro is at
`docs/retrospectives/M2-postS2-adr0002-correction.md` and is the authoritative record for
that work.

This session has three objectives:

1. **Resolve the production Aspire startup crash.** When both postgres and sqlserver
   connection strings are present, `AddMarten().IntegrateWithWolverine()` and
   `AddParticipantsModule()` → `AddPolecat().IntegrateWithWolverine()` each register as a
   Wolverine "main" message store. Wolverine requires exactly one. The crash:
   `InvalidWolverineStorageConfigurationException: There must be exactly one message store
   tagged as the 'main' store.`

2. **Author ADR 010** capturing the problem, the options evaluated, and the chosen solution.

3. **Close S3 formally** — write the M2-S3 retrospective and delete the obsolete
   `M2-S3-registered-sellers-consumer.md` prompt file (its content was authored against
   ADR 008 named-store patterns that no longer exist in the codebase; the file is preserved
   in git history).

At session close: `dotnet run --project src/CritterBids.AppHost` starts without error,
`dotnet test` still reports 13 passing tests, ADR 010 exists and is referenced from the
ADR index, and the S3 retrospective is written.

## Context to load

- `docs/retrospectives/M2-postS2-adr0002-correction.md` — **primary baseline.** Read in full.
  The "What remains" section documents the exact error, the three candidate resolutions,
  and the open JasperFx question. Start here.
- `docs/decisions/009-shared-marten-store.md` — current governing ADR; the dual-store
  conflict is in §Consequences ("Production multi-store conflict is unresolved").
- `src/CritterBids.Api/Program.cs` — current state; both module registrations are null-guarded.
  Examine the actual guard blocks before touching anything.
- `docs/skills/polecat-event-sourcing.md` — Polecat `AddPolecat()` / `IntegrateWithWolverine()`
  API surface; check whether Wolverine Polecat exposes an ancillary-store configuration path.
- `docs/skills/integration-messaging.md` — Wolverine transport and durability configuration;
  check for `PersistMessagesWithSqlServer()` or equivalent standalone message persistence.
- `docs/decisions/README.md` — confirms the next ADR number is **010**; verify before creating.

## In scope

### 1 — Research the fix (required before writing any code)

Three candidate resolutions from the post-S2 retro, ordered by preference:

**Option A — Mark Polecat as an ancillary Wolverine store.**
`AddPolecat().IntegrateWithWolverine()` may support a configuration callback that marks the
store as non-main. Check `docs/skills/polecat-event-sourcing.md` and Context7 for the actual
`IntegrateWithWolverine` overload. If an overload accepting a delegate exists, check whether
the delegate exposes a property or method to opt out of "main" status (e.g.
`cfg.IsMainMessageStore = false` or similar). If this API exists and is documented,
implement Option A.

**Option B — Standalone Wolverine message persistence.**
Wolverine may support a standalone message storage configuration separate from both Marten and
Polecat, e.g. `opts.PersistMessagesWithSqlServer(connectionString)` or
`opts.PersistMessagesWithPostgresql(connectionString)`, that registers Wolverine's inbox/outbox
independently of any application store. If this API exists, configure Wolverine to own its own
message tables; leave `AddMarten().IntegrateWithWolverine()` and
`AddParticipantsModule()` → `AddPolecat().IntegrateWithWolverine()` as ancillary to Wolverine's
standalone store.

**Option C — Deferred: file a JasperFx GitHub discussion.**
If neither A nor B can be confirmed correct and safe via Context7 and local source inspection,
do not guess. Author ADR 010 in a "Proposed / Pending JasperFx input" state documenting the
exact error, what was investigated, and the open question. Add a prominent comment in
`Program.cs` at both guard blocks. This session still closes successfully — the ADR and retro
are complete deliverables regardless of whether the runtime fix is implemented.

All three options require the same research step. Commit the research findings in the
retrospective and ADR regardless of which path is taken.

### 2 — Author ADR 010

`docs/decisions/010-wolverine-dual-store-resolution.md` — new file.

Structure: **Context** (reproduce the exact error and which guard blocks trigger it),
**Options Considered** (A, B, C — with the concrete API surface examined for each and why
it was accepted or rejected), **Decision** (the chosen option or explicit deferral),
**Consequences** (what changes in `Program.cs`, what the test fixture implications are,
whether the constraint propagates to future BCs). Keep tone and depth consistent with ADR 009.

Update `docs/decisions/README.md` — add the ADR 010 row to the status ledger.

### 3 — Implement the fix (if Option A or B is confirmed)

All code changes are confined to `src/CritterBids.Api/Program.cs`. No BC module files change.

**If Option A:** change the Polecat `IntegrateWithWolverine()` call to pass the ancillary
configuration callback. Verify the Participants test fixture is unaffected — it already excludes
Marten infrastructure and relies on Polecat as its only store.

**If Option B:** add a standalone Wolverine persistence call in `Program.cs`; confirm it is
also null-guarded (use the same sqlserver string as Participants, or document a separate
wolverine connection string if required). Verify both the Selling fixture (postgres only) and
the Participants fixture (sqlserver only) still pass.

### 4 — Write the M2-S3 retrospective

`docs/retrospectives/M2-S3-wolverine-dual-store-resolution-retrospective.md` — new file.

Include: baseline at session open (what was already done in post-S2 correction, 13 tests
passing, Aspire crash), items completed, research findings (what Wolverine/Polecat APIs were
checked, what was found and where), chosen option and rationale, any deviations from expected
API surface, build/test results, verification checklist, files changed. The retro must
explicitly note that the original S3 deliverables were completed in the post-S2 correction
session and cross-reference `M2-postS2-adr0002-correction.md`.

### 5 — Remove the obsolete prompt file

Delete `docs/prompts/implementations/M2-S3-registered-sellers-consumer.md`.

The file was authored against ADR 008 (named stores). It references `ISellingDocumentStore`
(deleted), `[MartenStore]` attributes (removed), and `AddMartenStore<T>()` (removed). Leaving
it alongside this file creates ambiguity about which is authoritative. The content is preserved
in git history; no information is lost by deleting it.

## Explicitly out of scope

- **`DraftListingCreated`, `SellerListing.Apply()`, `ListingValidator`** — S4
- **`POST /api/listings/draft` endpoint** — S4
- **`CritterBids.Listings` or `CritterBids.Listings.Tests` projects** — S6
- **`CritterBids.Contracts.Selling.ListingPublished`** — S5
- **Any changes to `CritterBids.Participants`, `CritterBids.Selling`, or their test projects** —
  the only permitted code change is `Program.cs`
- **`docs/skills/` updates** — skills pass is S7
- **`CLAUDE.md` changes** — S7
- **New tests** — the existing 13 must stay green; no new test files in this session

## Acceptance criteria

- [ ] `dotnet build`: 0 errors, 0 warnings
- [ ] `dotnet test`: 13/13 passing — no regressions
- [ ] Either: both postgres and sqlserver connection strings present → host starts without
      `InvalidWolverineStorageConfigurationException`; OR: ADR 010 is in
      "Proposed / Pending JasperFx input" state with research findings documented
- [ ] `docs/decisions/010-wolverine-dual-store-resolution.md` exists
- [ ] `docs/decisions/README.md` includes the ADR 010 row in the status ledger
- [ ] `docs/retrospectives/M2-S3-wolverine-dual-store-resolution-retrospective.md` exists
      and references `M2-postS2-adr0002-correction.md` for the original S3 deliverables
- [ ] `docs/prompts/implementations/M2-S3-registered-sellers-consumer.md` has been deleted
- [ ] No files modified outside: `src/CritterBids.Api/Program.cs` (if fix implemented),
      `docs/decisions/`, `docs/retrospectives/`, `docs/prompts/`
- [ ] No `ISellingDocumentStore`, `AddMartenStore<T>()`, or `[MartenStore]` references
      introduced anywhere in the solution

## Open questions

**Wolverine ancillary store API for Polecat.** The core question: does Polecat's
`IntegrateWithWolverine()` expose a callback that allows opting out of "main" store status?
Verify via Context7 against the current Wolverine and WolverineFx.Polecat source. If the API
does not exist, check whether a GitHub issue or JasperFx Slack thread already covers this
scenario before opening a new one.

**Connection string ownership under Option B.** If standalone Wolverine message persistence
is chosen, which connection string does it use? Reusing the sqlserver string ties Wolverine
durability to SQL Server; using the postgres string ties it to the Marten PostgreSQL instance.
Document the choice and implications in ADR 010.

**Aspire verification.** The fix is most easily confirmed by running
`dotnet run --project src/CritterBids.AppHost` and verifying a clean startup. If this is not
available in the session environment, construct a test host with both connection strings present
and verify the absence of `InvalidWolverineStorageConfigurationException`. Document the
verification approach used in the retrospective.
