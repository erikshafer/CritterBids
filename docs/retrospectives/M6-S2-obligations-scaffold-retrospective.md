# M6-S2: Obligations BC Scaffold + `SettlementCompleted` Saga-Start — Retrospective

**Date:** 2026-05-28
**Milestone:** M6 — Obligations BC + Relay BC
**Slice:** S2 of 7 — BC Scaffold + saga-start consumer
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M6-S2-obligations-scaffold.md`

## Baseline

- Branch `erikshafer/m6-s2-obligations-scaffold` off main @ the M6-S1 merge (PR #47, `8a32213`), even with `origin/main`.
- S1 had landed ADR-022, the four `CritterBids.Contracts.Obligations.*` stubs, and the demo-mode config *decision* (code deferred). No `CritterBids.Obligations` project existed; `ObligationsOptions.cs` did not exist.
- OpenSpec change `add-obligation-lifecycle` in progress — only task 1.6 (ADR-022) checked off.
- Full solution build green: 0 errors, 14 pre-existing NU1904 warnings (Marten 8.35.0 advisory; out of scope).
- Full suite baseline (non-Obligations): Contracts 1, Api 1, Listings 20, Participants 6, Selling 36, Settlement 25, Auctions 65 = 154 tests passing.

## Items completed

| Item | Description |
|------|-------------|
| S2a | `CritterBids.Obligations` project + slnx + Api `<ProjectReference>` + `AssemblyInfo` internals |
| S2b | `ObligationsIdentityNamespaces` + BC-internal `UuidV5` → deterministic `ObligationId` (UUID v5 from `ListingId`) |
| S2c | `ObligationsOptions` (production + demo durations, `DemoMode`, `Active` selector) — the code deferred from S1 |
| S2d | Saga state + types: `PostSaleCoordinationSaga`, `ObligationStatus`, `PostSaleCoordinationStarted`, `ObligationEventStream` |
| S2e | `SettlementCompletedHandler` — saga-start, computes `ShipByDeadline`, idempotent |
| S2f | `AddObligationsModule()` + `Program.cs` wiring (`obligations-settlement-events` route) |
| S2g | Cross-BC `ObligationsBcDiscoveryExclusion` in four sibling fixtures + the inline exclusion |
| S2h | `CritterBids.Obligations.Tests` — boots-clean, options-bind, saga-start, idempotent-start |
| S2i | `/opsx:apply add-obligation-lifecycle` — tasks 1.1–1.5, 2.1, 2.2, 3.1, 3.3, 9.2 checked off; `--strict` validation green |
| S2j | This retrospective + the backfilled S2 prompt |

## S2c: `ObligationsOptions`

**Why config, not `#if DEBUG`.** Confirms W005 Decision 4 / M6-S1 §S1b. The full post-sale lifecycle must run live in a conference-demo session (seconds) while production durations are days. `DemoMode` flips between two `ObligationsDurations` sets at runtime; integration tests inject short durations through the same `Active` selector. The saga's transitions are identical under either set — only the offsets differ.

| Setting | Production | Demo |
|---|---|---|
| `ReminderOffset` | 2 days | 5 s |
| `ShipByDeadline` | 5 days | 10 s |
| `AutoConfirmWindow` | 3 days | 10 s |

BC-internal config (binds from the `"Obligations"` appsettings section, consumed only inside the BC), so it lives with the project — not in `CritterBids.Contracts`, which stays integration-events-only. Matches the S1 decision exactly.

## S2e: `SettlementCompletedHandler` — saga-start

**Why a static handler outside the saga type.** Per `docs/skills/wolverine-sagas.md`, the Start pattern lives outside the saga so Wolverine can distinguish "create + persist" from "load existing and handle". Same shape as Settlement's `StartSettlementSagaHandler`.

**Handler signature after:**

```csharp
public static async Task<(PostSaleCoordinationSaga?, OutgoingMessages)> Handle(
    SettlementCompleted message,
    IDocumentSession session,
    IOptions<ObligationsOptions> options,
    CancellationToken cancellationToken)
```

**Idempotency at two layers.** (1) The deterministic UUID v5 `ObligationId`: the same `ListingId` always derives the same saga key. (2) An existing-saga `LoadAsync` guard: on re-delivery the handler returns `(null, new OutgoingMessages())` so no second obligation, stream, or event is created. `ShipByDeadline = startedAt + options.Value.Active.ShipByDeadline`; the deadline is carried on saga state and on `PostSaleCoordinationStarted` rather than a separate event (per `design.md`).

**Slice boundary held.** No `bus.ScheduleAsync()` timer scheduling — that is S3 (opsx 3.2 / 4 / 6). S2 stops at "scaffold + saga start".

| Metric | Settlement (`StartSettlementSagaHandler`) | Obligations (`SettlementCompletedHandler`) |
|---|---|---|
| Class type | `static` | `static` |
| Return type | `(SettlementSaga?, OutgoingMessages)` | `(PostSaleCoordinationSaga?, OutgoingMessages)` |
| Idempotent skip | `(null, empty)` after existing-saga check | `(null, empty)` after existing-saga check |
| `StartStream<Marker>` | `FinancialEventStream` | `ObligationEventStream` |

