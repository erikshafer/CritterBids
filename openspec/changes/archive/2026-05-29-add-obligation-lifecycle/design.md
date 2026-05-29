## Context

The Obligations BC is greenfield M6 work. It owns the post-sale commitment chain between a winning bidder and a seller for a single sold listing. It starts on Settlement's `SettlementCompleted` integration event and coordinates shipping, delivery, escalation, and disputes. W005 (`docs/workshops/005-obligations-bc-deep-dive.md`) is the event model; narrative 006 (`docs/narratives/006-seller-fulfills-post-sale-obligation.md`) is the happy-path journey prose. The defining shape is the cancellable-scheduled-message saga: unlike Settlement's linear phased workflow (W003), Obligations is state-driven with cancellable timers and recovery.

CritterBids is a modular monolith on the Critter Stack. Storage is PostgreSQL via Marten (ADR 011 All-Marten pivot). The bootstrap is uniform: a single `AddMarten().IntegrateWithWolverine().UseLightweightSessions()` in `Program.cs`, `AutoApplyTransactions()` on the Wolverine policy, and per-BC `services.ConfigureMarten()` contributing document types and projections inside `AddObligationsModule()`.

## Goals / Non-Goals

**Goals:**

- A self-contained Obligations BC registered via `AddObligationsModule()` with no project reference to any other BC.
- The post-sale coordination saga as a Wolverine `Saga`, including the cancellable reminder/escalation chain, clock-triggered auto-confirmation, and the dispute sub-workflow with a non-terminal `Extension` resolution.
- Deterministic, idempotent obligation identity tolerant of Wolverine at-least-once delivery.
- Demo-mode timing so the full lifecycle runs live in a conference session.

**Non-Goals:**

- Relay broadcast of obligation events (M6 Relay) and the Operations dashboard consumption of `OperationsObligationsView` (M7 Operations). This change publishes the integration events and builds the read model; it does not build the consumers.
- A real carrier-tracking webhook (W005-1, post-MVP); MVP uses an in-process command seam.
- Buyer-initiated delivery confirmation (W005-2, post-MVP); MVP auto-confirms.
- Frontend screens (M8); only the backend command seams land here.
- Multi-round dispute negotiation / appeals (W005-3, post-MVP).

## Decisions

**Saga hosting: Wolverine `Saga` (not `ProcessManager<TState>`, not handler-based process managers).** The obligation lifecycle is stateful with cancellable timers and recovery transitions; a Wolverine `Saga` is the shipped primitive that hosts saga state plus `bus.ScheduleAsync()` timers with cancellation keyed on the saga id. `ProcessManager<TState>` is JasperFx framework work, not shipping. Handler-based process managers are the right host for *stateless* event-reactive coordination (Relay's candidate, W003 Approach B), not for this stateful chain. This decision is recorded as **ADR-022** (citing ADR-019, the Settlement saga-hosting precedent), authored alongside the first implementation slice.

**Deterministic `ObligationId` = UUID v5 from `ListingId`.** `UuidV5(ObligationsIdentityNamespaces.PostSaleCoordination, $"obligation:{ListingId}")`, mirroring W003's `SettlementId` strategy. Idempotency guards against duplicate `SettlementCompleted` delivery; a second start finds existing state and no-ops. The namespace constant is Obligations-owned per BC isolation.

**Ship-by deadline carried as saga state, not its own event.** `PostSaleCoordinationStarted` carries `ShipByDeadline`; reminders and escalation compute off it. Avoids a redundant event whose only job is to record a timestamp the start event already carries. A `DisputeResolved(Extension)` recomputes and reschedules it.

**One reminder, then non-terminal escalation.** MVP sends a single `ShippingReminderSent`; `DeadlineEscalated` alerts Operations but does not terminate, so late tracking recovers. Multi-reminder cadence is post-MVP.

**Auto-confirm via clock-triggered `ConfirmDelivery`.** Delivery confirmation is a temporal-automation slice (Bruun pattern): the trigger is the passage of time after tracking, not an incoming command. No buyer command in MVP.

**Three dispute resolutions; `Extension` is the one non-`MarkCompleted()` path.** `Refund` and `Closed` terminate; `Extension` reschedules a fresh ship-by deadline and returns the saga to awaiting-tracking, reusing the start-time scheduling path.

**`ObligationsOptions` config with production + demo durations.** A bound options section with a `DemoMode` flag and demo-second durations alongside production durations, selectable without recompilation and injectable with short durations in integration tests. Resolves W001-6.

## Risks / Trade-offs

- **Scheduled-message cancellation correctness** → The saga must cancel the pending escalation the instant tracking arrives, or a false `DeadlineEscalated` can fire after the seller has shipped. Mitigation: cancellation keyed on saga id plus defense-in-depth no-op guards in the reminder/escalation handlers when saga state has already advanced (spec scenario "Stale reminder after tracking is a no-op").
- **Projection lag at saga start** → If a read model the start path depends on is not caught up, the start could miss data. Mitigation: the start path depends only on the `SettlementCompleted` payload, not on a projection; the payload carries all routing identities and amounts.
- **At-least-once delivery duplicates** → Duplicate `SettlementCompleted` could start a second saga. Mitigation: deterministic `ObligationId` makes the second start a no-op.
- **Demo vs production timing drift** → Demo durations diverging from production behavior could mask a bug that only appears at production durations. Mitigation: the saga's transitions are identical under either duration set; only the offsets differ, and integration tests inject short durations through the same `ObligationsOptions` path.

## Open Questions

- ADR-022 (saga hosting) is to be authored with the first implementation slice; this design names the decision but the ADR is the durable record.
- The exact demo and production duration values are an `ObligationsOptions` implementation detail to be set at slice time, not fixed here.
