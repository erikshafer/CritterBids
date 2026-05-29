# M6-S3: Obligations Saga Happy Path + Cancellable Reminder Chain — Retrospective

**Date:** 2026-05-29
**Milestone:** M6 — Obligations BC + Relay BC
**Slice:** S3 of 7 — saga happy path (reminder → tracking → auto-confirm → fulfilled) + cancellable timer chain
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M6-S3-obligations-saga-happy-path.md`

## Baseline

- Branch `erikshafer/refactored-disco` off the M6-S2 close.
- S2 had landed the `CritterBids.Obligations` project, `ObligationsOptions`, `ObligationsIdentityNamespaces`, the `PostSaleCoordinationSaga` state + `SettlementCompletedHandler` saga-start, `AddObligationsModule()`, and the `obligations-settlement-events` route. No timer scheduling, no forward transitions, no projection.
- OpenSpec change `add-obligation-lifecycle`: tasks 1.1–1.5, 2.1–2.2, 3.1, 3.3, 9.2 checked off.
- Full suite baseline: Contracts 1, Api 1, Obligations 4, Participants 6, Listings 20, Selling 36, Settlement 25, Auctions 65 = **158 tests, 0 failures**. ~14 pre-existing NU1904 Marten advisory warnings (out of scope).
- The S3 prompt was authored **up front** this slice (S2's was backfilled), per the cross-session instruction.

## Items completed

| Item | Description |
|------|-------------|
| S3a | S3 session prompt authored up front (`docs/prompts/implementations/M6-S3-obligations-saga-happy-path.md`) — Goal, In/Out scope, Spec delta, Acceptance criteria, three Open questions |
| S3b | BC-internal events `ShippingReminderSent`, `DeliveryConfirmed` (sealed records, stream-only, no "Event" suffix) |
| S3c | Timer/command messages `SendShippingReminder`, `SendDeadlineEscalation`, `ProvideTracking`, `ConfirmDelivery` (sealed records, carry `ObligationId`) |
| S3d | `SettlementCompletedHandler` injects `IMessageBus` and schedules the reminder + escalation timers at saga start; persists scheduled instants on saga state (opsx 3.2) |
| S3e | `PostSaleCoordinationSaga` forward transitions: `SendShippingReminder` (no-op guard), `SendDeadlineEscalation` (S4 stub), `ProvideTracking` (cancel timers + emit + schedule auto-confirm), `ConfirmDelivery` (`DeliveryConfirmed` → `ObligationFulfilled` + `MarkCompleted()`); four static `NotFound` safety nets; `CancelScheduledAsync` helper (opsx 4.1/4.3, 5.1/5.2) |
| S3f | `ProvideTracking` `[WolverinePost]` `[AllowAnonymous]` in-process HTTP endpoint cascading the command to the saga (opsx 4.2) |
| S3g | `ObligationStatusView` + `ObligationStatusViewProjection` (`SingleStreamProjection<ObligationStatusView, Guid>`, Inline) (opsx 8.1) |
| S3h | `AddObligationsModule()` registers the four new event types + the projection + the view schema |
| S3i | `Program.cs` publish-only `relay-obligations-events` routes for `TrackingInfoProvided` + `ObligationFulfilled` |
| S3j | Demo-duration fixture (`ObligationsLifecycleTestFixture` + collection) + happy-path test (9.1) + stale-reminder no-op test (9.3); updated `SettlementCompletedHandlerTests` idempotency test to the 5-arg handler signature |
| S3k | `/opsx:apply` — tasks 3.2, 4.1–4.3, 5.1–5.2, 8.1, 9.1, 9.3 checked off; `--strict` validation green |
| S3l | This retrospective + narrative 006 Document History row (Moments 3–4) |

## S3d/S3e: the cancellable timer chain

The saga-start handler now schedules two timers via `bus.ScheduleAsync()` — `SendShippingReminder` at start + `ReminderOffset`, and `SendDeadlineEscalation` at the ship-by deadline — and persists both scheduled instants on saga state (`ReminderScheduledAt`, `EscalationScheduledAt`) so cancellation can target them. `bus.ScheduleAsync()` is the only `IMessageBus` use in either handler, per CLAUDE.md.

`ProvideTracking` cancels both pending timers, appends + emits `TrackingInfoProvided`, schedules `ConfirmDelivery` at `providedAt + AutoConfirmWindow`, and advances to `Shipped`. `ConfirmDelivery` appends `DeliveryConfirmed`, emits `ObligationFulfilled`, calls `MarkCompleted()`, and advances to `Fulfilled` — the happy-path terminal transition.

**Event taxonomy applied.** Stream-only BC-internal (no "Event" suffix): `ShippingReminderSent`, `DeliveryConfirmed`. Stream **and** bus (frozen Contracts): `TrackingInfoProvided` — appended (drives the projection) **and** emitted via `OutgoingMessages`. Bus-only (Contracts): `ObligationFulfilled` — emitted, not appended. The projection's `Fulfilled` state is therefore driven by the internal `DeliveryConfirmed`, not by the external announcement.

## Skill correction: `wolverine-sagas.md` scheduled-message cancellation API is wrong for Wolverine 5.39.3

**The skill documents a fictional API.** `docs/skills/wolverine-sagas.md` shows capturing a token id from `ScheduleAsync` and calling `bus.CancelScheduledAsync(id)`. Neither exists in the installed Wolverine 5.39.3:

- `IMessageBus.ScheduleAsync(...)` returns `ValueTask` — **no token, no id**.
- There is **no** `bus.CancelScheduledAsync`.

**The working API** — confirmed against `AuctionClosingSaga.CancelPendingCloseAsync` and `AuctionClosingSagaTests` — is the scheduled-message store on `IMessageStore`:

```
messageStore.ScheduledMessages.CancelAsync(
    new ScheduledMessageQuery { ExecutionTimeFrom, ExecutionTimeTo, MessageType },
    cancellationToken);
