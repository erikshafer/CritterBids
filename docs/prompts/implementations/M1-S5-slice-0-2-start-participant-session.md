# M1-S5: Slice 0.2 — Start Participant Session

**Milestone:** M1 — Skeleton
**Slice:** S5 — Slice 0.2: `StartParticipantSession`
**Agent:** @PSA
**Estimated scope:** one PR, ~8 new files + 4 file modifications + 3 package pins + 1 new ADR

## Goal

Implement Slice 0.2 in full: the `StartParticipantSession` command, the `ParticipantSessionStarted`
domain event, the Wolverine HTTP endpoint at `POST /api/participants/session`, the first `Apply()`
method on the `Participant` aggregate, the integration test fixture for the Participants BC, and the
two integration tests from `001-scenarios.md` §0.2. Both tests must pass against a real SQL Server
instance via Testcontainers.

This session also establishes the `[AllowAnonymous]` M1 override convention (first endpoint), wires
`app.MapWolverineEndpoints()` in the API host, pins the three new packages this work requires, authors
`docs/decisions/0001-uuid-strategy.md` as **Proposed**, and verifies S4-F4 (Wolverine inbox/outbox
tables land in the `wolverine` schema, not `participants`).

At session close: 5 tests pass (3 existing smoke tests + 2 new Participants integration tests), the
solution builds clean, and `dotnet run --project src/CritterBids.AppHost --launch-profile http` boots
all services.

## Context to load

- `docs/milestones/M1-skeleton.md` — §3 non-goals, §6 conventions (`[AllowAnonymous]` M1 override,
  UUID v5, `OutgoingMessages`), §7 acceptance tests (exact test method names), §8 M1-D3 (UUID v7 ADR),
  §9 S5 row to update
- `docs/workshops/001-scenarios.md` — §0.2 scenarios (two scenarios, Given/When/Then specs)
- `docs/retrospectives/M1-S4-participants-bc-scaffold.md` — current state of all Participants files,
  S4-F4 (inbox/outbox schema verification), S4-F5 (source generator verification), key learnings
- `docs/skills/polecat-event-sourcing.md` — `PolecatOps.StartStream<T>()` pattern, `Apply()` method
  conventions, `polecat-event-sourcing.md` §Polecat BC TestFixture Pattern (Polecat-specific test
  fixture shape and cleanup helpers)
- `docs/skills/wolverine-message-handlers.md` — `[WolverinePost]`, `IStartStream`, `CreationResponse`,
  railway programming, anti-pattern #9 (StartStream must return `IStartStream`)
- `docs/skills/critter-stack-testing-patterns.md` — `ExecuteAndWaitAsync`, `TrackedHttpCall`, test
  isolation checklist, event-sourcing race condition avoidance
- `docs/skills/csharp-coding-standards.md` — `sealed record`, `DateTimeOffset.UtcNow`,
  `IReadOnlyList<T>`, record field nullability rules
- `docs/prompts/README.md` — the ten rules this prompt obeys

## In scope

### `CritterBids.Participants` — new files

**`ParticipantSessionStarted` domain event**

- `sealed record` with: `ParticipantId` (Guid), `DisplayName` (string), `BidderId` (string),
  `CreditCeiling` (decimal), `StartedAt` (DateTimeOffset). Aggregate ID (`ParticipantId`) must be
  the first property per domain event conventions.
- No "Event" suffix. Past-tense name. File: `Features/StartParticipantSession/ParticipantSessionStarted.cs`.

**`StartParticipantSession` command and handler**

- `StartParticipantSession` — `sealed record` with no fields. The system generates all output values.
- Handler produces a new `Participant` stream via `PolecatOps.StartStream<Participant>(...)`. Returns
  `(CreationResponse<Guid>, IStartStream)` — HTTP response type first per anti-pattern #3.
- HTTP endpoint: `[WolverinePost("/api/participants/session")]`.
- `[AllowAnonymous]` on the endpoint — **this is the M1 override convention being established for
  the first time**. See §6 of the milestone doc and **Conventions** section below.
- Stream ID: see **Open questions** — decide and document in the retro.
- `DisplayName` generation: produces a display name that is unique across concurrent sessions. See
  **Open questions** for the algorithm decision.
- `BidderId` assignment: see **Open questions**.
- `CreditCeiling` assignment: a randomly assigned decimal in a reasonable demo range (e.g., 200–1000).
  The exact range is an implementation choice; document it in the retro. The value is hidden from
  the participant (never returned in HTTP responses).
- File: `Features/StartParticipantSession/StartParticipantSession.cs` (command + handler colocated).

**`Participant` aggregate — update `Apply()`**

- Add `Apply(ParticipantSessionStarted @event)` method. Update `Id` and any state properties needed
  to support the M1-S6 `RegisterAsSeller` handler (which requires knowing whether a session has been
  started and whether the participant is already a seller — design now what state M1-S6 will read).
