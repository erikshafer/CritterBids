# ADR 022: Obligations Saga Hosting

**Status:** Accepted
**Date:** 2026-05-28

---

## Context

The Obligations BC's post-sale coordination workflow lands in M6. Triggered by
`SettlementCompleted`, it drives a sold listing from `PostSaleCoordinationStarted` through a
cancellable shipping-reminder chain to a terminal `ObligationFulfilled`, escalating missed
deadlines to Operations staff and running a dispute sub-workflow when the commitment chain
fails. The implementation host needs deciding before M6-S2 (the BC scaffold + saga slices)
begins. Event Modeling workshop W005 (`docs/workshops/005-obligations-bc-deep-dive.md`)
flagged this as the ADR-022 candidate, citing ADR-019 (Settlement Workflow Hosting) as the
governing precedent.

**CritterBids' framing constraint** is unchanged from ADR-019: CritterBids uses shipped
Wolverine features only. The proposed `ProcessManager<TState>` framework primitive Erik is
designing for Wolverine is JasperFx framework-design territory and is **not** an
implementation option here. Within shipped Wolverine, two coordination patterns are
available — Wolverine Saga and Process Managers via Handlers — exactly as ADR-019 laid out.

**What is new since ADR-019.** Two facts shape the Obligations choice specifically:

1. **The Obligations workflow is genuinely state-driven, not a linear pipeline.** W005's
   storyboard (the saga lifecycle diagram in §Phase 3) shows escalation as a *non-terminal*
   branch, and both late tracking and a dispute `Extension` resolution loop the saga back
   into the awaiting-tracking state. The saga carries evolving state across these
   transitions: `ShipByDeadline` (computed at start, rescheduled on `Extension`), the
   scheduled-message token ids used to cancel the reminder and escalation, the dispute id
   and status, and the participant identifiers. This is the shape the Saga primitive's
   persisted mutable document is built to host.

2. **Cancellable scheduled messages are the defining pattern.** Obligations is the canonical
   CritterBids lived example of `bus.ScheduleAsync()` combined with saga-state-driven
   cancellation: providing tracking cancels the pending reminder and escalation by their
   stashed token ids. Cancellation keyed on saga state requires a persisted state document
   to hold those tokens — the Saga primitive provides it directly; a handlers-only shape
   would have to invent an external state document to thread the tokens through, re-inventing
   saga state outside the primitive built for it.

ADR-019 already established the positive claim that Saga is the right host for workflows with
phased, evolving, shared state, and that Handlers remain the right host for event-reactive
coordination *without* shared state (Relay's broadcast pipeline being the canonical future
adopter). Obligations is squarely in the first category.

## Options Considered

### Option A: Wolverine Saga (chosen)

Obligations implements the workflow as a `PostSaleCoordinationSaga : Saga` document with a
status enum tracking lifecycle progression (Started → AwaitingTracking → Escalated →
Fulfilled / Disputed), per-transition `Handle` methods, self-scheduled continuation messages
(`SendShippingReminder`, `SendDeadlineEscalation`, `ConfirmDelivery`), and `MarkCompleted()`
at terminal state. State persists via Marten under the deterministic UUID v5 `ObligationId`
(`UuidV5(ObligationsIdentityNamespaces.PostSaleCoordination, $"obligation:{ListingId}")`).

This is the established CritterBids pattern: the AuctionClosingSaga (M3-S5) and the
SettlementSaga (M5, per ADR-019) are the two lived precedents. The cancellable-reminder
mechanics extend the AuctionClosingSaga's cancel-and-reschedule pattern (extended bidding)
to a longer chain with an explicit escalation branch.

### Option B: Process Managers via Handlers

Obligations implements the workflow as a chain of independent Wolverine handlers, each
reacting to an event, with no shared mutable state document. The scheduled-message token ids
needed for cancellation, the `ShipByDeadline`, and the dispute status would have to live
somewhere — either threaded through growing command payloads or rehydrated from the event
stream on every handler entry. Either path re-invents saga state outside the primitive built
for it, for no benefit: Obligations' coordination is not leaf-reactive, it is a stateful
lifecycle with loop-back transitions.

Option B remains the right host for Relay's broadcast pipeline (M6-S5 onward) — pure
event-reactive fan-out with no continuation state. It is the wrong host for Obligations for
the same reason ADR-019 rejected it for Settlement.

### Out of scope: the proposed `ProcessManager<TState>` framework primitive

Identical to ADR-019's stance. The primitive is JasperFx framework-design work, not a
CritterBids implementation option. Its stabilization is not a revisit trigger for this ADR.
The decider-pattern *design lens* (pure-function transition logic) remains available as an
implementation-detail refactor inside the Saga host if the dispute/escalation branch logic
benefits from extracted pure helpers — that is an M6-S3/S4 implementation choice, not an ADR.

## Decision

