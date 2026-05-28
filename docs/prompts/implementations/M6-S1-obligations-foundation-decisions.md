# M6-S1: Obligations Foundation Decisions — Saga Shape, Demo-Mode Config, Hub Naming, Contract Stubs

**Milestone:** M6 — Obligations BC + Relay BC
**Slice:** S1 of 7 (foundation slice; no BC project is created here — that is S2)
**Narrative:** `docs/narratives/006-seller-fulfills-post-sale-obligation.md` (foundation slice; the narrative's Moments are implemented in S3–S4, but S1 pins the decisions those Moments depend on)
**Agent:** @PSA (with @PO consulted on the W001-6 demo-mode decision)
**Estimated scope:** one PR; ~4 new contract records, 1 ADR, 1 config record, hub-naming convention captured; no saga, no handlers, no BC project

---

## Goal

Land the foundation decisions the Obligations BC's implementation slices (S2–S4) depend on, so that no later slice has to stop and escalate an architecture question mid-flight. This session makes the Obligations saga-hosting decision and records it as ADR-022 (confirming or diverging from ADR-019's Wolverine Saga precedent), closes W001-6 by choosing the demo-mode timeout configuration shape, fixes the `BiddingHub` / `OperationsHub` SignalR group-naming conventions Relay will consume, and authors the four `CritterBids.Contracts.Obligations.*` integration-event stubs. The design work (Event Modeling W005, narrative 006, and the OpenSpec `add-obligation-lifecycle` proposal) is already complete and committed; this slice converts those decisions into the first durable code and the governing ADR.

## Context to load

