# M6-S2: Obligations BC Scaffold + `SettlementCompleted` Saga-Start

**Milestone:** M6 ([Obligations BC + Relay BC](../../milestones/M6-obligations-relay-bc.md))
**Slice:** S2 of 7 (BC Scaffold + saga-start consumer)
**Narrative:** [`docs/narratives/006-seller-fulfills-post-sale-obligation.md`](../../narratives/006-seller-fulfills-post-sale-obligation.md) (the saga-start is the prerequisite for Moment 1; the narrative's dramatised Moments 3–4 land in S3–S4)
**Agent:** @PSA
**Estimated scope:** one PR; ~11 files added (Obligations source ×9, test project ×6 counting fixtures/global-usings, plus this prompt and the retro), ~9 files modified (`Program.cs`, `CritterBids.Api.csproj`, `CritterBids.slnx`, four sibling test fixtures, one inline-exclusion test, `tasks.md`)

---

## Goal

Stand up the `CritterBids.Obligations` and `CritterBids.Obligations.Tests` projects, wire them into the solution / Api host / Wolverine-Marten configuration, author the `ObligationsOptions` config record deferred from S1, and land the **first behavior**: the `SettlementCompleted` saga-start handler that opens a `PostSaleCoordinationSaga` with a deterministic `ObligationId` and a computed `ShipByDeadline`. This slice also begins applying the OpenSpec `add-obligation-lifecycle` change — checking off the foundation + saga-start tasks (1.1–1.5, 2.1–2.2, 3.1, 3.3) and the idempotent-start test (9.2). The reminder/escalation timer chain, tracking/auto-confirm/dispute handlers, and read-model projections are deliberately held for S3–S4.

S1 closed the foundation decisions (ADR-022 Wolverine-Saga hosting, the four `CritterBids.Contracts.Obligations.*` integration-event stubs, the demo-mode config shape, hub-group mapping) and deferred the `ObligationsOptions` code and the BC project itself to this slice. S2 walks in with zero vocabulary ambiguity and a known host primitive. If a new design decision surfaces mid-session, stop and flag — do not pivot in-session.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M6-obligations-relay-bc.md` | Milestone scope — S2 row in §7 slice table; §6 Conventions Pinned (saga hosting, UUID v5 strategy, demo-mode config); §"Integration contracts" for the `SettlementCompleted` shape |
| `docs/narratives/006-seller-fulfills-post-sale-obligation.md` | Joint-authoritative narrative; Cast and Setting carry the post-sale-coordination ground the saga-start prepares for |
| `docs/retrospectives/M6-S1-obligations-foundation-decisions-retrospective.md` | "What remains / next session should verify" — S2 inherits the S1 closure and the `ObligationsOptions` deferral |
| `docs/decisions/022-obligations-saga-hosting.md` | ADR-022 (Wolverine Saga) — informs the `PostSaleCoordinationSaga` shape and Marten registration |
| `openspec/changes/add-obligation-lifecycle/` | The proposal, `design.md`, delta spec, and `tasks.md` — authoritative for the obligation-lifecycle capability; S2 checks off tasks 1.1–1.5, 2.1–2.2, 3.1, 3.3, 9.2 |
| `docs/skills/wolverine-sagas.md` | Saga-start tuple shape, deterministic-id idempotency, and the idempotency-coverage note |
| `docs/skills/critter-stack-testing-patterns.md` | §Cross-BC Handler Isolation — the `{TargetBc}BcDiscoveryExclusion` fixture pattern |
| `src/CritterBids.Settlement/` (`SettlementModule.cs`, `StartSettlementSagaHandler.cs`, `SettlementsIdentityNamespaces.cs`, `FinancialEventStream.cs`) | Direct structural template — Obligations mirrors Settlement's scaffold, deterministic-id helper, saga-start tuple, and stream-marker shape |

## In scope

1. **`src/CritterBids.Obligations` class library** — `WolverineFx.Http.Marten` package reference matching sibling Marten BCs; `<ProjectReference>` to `CritterBids.Contracts` (the handler consumes `CritterBids.Contracts.Settlement.SettlementCompleted`); `AssemblyInfo.cs` with `InternalsVisibleTo("CritterBids.Obligations.Tests")`. Added to `CritterBids.slnx` under `/src/`, alphabetical after `CritterBids.Listings`.
2. **`ObligationsIdentityNamespaces` + BC-internal `UuidV5` helper** — the `PostSaleCoordination` namespace constant and the deterministic `ObligationId(listingId)` (UUID v5 from `ListingId`), per the delta spec and the Settlement `SettlementsIdentityNamespaces` precedent.
3. **`ObligationsOptions`** — `sealed record`, production durations (reminder / ship-by / auto-confirm) + demo durations + `DemoMode` flag + an `Active` selector; bound from configuration section `"Obligations"`. This is the code S1 deferred.
4. **Saga state + types** — `PostSaleCoordinationSaga` (Wolverine `Saga`, state fields the saga-start needs), `ObligationStatus` enum, the `PostSaleCoordinationStarted` BC-internal domain event, and the `ObligationEventStream` stream marker.
5. **`SettlementCompletedHandler`** — Wolverine saga-start that computes `ShipByDeadline` from `ObligationsOptions.Active`, opens the stream, and emits `PostSaleCoordinationStarted`. Idempotent: a duplicate `SettlementCompleted` for an existing `ObligationId` is a no-op.
6. **`AddObligationsModule()`** — binds `ObligationsOptions` and contributes the saga document + stream marker + event-type registrations via `services.ConfigureMarten()` (schema `obligations`). No `AddMarten()` call — the host owns the single one.
7. **`Program.cs` wiring** — `using CritterBids.Obligations;`, `Discovery.IncludeAssembly`, the `obligations-settlement-events` RabbitMQ route, and `AddObligationsModule()`. `<ProjectReference>` from `CritterBids.Api.csproj`.
8. **Cross-BC discovery exclusions** — add an `ObligationsBcDiscoveryExclusion` to the Settlement / Selling / Listings / Auctions test fixtures (and the inline exclusion in `RealSellingProducerSagaTerminationTests`) so the Obligations handler does not leak into their in-process hosts.
9. **`CritterBids.Obligations.Tests`** — fixture (excludes the four foreign BCs), boots-clean + options-bind tests, the saga-start happy path, and the idempotent-start test.
10. **Begin `/opsx:apply add-obligation-lifecycle`** — check off tasks 1.1–1.5, 2.1, 2.2, 3.1, 3.3, 9.2 in `tasks.md`; run `openspec validate add-obligation-lifecycle --strict`.

## Explicitly out of scope

- **`bus.ScheduleAsync()` reminder/escalation timer chain** — opsx task 3.2; **S3**. The saga-start handler schedules no timers in S2.
- **`SendShippingReminder` / `SendDeadlineEscalation` handlers** — opsx §4, §6; **S3**.
- **`ProvideTracking` / `OpenDispute` / `ResolveDispute` commands + endpoints** — opsx §4, §7; **S3/S4**.
- **Auto-confirm / fulfillment (`ConfirmDelivery`, `MarkCompleted()` terminal paths)** — opsx §5; **S3/S4**.
- **Read-model projections** (`ObligationStatusView`, `ObligationsAwaitingDelivery*`, `OperationsObligationsView`) — opsx §8; **S3/S4**.
- **Any Relay project, hub, or `relay-*` consumer** — **S5–S7**.
- **Editing OpenSpec-managed files** under `.github/prompts/` or `.github/skills/`.
- **Skill-file edits.** If S2 surfaces a skill gap (e.g. the idempotency-coverage note needs sharpening), record it in the retro — do not edit in-session per AUTHORING.md rule 4.

## Conventions to pin or follow

- Saga hosting per ADR-022; `docs/skills/wolverine-sagas.md` owns the how (saga-start tuple `(Saga?, OutgoingMessages)`, deterministic-id idempotency).
- Deterministic identity per the delta spec: `ObligationId` is UUID v5 from `ListingId` via the Obligations-specific namespace constant.
- Stream-marker types registered via `Schema.For<T>()` carry a `Guid Id` property (Marten document requirement; Settlement's `FinancialEventStream` precedent).
- Module shape per `docs/skills/adding-bc-module.md`: `services.ConfigureMarten()` inside the `AddObligationsModule` extension; no `AddMarten()` inside the module.
- Cross-BC fixture exclusion naming: `{TargetBc}BcDiscoveryExclusion` per `critter-stack-testing-patterns.md`.
- `sealed record`; no "Event" suffix on domain event names; no "paddle"; `[AllowAnonymous]` posture holds through M6 (no HTTP endpoints this slice).

## Spec delta

Per ADR 020, this slice's spec consequence is governed by the OpenSpec `add-obligation-lifecycle` change (already authored/committed in the design phase). S2 lands the first code anchors for the change's saga-start requirements: the delta spec's *deterministic identity*, *configurable durations*, and *idempotent start* requirements gain runnable implementations and test coverage (`ObligationsIdentityNamespaces`, `ObligationsOptions`, `SettlementCompletedHandler`, the idempotent-start test). The reminder/tracking/auto-confirm/dispute requirements remain unimplemented until S3–S4. No narrative or workshop Document History row is required — narrative 006's dramatised Moments are not yet code (the saga-start is the prerequisite, not a Moment). The retro's `## Spec delta — landed?` paragraph confirms the handler starts the saga idempotently, `openspec validate add-obligation-lifecycle --strict` passes, and tasks 1.1–1.5/2.1–2.2/3.1/3.3/9.2 are checked off.

## Acceptance criteria

- [ ] `src/CritterBids.Obligations/CritterBids.Obligations.csproj` exists; `WolverineFx.Http.Marten` reference matches sibling BCs; `<ProjectReference>` to `CritterBids.Contracts` present; `AssemblyInfo.cs` exposes internals to the test project.
- [ ] `ObligationsIdentityNamespaces` defines the `PostSaleCoordination` namespace constant and the deterministic `ObligationId(listingId)` UUID v5 helper.
- [ ] `ObligationsOptions` exists with production + demo durations, a `DemoMode` flag, an `Active` selector, and binds from the `"Obligations"` config section.
- [ ] `PostSaleCoordinationSaga`, `ObligationStatus`, `PostSaleCoordinationStarted`, and `ObligationEventStream` (with `Guid Id`) exist.
- [ ] `SettlementCompletedHandler` starts the saga, computes `ShipByDeadline`, emits `PostSaleCoordinationStarted`, and is idempotent for a duplicate `SettlementCompleted`.
- [ ] `AddObligationsModule()` binds options and registers the saga + stream marker + event types via `ConfigureMarten` (schema `obligations`); no `AddMarten()` call inside the module.
- [ ] `Program.cs` has the `using`, the `IncludeAssembly`, the `obligations-settlement-events` route, and `AddObligationsModule()`; `CritterBids.Api.csproj` references the project; `CritterBids.slnx` carries both new project nodes.
- [ ] Each of the Settlement / Selling / Listings / Auctions fixtures (and the inline `RealSellingProducerSagaTerminationTests` exclusion) registers an `ObligationsBcDiscoveryExclusion`.
- [ ] `CritterBids.Obligations.Tests` contains boots-clean, options-bind, saga-start, and idempotent-start tests — all green.
- [ ] `dotnet build` passes (0 errors); full `dotnet test` green with no regressions across any BC.
- [ ] `openspec validate add-obligation-lifecycle --strict` passes; tasks 1.1–1.5, 2.1, 2.2, 3.1, 3.3, 9.2 checked off in `tasks.md`.
- [ ] `docs/retrospectives/M6-S2-obligations-scaffold-retrospective.md` written with `## Spec delta — landed?`.
- [ ] No commit to `main`; no `Co-Authored-By` trailer.

## Open questions

- **Idempotent saga-start coverage shape.** Settlement's `StartSettlementSagaHandler` returns `(Saga?, OutgoingMessages)` and no-ops a duplicate by returning `(null, empty)`, but Settlement's own suite does not exercise that path by re-dispatching the start message twice through the bus (its `SettlementSagaFailurePathsTests` documents this). If a double bus-dispatch of `SettlementCompleted` against a still-live saga surfaces a framework error, follow the Settlement precedent — cover idempotency by **direct handler invocation** asserting the `(null, empty)` no-op — rather than forcing a bus-redispatch test. Flag the chosen shape in the retro.
- **Settlement's outbound `SettlementCompleted` publish route.** Confirm whether the `obligations-settlement-events` listen route pairs with an existing Settlement publish route or requires a new one; do not add Settlement-side publish wiring beyond what the route topology already established in M5 provides.
