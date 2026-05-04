# ADR 019: Settlement Workflow Hosting

**Status:** Accepted
**Date:** 2026-05-03

---

## Context

The Settlement BC's seven-phase financial workflow (Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed, with a `Failed` exit at any phase via `PaymentFailed`) lands in M5. The implementation host needs deciding before M5-S2 begins.

**CritterBids' framing constraint.** CritterBids uses shipped Wolverine features only. The proposed `ProcessManager<TState>` framework primitive that Erik is designing for Wolverine is explicitly out of scope as an implementation choice — that work is JasperFx framework-design territory, not a CritterBids implementation roadmap item. Within shipped Wolverine, two coordination patterns are available for the Settlement workflow:

- **Wolverine Saga.** The established CritterBids pattern. The Auction Closing saga shipped at M3-S5 is the lived precedent: a `Saga` subclass with mutable state, per-phase `Handle` methods, self-sending continuation commands, and `MarkCompleted()` at terminal state. Wolverine owns saga-state persistence via Marten and routes inbound commands through its inbox. Fits workflows whose phases share evolving state (`HammerPrice`, `FeePercentage`, `FeeAmount` once calculated, etc.) that subsequent phases read without re-deriving.
- **Process Managers via Handlers.** Wolverine's shipped event-reactive coordination pattern. Each handler reacts to a domain or integration event independently; coordination emerges from the handler chain rather than from a stateful saga document. Fits workflows whose steps don't share evolving state — pure event-cascade pipelines where each handler's input is the previous handler's output event. Relay-side broadcast pipelines are the canonical fit; the Auctions BC's cross-listing read-model handlers are also handlers-based, not saga-based.

