# M6-S3: Obligations Saga Happy Path + Cancellable Reminder Chain

**Milestone:** M6 ([Obligations BC + Relay BC](../../milestones/M6-obligations-relay-bc.md))
**Slice:** S3 of 7 (saga happy path: reminder → tracking → auto-confirm → fulfilled)
**Narrative:** [`docs/narratives/006-seller-fulfills-post-sale-obligation.md`](../../narratives/006-seller-fulfills-post-sale-obligation.md) (this slice lands the dramatised Moments 3–4 — tracking entry and self-closing fulfillment)
**Agent:** @PSA
**Estimated scope:** one PR; ~8 source files added (events ×2, timer/command messages ×4, projection + view, HTTP endpoint), ~4 source files modified (`PostSaleCoordinationSaga`, `SettlementCompletedHandler`, `ObligationsModule`, `Program.cs`), ~3 test files added/modified, plus this prompt, the retro, and `tasks.md`

---

## Goal

Land the Obligations post-sale coordination saga's **happy path** and its **cancellable timer chain**. The saga-start handler (S2) now also schedules — via `bus.ScheduleAsync()` — a `SendShippingReminder` and a `SendDeadlineEscalation` at saga start. The seller provides tracking through an in-process HTTP endpoint, which cancels both pending timers, records `TrackingInfoProvided`, and schedules a `ConfirmDelivery`. Delivery auto-confirms, emitting `DeliveryConfirmed` then `ObligationFulfilled`, and the saga calls `MarkCompleted()`. An `ObligationStatusView` single-stream projection surfaces the lifecycle. This slice checks off OpenSpec `add-obligation-lifecycle` tasks 3.2, 4.1–4.3, 5.1–5.2, 8.1, 9.1, and 9.3.

