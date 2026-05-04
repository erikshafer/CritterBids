# ADR 019: Settlement Workflow Hosting

**Status:** Accepted
**Date:** 2026-05-03

---

## Context

The Settlement BC's seven-phase financial workflow (Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed, with a `Failed` exit at any phase via `PaymentFailed`) lands in M5. Workshop 003 (`docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 2) presents two hosting paths for that workflow and explicitly defers the choice to implementation time:

- **Wolverine Saga.** The established CritterBids pattern. The Auction Closing saga shipped at M3-S5 is the lived precedent: a `Saga` subclass with mutable state, per-phase `Handle` methods, self-sending continuation commands, and `MarkCompleted()` at terminal state. Wolverine owns saga-state persistence via Marten and routes inbound commands through its inbox.
- **`ProcessManager<TState>` decider.** A framework Erik (JasperFx core team) is actively designing as a Wolverine primitive. Models the workflow as three pure functions — `Decide(state, command) → events`, `Evolve(state, event) → state`, and an explicit discriminated-union state type — so invalid transitions are pattern-match misses (compile-time errors when a new state lands), not runtime nullable checks. The exact framework API lives in Erik's in-progress JasperFx proposal; W003's sketches are conceptual.

W003 §Part 2's design decision is that the workflow is **designed around decider semantics regardless of implementation choice** — the events, phases, transitions, scenarios, and state-shape are identical between the two hosts. Only the framework that holds the workflow differs. The 41 scenarios in `003-scenarios.md` are written so 28 of them (Sections 1-7) are pure-function decider/evolver tests that pass against either host; only Section 9's five integration scenarios touch hosting-specific machinery.

The decision has to land at M5-S1 because M5-S2 (BC scaffold + module wiring) and onwards immediately consume the choice. Deferring further would either block M5-S2 or force the foundation decision to surface mid-implementation — the failure mode the M2 retrospective's "three rapid ADR pivots" warning called out.

Two facts shape the choice as of 2026-05-03:

1. **Framework readiness asymmetry.** The Wolverine Saga primitive ships in Wolverine 5+ and has shipped a saga in CritterBids (M3-S5 Auction Closing). `ProcessManager<TState>` is in active design. The framework's API surface, lifecycle hooks, persistence model, and integration with Wolverine's outbox / inbox / scheduling primitives are not yet stable.
2. **The decider-semantic preservation gate.** W003's Part 2 decision and the 28 pure-function scenarios mean the migration cost from Saga to `ProcessManager<TState>` is bounded — the events, state shapes, and transitions transfer verbatim. Only the host wrapper changes. That migration cost is real but mechanical, not semantic.

This ADR closes the choice for M5 implementation. It does not foreclose `ProcessManager<TState>` adoption later; it scopes the conditions under which the choice is revisited.

## Options Considered

### Option A: Wolverine Saga (chosen)

Settlement implements the workflow as a `Saga`-derived class. State is a single mutable document with a `SettlementStatus` enum tracking phase progression. Per-phase handlers (`Handle(CheckReserve)`, `Handle(ChargeWinner)`, `Handle(CalculateFee)`, `Handle(IssueSellerPayout)`, `Handle(CompleteSettlement)`, `Handle(FailSettlement)`) consume self-sent continuation commands. The saga is persisted via Marten under `SettlementId` (deterministic UUID v5 per W003 Phase 1 Part 6); `MarkCompleted()` terminates at `Completed` or `Failed`.

The W003 §Part 2 decider-semantic discipline is preserved at the design level: the events emitted, the phase order, the validation rules, and the scenarios from `003-scenarios.md` Sections 1-9 all apply unchanged. The discriminated-union state type sketched in W003 collapses into a `SettlementStatus` enum plus nullable fields on the saga document; the runtime checks the W003 sketch made compile-time become assertion-or-throw checks at handler entry. The scenarios in Sections 1-7 pass as pure-function decider/evolver tests against helper methods extracted from the saga's `Handle` bodies; the scenarios in Section 9 pass as Wolverine integration tests using Alba and Testcontainers per the standard CritterBids stack.

This option's costs are documented honestly: the type-safety gains the decider pattern provides (a `FeeCalculated` state cannot expose null `FeeAmount`) become disciplined nullable handling on the saga document, with the discipline carried by code review and the handler-entry assertions. The flow visibility gains (single decider switch vs seven scattered handlers) are similarly trade-offs the lived M3-S5 Auction Closing saga has already absorbed without incident.

### Option B: `ProcessManager<TState>` decider (deferred)