- `docs/milestones/M6-obligations-relay-bc.md` — §1 exit criteria, §6 Conventions Pinned (saga hosting, UUID strategy, hub naming), §7 slice table (S1 row), §"open questions" W001-6 row
- `CLAUDE.md` — routing layer and global conventions (sealed record, no "Event" suffix, no "paddle", `[AllowAnonymous]` through M6)
- `openspec/changes/add-obligation-lifecycle/` — the proposal, `design.md`, delta spec, and `tasks.md`; authoritative for the obligation-lifecycle capability this BC will implement
- `docs/decisions/019-settlement-workflow-hosting.md` — the Wolverine-Saga precedent ADR-022 must cite
- `docs/decisions/README.md` — ADR index + next-number convention (next unreserved is **022**, not 020 as the milestone doc's stale reference says)
- `docs/skills/wolverine-sagas.md` — saga-hosting rationale to weigh in ADR-022
- `docs/workshops/005-obligations-bc-deep-dive.md` — §Decisions Log (the five W005 decisions) + §Ubiquitous Language (contract payload fields)

## In scope

1. **ADR-022 — Obligations saga hosting.** Author `docs/decisions/022-obligations-saga-hosting.md`. Evaluate Wolverine Saga vs process-manager-by-hand; cite ADR-019 as precedent and the W005 Decision 3 rationale. Record the decision, status `Accepted`, and update `docs/decisions/README.md` index + next-number pointer.
2. **W001-6 demo-mode timeout config decision + `ObligationsOptions` record.** Choose among (A) `DemoMode` appsettings flag, (B) `ObligationsOptions` section with explicit production + demo durations, (C) hardcoded `#if DEBUG`. Decision 4 of W005 selected the `ObligationsOptions` shape — confirm with @PO and author the `ObligationsOptions` record in `src/CritterBids.Contracts/Obligations/` (or the BC namespace if the team prefers options stay BC-internal — flag in Open Questions). Record the closure in the M6-S1 retro and flip W001-6 to Resolved in `docs/workshops/PARKED-QUESTIONS.md`.
3. **Four integration-event contract stubs** in `src/CritterBids.Contracts/Obligations/` — `sealed record`, no "Event" suffix, `IReadOnlyList<T>` where applicable, payloads exactly per milestone doc §"Integration contracts authored in M6":
   - `TrackingInfoProvided` (`ObligationId`, `ListingId`, `SellerId`, `TrackingNumber`, `ProvidedAt`)
   - `ObligationFulfilled` (`ObligationId`, `ListingId`, `WinnerId`, `SellerId`, `FulfilledAt`)
   - `DisputeOpened` (`ObligationId`, `ListingId`, `DisputeId`, `RaisedBy`, `Reason`, `OpenedAt`)
   - `DisputeResolved` (`ObligationId`, `ListingId`, `DisputeId`, `ResolutionType` (`Refund | Extension | Closed`), `ResolvedAt`)
4. **SignalR hub group-naming convention** for `BiddingHub` and `OperationsHub` — captured as a short convention note (in the milestone doc §6 if it belongs there, or a skill-file append to `docs/skills/wolverine-signalr.md`). No hub code is written this slice; only the group-naming contract Relay's S5/S6 slices will consume.
5. `docs/retrospectives/M6-S1-obligations-foundation-decisions-retrospective.md` — written last; includes the `## Spec delta — landed?` paragraph and the W001-6 closure note.

## Explicitly out of scope

- `CritterBids.Obligations` project, `AddObligationsModule()`, Marten config, `Program.cs` wiring — **S2**
- `PostSaleCoordinationSaga` and any saga handler — **S3 (happy path) / S4 (escalation + dispute)**
- `SettlementCompletedHandler` and the `obligations-settlement-events` RabbitMQ route — **S2**
- `bus.ScheduleAsync()` reminder chain — **S3**
- Any Relay project, hub implementation, or `relay-*` consumer — **S5–S7**
- `ProvideTracking`, `OpenDispute`, `ResolveDispute` commands or endpoints — **S3/S4**
- Any read-model projection (`ObligationStatusView`, etc.) — **S3/S4**
- Applying the OpenSpec `add-obligation-lifecycle` change (running `/opsx:apply`) — that begins in S2
- Editing OpenSpec-managed files under `.github/prompts/` or `.github/skills/`

## Conventions to pin or follow

- Saga hosting: ADR-022 is the first encoding; `docs/skills/wolverine-sagas.md` owns the how.
- `ObligationId` is UUID v5 from `ListingId` via an Obligations-specific namespace constant (`ObligationsIdentityNamespaces.PostSaleCoordination`) — confirmed in the delta spec; the namespace constant itself lands in S2 with the BC, not here.
- Contract records follow `docs/skills/integration-messaging.md` and the global `sealed record` / no-"Event"-suffix rules.
- `[AllowAnonymous]` posture holds through M6 — no `[Authorize]` introduced this slice.

## Spec delta

Per ADR 020, this slice's spec consequence is governed by the OpenSpec `add-obligation-lifecycle` change (`openspec/changes/add-obligation-lifecycle/` — proposal, design, delta spec, tasks; already authored and committed in the design phase). S1 lands the foundation portion: the four integration-event contracts and `ObligationsOptions` become real code, and **ADR-022 (Obligations saga hosting)** is authored as a new governing decision. The OpenSpec delta spec's requirements for *deterministic identity*, *configurable durations*, and the *integration-event vocabulary* gain their first code anchors; the saga-behavior requirements remain unimplemented until S2–S4. The retro's `## Spec delta — landed?` paragraph confirms ADR-022 exists, the four contracts compile, `ObligationsOptions` is bound, and W001-6 is closed.

## Acceptance criteria

- [ ] `docs/decisions/022-obligations-saga-hosting.md` exists, status `Accepted`, cites ADR-019; `docs/decisions/README.md` index + next-number pointer updated
- [ ] `ObligationsOptions` record exists with production + demo durations; demo-mode config approach recorded in the retro; W001-6 flipped to Resolved in `PARKED-QUESTIONS.md` with counts updated
- [ ] Four `CritterBids.Contracts.Obligations.*` records exist as `sealed record`, payloads exactly per milestone doc, no "Event" suffix, no "paddle"
- [ ] `BiddingHub` / `OperationsHub` group-naming convention captured in a durable doc location (milestone §6 or `wolverine-signalr.md`)
- [ ] `dotnet build` passes (0 errors); `dotnet test` baseline unchanged (no new tests this slice — contracts + ADR + config only)
- [ ] `docs/retrospectives/M6-S1-obligations-foundation-decisions-retrospective.md` written with `## Spec delta — landed?`
- [ ] No `CritterBids.Obligations` or `CritterBids.Relay` project created; no saga or handler code added
- [ ] No commit to `main`; no `Co-Authored-By` trailer

## Open questions

- Does `ObligationsOptions` live in `CritterBids.Contracts/Obligations/` (shared) or `CritterBids.Obligations` (BC-internal)? W005 Decision 4 implies BC-internal config, but the contract stubs slice is the natural home if any other BC needs the durations. **Flag and let @PO/@PSA decide; do not assume.**
- Should the `BiddingHub` / `OperationsHub` group-naming convention be a `wolverine-signalr.md` skill append or a milestone-doc §6 entry? Pick the location that matches where Relay's S5 prompt will look first.
- Is `Reason` on `DisputeOpened` a free-form string or an enum mirroring `DisputeResolved.ResolutionType`'s closed set? The delta spec lists dispute reasons (`NonDelivery`, `ItemCondition`, `MissedDeadline`) — confirm whether to encode them as an enum now or defer to S4.