**Option A.** The Obligations BC implements its post-sale coordination workflow as a
Wolverine Saga (`PostSaleCoordinationSaga : Saga`) in M6, following the AuctionClosingSaga
and SettlementSaga precedents. Persistence is via Marten under the deterministic UUID v5
`ObligationId`. The saga hosts the cancellable reminder/escalation chain (`bus.ScheduleAsync`
plus state-keyed cancellation), the auto-confirm delivery timer, and the dispute
sub-workflow. Terminal states (`ObligationFulfilled`, dispute `Refund`, dispute `Closed`)
call `MarkCompleted()`; the dispute `Extension` resolution is the one deliberate non-terminal
path that reschedules `ShipByDeadline` and continues the saga (W005 Decision 5).

This confirms the ADR-019 precedent rather than diverging from it. Per the M6 milestone doc
§6 ("If it confirms the pattern without novel options, it is recorded in the M6-S1
retrospective rather than a full ADR"), a full ADR is nonetheless warranted here because
Obligations introduces two patterns ADR-019's Settlement workflow did not exercise: a
**non-terminal escalation branch** with loop-back recovery, and **state-keyed scheduled-
message cancellation** as the workflow's defining behavior. Recording these as an accepted
ADR gives M6-S3/S4 and future BCs (Relay, Operations) a citable rationale.

### Revisit trigger

This ADR is reopened if the Saga shape produces specific friction during M6-S3/S4
implementation that the Handlers shape (Option B) would have prevented — for example, if the
loop-back transitions (late-tracking recovery, `Extension` reschedule) prove awkward to
express as saga state mutations, or if scheduled-message token lifetime under cancellation
surfaces correctness bugs the saga document cannot cleanly hold. The default response if the
trigger fires is to extract pure-function transition helpers inside the existing Saga host
(the decider lens), keeping the change scoped to one BC's internals. Migrating to Handlers
would only be right if implementation revealed the coordination to be more event-reactive
than W005 modeled — unlikely given the explicit state-driven storyboard.

## Consequences

### M6-S2 onward builds against the Wolverine Saga primitive

`docs/skills/wolverine-sagas.md` is the implementation reference. The skill file gains the
Obligations-side amendment (cancellable reminder chain + non-terminal escalation branch +
loop-back recovery) at the M6-S3/S4 retrospective — the slice that produces the lived
example — not at this slice, mirroring the ADR-019 precedent (the Settlement amendment landed
at M5-S4's retro, not M5-S1).

### `ObligationId` is deterministic UUID v5

Per ADR-007 and the W005 Decision: a natural business key exists (`ListingId` → one
obligation per settled listing), so the obligation id is UUID v5 namespace-derived. The
namespace constant `ObligationsIdentityNamespaces.PostSaleCoordination` lands with the BC
project in M6-S2, analogous to `SettlementsIdentityNamespaces` and `AuctionsIdentityNamespaces`.
Determinism gives idempotent saga start under Wolverine's at-least-once `SettlementCompleted`
delivery.

### Process Managers via Handlers remains the right tool for Relay

This ADR does not foreclose Option B for CritterBids. Relay's broadcast pipeline (M6-S5
onward) is the canonical handlers-based coordination shape — leaf-reactive fan-out with no
continuation state. The choice stays per-BC by coordination shape: Saga for Obligations,
Handlers for Relay.

### Out-of-scope for this ADR

- The demo-mode timeout configuration shape (W001-6) — resolved separately at W005 Decision 4
  (`ObligationsOptions` with real + demo durations). The config record lands with the BC
  project in M6-S2.
- The dispute reason / resolution-type wire encoding — fixed at M6-S1 as string-valued enums
  on the contract records (`DisputeOpened.Reason`, `DisputeResolved.ResolutionType`),
  matching the `ListingPassed.Reason` precedent. That is a contract-shape decision, not a
  host-choice decision.

## References

- `docs/decisions/019-settlement-workflow-hosting.md` — the governing precedent this ADR
  confirms; the Saga-vs-Handlers framing applies unchanged
- `docs/decisions/007-uuid-strategy.md` — UUID v5 deterministic stream-ID convention used by
  `ObligationId`
- `docs/decisions/011-all-marten-pivot.md` — Obligations uses Marten on PostgreSQL; saga-state
  persistence lands in the shared Marten store
- `docs/workshops/005-obligations-bc-deep-dive.md` §Decisions Log Decision 3 — the workshop
  decision this ADR formalizes; §Phase 3 storyboard — the state-driven lifecycle diagram
- `docs/narratives/006-seller-fulfills-post-sale-obligation.md` — the journey the saga hosts
- `openspec/changes/add-obligation-lifecycle/design.md` — the OpenSpec technical design
  recording the same saga-hosting decision as a capability-level choice
- `docs/skills/wolverine-sagas.md` — the implementation reference for M6-S2 onward; gets the
  Obligations amendment at the M6-S3/S4 retro
- `docs/retrospectives/M3-S5-auction-closing-saga-skeleton-retrospective.md` — the
  AuctionClosingSaga precedent (cancel-and-reschedule scheduled messages)
