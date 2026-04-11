# M1-S4: Participants BC Scaffold

**Milestone:** M1 — Skeleton
**Slice:** S4 — Participants BC scaffold
**Agent:** @PSA
**Estimated scope:** one PR, 2 new projects + 4–6 new files + 3 files modified

## Goal

Create the `CritterBids.Participants` BC class library and its test project
sibling, wire `AddParticipantsModule()` into the API host, register the
`Participant` aggregate stream with Polecat at the BC level, and pin the UUID
v5 namespace constant for Participants stream IDs (resolves M1-D4).

End state: the app still boots clean via `dotnet run --project
src/CritterBids.AppHost`, the SQL Server schema for the Participants BC is
created on startup, the two existing smoke tests still pass, and the new
Participants test project compiles and passes its placeholder test. No
commands, no events, no handlers, no endpoints — this session produces only
the scaffolding that S5 and S6 will build on.

This session also resolves two open items carried over from M1-S3: the Polecat
**migration strategy** (auto-apply vs. explicit call — deferred from S3
because no stream was registered) and the **Wolverine outbox schema**
verification (deferred until the first Polecat stream is registered).

## Context to load

1. `docs/milestones/M1-skeleton.md` — milestone scope, §5 Polecat config
   requirements, §6 conventions, §8 open questions (M1-D4 resolved here)
2. `CLAUDE.md` — modular monolith rules, conventions, BC module table
3. `docs/retrospectives/M1-S3-infrastructure-baseline.md` — confirms what
   S3 wired at host level; lists the two carry-over items (migration strategy,
   outbox schema) this session must resolve
4. `docs/skills/polecat-event-sourcing.md` — Polecat patterns (🟡 status;
   this is the first Polecat BC — work through the implementation checklist
   as you go and document findings directly in the file)
5. `docs/skills/marten-event-sourcing.md` — primary reference for aggregate
   patterns until `polecat-event-sourcing.md` is complete
6. `docs/skills/wolverine-message-handlers.md` — module registration
   conventions, handler return patterns
7. `docs/skills/csharp-coding-standards.md` — C#/.NET baseline
8. `docs/prompts/README.md` — the ten rules this prompt obeys

> **Note:** `docs/skills/adding-bc-module.md` does not yet exist (🔴 status
> in the skill index). This session is the one that establishes the module
> registration pattern for CritterBids. The `adding-bc-module.md` skill will
> be authored retrospectively in M1-S7 based on what this session produces.
> Be intentional about the pattern you establish here — it is the template
> every future BC will follow.

## In scope

### New project: `src/CritterBids.Participants`

A .NET class library targeting `net10.0`.

**`CritterBids.Participants.csproj`** — package references:
- `WolverineFx.Polecat` (already pinned in `Directory.Packages.props`)
- `WolverineFx` (already pinned)
- Any `Microsoft.Extensions.*` packages needed for the `IServiceCollection`
  extension method, if not already pinned

**Files to create:**

- **`Participant.cs`** — empty aggregate scaffold. A `sealed record` with at
  minimum an `Id` property. No `Apply()` methods, no state fields — those
  arrive in S5. The type exists now so Polecat can register the stream.

- **`ParticipantsStreamIds.cs`** (or an equivalent well-named file) — contains
  the UUID v5 namespace constant for Participants stream ID generation. This
  resolves M1-D4. Generate the GUID via `Guid.NewGuid()` in a one-shot
  PowerShell or `dotnet-script` command during the session; hard-code the
  resulting value as a `static readonly Guid`. Once set, this value must never
  change — it is the namespace seed for every Participants stream ID in
  production. Add a brief comment in the file saying it was generated once and
  is immutable.

- **`AddParticipantsModule.cs`** — the `IServiceCollection` extension method
  `AddParticipantsModule()`. This is the single entry point for the
  Participants BC's DI registration. Responsibility in S4:
  - Configure Polecat at the BC level — register the `Participant` aggregate
    stream type and set `opts.DatabaseSchemaName = "participants"`.
  - **See the critical open question below** about how BC-level Polecat config
    interacts with the host-level `AddPolecat()` call from S3. Resolve that
    question before writing this file.
  - No handler registrations, no HTTP endpoint wiring — those come in S5/S6.
  - Return `IServiceCollection` for fluent chaining.

### New project: `tests/CritterBids.Participants.Tests`

Layout 2 rule: every new `src/` project requires a `tests/` sibling in the
same PR.

**`CritterBids.Participants.Tests.csproj`** — package references:
- `xunit` + `xunit.runner.visualstudio` (already pinned)
- `Shouldly` (already pinned)
- `Alba` — **check `AlbaVersion` in `Directory.Build.props` against current
  NuGet before pinning; the S3-F3 flag notes this value was not verified.**
  Add to `Directory.Packages.props` if not already present.
- `Testcontainers.MsSql` — verify current version on NuGet; add pin to
  `Directory.Packages.props`. These are needed in S5/S6; pin now so the test
  project csproj is complete and future sessions only add test code.