- Update `{ get; set; }` property for `Id`. Add any additional mutable properties that `Apply()` sets.
- File: `src/CritterBids.Participants/Participant.cs` (modify existing).

### `CritterBids.Api` — modifications

**`Program.cs`**

- Add `app.MapWolverineEndpoints()` before `app.Run()`.
- Add auth middleware stubs (`app.UseAuthentication()` / `app.UseAuthorization()`) before
  `MapWolverineEndpoints()` — required for `[AllowAnonymous]` attribute resolution. See **Open
  questions** for whether minimal ASP.NET Core auth setup is needed alongside this.

**`CritterBids.Api.csproj`**

- Add `<PackageReference Include="WolverineFx.Http" />` — required for `app.MapWolverineEndpoints()`.

### `CritterBids.Participants.csproj` — package addition

- Add `<PackageReference Include="WolverineFx.Http.Polecat" />` — provides `[WolverinePost]`,
  `PolecatOps.StartStream<T>()`, `CreationResponse`, and `IStartStream` for HTTP Polecat endpoints.

### `Directory.Packages.props` — new pins

Pin the three packages this session first requires. Verify current stable versions at session time;
do not silently use stale pins. Packages to pin:

- `WolverineFx.Http` — same major.minor version family as `WolverineFx` (5.30.0). Confirm current.
- `WolverineFx.Http.Polecat` — same family. Confirm current.
- `Alba` — the `AlbaVersion` property in `Directory.Build.props` shows `8.4.0`; verify current stable
  before pinning. If stale, use current and flag in retro (same pattern as `WolverineVersion` in S3).
- `Testcontainers.MsSql` — confirm current stable version at session time.

No `Version=` on any `<PackageReference>`.

### `CritterBids.Participants.Tests` — integration test infrastructure + tests

**`ParticipantsTestFixture.cs`**

- Follow the Polecat BC TestFixture Pattern from `polecat-event-sourcing.md`. Uses `MsSqlBuilder`
  (Testcontainers), bootstraps `AlbaHost.For<Program>`, overrides the Polecat connection string with
  the container's connection string, calls `DisableAllExternalWolverineTransports()`.
- Exposes `Host`, `GetDocumentSession()`, `GetDocumentStore()`, `CleanAllPolecatDataAsync()`,
  `ExecuteAndWaitAsync<T>()`, and `TrackedHttpCall()`.
- No auth setup required in M1 — all endpoints are `[AllowAnonymous]`. See **Open questions**.
- File: `Fixtures/ParticipantsTestFixture.cs`.

**`ParticipantsTestCollection.cs`**

- `[CollectionDefinition]` + `ICollectionFixture<ParticipantsTestFixture>` for sequential execution.
- File: `Fixtures/ParticipantsTestCollection.cs`.

**`StartParticipantSessionTests.cs`**

Two test methods per §7 of the milestone doc:

| Scenario from `001-scenarios.md` §0.2 | Test method |
|---|---|
| Happy path — new participant session | `StartingSession_FromEmptyStream_ProducesParticipantSessionStarted` |
| Display name is unique within active sessions | `StartingSecondSession_ProducesDifferentDisplayName_ThanActiveSessions` |

Both tests assert against: (a) HTTP response shape (201 status, participant ID in response), and
(b) the Polecat event stream contents (event type present, key fields populated). Use
`ExecuteAndWaitAsync` + direct event store query to avoid race conditions (see skill doc).

File: `StartParticipantSession/StartParticipantSessionTests.cs`.

**`CritterBids.Participants.Tests.csproj` — package additions**

Add:
- `<ProjectReference>` to `src/CritterBids.Api` — required for `AlbaHost.For<Program>`.
- `<PackageReference Include="Alba" />`
- `<PackageReference Include="Testcontainers.MsSql" />`
- `<PackageReference Include="WolverineFx.Http" />` — for Wolverine test tracking helpers with HTTP.

### `docs/decisions/0001-uuid-strategy.md` — UUID strategy ADR as Proposed

Author this ADR with status **Proposed**. Key content per §8 M1-D3:

- v5 stays the convention for stream IDs where a natural business key exists (determinism is
  load-bearing for idempotent stream creation).
- v7 is under consideration for event row IDs and high-write projection IDs (insert locality,
  PostgreSQL range partitioning potential). Auctions bid events and Listings projections are the
  primary targets.
- Acceptance gates before moving from Proposed to Accepted: (a) verify Marten 8 / Polecat 2 expose
  event row ID generation; (b) verify v7 support or path; (c) JasperFx team input.
- Record what was actually used for the `Participant` stream ID in M1-S5 (UUID v7 or v5) and why.