W003 (`docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 2) was authored before this stance was made explicit; it framed the comparison as Saga vs the proposed `ProcessManager<TState>` primitive. The corrected framing is documented in the W003 amendment landing alongside this ADR.

**Decider-pattern semantics as a design lens.** Independent of host choice, the decider pattern (discriminated-union state types, pure-function `Decide` and `Evolve`) is a useful *design lens* for type-safe state machines. W003's design decision — "design around decider semantics regardless of implementation choice" — applies to either host: the events, phases, transitions, and scenarios in `003-scenarios.md` are identical regardless of whether the host is a Saga or a Handlers-based pipeline. The 28 pure-function scenarios in Sections 1-7 of `003-scenarios.md` test the decider pattern's `Decide(state, command) → events` and `Evolve(state, event) → state` shapes directly, with no framework dependency.

The decision has to land at M5-S1 because M5-S2 (BC scaffold + module wiring) and onwards immediately consume the choice. Deferring further would force the foundation decision to surface mid-implementation — the failure mode the M2 retrospective's "three rapid ADR pivots" warning called out.

Two facts shape the choice as of 2026-05-03:

1. **Phased-state-fit asymmetry.** Settlement's seven phases share evolving state — `HammerPrice` and `FeePercentage` are read at multiple phases; `FeeAmount` and `SellerPayout` materialize at the FeeCalculated phase and are read at PayoutIssued and Completed; the participant identifiers persist across the entire saga. This is the shape Wolverine Saga is designed to host. Process Managers via Handlers fit workflows where each step is event-reactive without shared state; forcing Settlement onto a handlers-only shape would require either a separate state document threaded through every handler (re-inventing saga state with extra plumbing) or hydrating state from the event stream on every handler entry (re-inventing event sourcing's read path on a per-handler basis).
2. **The decider-semantic preservation gate.** W003's Part 2 decision and the 28 pure-function scenarios mean the workflow's *semantics* — events, state shapes, transitions — are decoupled from the host primitive. If a future BC's coordination shape calls for Handlers (e.g., Relay's broadcast pipeline), the same scenario-grade test discipline applies. The lens is portable; the host is per-BC.

This ADR closes the choice for M5 implementation. Process Managers via Handlers remains the right tool for future BCs whose coordination shape is event-reactive without phased state; it is not foreclosed for CritterBids — it is simply not the right host for Settlement.

## Options Considered

### Option A: Wolverine Saga (chosen)

Settlement implements the workflow as a `Saga`-derived class. State is a single mutable document with a `SettlementStatus` enum tracking phase progression. Per-phase handlers (`Handle(CheckReserve)`, `Handle(ChargeWinner)`, `Handle(CalculateFee)`, `Handle(IssueSellerPayout)`, `Handle(CompleteSettlement)`, `Handle(FailSettlement)`) consume self-sent continuation commands. The saga is persisted via Marten under `SettlementId` (deterministic UUID v5 per W003 Phase 1 Part 6); `MarkCompleted()` terminates at `Completed` or `Failed`.

The W003 §Part 2 decider-semantic discipline is preserved at the design level: the events emitted, the phase order, the validation rules, and the scenarios from `003-scenarios.md` Sections 1-9 all apply unchanged. The discriminated-union state type sketched in W003 collapses into a `SettlementStatus` enum plus nullable fields on the saga document; the runtime checks the W003 sketch made compile-time become assertion-or-throw checks at handler entry. The scenarios in Sections 1-7 pass as pure-function decider/evolver tests against helper methods extracted from the saga's `Handle` bodies; the scenarios in Section 9 pass as Wolverine integration tests using Alba and Testcontainers per the standard CritterBids stack.

This option's costs are documented honestly: the type-safety gains the decider pattern provides (a `FeeCalculated` state cannot expose null `FeeAmount`) become disciplined nullable handling on the saga document, with the discipline carried by code review and the handler-entry assertions. The flow visibility gains (single decider switch vs seven scattered handlers) are similarly trade-offs the lived M3-S5 Auction Closing saga has already absorbed without incident.

### Option B: Process Managers via Handlers

Settlement implements the workflow as a chain of Wolverine handlers, each reacting to a specific event. `ListingSold` arrives at `InitiateSettlementHandler`, which emits `SettlementInitiated` and triggers a self-send `CheckReserveCommand`. A separate `CheckReserveHandler` consumes that and emits `ReserveCheckCompleted`, triggering the next step. Each handler is independent; no shared mutable state document persists across the chain.

State that subsequent phases need (`HammerPrice`, `FeePercentage`, the materialized `FeeAmount` after calculation) has to live somewhere. The two paths are: (a) thread the state through the self-sent commands as growing payloads, accumulating fields on each command; (b) hydrate state from the event stream on every handler entry, replaying `SettlementInitiated` plus all subsequent events to rebuild the current shape. Path (a) couples handler signatures to evolving payload contracts; path (b) re-implements event sourcing's read path on a per-handler basis.

Either path works for event-reactive coordination without phased state — the handler chain is what Wolverine ships for that shape. Settlement is not that shape. The seven phases share state by design (W003 Phase 1 Part 2's framing is explicitly "phased progression with evolving state"); forcing Settlement onto a Handlers-only shape would invent saga state outside the saga primitive that is built for it.

This option remains the right host for future CritterBids BCs whose coordination is event-reactive without phased state. The Relay BC's broadcast pipeline (post-M5) is the most likely first adopter — each broadcast event is a leaf reaction with no continuation state to thread. The Auctions BC's cross-listing read-model handlers (`AuctionStatusHandler`, `ListingSnapshotHandler` from M3-S6) are also handlers-based; they are the lived precedent for Wolverine handler coordination in CritterBids. Settlement is not the right test case for the pattern; ADR-019 closes that misfit.

### Option C: Hybrid — Saga shell with decider-pattern internals

Implement as a `Saga`-derived class but extract a pure `Decide` static helper that the per-phase handlers call. The saga becomes a thin host that handles persistence and command dispatch; the business logic lives in pure functions whose shape matches the decider lens W003 documents. The discriminated-union state type can be modeled as records the saga maps to/from at handler entry/exit, or as a parallel "decision context" object that the helper consumes without touching the saga's mutable fields.

This option preserves the pure-function testability for Sections 1-7 of `003-scenarios.md` while retaining the Saga primitive for hosting. The cost is a small adapter layer between the saga's mutable document and the decider helper's value-typed inputs.

Rejected for M5 in favor of Option A's simpler shape, but flagged as a viable intermediate step if `003-scenarios.md` Sections 1-7's pure-function tests turn out to need decider-shaped helpers anyway during M5-S4 implementation. If that need surfaces, Option C is a sub-decision inside Option A — adopt the helper extraction without changing the host. This ADR's scope is host choice; helper-extraction is implementation-detail and lives in the M5-S4 retrospective if it materializes.

### Out of scope: the proposed `ProcessManager<TState>` framework primitive

Erik (JasperFx core team) is designing a `ProcessManager<TState>` primitive for Wolverine that wraps the load-decide-evolve loop natively, with framework-managed state hydration from the event stream and pattern-matched `Decide`/`Evolve` pure functions. CritterBids' stance is to use shipped Wolverine features only; the proposed primitive is JasperFx framework-design work and is **not** an implementation option for CritterBids. The decider pattern's *design lens* (discriminated-union state, pure-function semantics) remains useful regardless — it survives in W003 as a design discipline applied to Saga or Handlers, not as a future framework target.

This ADR does not gate on the proposed primitive's stabilization, because adopting it is not the plan. If JasperFx ships the primitive in a future Wolverine release and CritterBids' direction shifts to adopt shipped framework features that include it, that is a separate ADR and a separate decision — not a revisit trigger of this one.

## Decision

**Option A.** Settlement implements its workflow as a Wolverine Saga in M5. The saga's shape mirrors W003 Phase 1 Part 2 Approach A: a `SettlementSaga : Saga` document with a `SettlementStatus` enum tracking phase progression, per-phase `Handle` methods, self-sending continuation commands (`CheckReserve`, `ChargeWinner`, `CalculateFee`, `IssueSellerPayout`, `CompleteSettlement`), and `MarkCompleted()` at terminal state. Persistence is via Marten under the deterministic UUID v5 `SettlementId` per W003 Phase 1 Part 6.

The choice between Option A (Saga) and Option B (Handlers) turns on Settlement's phased-state shape: the seven phases share evolving state by design, which is exactly what the Saga primitive is built to host. Handlers fit event-reactive coordination without phased state; Settlement is not that shape. Option B remains the right host for future CritterBids BCs whose coordination is leaf-reactive (Relay's broadcast pipeline is the canonical post-M5 candidate); choosing it for Settlement would re-invent saga-state plumbing outside the primitive Wolverine ships for that purpose.

The W003 §Part 2 decider-pattern design lens is preserved at the workshop and scenarios level: events, state transitions, phase order, and the 41 scenarios in `003-scenarios.md` apply unchanged. M5-S4's implementation may extract pure-function helpers from the saga's per-phase handlers when scenarios from Sections 1-7 (28 pure-function decider/evolver tests) demand them; that extraction is implementation-detail inside Option A and does not require an ADR amendment. This is the discriminated-union design lens applied to a Saga host, not the proposed framework primitive.

### Revisit trigger

This ADR is reopened when **the Saga shape produces specific friction** during M5 implementation that the decider design lens (Option C) or the Handlers shape (Option B) would have prevented. Examples that would justify revisit: nullable-field correctness bugs surfacing in Sections 1-7's scenarios, handler-entry assertion duplication across more than two handlers, or `SettlementStatus` enum drift requiring repeated W003 amendments. Each of these is a M5-S{2-6} retrospective signal; the cumulative pattern triggers revisit, not any single occurrence.

The default response if the trigger fires: extract pure-function decider helpers per Option C inside the existing Saga host. That keeps the change scoped to one BC's internals without disturbing the contracts, scenarios, or W003 design. Migrating to Option B (Handlers) would only become the right move if M5 implementation revealed Settlement's coordination shape to be more event-reactive than W003 modeled — an unlikely outcome given the workshop's explicit "phased progression with evolving state" framing, but recorded here for completeness.

This ADR does **not** gate on framework-design work outside CritterBids' shipped-Wolverine stance. The proposed `ProcessManager<TState>` primitive's stabilization is not a revisit trigger; if JasperFx ships it and CritterBids' direction shifts to adopt shipped framework features that include it, that is a separate ADR.

## Consequences

### M5 implementation proceeds on the Wolverine Saga primitive

M5-S2 onwards builds against the Saga shape. The skill file `docs/skills/wolverine-sagas.md` is the implementation reference; `docs/skills/wolverine-message-handlers.md` is the cross-reference for handler conventions. M5-S4 retrospectively amends `wolverine-sagas.md` with the Settlement-side example (the seven-phase progression with self-sent continuation commands is structurally distinct from the Auction Closing saga's two-phase shape).

The 28 pure-function scenarios from `003-scenarios.md` Sections 1-7 are exercised as helper-method tests if the per-phase handlers extract pure decider/evolver helpers, or as saga-harness tests if they don't. M5-S4's retro records the choice; either way, the scenarios pass.

### `wolverine-sagas.md` skill file gets the Settlement amendment after M5-S4 ships, not in M5-S1

The skill file's M5 amendment is flagged as in-scope for the M5-S4 retrospective, not this slice. M5-S1 is docs-and-stubs only — no implementation, no skill-file edits beyond flagging. This mirrors the precedent set by the M3-S6 / `marten-projections.md` cross-BC view-extension amendment landing at the slice that produced the lived example, not the slice that decided the pattern.

### `marten-projections.md` skill file flagged for M5-S3 cross-BC-event-seeded projection amendment

Independent of the host-choice decision, the `PendingSettlement` projection is the first CritterBids projection seeded from a cross-BC integration event (`ListingPublished` from Selling) rather than from same-BC streams. M5-S3 (the projection's implementation slice) retrospectively amends `marten-projections.md` with the pattern. M5-S1 flags the file as in-scope for that future amendment; the full pattern documentation lands at M5-S3's retro.

### Decider-pattern design lens holds at the workshop layer

W003 Part 2's "design around decider semantics" decision survives as a design lens, not as a future framework target. Future amendments to W003 (M5-S1's F002, F004, F005 amendments per narrative 002 findings; any future workshop-cleanup) preserve the lens. The Saga's per-phase handlers may extract pure `Decide`/`Evolve` helpers (Option C inside Option A) without ADR amendment.

### Process Managers via Handlers remains the right tool for future event-reactive coordination

Option B is not foreclosed for CritterBids — it is correctly scoped to event-reactive coordination shapes that don't share phased state. Future BCs whose workflow is a leaf-reaction cascade (Relay's broadcast pipeline post-M5; cross-BC read-model updates of the kind M3-S6 already lived) use Handlers. The ADR's positive claim: Saga for Settlement; Handlers for Relay-style broadcast cascades; the choice is per-BC by coordination shape.

### Out-of-scope for this ADR

This ADR does not close any of the following, by design:

- The fee-percentage configuration boundary (W003 Phase 1 Q6's "platform config vs per-seller vs per-listing" question) — that lives in M5-S3 alongside `PendingSettlement`'s seed-from-`ListingPublished` work.
- Compensation paths beyond MVP (W003 Phase 1 Part 3 defers; this ADR honors the deferral).
- Real payment-processor integration (W003 §"Winner Charge" defers; this ADR honors the deferral).
- The bidder-credit projection name, shape, lifecycle, and consumer model — those land in this same M5-S1 slice via the W003 F005 amendment.

## References

- `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 2 — the workshop framing this ADR closes; the corrected Saga vs Handlers comparison lands in the W003 amendment alongside this ADR
- `docs/workshops/003-scenarios.md` Sections 1-9 — the 41 scenarios that pass against either Saga or Handlers (Sections 1-7 are pure-function decider/evolver tests applicable as design-lens helpers regardless of host)
- `docs/narratives/002-winner-clears-settlement.md` — the joint-authoritative narrative for M5-S1 per AUTHORING.md rule 3; renders the saga's per-phase progression as five Moments without committing to host choice
- `docs/decisions/007-uuid-strategy.md` — UUID v5 deterministic stream ID convention used by `SettlementId` per W003 Phase 1 Part 6
- `docs/decisions/011-all-marten-pivot.md` — Settlement uses Marten on PostgreSQL; saga-state persistence and the financial event stream both land in the shared Marten store
- `docs/skills/wolverine-sagas.md` — the implementation reference for M5-S2 onwards; gets the Settlement amendment at M5-S4 retro
- `docs/skills/wolverine-message-handlers.md` — handler conventions cross-reference (also the reference for Option B's Process Managers via Handlers shape, applicable to future event-reactive BCs)
- `docs/retrospectives/M3-S5-auctions-saga-extended-bidding-retrospective.md` (and adjacent M3-S5 retros) — the lived precedent for the Wolverine Saga pattern in CritterBids
- `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md` — the lived precedent for Wolverine Handlers coordination (`AuctionStatusHandler`, `ListingSnapshotHandler` cross-BC sibling-handler pattern); the shape Option B would extend