> **Do not add Testcontainers-backed test code in this session.** The test
> project must exist and compile; actual integration tests land in S5 and S6.

**Placeholder test file** — a single `[Fact]` that passes immediately (e.g.,
asserting the `ParticipantsStreamIds` namespace GUID is not empty). This
proves the project compiles and satisfies `dotnet test`. Name the file
logically — do not call it `PlaceholderTests.cs`; name it for what it tests.

### Modified files

- **`src/CritterBids.Api/Program.cs`** — add the `AddParticipantsModule()`
  call after the existing Polecat/Wolverine host configuration. Pass the SQL
  Server connection string (already available from the Aspire-injected
  environment, used in the host-level `AddPolecat()` call in S3) to the module
  if the BC-level Polecat config requires it.

- **`CritterBids.slnx`** — add both `src/CritterBids.Participants` and
  `tests/CritterBids.Participants.Tests` to the solution, in their respective
  `src/` and `tests/` folders.

- **`docs/milestones/M1-skeleton.md`** — update §9 S4 row from `*TBD*` to
  `docs/prompts/M1-S4-participants-bc-scaffold.md`.

### Carry-over items to resolve (from M1-S3 retro)

Both of these must be resolved and documented in this session's retro:

1. **Polecat migration strategy** — The S3 retro explicitly deferred this:
   "Schema creation happens when BC modules register stream types (M1-S4)."
   Determine whether Polecat auto-applies the `participants` schema on startup
   when the stream type is registered, or whether an explicit call (e.g.
   `ApplyAllDatabaseChangesOnStartup()`) is required. Apply whatever is correct
   and document the finding. If the answer is unclear or requires a design
   decision beyond your authority, flag and stop per rule 7.

2. **Wolverine outbox schema verification** — The S3 retro noted: "Verify at
   M1-S4 when the first Polecat stream is registered." After wiring the module
   and booting the app, confirm the Wolverine inbox/outbox schema tables exist
   in the SQL Server database. Document the result in the retro.

## Explicitly out of scope

- **No commands, events, or handlers.** `ParticipantSessionStarted`,
  `RegisterAsSeller`, `StartParticipantSession` — all of these belong to S5
  and S6.
- **No HTTP endpoints.** No minimal API routes, no controllers.
- **No auth wiring.** The `[AllowAnonymous]` M1 override is established when
  the first endpoint lands in S5.
- **No projections, no read models, no query handlers.**
- **No `SellerRegistrationCompleted` integration event.** That type lives in
  `CritterBids.Contracts` and is created in S6 when the handler that publishes
  it is written.
- **`adding-bc-module.md` skill.** Do not write this skill file in S4. Collect
  notes; the skill is authored in M1-S7.
- **`docs/skills/aspire.md`.** Still deferred to M1-S7.
- **`docs/decisions/0001-uuid-strategy.md`.** Assigned to M1-S5.
- **`WolverineVersion` property cleanup** in `Directory.Build.props`. Still
  deferred to M1-S7.
- **No changes to `CritterBids.Contracts`.** That project is touched in S6.
- **No Marten configuration.** Participants is Polecat-only; Marten remains
  absent from M1.
- **No E2E test project** (`CritterBids.E2E.Tests`).
- **No CI workflow changes.**
- **No SignalR, no frontend.**

## Conventions to pin or follow

- **Module registration pattern (first use):** `AddParticipantsModule()` is an
  extension on `IServiceCollection`. It is the **only** public entry point for
  the Participants BC. `CritterBids.Api/Program.cs` calls it once; nothing
  else calls it. Do not add any other registrations directly in `Program.cs`
  for Participants.

- **Schema isolation:** Every Polecat BC uses its own `DatabaseSchemaName`.
  Participants uses `"participants"` (lowercase BC name). Establish this here;
  all future Polecat BCs follow the same convention.

- **UUID v5 namespace constant:** Generated once, stored as a `static readonly
  Guid` in the Participants project, never modified. Comments in the source
  file must state it was generated on a specific date and must not be changed.
  The field is `public` — S5 will use it when building stream ID helpers.

- **`sealed record` for aggregate:** The `Participant` type is a `sealed
  record`. No exceptions; this is established in `CLAUDE.md` and in §6 of the
  milestone doc.

- **No `Version=` on any `<PackageReference>`.** Central package management
  applies to all new package references in both `.csproj` files.

- **`[AllowAnonymous]` override:** Not applicable in S4 — no endpoints yet.
  The convention is noted here only to remind: it lands in S5, not here.

- **`opts.Policies.AutoApplyTransactions()`:** Already applied globally in S3
  via `builder.UseWolverine()`. Do not duplicate it in the BC-level Polecat
  config unless the resolution of the open question about BC-level config
  interaction reveals a reason to do so.

## Acceptance criteria