## S2d/S2e: `ObligationEventStream` needed a `Guid Id` — Discovery

**Symptom (first test run).**

```
Marten.Exceptions.InvalidDocumentException: Could not determine an 'id/Id' field or property for requested document type CritterBids.Obligations.ObligationEventStream
```

**Root cause.** The stream-marker type was authored as `public sealed class ObligationEventStream;` but is registered as a Marten document via `Schema.For<ObligationEventStream>()`. Marten requires every registered document to expose an `id/Id`.

**Resolution.** Added `public Guid Id { get; set; }`, mirroring Settlement's `FinancialEventStream`. Stream markers registered via `Schema.For<T>()` are documents and must carry an `Id`.

## S2h: idempotent-start test shape — Discovery

**Symptom.** A first attempt at `DuplicateSettlementCompleted_IsNoOp` dispatched `SettlementCompleted` through the bus **twice** via `InvokeMessageAndWaitAsync`. The second dispatch threw:

```
System.NullReferenceException
   at Marten.Generated...PostSaleCoordinationSagaDocumentStorage.AssignIdentity(PostSaleCoordinationSaga document, ...)
```

**Root cause.** Wolverine's generated saga-start wrapper inserts whatever saga the Start handler returns. On the duplicate, the handler correctly returns a `null` saga element, but the wrapper still routes it to `AssignIdentity(null)` → NRE. Re-dispatching a *start* message against a still-live saga is not a framework-supported path; changing the return type to the tuple did not avoid it.

**Resolution.** Aligned with the Settlement BC's own coverage posture. `tests/CritterBids.Settlement.Tests/SettlementSagaFailurePathsTests.cs` (the §1.3 comment block) records that Settlement does **not** exercise its idempotent-start path via double bus-dispatch — it covers it structurally through the deterministic-id helper and the existing-saga guard, and via direct handler invocation (cf. its `PendingSettlement_NotFound` test). The Obligations idempotent-start test now starts the saga via the bus once, then **invokes `SettlementCompletedHandler.Handle` directly** with a fresh session, asserting the returned saga element is `null`, `OutgoingMessages` is empty, the obligation stream still has exactly one event, and the saga remains at `AwaitingShipment`. The handler's existence-check is the correctness guarantee; the test exercises it directly rather than through an unsupported bus re-dispatch.

## Test results

| Phase | Obligations Tests | Full solution |
|-------|-------------------|---------------|
| After first run (bus double-dispatch) | 3/4 (idempotent-start NRE) | not run |
| After `ObligationEventStream.Id` fix | 3/4 (idempotent-start still NRE) | not run |
| After direct-invocation idempotent test | **4/4** | — |
| Session close | **4/4** | **158 / 158, 0 failures** |

Full suite at close: Contracts 1, Api 1, Obligations 4, Participants 6, Listings 20, Selling 36, Settlement 25, Auctions 65 = **158 tests, 0 failures**. The four sibling BCs whose fixtures gained the `ObligationsBcDiscoveryExclusion` (Settlement/Selling/Listings/Auctions) all stayed green — confirming the exclusion suppresses the Obligations handler without breaking their hosts. Test count delta: +4 (the new Obligations project).

## Build state at session close

- Errors: 0.
- Warnings: 14, all pre-existing NU1904 (Marten 8.35.0 advisory). Delta from baseline: 0. Not addressed — package remediation is out of scope.
- `CritterBids.Obligations` project: exists. `CritterBids.Relay` project: does not exist (S5).
- Saga-start handlers added: 1 (`SettlementCompletedHandler`). `bus.ScheduleAsync()` calls: 0 (deferred to S3). `MarkCompleted()` calls: 0 (no terminal path this slice).
- New `sealed record` config: 2 (`ObligationsOptions`, `ObligationsDurations`). New domain event records: 1 (`PostSaleCoordinationStarted`). "Event"-suffix names: 0. "paddle" references: 0.
- `AddMarten()` calls inside `AddObligationsModule()`: 0 (host owns the single one). Cross-BC `<ProjectReference>` from Obligations to another BC: 0 (Contracts only).

## Key learnings

1. **A Wolverine saga-start handler cannot cleanly no-op via a `null` return on a bus re-dispatch against a live saga.** The generated wrapper routes the returned saga (including `null`) into `AssignIdentity`, which NREs. Cover idempotent-start by **direct handler invocation** asserting the `(null, empty)` no-op — not by dispatching the start message twice through the bus. This is the established Settlement posture, now confirmed empirically for Obligations.
2. **Stream-marker types registered via `Schema.For<T>()` are Marten documents and must carry a `Guid Id`.** A bare marker class (`sealed class Foo;`) fails registration with `InvalidDocumentException`. Same shape as Settlement's `FinancialEventStream`.
3. **Cross-BC handler-discovery exclusions must be added to every fixture whose host discovers the new BC's assembly.** Adding `CritterBids.Obligations` to `Program.cs`'s `IncludeAssembly` means the four sibling fixtures (Settlement/Selling/Listings/Auctions) each need an `ObligationsBcDiscoveryExclusion`, or their hosts attempt to code-gen the Obligations handler against an unconfigured store. Participants was deliberately left unmodified (its minimal posture tolerates the foreign handler, matching the existing precedent).