Settlement implements the workflow as a `ProcessManager<TState>` consumer (final API TBD). State is a discriminated union (`SettlementState.Initiated`, `SettlementState.ReserveChecked`, `SettlementState.WinnerCharged`, `SettlementState.FeeCalculated`, `SettlementState.PayoutIssued`, `SettlementState.Completed`, `SettlementState.Failed`) with phase-specific fields populated only on the relevant variant. `Decide` and `Evolve` are pure functions; the framework wraps the load-decide-evolve loop and handles persistence, dispatch, and scheduling.

The advantages are real and aligned with the financial-workflow domain: invalid transitions cannot compile, the decider is a one-line pure-function test per scenario, the state machine is visible in one place, and CritterBids becomes the first lived `ProcessManager<TState>` example — feeding implementation experience back into the JasperFx framework design.

The cost as of 2026-05-03 is timing. The framework API is not stable. M5 cannot block on framework finalization without blocking the demo-vehicle and reference-architecture goals CritterBids serves. Adopting an in-design framework primitive in a milestone slice introduces risk that is bounded only by the framework's own development cadence, which CritterBids does not control even though Erik is the framework's author.

### Option C: Hybrid — Saga shell with decider-pattern internals

Implement as a `Saga`-derived class but extract a pure `Decide` static helper that the per-phase handlers call. The saga becomes a thin host that handles persistence and command dispatch; the business logic lives in pure functions identical in shape to what `ProcessManager<TState>` would consume.

This option preserves the pure-function testability for Sections 1-7 of `003-scenarios.md` while retaining the established Wolverine Saga primitive for hosting. The cost is duplication: the saga document holds mutable state, and the decider operates on a discriminated-union state type, so a small adapter layer translates between the two on each handler entry.

Rejected for M5 in favor of Option A's simpler shape, but flagged as a viable intermediate step if `003-scenarios.md` Sections 1-7's pure-function tests turn out to need decider-shaped helpers anyway during M5-S4 implementation. If that need surfaces, Option C is a sub-decision inside Option A — adopt the helper extraction without changing the host. This ADR's scope is host choice; helper-extraction is implementation-detail and lives in the M5-S4 retrospective if it materializes.

## Decision

**Option A.** Settlement implements its workflow as a Wolverine Saga in M5. The saga's shape mirrors W003 Phase 1 Part 2 Approach A: a `SettlementSaga : Saga` document with a `SettlementStatus` enum tracking phase progression, per-phase `Handle` methods, self-sending continuation commands (`CheckReserve`, `ChargeWinner`, `CalculateFee`, `IssueSellerPayout`, `CompleteSettlement`), and `MarkCompleted()` at terminal state. Persistence is via Marten under the deterministic UUID v5 `SettlementId` per W003 Phase 1 Part 6.

The W003 §Part 2 design discipline of "decider semantics regardless of host" is preserved at the workshop and scenarios level: events, state transitions, phase order, and the 41 scenarios in `003-scenarios.md` apply unchanged. M5-S4's implementation may extract pure-function helpers from the saga's per-phase handlers when scenarios from Sections 1-7 (28 pure-function decider/evolver tests) demand them; that extraction is implementation-detail inside Option A and does not require an ADR amendment.

This decision is **not** a rejection of `ProcessManager<TState>`. It is a sequencing choice: ship M5 on the established Saga primitive now, migrate when the framework primitive ships and stabilizes. The 28 pure-function scenarios are the migration's contract — they pass against either host.

### Revisit triggers

This ADR is reopened when **any one** of the following lands:

1. **`ProcessManager<TState>` framework API stabilizes** in a Wolverine release (1.0-grade API surface for the primitive, with persistence integration, scheduling, and outbox semantics confirmed). The natural revisit point is the next post-M5 milestone where Settlement is touched substantively; the migration is its own slice with its own retro.
2. **The Saga shape produces specific friction** during M5 implementation that the decider pattern would have prevented. Examples that would justify revisit: nullable-field correctness bugs surfacing in Sections 1-7's scenarios, handler-entry assertion duplication across more than two handlers, or `SettlementStatus` enum drift requiring repeated W003 amendments. Each of these is a M5-S{2-6} retrospective signal; the cumulative pattern triggers revisit, not any single occurrence.
3. **JasperFx project direction explicitly requires CritterBids to be the first lived `ProcessManager<TState>` example.** Erik holds the framework-roadmap context; if framework-evangelism timing makes CritterBids the demonstration vehicle, the decision is reopened on that input. The migration would land as its own milestone slice (likely a post-M5 BC-internal refactor PR) with its own retro and acceptance criteria.

The default migration path when any trigger fires: a separate, well-scoped slice prompt under `docs/prompts/implementations/<slug>.md` consuming the W003 Phase 1 Part 2 design (unchanged) and the 28 pure-function scenarios (unchanged). The migration's diff lives in the BC's hosting wrapper and the saga-vs-process-manager skill file; the integration scenarios from Section 9 are exercised end-to-end against the new host to confirm equivalence.

## Consequences