S2 closed the scaffold and the idempotent saga-start. S3 walks in with the saga state and start path proven; it adds the forward transitions only. The escalation *handler body* (`DeadlineEscalated` emission), the dispute sub-workflow, their projections, and the failure-path tests are deliberately held for S4. The Relay BC is S5–S7.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M6-obligations-relay-bc.md` | Milestone scope — §7 S3 slice-table row; §6 Conventions Pinned (saga hosting, demo-mode config, `ScheduleAsync`-only IMessageBus rule) |
| `docs/narratives/006-seller-fulfills-post-sale-obligation.md` | Joint-authoritative narrative; Moments 3–4 (tracking entry, self-closing fulfillment) are what this slice makes real |
| `docs/retrospectives/M6-S2-obligations-scaffold-retrospective.md` | S2 closure + carried-over lessons (saga-start tuple, direct-invocation idempotency coverage, cross-BC discovery exclusions) |
| `openspec/changes/add-obligation-lifecycle/` (`design.md`, `tasks.md`, delta spec) | Authoritative for the lifecycle capability; S3 checks off 3.2, 4.1–4.3, 5.1–5.2, 8.1, 9.1, 9.3 |
| `docs/skills/wolverine-sagas.md` | Saga `Handle` shape, `[SagaIdentityFrom]`, `MarkCompleted()`, scheduled-message cancellation (see Open question on the API divergence) |
| `docs/skills/marten-projections.md` | `SingleStreamProjection<TView, Guid>` `Create`/`Apply` conventions for `ObligationStatusView` |
| `src/CritterBids.Auctions/AuctionClosingSaga.cs` | In-repo template for `bus.ScheduleAsync()` scheduling + `IMessageStore.ScheduledMessages.CancelAsync(ScheduledMessageQuery)` cancellation + the static `NotFound` safety net |
| `src/CritterBids.Settlement/SettlementSaga.cs` | Structural template for saga `Handle` (append-to-stream + emit-via-`OutgoingMessages` + `MarkCompleted()`) |

## In scope

1. **Cancellable timer scheduling at saga start** — `SettlementCompletedHandler` injects `IMessageBus` and schedules `SendShippingReminder` (at start + reminder offset) and `SendDeadlineEscalation` (at the ship-by deadline) via `bus.ScheduleAsync()`. The scheduled instants are stored on saga state so cancellation can target them. (opsx 3.2)
2. **BC-internal events** — `ShippingReminderSent`, `DeliveryConfirmed` (sealed records, no "Event" suffix, stream-only, not on the bus).
3. **Timer / command messages** — `SendShippingReminder`, `SendDeadlineEscalation`, `ProvideTracking`, `ConfirmDelivery` (sealed records carrying `ObligationId`).
4. **`SendShippingReminder` handler** — emits `ShippingReminderSent`, with a no-op guard once state has advanced past awaiting-shipment. (opsx 4.1)
5. **`ProvideTracking` command + in-process HTTP endpoint** — `[AllowAnonymous]` `[WolverinePost]`; the saga handler cancels both pending timers, appends + emits `TrackingInfoProvided`, schedules `ConfirmDelivery` at the auto-confirm offset, and advances to `Shipped`. (opsx 4.2, 4.3)
6. **`ConfirmDelivery` handler** — appends `DeliveryConfirmed`, emits `ObligationFulfilled`, calls `MarkCompleted()`, advances to `Fulfilled`. (opsx 5.1, 5.2)
7. **`SendDeadlineEscalation` S3 stub** — a routable, schedulable, cancellable no-op handler so the timer can be scheduled at start and cancelled on tracking; its `DeadlineEscalated`-emitting body is S4. Documented as such in an XML comment.
8. **`ObligationStatusView` single-stream projection** — registered Inline; surfaces status, `ShipByDeadline`, tracking number, and reminder/tracking timestamps. (opsx 8.1)
9. **Module + route wiring** — register the new event types and the projection in `AddObligationsModule()`; add `relay-obligations-events` publish routes for `TrackingInfoProvided` and `ObligationFulfilled` in `Program.cs` (publish-only; the Relay consumer is S5–S7).
10. **Tests** — a demo-duration fixture; the happy-path integration test (start → reminder → tracking → auto-confirm → fulfilled, asserting scheduling and cancellation via `IMessageStore.ScheduledMessages`); the stale-reminder-after-tracking no-op test. (opsx 9.1, 9.3)
11. **`/opsx:apply add-obligation-lifecycle`** — check off 3.2, 4.1–4.3, 5.1–5.2, 8.1, 9.1, 9.3; run `openspec validate add-obligation-lifecycle --strict`.

## Explicitly out of scope

- **Missed-deadline escalation body** (`DeadlineEscalated` emission, `Escalated` state transition) — opsx 6.1; **S4**. S3 ships only the routable no-op stub so the timer is cancellable.
- **Late-tracking recovery from `Escalated`** — opsx 6.2; **S4**.
- **Dispute sub-workflow** (`OpenDispute` / `ResolveDispute`, their endpoints and terminal/extension paths) — opsx §7; **S4**.
- **`ObligationsAwaitingDelivery*` and `OperationsObligationsView` projections** — opsx 8.2, 8.3; **S4**.
- **Failure-path / escalation / dispute tests** — opsx 9.4, 9.5; **S4**.
- **Any Relay project, hub, or `relay-*` consumer** — **S5–S7**. The `relay-obligations-events` publish routes are wired publish-only with no listener this slice.
- **Editing OpenSpec-managed files** under `.github/prompts/` or `.github/skills/`.
- **Skill-file edits.** If S3 surfaces a skill gap (see Open questions on the cancellation API), record it in the retro — do not edit in-session per AUTHORING.md rule 4.
- **`Carrier` on `ProvideTracking` or the view.** The frozen `TrackingInfoProvided` contract carries no carrier field; adding one is an additive contract change (ADR 005), deferred. Flag the narrative/spec drift in the retro.

## Conventions to pin or follow

- Saga hosting + transitions per ADR-022; `docs/skills/wolverine-sagas.md` owns the `Handle` shape, `[SagaIdentityFrom]`, and `MarkCompleted()`.
- `bus.ScheduleAsync()` is the **only** justified `IMessageBus` use in a handler (CLAUDE.md). Cancellation is via `IMessageStore.ScheduledMessages.CancelAsync(...)`, mirroring `AuctionClosingSaga.CancelPendingCloseAsync` — not via a bus method.
- Integration events (`TrackingInfoProvided`, `ObligationFulfilled`) are both appended to the obligation stream **and** returned via `OutgoingMessages` for cross-BC emission. BC-internal events (`ShippingReminderSent`, `DeliveryConfirmed`) are stream-only.
- `SingleStreamProjection<ObligationStatusView, Guid>` per `docs/skills/marten-projections.md`; registered Inline so the view is immediately queryable in tests.
- `sealed record`; `IReadOnlyList<T>`; no "Event" suffix; no "paddle"; `[AllowAnonymous]` posture holds through M6; deterministic UUID v5 `ObligationId`.
- No commit to `main`; no `Co-Authored-By` trailer.

## Spec delta

Per ADR 020, this slice's spec consequence is governed by the OpenSpec `add-obligation-lifecycle` change. S3 makes the delta spec's *reminder*, *tracking*, and *delivery-auto-confirms* requirements runnable: the cancellable timer chain, the `ProvideTracking` intake, and the auto-confirm → `ObligationFulfilled` → `MarkCompleted()` terminal path gain implementations and integration coverage. `docs/narratives/006-seller-fulfills-post-sale-obligation.md` gains a Document History row covering Moments 3–4 (tracking entry and self-closing fulfillment) — happy-path implemented, surfaced in `ObligationStatusView`. The *missed-deadline*, *late-tracking-recovery*, and *dispute* requirements remain unimplemented until S4. The retro's `## Spec delta — landed?` paragraph confirms the happy path runs end to end, the stale-reminder guard holds, `openspec validate add-obligation-lifecycle --strict` passes, and tasks 3.2/4.1–4.3/5.1–5.2/8.1/9.1/9.3 are checked off.

