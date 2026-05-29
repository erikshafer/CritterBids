# M6-S1: Obligations Foundation Decisions — Retrospective

**Date:** 2026-05-28
**Milestone:** M6 — Obligations BC + Relay BC
**Slice:** S1 of 7 — Foundation Decisions (Obligations Saga Shape + Demo-Mode Config + Hub Naming + Contract Stubs)
**Agent:** @PSA (with @PO consulted on the demo-mode config location)
**Prompt:** `docs/prompts/implementations/M6-S1-obligations-foundation-decisions.md`

## Baseline

- Branch `erikshafer/m6-obligations-opening` off main @ PR #46 (`5bb2dfd`).
- Design phase complete and committed: W005 workshop (`7662ae5`), narrative 006 (`6efa3c8`), OpenSpec change `add-obligation-lifecycle` + M6-S1 prompt (`b14f55d`).
- Full solution build green: 0 errors, 24 pre-existing NU1904 warnings (Marten 8.35.0 advisory — present before this slice; not in scope).
- `src/CritterBids.Contracts/` had no `Obligations/` namespace; ADR index next-unreserved was `022`.
- W001-6 already in PARKED-QUESTIONS Resolved table (resolved at W005 Decision 4).

## Items completed

| Item | Description |
|------|-------------|
| S1a | ADR-022 — Obligations saga hosting (Wolverine Saga, confirms ADR-019); ADR index + next-number pointer updated |
| S1b | W001-6 demo-mode config decision closed (shape fixed; `ObligationsOptions` code deferred to S2) |
| S1c | Four `CritterBids.Contracts.Obligations.*` integration-event records |
| S1d | Obligations event → SignalR hub-group mapping appended to `wolverine-signalr.md` |
| S1e | This retrospective |

## S1a: ADR-022 — Obligations saga hosting

**Why this approach.** Wolverine Saga, confirming the ADR-019 (Settlement) precedent rather than diverging. Process Managers via Handlers was rejected for the same reason ADR-019 rejected it for Settlement: the Obligations workflow is state-driven with loop-back transitions (non-terminal escalation, late-tracking recovery, dispute `Extension` reschedule) and its defining behavior — state-keyed scheduled-message cancellation — requires a persisted state document to hold the `bus.ScheduleAsync()` token ids. A handlers-only shape would re-invent saga state outside the primitive built for it.

The milestone doc §6 said a confirming-without-novelty decision could be a retro note rather than a full ADR. A full ADR was written anyway because Obligations introduces two patterns Settlement did not exercise — a **non-terminal escalation branch** with loop-back, and **state-keyed scheduled-message cancellation** as the workflow's centre of gravity — and those deserve a citable rationale for M6-S3/S4 and for Relay's contrasting handlers-based choice.

## S1b: W001-6 demo-mode config decision

W001-6 was already resolved at W005 Decision 4 (`ObligationsOptions` with real + demo durations, selected by config not `#if DEBUG`). This slice confirmed the shape and made the **location** decision that W005 left open: `ObligationsOptions` is BC-internal config, so its code lands in `src/CritterBids.Obligations` with the BC project in **M6-S2** — not in the contracts assembly (which is integration-events-only by discipline) and not by pulling the S2 scaffold forward. @PO confirmed this split. No `ObligationsOptions.cs` was authored this slice; the decision is the deliverable.

## S1c: Contract stubs

Four `sealed record` integration events in `src/CritterBids.Contracts/Obligations/`, payloads exactly per milestone doc §"Integration contracts authored in M6":

- `TrackingInfoProvided(ObligationId, ListingId, SellerId, TrackingNumber, ProvidedAt)`
- `ObligationFulfilled(ObligationId, ListingId, WinnerId, SellerId, FulfilledAt)`
- `DisputeOpened(ObligationId, ListingId, DisputeId, RaisedBy, Reason, OpenedAt)`
- `DisputeResolved(ObligationId, ListingId, DisputeId, ResolutionType, ResolvedAt)`

**Why string-valued enums.** `DisputeOpened.Reason` (`NonDelivery | ItemCondition | MissedDeadline`) and `DisputeResolved.ResolutionType` (`Refund | Extension | Closed`) are `string`, not int-valued enums — matching the `ListingPassed.Reason` precedent in `CritterBids.Contracts.Auctions`. The wire contract stays decoupled from any enum type; consumers and the saga's terminal-vs-continue branch pattern-match on the string constant. This closed the prompt's Open Question 3.

## S1d: Hub-group mapping

The group-key conventions (`bidder:{id}`, `listing:{id}`, `ops:staff`) already existed in `wolverine-signalr.md` § Hub Group Management. The slice added an "Obligations Event → Hub Group Mapping (M6)" subsection fixing which existing group each of the four events targets — no new conventions introduced. This is where Relay's S5/S6 prompts will look first, resolving the prompt's Open Question 2 in favour of the skill file over a milestone-doc §6 entry.

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Baseline | full solution build | 0 errors, 24 NU1904 warnings |
| After S1c (contracts) | `CritterBids.Contracts` build | 0 errors, 0 warnings |
| Session close | full solution build | 0 errors, 24 NU1904 warnings |

