## Why

CritterBids has no post-sale coordination: once Settlement moves the money (`SettlementCompleted`), nothing tracks whether the seller ships, whether delivery lands, or how a stalled or disputed hand-off is escalated and resolved. M6 opens the Obligations BC to own that commitment chain. It is the first OpenSpec-adopting BC (ADR 021) and the project's canonical lived example of the cancellable-scheduled-message saga (W005).

## What Changes

- Introduce the **Obligations BC** (`src/CritterBids.Obligations`), a Marten-on-PostgreSQL module registered via `AddObligationsModule()`, wired into the canonical bootstrap (single `AddMarten().IntegrateWithWolverine()`, `AutoApplyTransactions()` in `Program.cs`).
- Add the **Post-Sale Coordination saga**, a Wolverine `Saga` that starts on `SettlementCompleted` and drives an obligation to a terminal `ObligationFulfilled` or a resolved dispute.
- Add the **cancellable reminder/escalation chain**: one `ShippingReminderSent` before the ship-by deadline; a **non-terminal** `DeadlineEscalated` when the deadline passes without tracking; late tracking recovers the happy path.
- Add **seller tracking intake** (`ProvideTracking` → `TrackingInfoProvided`), which cancels the pending reminder and escalation and schedules the auto-confirm timer.
- Add **clock-triggered delivery auto-confirmation** (`ConfirmDelivery` → `DeliveryConfirmed` → `ObligationFulfilled`), `N` days after tracking; no buyer command in MVP.
- Add the **dispute sub-workflow** (`OpenDispute` → `DisputeOpened`; `ResolveDispute` → `DisputeResolved`) with three resolutions: `Refund` and `Closed` terminate; `Extension` reschedules the ship-by deadline and the saga continues (the one deliberate non-`MarkCompleted()` path).
- Add read models: `ObligationStatusView` (seller + winner), `ObligationsAwaitingDelivery*` (saga internals), and `OperationsObligationsView` (escalation + dispute queues for the M7 Operations dashboards).
- Add **`ObligationsOptions`** configuration carrying production and demo-mode durations for the reminder, ship-by deadline, and auto-confirm timers (resolves W001-6).

## Capabilities

### New Capabilities

- `obligation-lifecycle`: the post-sale commitment lifecycle for a single sold listing — saga start on `SettlementCompleted`, the cancellable reminder/escalation chain, seller tracking intake, clock-triggered delivery auto-confirmation, the dispute sub-workflow, and the obligation read models. One obligation per sold listing, identified by a deterministic `ObligationId` (UUID v5 from `ListingId`).

### Modified Capabilities

<!-- None. The Obligations BC is greenfield; openspec/specs/ has no existing capability to modify. Upstream Settlement (SettlementCompleted) and downstream Relay/Operations integrations are BC-boundary contracts in CritterBids.Contracts, not OpenSpec capabilities of other BCs. -->

## Impact

- **New code:** `src/CritterBids.Obligations` (BC module, saga, handlers, projections, options); registration in `src/CritterBids.Api/Program.cs`.
- **Contracts:** integration events `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` published to `CritterBids.Contracts`; `SettlementCompleted` consumed (already defined by Settlement).
- **Storage:** a new Marten store schema for the Obligations BC (saga state + projections) on the existing PostgreSQL instance; no new infrastructure.
- **Messaging:** Wolverine scheduled messages (`bus.ScheduleAsync()`) for the reminder, escalation, and auto-confirm timers; RabbitMQ integration-event publication to Relay (M6) and Operations (M7).
- **Downstream (not in this change):** Relay broadcast of obligation events and the Operations dashboard consumption of `OperationsObligationsView` are M6 Relay / M7 Operations work.
- **Decisions:** ADR-022 (Obligations saga hosting = Wolverine Saga, citing ADR-019) is authored alongside the first implementation slice.