## Acceptance criteria

- [ ] `SettlementCompletedHandler` schedules `SendShippingReminder` and `SendDeadlineEscalation` via `bus.ScheduleAsync()` at saga start and stores their scheduled instants on saga state.
- [ ] `ShippingReminderSent` and `DeliveryConfirmed` BC-internal events and `SendShippingReminder` / `SendDeadlineEscalation` / `ProvideTracking` / `ConfirmDelivery` messages exist as sealed records carrying `ObligationId`.
- [ ] `Handle(SendShippingReminder)` emits `ShippingReminderSent` and no-ops once state has advanced past awaiting-shipment.
- [ ] A `[WolverinePost]` `[AllowAnonymous]` endpoint cascades `ProvideTracking`; the saga handler cancels both pending timers via `IMessageStore.ScheduledMessages`, appends + emits `TrackingInfoProvided`, schedules `ConfirmDelivery`, and sets status `Shipped`.
- [ ] `Handle(ConfirmDelivery)` appends `DeliveryConfirmed`, emits `ObligationFulfilled`, calls `MarkCompleted()`, and sets status `Fulfilled`.
- [ ] `Handle(SendDeadlineEscalation)` is a routable no-op stub (XML comment notes S4 fills the body).
- [ ] `ObligationStatusView` projection is registered Inline and surfaces status, `ShipByDeadline`, tracking number, and reminder/tracking timestamps.
- [ ] `AddObligationsModule()` registers the four new event types and the projection; `Program.cs` has publish-only `relay-obligations-events` routes for `TrackingInfoProvided` and `ObligationFulfilled`.
- [ ] Happy-path integration test (demo durations) and stale-reminder no-op test are green.
- [ ] `dotnet build` passes (0 errors); full `dotnet test CritterBids.slnx` green with no regressions.
- [ ] `openspec validate add-obligation-lifecycle --strict` passes; tasks 3.2, 4.1–4.3, 5.1–5.2, 8.1, 9.1, 9.3 checked off.
- [ ] `docs/retrospectives/M6-S3-obligations-saga-happy-path-retrospective.md` written with `## Spec delta — landed?`.
- [ ] No commit to `main`; no `Co-Authored-By` trailer.

## Open questions

- **`wolverine-sagas.md` scheduled-message cancellation API is wrong for the installed version.** The skill documents capturing `token.Id` from `ScheduleAsync` and calling `bus.CancelScheduledAsync(id)`. In Wolverine 5.39.3, `IMessageBus.ScheduleAsync` returns `ValueTask` (no token/id) and there is **no** `CancelScheduledAsync`. The working API — confirmed by `AuctionClosingSaga` — is `IMessageStore.ScheduledMessages.CancelAsync(new ScheduledMessageQuery { ExecutionTimeFrom, ExecutionTimeTo, MessageType })`, keyed on the exact scheduled instant. S3 follows the `AuctionClosingSaga` precedent and **flags the skill correction in the retro** rather than editing the skill in-session.
- **Carrier drift.** Narrative 006 / the spec describe a carrier alongside the tracking number in the view, but the frozen `TrackingInfoProvided` contract has only `TrackingNumber`. S3 honors the frozen contract (no carrier) and flags the additive-contract deferral (ADR 005) in the retro.
- **Cross-listing cancellation precision.** `ScheduledMessageQuery` filters by time window + message type, not saga id; two obligations scheduled at the same instant could cross-cancel (same limitation `AuctionClosingSaga` documents). Demo/test instants are unique so it is not exercised; note the production limitation in the retro.
