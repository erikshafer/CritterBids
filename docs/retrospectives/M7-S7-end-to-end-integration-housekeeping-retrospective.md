# M7-S7: End-to-End Integration & Housekeeping - Retrospective

**Date:** 2026-06-03
**Milestone:** M7 - Operations BC
**Slice:** S7 of 7 - cross-BC end-to-end journey test, route audit, attribute audit, doc updates, milestone close
**Agent:** Windsurf (Cascade)
**Prompt:** `docs/prompts/implementations/M7-S7-end-to-end-integration-housekeeping.md`

## Baseline

- Build clean at session start: 0 errors / 0 warnings across `CritterBids.slnx`; full suite green
  (280 tests, M7-S6 close).
- All six Operations views existed (S2–S5): `SettlementQueueView`, `LotBoardView`, `BidActivityEntry`,
  `OperationsObligationsView`, `SessionActivityView`, `ParticipantActivityView`. All seven staff query
  endpoints were gated with `[Authorize(Policy = "StaffOnly")]` (S6).
- Five `operations-*` consumer queues declared in `Program.cs` with `ListenToRabbitQueue` and
  `AutoProvision` (S2–S5): `operations-settlement-events`, `operations-auctions-events`,
  `operations-selling-events`, `operations-obligations-events`, `operations-participants-events`.
- `bounded-contexts.md` still listed Operations as "Planned" / "(target M7+)".
- `wolverine-signalr/SKILL.md` listed OperationsHub as "MVP passphrase, production JWT claims" —
  not updated for the ADR-024 `StaffOnly` gating that landed in S6.

## What was delivered

### 1. Cross-BC end-to-end journey test

One `[Fact]` in `OperationsEndToEndJourneyTest.CrossBc_Consume_Project_Query_Journey_Through_All_Operator_Views`
that dispatches a representative integration event from every source BC family (Settlement, Selling,
Auctions, Obligations, Participants), waits via the tracked-session pattern (`InvokeMessageAndWaitAsync` /
`SendMessageAndWaitAsync` — never a sleep), and reads back every projected view through the seven
StaffOnly-gated query endpoints with a valid staff token.

**Fixture:** `JourneyTestFixture` — boots from `Program.cs` via `AlbaHost.For<Program>()` (so full
Wolverine routing, Separated-handler wiring, and `MapWolverineEndpoints()` are exercised), overlays
Testcontainers PostgreSQL + staff auth, and limits handler discovery to the Operations BC only via six
`IWolverineExtension` discovery exclusions.

**Pure-consumer assertion:** `tracked.Sent.AllMessages().ShouldBeEmpty()` on every Invoke dispatch,
confirming ADR-014 Path A: Operations handlers produce no outgoing messages.

**BidPlaced sticky-queue finding:** BidPlaced has two Operations handlers (`LotBoardAuctionsHandler` +
`BidActivityHandler`) on sticky Separated queues. `InvokeAsync` cannot resolve the ambiguity; the test
uses `SendMessageAndWaitAsync` for BidPlaced only — matching the established pattern from
`BidActivityHandlerTests`. The pure-consumer assertion for these handlers is proven via the single-handler
Invoke path in `LotBoardHandlerTests`.

### 2. Program.cs route audit

Verified all five `operations-*` consumer queues are declared with `ListenToRabbitQueue` and covered by
the top-level `AutoProvision()` call. Publish routes for all consumed event types are present. No changes
required — the routing topology was correct as delivered through S2–S5.

### 3. Attribute audit

All M7-introduced/modified endpoints carry explicit auth attributes:

- **OperationsQueryEndpoints** — all 7 endpoints: `[Authorize(Policy = "StaffOnly")]`
- **WithdrawListingEndpoint** — `[Authorize(Policy = "StaffOnly")]`
- **SessionStaffEndpoints** (CreateSession, StartSession) — both `[Authorize(Policy = "StaffOnly")]`
- **ResolveDisputeEndpoint** — `[Authorize(Policy = "StaffOnly")]`
- **OperationsHub** — `[Authorize(Policy = "StaffOnly")]`
- **Participant/Selling endpoints** — `[AllowAnonymous]` (unchanged)
- **CatalogEndpoints** — `[AllowAnonymous]` (unchanged)