## Findings against narrative

Narrative 006 (`docs/narratives/006-seller-fulfills-post-sale-obligation.md`) is the slice's anchor. The `SettlementCompleted` saga-start is the **prerequisite** for the narrative's journey, not one of its dramatised Moments (Moment 3 tracking, Moment 4 auto-confirm close land in S3–S4). The saga-start and `PostSaleCoordinationStarted` are consistent with narrative 006 as drafted — the post-sale coordination opens when settlement completes. No drift surfaced; no finding routed to any of the four lanes. The narrative's Document History gains no row this slice — its Moments are not yet code.

## Spec delta — landed?

Landed as written, with the saga-start portion the prompt scoped. The OpenSpec `add-obligation-lifecycle` change governs the capability's spec consequence and was authored/committed in the design phase; this slice did not modify its proposal/design/delta-spec, only checked off tasks in `tasks.md` (1.1–1.5, 2.1–2.2, 3.1, 3.3, 9.2). The delta spec's *deterministic identity*, *configurable durations*, and *idempotent start on duplicate settlement completion* requirements gained their first runnable code anchors (`ObligationsIdentityNamespaces`, `ObligationsOptions`, `SettlementCompletedHandler`) and test coverage. `openspec validate add-obligation-lifecycle --strict` passes. The reminder/tracking/auto-confirm/dispute requirements remain unimplemented until S3–S4, as planned. No narrative or workshop Document History row was required — no Moment was implemented and no workshop slice gained or lost coverage.

## Verification checklist

- [x] `CritterBids.Obligations.csproj` exists; `WolverineFx.Http.Marten` reference matches siblings; `<ProjectReference>` to `CritterBids.Contracts`; `AssemblyInfo` exposes internals to the test project
- [x] `ObligationsIdentityNamespaces` defines the `PostSaleCoordination` namespace + deterministic `ObligationId(listingId)` UUID v5 helper
- [x] `ObligationsOptions` exists with production + demo durations, `DemoMode`, `Active` selector; binds from `"Obligations"`
- [x] `PostSaleCoordinationSaga`, `ObligationStatus`, `PostSaleCoordinationStarted`, `ObligationEventStream` (with `Guid Id`) exist
- [x] `SettlementCompletedHandler` starts the saga, computes `ShipByDeadline`, emits `PostSaleCoordinationStarted`, idempotent for duplicates
- [x] `AddObligationsModule()` binds options + registers saga/stream/events via `ConfigureMarten` (schema `obligations`); no `AddMarten()` inside the module
- [x] `Program.cs` has the `using`, `IncludeAssembly`, `obligations-settlement-events` route, and `AddObligationsModule()`; Api `<ProjectReference>` + `slnx` nodes present
- [x] Four sibling fixtures + the inline exclusion register an `ObligationsBcDiscoveryExclusion`
- [x] `CritterBids.Obligations.Tests`: boots-clean, options-bind, saga-start, idempotent-start — all green
- [x] `dotnet build` 0 errors; full `dotnet test` green (158 tests, no regressions)
- [x] `openspec validate add-obligation-lifecycle --strict` passes; tasks 1.1–1.5, 2.1, 2.2, 3.1, 3.3, 9.2 checked off
- [x] This retrospective written with `## Spec delta — landed?`
- [x] No commit to `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M6-S3 (in scope, next):** schedule the cancellable `SendShippingReminder` + `SendDeadlineEscalation` timers via `bus.ScheduleAsync()` at saga start (opsx 3.2); implement `SendShippingReminder` with the stale-after-tracking no-op guard (opsx 4.1); `ProvideTracking` command + endpoint emitting `TrackingInfoProvided` and cancelling pending timers (opsx 4.2/4.3); `ConfirmDelivery` auto-confirm → `ObligationFulfilled` + `MarkCompleted()` (opsx 5); the `ObligationStatusView` projection (opsx 8.1).
- **M6-S4:** escalation/recovery (opsx 6), the dispute sub-workflow (opsx 7), the remaining projections (opsx 8.2/8.3), and the lifecycle/dispute tests (opsx 9.1, 9.3, 9.4, 9.5). opsx 9.6 (the full-change closing gate) flips when S4 lands.
- **Idempotent-start coverage note:** the direct-invocation shape chosen here (documented above) should be the template for any future saga-start idempotency test in the codebase; a bus double-dispatch against a live saga NREs by framework design.
- **Out of scope, tracked elsewhere:** the NU1904 Marten advisory warnings (repo-wide, pre-existing); package remediation is a separate concern, not an M6-S2 deliverable.