```

Namespaces: `Wolverine.Persistence.Durability` (`IMessageStore`), `Wolverine.Persistence.Durability.ScheduledMessageManagement` (`ScheduledMessageQuery`, `ScheduledMessageSummary`). Cancellation is keyed on the **exact scheduled instant + message type** — hence the saga persists the instants on its state. S3 followed the in-repo `AuctionClosingSaga` precedent rather than the skill. **Action: `docs/skills/wolverine-sagas.md` needs a correction in a future doc pass (not edited in-session per AUTHORING.md rule 4).**

## S3f: `ProvideTracking` endpoint needed an explicit `Microsoft.AspNetCore.Http` using — Discovery

**Symptom (first Obligations build).**

```
ProvideTrackingEndpoint.cs(22,20): error CS0246: The type or namespace name 'IResult' could not be found
```

**Root cause.** `WolverineFx.Http.Marten` transitively brings the ASP.NET Core framework reference, but `IResult` / `Results` are not in an implicit-usings namespace for a class library. The sibling precedent (`Participants/.../RegisterAsSeller.cs`) carries an explicit `using Microsoft.AspNetCore.Http;`.

**Resolution.** Added `using Microsoft.AspNetCore.Http;`. Endpoints returning `IResult` from a BC class library need the explicit using.

## S3j: demo-duration fixture + the AuctionClosingSaga test precedent

The happy-path and stale-reminder tests run against a new `ObligationsLifecycleTestFixture` that flips `DemoMode=true` and injects **minute-scale** durations (reminder 2 m / ship-by 5 m / auto-confirm 3 m) via `builder.UseSetting("Obligations:Demo:*", ...)` — proving the demo-duration config seam (W001-6) end to end. Minutes (not seconds) so no scheduled timer fires on its own during a run; every transition is driven deterministically via direct `InvokeMessageAndWaitAsync`, the `AuctionClosingSagaTests` precedent (no real clock waits).

Scheduling is asserted by querying `IMessageStore.ScheduledMessages.QueryAsync` and filtering by message-type name; cancellation by the reminder + escalation types disappearing from the store after `ProvideTracking` (while `ConfirmDelivery` appears). The emitted `TrackingInfoProvided` / `ObligationFulfilled` land in `tracked.NoRoutes` — the `relay-obligations-events` publish routes live inside Program.cs's RabbitMQ block, which the broker-less fixture skips, so the integration events have no route. This is the established CritterBids fixture stance for cross-BC events whose consumer (Relay, S5–S7) has not shipped.

**5-arg handler signature update.** `SettlementCompletedHandler.Handle` gained `IMessageBus bus` (now `message, session, bus, options, ct`). The existing `DuplicateSettlementCompleted_IsNoOp` direct-invocation test was updated to resolve `IMessageBus` from a `CreateAsyncScope()` (it is scoped — resolving from the root provider throws) and pass it through; the bus is untouched on the no-op path (the existing-saga guard returns before scheduling).

## Test results

| Phase | Obligations Tests | Full solution |
|-------|-------------------|---------------|
| After `IResult` using fix | builds | — |
| Obligations project run | **6/6** | — |
| Session close | **6/6** | **160 / 160, 0 failures** |

Full suite at close: Contracts 1, Api 1, Obligations 6, Participants 6, Listings 20, Selling 36, Settlement 25, Auctions 65 = **160 tests, 0 failures**. Test-count delta: +2 (happy-path 9.1 + stale-reminder 9.3); the existing 4 Obligations tests stayed green after the 5-arg signature update. No regressions in any sibling BC.

## Build state at session close

- Errors: 0.
- Warnings: pre-existing NU1904 (Marten 8.35.0 advisory) only. Delta from baseline: 0. Not addressed — package remediation is out of scope.
- `bus.ScheduleAsync()` calls added: 3 (reminder + escalation at start; auto-confirm on tracking). `IMessageStore.ScheduledMessages.CancelAsync` calls: 2 (both timers on tracking). `MarkCompleted()` calls added: 1 (`ConfirmDelivery`, the happy-path terminal).
- New `sealed record` events: 2 BC-internal (`ShippingReminderSent`, `DeliveryConfirmed`). New `sealed record` messages: 4. New read model: 1 (`ObligationStatusView`). New projection: 1 (Inline). "Event"-suffix names: 0. "paddle" references: 0.
- `AddMarten()` calls inside `AddObligationsModule()`: 0 (host owns the single one). Cross-BC `<ProjectReference>` from Obligations: 0 (Contracts only). `relay-obligations-events` routes: publish-only, no `ListenTo` (Relay is S5–S7).

## Key learnings

1. **`wolverine-sagas.md`'s scheduled-message cancellation API is fictional for Wolverine 5.39.3.** Use `IMessageStore.ScheduledMessages.CancelAsync(ScheduledMessageQuery)` keyed on the scheduled instant + message type, persisting the instant on saga state — the `AuctionClosingSaga` precedent. The skill needs a correction pass.
2. **A BC class-library endpoint returning `IResult` needs an explicit `using Microsoft.AspNetCore.Http;`.** The framework reference comes transitively via `WolverineFx.Http.Marten`, but the namespace is not implicitly imported.
3. **Demo durations belong in minutes, not seconds, for deterministic saga tests.** Second-scale demo durations risk a scheduled timer firing mid-test; minute-scale durations keep the config seam exercised while transitions are driven manually via `InvokeMessageAndWaitAsync`.
4. **Adding an injected dependency to a saga-start handler ripples into its direct-invocation tests.** The `IMessageBus` addition forced the idempotency test to resolve a scoped bus; saga-start handler signatures and their unit-level tests move together.

## Findings against narrative

Narrative 006 Moments 3 (seller provides tracking) and 4 (self-closing fulfillment) are now code. **One drift flagged and routed:** the narrative's tracking-entry moment implies a **carrier** alongside the tracking number, but the frozen `TrackingInfoProvided` contract (M6-S1) carries only `TrackingNumber`. Adding a carrier is an additive contract change (ADR 005), deferred beyond S3. Per ADR 016's lanes this is a narrative-vs-contract drift; recorded as a narrative 006 Document History row (the carrier mention is aspirational until the additive change lands) rather than forcing an unscoped contract edit this slice. No code-update finding — the implementation matches the frozen contract exactly.

A second, lesser limitation noted (not a narrative drift): `ScheduledMessageQuery` filters by time window + message type, not saga id, so two obligations scheduled at the same instant could cross-cancel — the same limitation `AuctionClosingSaga` documents. Demo/test instants are unique so it is not exercised; flagged here as a production consideration for a future precision pass (e.g., embedding the obligation id in a message header filter).

## Spec delta — landed?

Landed as written. Per ADR 020 the spec consequence is governed by the OpenSpec `add-obligation-lifecycle` change; this slice modified only its `tasks.md` (checking off 3.2, 4.1–4.3, 5.1–5.2, 8.1, 9.1, 9.3), not the proposal/design/delta-spec. The delta spec's **reminder**, **tracking**, and **delivery-auto-confirms** requirements gained runnable code and integration coverage: the cancellable timer chain, the `ProvideTracking` intake + endpoint, and the auto-confirm → `ObligationFulfilled` → `MarkCompleted()` terminal path, surfaced through `ObligationStatusView`. The happy path runs end to end and the stale-reminder guard holds (both green). `openspec validate add-obligation-lifecycle --strict` passes. The **missed-deadline escalation body**, **late-tracking recovery**, **dispute sub-workflow**, the **`ObligationsAwaitingDelivery*` / `OperationsObligationsView`** projections, and the **failure-path tests** remain unimplemented until S4, as scoped. Narrative 006 gained a Document History row for Moments 3–4 with the carrier-drift note. One skill correction (`wolverine-sagas.md` cancellation API) is recorded for a future doc pass.

## Verification checklist

- [x] `SettlementCompletedHandler` schedules `SendShippingReminder` + `SendDeadlineEscalation` via `bus.ScheduleAsync()` at start; persists scheduled instants on saga state
- [x] `ShippingReminderSent`, `DeliveryConfirmed` BC-internal events + `SendShippingReminder` / `SendDeadlineEscalation` / `ProvideTracking` / `ConfirmDelivery` messages exist as sealed records carrying `ObligationId`
- [x] `Handle(SendShippingReminder)` emits `ShippingReminderSent` and no-ops once state advanced past awaiting-shipment
- [x] `[WolverinePost]` `[AllowAnonymous]` endpoint cascades `ProvideTracking`; saga handler cancels both timers via `IMessageStore.ScheduledMessages`, appends + emits `TrackingInfoProvided`, schedules `ConfirmDelivery`, sets `Shipped`
- [x] `Handle(ConfirmDelivery)` appends `DeliveryConfirmed`, emits `ObligationFulfilled`, calls `MarkCompleted()`, sets `Fulfilled`
- [x] `Handle(SendDeadlineEscalation)` is a routable no-op stub (XML comment notes S4 fills the body)
- [x] `ObligationStatusView` Inline projection surfaces status, `ShipByDeadline`, tracking number, reminder/tracking/fulfilled timestamps
- [x] `AddObligationsModule()` registers the four new event types + the projection + view schema; `Program.cs` has publish-only `relay-obligations-events` routes
- [x] Happy-path (demo durations) + stale-reminder no-op tests green
- [x] `dotnet build` 0 errors; full `dotnet test CritterBids.slnx` green (160 tests, no regressions)
- [x] `openspec validate add-obligation-lifecycle --strict` passes; tasks 3.2, 4.1–4.3, 5.1–5.2, 8.1, 9.1, 9.3 checked off
- [x] This retrospective written with `## Spec delta — landed?`
- [x] No commit to `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M6-S4 (next):** missed-deadline escalation body — `SendDeadlineEscalation` emits `DeadlineEscalated`, transitions to `Escalated` (non-terminal) (opsx 6.1); late-tracking recovery from `Escalated` (opsx 6.2); the dispute sub-workflow (`OpenDispute` / `ResolveDispute`, terminal/extension paths) (opsx §7); the `ObligationsAwaitingDelivery*` + `OperationsObligationsView` projections (opsx 8.2/8.3); escalation + dispute tests (opsx 9.4/9.5). opsx 9.6 (closing gate) flips when the change is complete.
- **Skill correction owed:** `docs/skills/wolverine-sagas.md` documents a non-existent scheduled-message cancellation API; correct it to the `IMessageStore.ScheduledMessages.CancelAsync(ScheduledMessageQuery)` pattern in a doc pass.
- **Carrier additive contract (ADR 005):** if the narrative's carrier is to be honored, `TrackingInfoProvided` needs an additive `Carrier` field — a deliberate contract change, deferred.
- **Cross-listing cancellation precision:** consider a saga-id-scoped scheduled-message filter for production to remove the same-instant cross-cancel risk `AuctionClosingSaga` also carries.
- **Out of scope, tracked elsewhere:** NU1904 Marten advisory warnings (repo-wide, pre-existing); the Relay BC (S5–S7).