### 4. Documentation updates

- **`bounded-contexts.md`** — Operations status flipped from "Planned" to "Active (M7)". Heading
  changed from "7 implemented + 1 planned" to "8 implemented". Storage table and integration topology
  note updated.
- **`wolverine-signalr/SKILL.md`** — OperationsHub table row updated to reflect `[Authorize(Policy =
  "StaffOnly")]` gating (ADR-024). New "Hub authentication (ADR-024)" section documenting the
  `X-Staff-Token` / `access_token` query-string pattern for WebSocket negotiation.

### 5. Package addition

- `Alba` added to `CritterBids.Api.Tests.csproj` — required by the `JourneyTestFixture` which uses
  `AlbaHost.For<Program>()`. The existing S6 `StaffAuthTestFixture` intentionally did not use Alba
  (ADR-024 item 8 scoped it out); the journey test has different needs (tracked sessions + endpoint
  queries, no SignalR WebSocket).

## Key learnings

1. **Separated handler behavior + InvokeAsync = single-handler-per-message-type only.** With
   `MultipleHandlerBehavior.Separated`, `InvokeAsync` fails with `NoHandlerForEndpointException` when a
   message type has multiple handler chains on sticky queue endpoints. `SendMessageAndWaitAsync` routes
   through the pipeline and reaches all sticky endpoints. This was already known and documented in
   `BidActivityHandlerTests` but was rediscovered here when building the journey fixture from scratch.

2. **AlbaHost.For<Program>() vs hand-built WebApplication.** A hand-built `WebApplication` that
   registers the same services but skips `Program.cs`'s RabbitMQ route configuration loses the
   endpoint-to-handler binding that Separated behavior depends on. Booting from `Program.cs` via Alba
   preserves the full routing topology even with `DisableAllExternalWolverineTransports()`.

3. **Discovery exclusion via IWolverineExtension.** Registering `IWolverineExtension` implementations
   that call `CustomizeHandlerDiscovery` from `ConfigureServices` integrates cleanly with
   `AlbaHost.For<Program>()` — the extensions fire after `Program.cs`'s `UseWolverine` block, adding
   exclusions without disturbing the main routing.

## Test count

**281 tests** (280 at S6 close + 1 journey test added in S7).

Breakdown: Auctions 65, Api 41, Operations 38, Selling 36, Relay 36, Settlement 25, Listings 20,
Obligations 13, Participants 6, Contracts 1.

## Exit checklist

- [x] Journey test exercises all six operator views through consume → project → query on real Postgres.
- [x] Pure-consumer contract (tracked.Sent empty) asserted on all Invoke-dispatched events.
- [x] Program.cs route audit: all five operations-* queues declared with ListenToRabbitQueue + AutoProvision.
- [x] Attribute audit: all M7 endpoints carry explicit StaffOnly or AllowAnonymous.
- [x] `bounded-contexts.md` Operations status flipped to Active.
- [x] `wolverine-signalr/SKILL.md` updated with ADR-024 hub gating.
- [x] `dotnet build CritterBids.slnx` 0 warnings / 0 errors; full `dotnet test` 281/281 green.
- [x] No "Event"-suffixed type name; no "paddle" reference; `sealed record` / `IReadOnlyList<T>` held.
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

- **M8** is the React frontend milestone. The Operations BC backend is complete; the staff dashboard
  SPA that renders all this is M8 scope.
- **Per-user / role-based auth** (beyond the single shared staff secret) remains post-MVP per ADR-024.
- **Hub group targeting** (`Clients.All` → per-staff-group) is deferred past M7 per ADR-024 item 6.
- **CI matrix extension** — Settlement, Obligations, Relay, and Operations tests still run only on
  developer machines (Risk #1 from STATUS.md). A CI PR is a natural follow-up.
- **STATUS.md regeneration** — should be refreshed to reflect M7 complete and the 281-test baseline.