### Boot verification — S4-F4

Verify that after M1-S5 changes trigger Polecat schema creation on first app boot:
- Wolverine inbox/outbox tables land in the `wolverine` schema (not `participants`).
- Polecat event/stream tables land in the `participants` schema.
- Document the result in the retrospective. If the schemas differ from expected, flag and do not
  merge until resolved.

### Polecat source generator verification — S4-F5

Verify that the Polecat source generator activates for the `Participant` aggregate with its first
`Apply()` method and emits valid code (no compile-time warnings or errors from the generator).
Note the result in the retrospective.

### `docs/milestones/M1-skeleton.md` §9 — doc fix

Update the S5 row from `*TBD*` to `docs/prompts/implementations/M1-S5-slice-0-2-start-participant-session.md`.

## Explicitly out of scope

- **Slice 0.3** (`RegisterAsSeller`, `SellerRegistrationCompleted`) — M1-S6.
- **`RegisterAsSellerTests.cs`** — M1-S6.
- **Real authentication scheme** — M1 uses `[AllowAnonymous]` everywhere (§3 non-goal).
- **`CritterBids.Contracts` additions** — `SellerRegistrationCompleted` is M1-S6 scope.
- **Projections** — milestone doc §5 states "No projections in M1 (Participants has no views)."
- **Display name uniqueness via projection** — no projection for tracking active sessions; the
  generation algorithm must produce uniqueness without a DB read (see **Open questions**).
- **`docs/skills/aspire.md`** and `polecat-event-sourcing.md` 🟡 → ✅ updates — M1-S7.
- **Orphaned `WolverineVersion` / `MartenVersion` / `AlbaVersion` property cleanup** — M1-S7.
- **`docs/skills/polecat-event-sourcing.md` fix** for `UseSystemTextJsonForSerialization` error (S4-F1) — M1-S7.
- **Named Polecat store refactoring** (S4-F2) — M2 planning.
- **`OutgoingMessages` for `SellerRegistrationCompleted`** — M1-S6.
- **No CI workflow changes.**
- **No frontend, no SignalR.**

## Conventions to pin or follow

- **`[AllowAnonymous]` — M1 override (first use).** Apply `[AllowAnonymous]` to every HTTP endpoint
  class or method in `CritterBids.Participants` for the duration of M1. This overrides the global
  `[Authorize]` convention from `CLAUDE.md`. The override does not extend to M2. Document the
  establishment of this pattern in the retrospective.