No test project was run: this slice added no behavior code (four new contract records nothing references yet, one ADR, doc edits). Test count is unchanged by construction.

## Build state at session close

- Errors: 0.
- Warnings: 24, all pre-existing NU1904 (Marten 8.35.0 advisory). Delta from baseline: 0. Not addressed — package-version remediation is out of scope for a contracts+docs slice.
- `CritterBids.Obligations` project: does not exist (S2). `CritterBids.Relay` project: does not exist (S5).
- Saga classes added: 0. Handlers added: 0. `ObligationsOptions.cs`: 0 (deferred to S2).
- New contract records in `Contracts/Obligations/`: 4. All `sealed record`. "Event"-suffix names: 0. "paddle" references: 0.

## Key learnings

1. **A capability-grain OpenSpec change can span multiple milestone slices.** `add-obligation-lifecycle` covers the whole obligation lifecycle (its `tasks.md` maps to S2–S4 work), while M6-S1 is foundation-only. The OpenSpec change is the design unit; the milestone slices are the execution units. They are not 1:1 and do not need to be.
2. **Config records are not contracts.** `ObligationsOptions` binds from appsettings and is consumed only inside the Obligations BC — it belongs with the BC project, not the integration-events assembly. When a foundation slice closes a config *decision* before the owning project exists, close the decision and defer the code to the project's slice.
3. **String-valued enums on integration-event payloads are the established CritterBids wire convention** (`ListingPassed.Reason` precedent). New BCs follow it for closed-set fields rather than introducing shared enum types across the contracts boundary.

## Findings against narrative

Narrative 006 (`docs/narratives/006-seller-fulfills-post-sale-obligation.md`) is the slice's anchor. This is a foundation slice — it implements none of the narrative's Moments (those land in S3–S4). The contract records and the saga-hosting decision are consistent with narrative 006 as drafted: Moment 3 (tracking) ↔ `TrackingInfoProvided`, Moment 4 (auto-confirm close) ↔ `ObligationFulfilled`. No drift surfaced; no finding routed to any of the four lanes. The narrative's Document History gains no row this slice — its Moments are not yet code.

## Spec delta — landed?

Landed as written, with the foundation portion the prompt scoped. The OpenSpec `add-obligation-lifecycle` change (proposal, design, delta spec, tasks) governs the capability's spec consequence and was authored/committed in the design phase (`b14f55d`); this slice did not modify it. The S1 code anchors landed: the four integration-event contracts compile, and **ADR-022 (Obligations saga hosting)** was authored as a new governing decision (status Accepted) and recorded in `docs/decisions/README.md`. The delta spec's saga-behavior requirements remain unimplemented until S2–S4, as planned. W001-6 remained Resolved (closed at W005 Decision 4; this slice added the location sub-decision recorded in S1b above). No narrative or workshop Document History row was required — no Moment was implemented and no workshop slice gained or lost coverage.

## Verification checklist

- [x] `docs/decisions/022-obligations-saga-hosting.md` exists, status Accepted, cites ADR-019; README index + next-number pointer (now `023`) updated
- [x] Demo-mode config approach recorded (S1b); `ObligationsOptions` code deferred to S2 per @PO decision; W001-6 confirmed Resolved in `PARKED-QUESTIONS.md` (closed at W005 Decision 4)
- [x] Four `CritterBids.Contracts.Obligations.*` records exist as `sealed record`, payloads exactly per milestone doc, no "Event" suffix, no "paddle"
- [x] `BiddingHub` / `OperationsHub` group-naming captured durably (appended to `wolverine-signalr.md`)
- [x] `dotnet build` passes (0 errors); test baseline unchanged (no new tests this slice)
- [x] This retrospective written with `## Spec delta — landed?`
- [x] No `CritterBids.Obligations` or `CritterBids.Relay` project created; no saga or handler code added
- [x] No commit to `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M6-S2 (in scope, next):** create `CritterBids.Obligations` + `CritterBids.Obligations.Tests`; `AddObligationsModule()`; Marten config; `Program.cs` wiring; `ObligationsIdentityNamespaces.PostSaleCoordination`; **author `ObligationsOptions`** (deferred from S1); `obligations-settlement-events` route; `SettlementCompletedHandler` starts the saga.
- **Begin applying the OpenSpec change** (`/opsx:apply add-obligation-lifecycle`) at S2 — S1 produced no task check-offs in `tasks.md` beyond ADR-022 (task 1.6).
- **Open Question for S4:** confirm whether `DisputeOpened.Reason` should become an enum on the saga side even though the wire stays string — defer-to-S4 stance taken here.
- **Out of scope, tracked elsewhere:** the NU1904 Marten advisory warnings (repo-wide, pre-existing); package remediation is a separate concern, not an M6-S1 deliverable.
