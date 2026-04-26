# M1-S4: Participants BC Scaffold

**Milestone:** M1 — Skeleton
**Slice:** S4 — Participants BC scaffold
**Agent:** @PSA
**Estimated scope:** one PR, ~4 new files + 2 project files + 2 file modifications

## Goal

Create the `CritterBids.Participants` class library and its `CritterBids.Participants.Tests` sibling,
establish the `AddParticipantsModule()` extension that registers the Participants BC with the API host,
define the empty `Participant` aggregate, and pin the UUID v5 namespace constant for the BC (resolves
M1-D4 from the milestone doc).

This is a scaffold session — no commands, no events, no HTTP endpoints, no integration tests beyond a
single smoke test. The key architectural question left open by M1-S3 is resolved here: how BC modules
extend the host-level Polecat registration with BC-specific schema and stream configuration. Document
the answer in the retrospective.

At session close: the solution builds green, the three smoke tests pass (2 existing + 1 new), and
`dotnet run --project src/CritterBids.AppHost --launch-profile http` still boots all four services.

## Context to load

- `docs/milestones/M1-skeleton.md` — §4 solution layout and test project naming, §5 Polecat config
  requirements (schema, stream registration, no projections), §6 conventions (UUID v5 namespace constant,
  `AutoApplyTransactions` placement, `[AllowAnonymous]` M1 override), §8 M1-D4 open question
- `CLAUDE.md` — modular monolith rules, `AddXyzModule()` convention, global conventions
- `docs/retrospectives/M1-S3-infrastructure-baseline.md` — confirmed Polecat and Wolverine API shapes;
  `AutoApplyTransactions` placement rationale; migration strategy deferral; open flags S3-F1 through S3-F5
- `docs/skills/polecat-event-sourcing.md` — BC-level Polecat configuration pattern, standard module
  registration example, `IntegrateWithWolverine()` behavior; status 🟡 — fill in gaps as you go and
  note missing content for the M1-S7 retro session
- `docs/skills/csharp-coding-standards.md` — C#/.NET baseline, UUID conventions, sealed record and
  aggregate class distinctions
- `docs/prompts/README.md` — the ten rules this prompt obeys
- `src/CritterBids.Api/Program.cs` — the host-level Polecat and Wolverine config from M1-S3; the inline
  comment "each BC sets its own schema in AddXyzModule()" is the target this session fulfills

## In scope

### New project: `src/CritterBids.Participants`

- `CritterBids.Participants.csproj` — class library targeting net10.0.
- Add to `.slnx` in the `/src/` folder.
- Package reference: `WolverineFx.Polecat` (already pinned in `Directory.Packages.props`). Add pins to
  `Directory.Packages.props` only for packages this session actually requires to build; do not pre-pin
  packages for M1-S5 or beyond.

### `AddParticipantsModule()` extension

- Static extension method on `IServiceCollection` accepting a SQL Server connection string as a parameter.
- Configures Polecat for the Participants BC: schema name `"participants"`, `AutoCreate.CreateOrUpdate`,
  `StreamIdentity.AsGuid`, `UseSystemTextJsonForSerialization`.
- Integrates with Wolverine via `.IntegrateWithWolverine()`.
- Does not read `IConfiguration` directly — the caller passes the connection string.
- See **Open questions** for the key architectural question that must be resolved before implementing.

### Empty `Participant` aggregate

- A class (not a record — aggregates are class-based per the Polecat/Marten convention) with a public
  `Guid Id` property and no other members. No `Apply()` methods, no events, no status enum.
- Structured so that `PolecatOps.StartStream<Participant>(id, ...)` in M1-S5 can reference it by type
  without modification.

### UUID v5 namespace constant — resolves M1-D4

- A `public static readonly Guid` constant pinned in the Participants project as the BC-specific
  namespace for UUID v5 stream ID computation.
- Generate a fresh random `Guid` for this value (run `Guid.NewGuid()` once, then commit the resulting
  literal). Do not use `Guid.Empty` or any sentinel.
- Name it clearly (e.g. `ParticipantsNamespace` or `ParticipantStreamNamespace`).
- Include a brief inline comment stating what it is and that it must never change once committed.
- This constant is defined now; it will be used in M1-S5 when `StartParticipantSession` is handled.

### `CritterBids.Api/Program.cs` — call `AddParticipantsModule()`

- Call `builder.Services.AddParticipantsModule(...)` passing the SQL Server connection string that is
  already resolved from Aspire's `IConfiguration` in the existing Program.cs.
- Add the necessary `using` directive for the Participants namespace.

### New project: `tests/CritterBids.Participants.Tests`

