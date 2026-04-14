# M1-S6: Slice 0.3 — Register as Seller

**Milestone:** M1 — Skeleton
**Slice:** S6 — Slice 0.3: `RegisterAsSeller`
**Agent:** @PSA
**Estimated scope:** one PR, ~4 new files + 3 file modifications + 1 new project reference

## Goal

Implement Slice 0.3 in full: the `RegisterAsSeller` command, its domain event, the Wolverine HTTP
endpoint at `POST /api/participants/{id}/register-seller`, the second `Apply()` method on the
`Participant` aggregate, the `SellerRegistrationCompleted` integration event in `CritterBids.Contracts`,
`OutgoingMessages` publishing, and the three integration tests from `001-scenarios.md` §0.3.

This session closes all remaining implementation scope for M1. At session close: 7 tests pass
(4 from prior sessions + 3 new), the solution builds clean, and the S4-F4 schema verification is
completed and documented. Only retrospective and skills work remains (M1-S7).

## Context to load

- `docs/milestones/M1-skeleton.md` — §2 scope (Slice 0.3), §3 non-goals, §6 conventions
  (`OutgoingMessages`, `[AllowAnonymous]` M1 override), §7 acceptance tests (exact test method
  names for `RegisterAsSellerTests.cs`), §9 S6 row to update
- `docs/workshops/001-scenarios.md` — §0.3 scenarios (three scenarios, Given/When/Then specs)
- `docs/retrospectives/M1-S5-slice-0-2-start-participant-session.md` — current build state
  (key learnings 1–7), S5h architecture (module-owned Polecat chain), S5f fixture pattern
  (`ConfigureServices` override, `DisableAllExternalWolverineTransports`), S5g assertion pattern
  (`Location` header for ID extraction)
- `docs/skills/polecat-event-sourcing.md` — `IAppendToStream` pattern, second `Apply()` conventions,
  rejection guard pattern
- `docs/skills/wolverine-message-handlers.md` — `OutgoingMessages` publishing, railway programming /
  guard-return rejection pattern, `[WolverinePost]` with route parameter
- `docs/skills/critter-stack-testing-patterns.md` — `ExecuteAndWaitAsync`, outgoing message
  verification in integration tests, test isolation (`CleanAllPolecatDataAsync`)
- `docs/skills/csharp-coding-standards.md` — `sealed record`, nullability, `DateTimeOffset.UtcNow`

## In scope

### `CritterBids.Participants` — new files and modifications

**Domain event**

A `sealed record` domain event representing a participant having successfully registered as a seller.
See **Open questions** for the naming decision. Aggregate ID (`ParticipantId`) must be the first
property. `CompletedAt` (DateTimeOffset) is required. No "Event" suffix. File under
`Features/RegisterAsSeller/`.

**`RegisterAsSeller` command and handler**

- `RegisterAsSeller` — `sealed record` carrying `ParticipantId` (Guid), bound from the `{id}` route
  segment.
- HTTP endpoint: `[WolverinePost("/api/participants/{id}/register-seller")]`.
- `[AllowAnonymous]` — M1 override still in effect.
- Handler appends to an existing `Participant` stream via `IAppendToStream` (not `IStartStream` —
  the stream already exists from `StartParticipantSession`).
- Rejection scenario (a): participant has no active session — return an appropriate HTTP problem
  response, no event appended.
- Rejection scenario (b): participant is already registered as a seller — return an appropriate HTTP
  problem response, no event appended.
- Happy path: appends the domain event, publishes `SellerRegistrationCompleted` to `OutgoingMessages`.
- File: `Features/RegisterAsSeller/RegisterAsSeller.cs` (command + handler colocated).

**`Participant` aggregate — second `Apply()` method**

- Before writing the handler: read `src/CritterBids.Participants/Participant.cs` and confirm the
  properties S5 set up for session tracking (e.g., `IsSessionStarted`) and seller status (e.g.,
  `IsSeller`). See **Open questions** for what to do if either is absent or misnamed.
- Add `Apply(«DomainEvent»)` that sets `IsSeller` (or equivalent) to `true`.
- Do not alter the S5 `Apply(ParticipantSessionStarted)` logic unless correcting a defect. Document
  any corrections in the retrospective.
- File: `src/CritterBids.Participants/Participant.cs` (modify existing).

**`CritterBids.Participants.csproj` — project reference**

- Add `<ProjectReference>` to `CritterBids.Contracts` so the handler can construct and enqueue
  `SellerRegistrationCompleted`.