- [ ] `src/CritterBids.Participants/CritterBids.Participants.csproj` exists
      and is listed in `CritterBids.slnx` under the `src/` folder.
- [ ] `tests/CritterBids.Participants.Tests/CritterBids.Participants.Tests.csproj`
      exists and is listed in `CritterBids.slnx` under the `tests/` folder.
- [ ] `Participant.cs` exists as a `sealed record` with at minimum an `Id`
      property (`Guid`). No `Apply()` methods.
- [ ] `ParticipantsStreamIds.cs` (or equivalent) exists with a non-empty
      `static readonly Guid` namespace constant. Value was generated during
      this session and is hard-coded.
- [ ] `AddParticipantsModule.cs` exists with a public `AddParticipantsModule()`
      extension on `IServiceCollection`. It configures Polecat at the BC level:
      registers the `Participant` stream type and sets
      `opts.DatabaseSchemaName = "participants"`.
- [ ] `CritterBids.Api/Program.cs` calls `AddParticipantsModule()`.
- [ ] `dotnet run --project src/CritterBids.AppHost` boots clean — Aspire
      dashboard up, all three containers running, API running (verified via
      `docker ps` and Aspire dashboard).
- [ ] After startup, the `participants` schema (or equivalent) exists in the
      SQL Server database — Polecat schema creation verified.
- [ ] After startup, Wolverine inbox/outbox tables exist in the SQL Server
      database — outbox schema creation verified.
- [ ] `dotnet build` succeeds with zero errors and zero warnings across the
      entire solution.
- [ ] `dotnet test` reports all tests passing — the existing 2 smoke tests
      plus the new Participants.Tests placeholder test (minimum 3 total), zero
      failing.
- [ ] `Directory.Packages.props` has new pins for `Alba` and
      `Testcontainers.MsSql`; no `Version=` attribute on any
      `<PackageReference>` anywhere in the solution.
- [ ] `AlbaVersion` in `Directory.Build.props` was checked against current
      NuGet and the pin reflects the current stable version (flag if stale).
- [ ] `docs/milestones/M1-skeleton.md` §9 S4 row updated from `*TBD*` to
      `docs/prompts/M1-S4-participants-bc-scaffold.md`.
- [ ] Polecat migration strategy resolved and documented in the retro.
- [ ] Wolverine outbox schema verification documented in the retro.
- [ ] No files created or modified outside the six allowed paths:
      `src/CritterBids.Participants/`, `tests/CritterBids.Participants.Tests/`,
      `src/CritterBids.Api/Program.cs`, `Directory.Packages.props`,
      `CritterBids.slnx`, `docs/milestones/M1-skeleton.md`, and this
      session's retrospective.
- [ ] No commands, events, handlers, endpoints, or auth wiring introduced.
- [ ] No `CritterBids.Contracts` changes.

## Open questions

- **BC-level Polecat config interaction with host-level `AddPolecat()`.** S3
  called `AddPolecat()` on the service collection with the SQL Server connection
  string and `AutoApplyTransactions`. Calling `AddPolecat()` again in
  `AddParticipantsModule()` may replace the host-level registration rather than
  extend it. Before writing `AddParticipantsModule()`, determine the correct
  API for extending an existing Polecat registration — Polecat may expose a
  `ConfigurePolecat()` method (analogous to Marten's) or a different extension
  point for adding stream types to an already-registered store. If the extension
  API is unclear or the answer requires architectural input beyond what the skill
  files or source provide, flag and stop — do not guess, as a wrong choice here
  breaks every future BC.

- **Polecat migration strategy** (carry-over from M1-S3 retro). Does Polecat
  auto-apply the `participants` schema on first startup, or is an explicit
  `ApplyAllDatabaseChangesOnStartup()` / `MigrateAsync()` call required? Verify
  by booting the app and checking the database. If the answer requires a
  design decision (e.g., whether to auto-migrate in dev vs. run explicit
  migrations in prod), flag and stop — do not choose a migration strategy
  unilaterally. This is architectural.

- **AlbaVersion in `Directory.Build.props`** — S3-F3 flagged this as
  unverified. Check the current stable version on NuGet before pinning. If the
  property value is stale, use the current version and record the discrepancy
  in the retro (same treatment as `WolverineVersion` in S3).

- **UUID v5 generation tooling.** There is no established project tool for
  generating the namespace GUID. Use `Guid.NewGuid()` via a one-shot
  PowerShell command (`[guid]::NewGuid()`) or `dotnet-script` — whichever is
  available in the shell context. The exact tool does not matter; what matters
  is that the result is captured, hard-coded, and documented in the retro with
  the generation date.

- **`Participant` aggregate constructor requirements.** Polecat may require a
  specific constructor shape on aggregates (e.g., a parameterless constructor,
  or one accepting an `Id` parameter). Verify against `polecat-event-sourcing.md`
  and the Polecat source if needed. If Polecat imposes a shape constraint that
  conflicts with `sealed record`, flag and stop.