- `CritterBids.Participants.Tests.csproj` — xUnit test project.
- References `CritterBids.Participants`.
- One trivial smoke test so the test runner is exercised (same pattern as M1-S1's existing smoke tests).
- Add to `.slnx` in the `/tests/` folder.
- No Testcontainers, no Alba, no integration test infrastructure — those arrive in M1-S5.

### Doc fix: `docs/milestones/M1-skeleton.md` §9

- Update the S4 row from `*TBD*` to `docs/prompts/implementations/M1-S4-participants-bc-scaffold.md`.

## Explicitly out of scope

- **No commands, events, or domain types.** Nothing from scenarios 0.2 or 0.3 — no `StartParticipantSession`,
  no `RegisterAsSeller`, no `ParticipantSessionStarted`, no `SellerRegistrationCompleted`.
- **No HTTP endpoints.** No `[WolverinePost]`, no `[WolverineGet]`, no minimal API handlers.
- **No `[AllowAnonymous]` wiring.** Established in M1-S5 when the first endpoint lands.
- **No projections.** The milestone doc §5 states "No projections in M1 (Participants has no views)."
- **No integration test infrastructure.** No Testcontainers, no Alba, no `WebApplicationFactory`, no
  base fixture class.
- **No `CritterBids.Contracts` additions.** `SellerRegistrationCompleted` is M1-S6 scope.
- **No `WolverineFx.Http.Polecat` pin.** HTTP endpoint integration deferred to M1-S5.
- **Orphaned `WolverineVersion` property cleanup** (S3-F1) — M1-S7.
- **`docs/skills/aspire.md` authoring** (S3-F4) — M1-S7.
- **`docs/decisions/0001-uuid-strategy.md`** (UUID strategy ADR) — M1-S5.
- **No Marten configuration.** Marten arrives with the Selling BC in M2.
- **No CI workflow changes.**
- **No SignalR, no frontend, no real-time wiring.**

## Conventions to pin or follow

- Aggregate classes follow the class-based pattern documented in `polecat-event-sourcing.md` and
  `marten-event-sourcing.md`. Aggregates are not `sealed record` — they are classes with `Apply()`
  methods that use `with` expressions or mutable property sets.
- `AddParticipantsModule()` receives the connection string as a parameter — it does not resolve
  configuration itself. This keeps the extension testable and keeps `IConfiguration` reads at the
  composition root (`Program.cs`).
- UUID v5 namespace constant is a `public static readonly Guid` literal. Once committed, it must
  never be regenerated or changed — its permanence is what makes stream IDs deterministic.
- All new source files use file-scoped namespaces under `CritterBids.Participants.*`.
- No `Version=` on any `<PackageReference>` anywhere in the solution.

## Acceptance criteria

- [ ] `src/CritterBids.Participants/CritterBids.Participants.csproj` exists and is listed in `.slnx`.
- [ ] `tests/CritterBids.Participants.Tests/CritterBids.Participants.Tests.csproj` exists and is listed
      in `.slnx`.
- [ ] A `Participant` aggregate class exists with a public `Guid Id` property and no other members.
- [ ] A UUID v5 namespace `Guid` constant exists in the Participants project with a clear name, a
      non-empty, non-sentinel literal value, and an inline comment.
- [ ] `AddParticipantsModule()` exists as an `IServiceCollection` extension method.
- [ ] `AddParticipantsModule()` configures Polecat with schema `"participants"`, `AutoCreate.CreateOrUpdate`,
      and `StreamIdentity.AsGuid`.
- [ ] `AddParticipantsModule()` chains `.IntegrateWithWolverine()`.
- [ ] `CritterBids.Api/Program.cs` calls `AddParticipantsModule()` with the SQL Server connection string.
- [ ] `dotnet build` succeeds with zero errors and zero warnings from new or modified projects.
- [ ] `dotnet test` reports 3 passing tests (2 existing + 1 new Participants smoke test), zero failing.
- [ ] `dotnet run --project src/CritterBids.AppHost --launch-profile http` still boots all four services
      without error.
- [ ] No `Version=` attribute on any `<PackageReference>` in any `.csproj`.
- [ ] `docs/milestones/M1-skeleton.md` §9 S4 row updated from `*TBD*` to the prompt filename.
- [ ] No files created or modified outside `src/CritterBids.Participants/`,
      `tests/CritterBids.Participants.Tests/`, `src/CritterBids.Api/Program.cs`,
      `Directory.Packages.props` (only if new pins are required), `CritterBids.slnx`,
      `docs/milestones/M1-skeleton.md`, and this session's retrospective.
- [ ] No commands, events, projections, or HTTP endpoints introduced.

## Open questions

- **How does `AddParticipantsModule()` interact with the host-level `AddPolecat()` call from M1-S3?**
  `src/CritterBids.Api/Program.cs` already calls `AddPolecat()` + `.IntegrateWithWolverine()` with the
  SQL Server connection string. A second `AddPolecat()` call in the BC module may register a separate
  named store rather than extend the existing default store. Before implementing:
  - Check `polecat-event-sourcing.md` references and consult the critter-stack-docs MCP server and/or
    Context7 (`/jasperfx/wolverine`, `/jasperfx/marten`) to confirm the correct extension pattern.
  - Determine whether Polecat exposes a `ConfigurePolecat()` (or equivalent) to extend an existing store
    from a BC module — the Marten equivalent is `services.ConfigureMarten()`.
  - If the correct pattern is a single store extended by BC modules: implement that.
  - If the correct pattern is multi-store (each SQL Server BC owns a named Polecat store): implement that
    and adjust how Program.cs wires them.
  - If the host-level `AddPolecat()` call should be removed and each BC module owns its store in full:
    remove the host-level call, update the comment in Program.cs, and document the decision.
  - **Do not guess. Flag the chosen path in the retrospective.** This is an architectural decision.

- **`AutoApplyTransactions()` scope with multi-store.** If `AddParticipantsModule()` introduces a second
  named Polecat store, confirm whether Wolverine's global `AutoApplyTransactions()` policy (set at host
  level in M1-S3) applies automatically to both stores, or whether per-store configuration is needed.

- **`UseSystemTextJsonForSerialization` — required or implicit?** Polecat 2.x targets System.Text.Json
  only. Confirm whether the explicit `UseSystemTextJsonForSerialization(EnumStorage.AsString)` call is
  still required in BC module registration, or whether it is now the implicit default and the call is
  unnecessary.

- **`Polecat` vs `WolverineFx.Polecat` package reference in the BC project.** Both are already pinned
  in `Directory.Packages.props`. Determine which (or both) the `CritterBids.Participants` project
  needs to reference to access `AddPolecat()`, `AutoCreate`, and `StreamIdentity` at the BC level.

- If any root configuration file (`.slnx`, `Directory.Build.props`, etc.) conflicts with this prompt's
  scope, flag and stop before editing. (Carried forward from M1-S1 retro finding #2.)