### `CritterBids.Contracts` — new file

**`SellerRegistrationCompleted` integration event**

- `sealed record` in `CritterBids.Contracts`. Fields: `ParticipantId` (Guid) first, `CompletedAt`
  (DateTimeOffset).
- This is the type enqueued via `OutgoingMessages`. It is distinct from the Participants BC domain
  event even if their field shapes look similar. M2 consumers will reference `CritterBids.Contracts`,
  not `CritterBids.Participants`.
- File: `src/CritterBids.Contracts/SellerRegistrationCompleted.cs`.

### `CritterBids.Participants.Tests` — new test file

**`RegisterAsSellerTests.cs`**

Three test methods per §7 of the milestone doc:

| Scenario from `001-scenarios.md` §0.3 | Test method |
|---|---|
| Happy path — participant becomes a seller | `RegisterAsSeller_WithActiveSession_ProducesSellerRegistrationCompleted` |
| Reject — no active session | `RegisterAsSeller_WithoutActiveSession_IsRejected` |
| Reject — already registered | `RegisterAsSeller_WhenAlreadyRegistered_IsRejectedIdempotently` |

Happy-path test must assert: (a) expected HTTP success status, (b) the domain event present in the
Polecat event stream via direct store query, (c) `SellerRegistrationCompleted` enqueued on the
Wolverine outbox. See **Open questions** for outbox assertion pattern.

Rejection tests must assert: (a) HTTP 4xx status, (b) no new events appended to the stream.

Use `ExecuteAndWaitAsync` + direct event store query for all assertions. Use `CleanAllPolecatDataAsync()`
between tests per the isolation checklist. Reuse `ParticipantsTestFixture` and `ParticipantsTestCollection`
from S5 — do not create a second fixture.

File: `RegisterAsSeller/RegisterAsSellerTests.cs`.

### Boot verification — S4-F4 (deferred from S5)

After tests pass, run `dotnet run --project src/CritterBids.AppHost` and verify via `sqlcmd` or
SSMS (or the `playwright` MCP against the Aspire dashboard):

- Polecat event and stream tables exist in the `participants` schema.
- Wolverine inbox/outbox tables exist in the `wolverine` schema.

Document the result in the retrospective. If schemas are incorrect, do not merge — flag and stop.

### `docs/milestones/M1-skeleton.md` §9 — doc fix

Update the S6 row from `*TBD*` to `docs/prompts/M1-S6-slice-0-3-register-as-seller.md`.

## Explicitly out of scope

- **`docs/skills/aspire.md` authoring** — M1-S7.
- **`docs/skills/polecat-event-sourcing.md`** 🟡 → ✅ update — M1-S7.
- **S4-F1 `UseSystemTextJsonForSerialization` skill doc correction** — M1-S7.
- **Fixture pattern updates to `critter-stack-testing-patterns.md`** — M1-S7.
- **M1 retrospective document** — M1-S7.
- **`docs/decisions/0001-uuid-strategy.md` edits** — already Proposed from S5; no changes unless S6
  reveals a blocking gap.
- **Named Polecat store refactoring (S4-F2)** — M2 planning.
- **Real authentication scheme** — M1 uses `[AllowAnonymous]` everywhere (§3 non-goal).
- **Any M2 scope** — Selling BC, Marten, `RegisteredSellers` projection, M2 consumers of
  `SellerRegistrationCompleted`.
- **Integration tests beyond the three `RegisterAsSellerTests` methods from §7.**
- **No CI workflow changes.**
- **No frontend, no SignalR.**

## Conventions to pin or follow

- **`[AllowAnonymous]`** — M1 override still in effect for the `RegisterAsSeller` endpoint.
- **`OutgoingMessages`** — the only correct mechanism for publishing `SellerRegistrationCompleted`.
  No `IMessageBus` direct publish.
- **`IAppendToStream`** — correct return type for appending to an existing aggregate stream.
  `IStartStream` would be wrong here.
- **`sealed record` for all commands, domain events, and integration events** — no exceptions.
- **Aggregate ID as first property** on all domain events and integration events.
- **No "Event" suffix** on domain event type names.
- **`DateTimeOffset.UtcNow` for all timestamps.**
- **No `Version=` on any `<PackageReference>`.**

## Acceptance criteria

- [ ] Domain event `sealed record` exists in `CritterBids.Participants` with `ParticipantId` as
      first property, `CompletedAt` (DateTimeOffset) present, no "Event" suffix.