### M5 implementation proceeds on the Wolverine Saga primitive

M5-S2 onwards builds against the Saga shape. The skill file `docs/skills/wolverine-sagas.md` is the implementation reference; `docs/skills/wolverine-message-handlers.md` is the cross-reference for handler conventions. M5-S4 retrospectively amends `wolverine-sagas.md` with the Settlement-side example (the seven-phase progression with self-sent continuation commands is structurally distinct from the Auction Closing saga's two-phase shape).

The 28 pure-function scenarios from `003-scenarios.md` Sections 1-7 are exercised as helper-method tests if the per-phase handlers extract pure decider/evolver helpers, or as saga-harness tests if they don't. M5-S4's retro records the choice; either way, the scenarios pass.

### `wolverine-sagas.md` skill file gets the Settlement amendment after M5-S4 ships, not in M5-S1

The skill file's M5 amendment is flagged as in-scope for the M5-S4 retrospective, not this slice. M5-S1 is docs-and-stubs only — no implementation, no skill-file edits beyond flagging. This mirrors the precedent set by the M3-S6 / `marten-projections.md` cross-BC view-extension amendment landing at the slice that produced the lived example, not the slice that decided the pattern.

### `marten-projections.md` skill file flagged for M5-S3 cross-BC-event-seeded projection amendment

Independent of the saga vs ProcessManager choice, the `PendingSettlement` projection is the first CritterBids projection seeded from a cross-BC integration event (`ListingPublished` from Selling) rather than from same-BC streams. M5-S3 (the projection's implementation slice) retrospectively amends `marten-projections.md` with the pattern. M5-S1 flags the file as in-scope for that future amendment; the full pattern documentation lands at M5-S3's retro. (See the F002 amendment in W003 Phase 1 Part 2 for the rename rationale that the saga-vs-decider hosting comparison frames.)

### Decider-pattern semantic discipline holds at the workshop layer

W003 Part 2's "design around decider semantics" decision is unchanged. Future amendments to W003 (M5-S1's F002, F004, F005 amendments per narrative 002 findings; any future workshop-cleanup) preserve the decider framing. If a future ADR migrates the host to `ProcessManager<TState>`, W003 does not require restructuring — the workshop already speaks the decider's vocabulary.

### `ProcessManager<TState>` adoption remains a future option, framed as a single-slice migration

When any of the three revisit triggers fires, the migration is one slice's work: rewrite the Saga shell as a ProcessManager consumer; preserve the events, scenarios, and W003 design verbatim; verify Section 9's integration scenarios pass against the new host. The skill file `wolverine-sagas.md` extends to cover the new pattern (or splits to a dedicated `process-manager.md` per the established skill-file pattern when files grow past coherent scope). No W003 rewrite, no scenario rewrite, no integration-event-contract change — the contract stubs authored at M5-S1 (`SettlementCompleted`, `PaymentFailed`, `SellerPayoutIssued`) remain stable across the migration.

### Out-of-scope for this ADR

This ADR does not close any of the following, by design:

- The fee-percentage configuration boundary (W003 Phase 1 Q6's "platform config vs per-seller vs per-listing" question) — that lives in M5-S3 alongside `PendingSettlement`'s seed-from-`ListingPublished` work.
- Compensation paths beyond MVP (W003 Phase 1 Part 3 defers; this ADR honors the deferral).
- Real payment-processor integration (W003 §"Winner Charge" defers; this ADR honors the deferral).
- The bidder-credit projection name, shape, lifecycle, and consumer model — those land in this same M5-S1 slice via the W003 F005 amendment.

## References

- `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 2 — the workshop framing this ADR closes; presents both options at design-grade with the decider-semantic preservation decision
- `docs/workshops/003-scenarios.md` Sections 1-9 — the 41 scenarios that pass against either host (Sections 1-7 are pure-function tests applicable to both)
- `docs/narratives/002-winner-clears-settlement.md` — the joint-authoritative narrative for M5-S1 per AUTHORING.md rule 3; renders the saga's per-phase progression as five Moments without committing to host choice
- `docs/decisions/007-uuid-strategy.md` — UUID v5 deterministic stream ID convention used by `SettlementId` per W003 Phase 1 Part 6
- `docs/decisions/011-all-marten-pivot.md` — Settlement uses Marten on PostgreSQL; saga-state persistence and the financial event stream both land in the shared Marten store
- `docs/skills/wolverine-sagas.md` — the implementation reference for M5-S2 onwards; gets the Settlement amendment at M5-S4 retro
- `docs/skills/wolverine-message-handlers.md` — handler conventions cross-reference
- `docs/retrospectives/M3-S5-auctions-saga-extended-bidding-retrospective.md` (and adjacent M3-S5 retros) — the lived precedent for the Wolverine Saga pattern in CritterBids