- **`sealed record` for all commands and events** — no exceptions.
- **`DateTimeOffset.UtcNow` for all timestamps** — never `DateTime`.
- **Aggregate ID as first property** on domain events.
- **No "Event" suffix** on domain event type names.
- **`app.MapWolverineEndpoints()`** must be called in `Program.cs` before `app.Run()`.
- **`PolecatOps.StartStream<Participant>(id, event)`** — not `session.Events.StartStream()` directly
  (anti-pattern #9 in `wolverine-message-handlers.md`). Return `IStartStream` from the handler.
- **Tuple order:** HTTP response type (`CreationResponse`) before `IStartStream` in the return tuple
  (anti-pattern #3).
- **No `Version=` on any `<PackageReference>`.**

## Acceptance criteria

- [ ] `ParticipantSessionStarted` sealed record exists with `ParticipantId` as the first property.
- [ ] `StartParticipantSession` command sealed record exists with no fields.
- [ ] `[WolverinePost("/api/participants/session")]` handler exists, returns
      `(CreationResponse<Guid>, IStartStream)`, uses `PolecatOps.StartStream<Participant>`.
- [ ] `[AllowAnonymous]` attribute is present on the endpoint.
- [ ] `Participant.Apply(ParticipantSessionStarted)` method exists and updates aggregate state.
- [ ] `app.MapWolverineEndpoints()` is called in `Program.cs`.
- [ ] `WolverineFx.Http` is referenced by `CritterBids.Api.csproj`.
- [ ] `WolverineFx.Http.Polecat` is referenced by `CritterBids.Participants.csproj`.
- [ ] `Directory.Packages.props` contains pins for `WolverineFx.Http`, `WolverineFx.Http.Polecat`,
      `Alba`, and `Testcontainers.MsSql`. No `Version=` on any `<PackageReference>` anywhere.
- [ ] `ParticipantsTestFixture` exists, boots `AlbaHost.For<Program>`, uses a Testcontainers SQL
      Server container, and calls `DisableAllExternalWolverineTransports()`.
- [ ] `ParticipantsTestCollection` defines the xUnit collection fixture.
- [ ] `StartParticipantSessionTests.cs` exists with both test methods from §7 of the milestone doc.
- [ ] `dotnet test` reports 5 passing tests, zero failing (3 smoke + 2 integration).
- [ ] `dotnet build` succeeds with zero errors and zero warnings from new or modified projects.
- [ ] `POST /api/participants/session` returns 201 and a participant ID in the response body
      (verified via integration test).
- [ ] S4-F4 verified: Wolverine inbox/outbox tables are in the `wolverine` schema; Polecat event
      tables are in the `participants` schema. Result documented in retrospective.
- [ ] S4-F5 verified: Polecat source generator activated; no generator-related compile errors.
- [ ] `docs/decisions/0001-uuid-strategy.md` exists as Proposed.
- [ ] `docs/milestones/M1-skeleton.md` §9 S5 row updated from `*TBD*` to the prompt filename.
- [ ] No files created or modified outside: `src/CritterBids.Participants/`,
      `src/CritterBids.Api/`, `tests/CritterBids.Participants.Tests/`,
      `Directory.Packages.props`, `docs/decisions/0001-uuid-strategy.md`,
      `docs/milestones/M1-skeleton.md`, and this session's retrospective.
- [ ] No commands, events, or endpoints for Slice 0.3 introduced.

## Open questions

- **Stream ID algorithm for `StartParticipantSession`.** The `ParticipantsNamespace` constant exists
  (M1-D4 resolved). However, `StartParticipantSession {}` has no natural business key from which to
  derive a deterministic UUID v5. Options:
  - Use `Guid.CreateVersion7()` — unique, time-ordered, no determinism from business key. Correct for
    truly anonymous participants.
  - Defer UUID v5 to BCs that have a natural key; use v7 for Participants. Document the decision in
    both the retro and `docs/decisions/0001-uuid-strategy.md`.
  - **Do not guess.** Make a pragmatic choice for M1, record it clearly, and note whether the QR-code
    session-token use case (deferred in `001-scenarios.md` §0.2) would change this decision later.

- **Display name generation algorithm.** The test `StartingSecondSession_ProducesDifferentDisplayName_ThanActiveSessions`
  requires two sessions started in sequence to have different display names. The milestone doc says
  "No projections in M1" — you cannot query existing active sessions for a collision check. Choose
  an algorithm that produces uniqueness by design rather than by checking the store:
  - UUID-derived display name (hash the stream ID into an adjective+animal+number string) — always
    unique because stream IDs are unique.
  - Timestamp-seeded random selection from a word list — low collision probability; acceptable for M1
    demo scale.
  - The choice does not need to be perfect for M1. Document the algorithm in the retro.

- **`BidderId` assignment.** The scenario shows `BidderId: "Bidder 42"` — implying a sequential
  number. A sequential counter in an event-sourced system requires care (no mutable counters in
  aggregates). Options for M1:
  - Use a timestamp-derived ordinal or short random string as BidderId.
  - Use a UUID-derived short identifier.
  - Defer true sequential bidder numbers to a later milestone when a counter mechanism is in scope.
  Record the M1 approach in the retro.

- **Auth middleware in `Program.cs`.** `app.UseAuthentication()` and `app.UseAuthorization()` are
  normally added before `MapWolverineEndpoints()`. In M1, all endpoints are `[AllowAnonymous]` so
  the auth system is not enforced. Confirm whether adding these middleware calls is needed now (for
  correctness/convention) or can be deferred to when real auth lands (M6). Either choice is
  acceptable — document it in the retro.

- **Test fixture connection string override.** The test fixture must replace the host's Polecat
  connection string (which points to the Aspire-provisioned SQL Server) with the Testcontainers
  container's connection string. The `critter-stack-testing-patterns.md` fixture pattern shows
  `services.AddPolecat(opts => opts.Connection(...))` — however, given M1-S4's finding that
  `ConfigurePolecat()` is the correct extension pattern, verify whether the override should use:
  - `services.ConfigurePolecat(opts => { opts.ConnectionString = testConnStr; })`, or
  - `builder.UseSetting("ConnectionStrings:sqlserver", testConnStr)` in the `AlbaHost.For<Program>`
    builder callback, which overrides the value before `Program.cs` reads it from `IConfiguration`.
  The second option is likely cleaner. Flag the chosen approach in the retro and update
  `critter-stack-testing-patterns.md` if the fixture example is incorrect.

- **`CritterBids.Participants.Tests.csproj` reference to `CritterBids.Api`.** `AlbaHost.For<Program>`
  requires access to the `Program` class, which lives in `CritterBids.Api`. The test project currently
  references only `CritterBids.Participants`. Add a `<ProjectReference>` to `CritterBids.Api` — confirm
  that this does not violate any modular monolith rule. (Test projects are outside the module
  boundary; the rule "no BC project references another BC project" applies to `src/` only.)

- If any root configuration file conflicts with this prompt's scope, flag and stop before editing.
  (Carried forward from M1-S1 retro finding #2.)