- [ ] `RegisterAsSeller` command `sealed record` exists with `ParticipantId` (Guid).
- [ ] `[WolverinePost("/api/participants/{id}/register-seller")]` handler exists, appends domain event
      via `IAppendToStream`, publishes `SellerRegistrationCompleted` via `OutgoingMessages` on happy path.
- [ ] `[AllowAnonymous]` attribute present on the endpoint.
- [ ] Handler rejects when no active session — returns 4xx, no event appended.
- [ ] Handler rejects when already registered — returns 4xx, no event appended.
- [ ] `Participant.Apply(«DomainEvent»)` method exists and sets seller-status property to `true`.
- [ ] `Participant` retains `Apply(ParticipantSessionStarted)` from S5 — no regression.
- [ ] `SellerRegistrationCompleted` `sealed record` exists in `CritterBids.Contracts` with
      `ParticipantId` first, `CompletedAt` field.
- [ ] `CritterBids.Participants.csproj` contains a `<ProjectReference>` to `CritterBids.Contracts`.
- [ ] `RegisterAsSellerTests.cs` exists with all three test methods from §7 of the milestone doc.
- [ ] Happy-path test asserts: HTTP success status, domain event in Polecat stream, `SellerRegistrationCompleted`
      enqueued on Wolverine outbox.
- [ ] Rejection tests assert: HTTP 4xx status, no new event in stream.
- [ ] `dotnet test` reports 7 passing tests (4 existing + 3 new), zero failing.
- [ ] `dotnet build` succeeds with zero errors and zero warnings.
- [ ] S4-F4 verified: Polecat tables in `participants` schema, Wolverine tables in `wolverine` schema.
      Result documented in the retrospective.
- [ ] `docs/milestones/M1-skeleton.md` §9 S6 row updated from `*TBD*` to this prompt's filename.
- [ ] No files created or modified outside: `src/CritterBids.Participants/`,
      `src/CritterBids.Contracts/`, `tests/CritterBids.Participants.Tests/`,
      `docs/milestones/M1-skeleton.md`, and this session's retrospective.
- [ ] No Slice 1.x commands, events, or endpoints introduced.

## Open questions

- **Domain event name.** The `001-scenarios.md` §0.3 "Then" block uses `SellerRegistrationCompleted`
  — this is the integration event shape in `CritterBids.Contracts`. The domain event on the
  `Participant` aggregate should be distinct (e.g., `SellerRegistered`) to avoid name collision
  between the BC and Contracts types, and to maintain the convention that domain events belong to
  the BC and integration events belong to Contracts. If both types shared the name, `CritterBids.Participants`
  and `CritterBids.Contracts` would each define a `SellerRegistrationCompleted` — a confusion vector.
  Make a pragmatic choice, document it in the retrospective, and record it as a convention for future
  BC slices that emit integration events.

- **Rejection response pattern.** The two rejection scenarios need an HTTP 4xx response with no
  event written to the stream. Consult `wolverine-message-handlers.md` and `critter-stack-docs` MCP
  for the correct pattern before trial-and-error. Do not guess at method names or return types — query
  the docs first (S5 key learning #7 applies here directly). Document the chosen pattern in the retro;
  it will be extracted into `wolverine-message-handlers.md` in M1-S7.

- **Verify `Participant` state from S5.** Before writing the handler, read
  `src/CritterBids.Participants/Participant.cs`. Confirm: (a) a property exists tracking whether a
  session has been started (enabling the "no active session" rejection guard); (b) a property exists
  or can be added for seller status (enabling the "already registered" rejection guard). If either is
  absent, add it in this session and document the addition in the retro. Use whatever names S5
  established — do not rename unless correcting a defect.

- **HTTP response status for the happy path.** `RegisterAsSeller` appends to an existing resource
  rather than creating one. Likely 200 OK rather than 201 Created. Confirm what Wolverine returns by
  default for append-type endpoints and whether the return type affects it. Document the chosen status
  and assert it explicitly in the happy-path test.

- **Outbox assertion pattern in integration tests.** Confirm the `critter-stack-testing-patterns.md`
  pattern for asserting that a specific integration event type was enqueued via `OutgoingMessages` in
  a test. If the pattern is absent from the skill doc, query `critter-stack-docs` MCP
  (`wolverinefx.net/llms-full.txt`) and document the finding in the retro — the skill doc will be
  updated in M1-S7.

- If any root configuration file conflicts with this prompt's scope, flag and stop before editing.
  (Carried forward from M1-S1.)
