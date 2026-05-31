# M6 — Obligations BC + Relay BC — Milestone Retrospective

**Date:** 2026-05-30
**Milestone:** M6 — Obligations BC + Relay BC
**Sessions:** S1 → S7 (7 sessions; no mid-flight splits)
**Author:** Claude (PSA mode, explanatory output style)

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M6-obligations-relay-bc.md` §1:

| Exit criterion | Status |
|---|---|
| Solution builds clean with `dotnet build` — 0 errors, 0 warnings | ✅ 0 errors / 0 warnings at M6 close. The long-standing NU1904 Marten 8.35.0 advisory warnings (14–24 across earlier slices) were **cleared by the Wolverine 6 / Marten 9 / JasperFx 2 upgrade** that landed mid-milestone between S5 and S6 (`f5669cf`, PR #55) |
| Obligations BC implemented: `CritterBids.Obligations` + `CritterBids.Obligations.Tests`, `AddObligationsModule()`, Marten config per ADR 011 | ✅ S2 (`56c8613`) |
| Relay BC implemented: `CritterBids.Relay` + `CritterBids.Relay.Tests`, `AddRelayModule()`, Marten config per ADR 011 | ✅ S5 (`58407d0`) |
| Obligations saga hosting decision made in S1, recorded as an ADR | ✅ S1 — [ADR-022](../decisions/022-obligations-saga-hosting.md): Wolverine Saga, confirming the ADR-019 Settlement precedent |
| Demo-mode timeout config decided in S1 (W001-6 closed) | ✅ S1 — `ObligationsOptions` section with demo-mode durations; decision recorded in the M6-S1 retro (BC-internal, not ADR-worthy) |
| Obligations saga happy path green: `PostSaleCoordinationStarted` → reminder chain → `ObligationFulfilled`; cancellation paths green | ✅ S3 (`ee4cbe8`) — cancellable `bus.ScheduleAsync()` reminder chain; tracking provided cancels reminders |
| Obligations saga escalation path green: missed deadline → `DeadlineEscalated`; dispute sub-workflow (`DisputeOpened` → `DisputeResolved`) green | ✅ S4 (`076c6d3`) |
| `CritterBids.Contracts.Obligations.*` integration events authored: `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` | ✅ S1 stubs, emitted from the saga S3/S4 |
| At least one dispatch test per Obligations command exercising the Wolverine routing path | ✅ S3/S4 |
| Relay `BiddingHub` wired (participant push for the bid/listing/settlement event set) | ✅ S5 (core: `BidPlaced`, `ListingSold`, `SettlementCompleted`) + S6 (remaining) |
| Relay `OperationsHub` wired (staff feed) | ✅ S6 (`e839420`) |
| Relay notification history projection implemented (queryable by participant ID) | ✅ S6 — `NotificationHistoryView` (`relay` schema), handler-driven Marten upsert |
| All new RabbitMQ routes wired in `Program.cs`; `relay-settlement-events` `ListenTo` added | ✅ S5/S6; verified by the S7 seven-route audit |
| Relay + Obligations discovery added to `Program.cs` `IncludeAssembly(...)` | ✅ S2 / S5 |
| `AddObligationsModule()` and `AddRelayModule()` called in `Program.cs` | ✅ S2 / S5 |
| M6-S1 through final-session retrospective docs written | ✅ All seven slice retros land with their slices |
| M6 retrospective doc written | ✅ This document |

All exit criteria honored. No deferrals to post-M6 except the explicitly-scoped Operations BC consumers (M7).

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 (`8a32213`) | Foundation decisions: Obligations saga hosting ([ADR-022](../decisions/022-obligations-saga-hosting.md)); demo-mode timeout config (`ObligationsOptions`, W001-6 closed); four contract stubs; deterministic UUID v5 `ObligationId` from `ListingId`; Relay hub group-naming conventions | ✅ | Preceded by M6-prep ADRs 020 (spec-delta closure loop) + 021 (OpenSpec CLI for M6) in `5bb2dfd` |
| S2 (`56c8613`) | Obligations BC scaffold — project, `AddObligationsModule()`, Marten config, schema isolation, `SettlementCompletedHandler` saga-start, `obligations-settlement-events` route | ✅ | Precedent pattern from prior BCs applied cleanly |
| S3 (`ee4cbe8`) | Obligations saga happy path — `PostSaleCoordinationSaga`; cancellable `bus.ScheduleAsync()` reminder chain; `TrackingInfoProvided` cancels scheduled messages; `ObligationFulfilled`; `MarkCompleted()` | ✅ | First production cancellable-scheduled-message saga (Auctions uses `ScheduleAsync` for the close timer; Obligations uses it for the reminder chain with state-keyed cancellation) |
| S4 (`076c6d3`) | Escalation path (`DeadlineEscalated`); dispute sub-workflow (`DisputeOpened` → `DisputeResolved`); `MarkCompleted()` on resolution; `internal static class DisputeResolutions` consts shared by saga branch + projection | ✅ | Escalation modeled as a non-terminal loop-back transition (saga continues after escalating) — the asymmetry ADR-022 cites over Handlers |
| S5 (`58407d0`) | Relay BC scaffold — project, `AddRelayModule()`, `BiddingHub` + `OperationsHub`; core `relay-auctions-events` / `relay-settlement-events` consumers; [ADR-023](../decisions/023-relay-reactive-broadcast-architecture.md) (plain `Hub` + direct `IHubContext`) | ✅ | Transitive handler-leak discovery: `Relay.Tests` → `Api` → every BC, so Auctions' own `BidPlacedHandler` co-consumed `BidPlaced`; resolved with the `RelayBcDiscoveryExclusion` sibling-exclusion pattern |
| — (`f5669cf`, PR #55) | **Mid-milestone infrastructure upgrade:** Wolverine 5.39.3 → 6.2.2, Marten 9, JasperFx 2 | ✅ | Landed between S5 and S6; cleared the NU1904 Marten 8.35.0 advisory warnings carried since M3 (see §"Mid-Milestone Engine Upgrade") |
| S6 (`e839420`) | Relay remaining inbound consumers (`relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, `relay-listings-events`); full `OperationsHub` push set; `NotificationHistoryView` projection | ✅ | Full suite Docker-blocked this session (Testcontainers endpoint unavailable); the 31/31 hub subset passed, full green confirmed at S7 open. S6 prompt was authored in a companion worktree and backfilled to `main` in `df1ad60` (PR #59) |
| S7 (`5159eca`, PR #57) | End-to-end fan-out proof (one `SettlementCompleted` → Obligations saga start **and** Relay winner push as sibling consumers); `Program.cs` seven-route topology audit; M6-close test baseline | ✅ | `PostSaleFanOutTestFixture` composes Settlement-publish + Obligations + Relay in one real-Kestrel + Testcontainers host without weakening any per-BC exclusion fixture; route audit passed with no correction |

No mid-flight splits in M6 — every slice closed at its planned scope. The one off-script event was the engine upgrade, which was infrastructure, not a slice.

---

## Mid-Milestone Engine Upgrade

M6 is the first CritterBids milestone to absorb a **major framework-version bump mid-flight**. Between S5 and S6, `f5669cf` (PR #55) moved the solution from Wolverine 5.39.3 → 6.2.2, Marten 8.35.0 → 9, and JasperFx 1 → 2.

- **NU1904 warnings cleared.** Every slice through S5 reported 14–24 pre-existing NU1904 critical-vulnerability warnings against Marten 8.35.0, consistently flagged "out of scope — package remediation deferred." The Marten 9 bump retired the vulnerable transitive dependency; S6 and S7 build clean at **0 warnings**. The M5 retro's deferred "Marten advisory" debt item is closed by this upgrade, not by a separate remediation pass.
- **No BC code churn surfaced in the slice retros.** The upgrade landed as its own commit; S6 resumed Relay work against the upgraded engine without a recorded API-break scramble. Marten 9 / Wolverine 6 idiom differences (if any were hit) did not produce a slice-level finding — a signal that CritterBids' uniform-bootstrap discipline (one `AddMarten()`, per-BC `ConfigureMarten()`, `IntegrateWithWolverine()`) absorbed the bump cleanly.
- **Lesson for M7.** The engine baseline at M7 open is Wolverine 6 / Marten 9 / JasperFx 2. Any M7 skill-file code blocks still showing Marten 8 / Wolverine 5 signatures are now stale and should be checked against the upgraded engine when next touched.

---

## OpenSpec — First Lived Change

M6 is the **first milestone to use the OpenSpec CLI** per [ADR-021](../decisions/021-openspec-cli-for-m6.md) (per-BC, opt-in). The Obligations BC adopted; the `add-obligation-lifecycle` change ran the full propose → implement → archive loop and is archived at `openspec/changes/archive/2026-05-29-add-obligation-lifecycle/`, with the accumulated capability spec promoted to `openspec/specs/obligation-lifecycle/spec.md`.

- **Obligations adopted; Relay did not.** Relay (S5/S6) proceeded narrative-anchored only, per the ADR-021 per-BC evaluation gate. The formal Relay decline/defer record in `openspec/README.md`'s adoption ledger remains a housekeeping touch owed (see Deferred Items).
- **ADR-020 + ADR-021 interplay held.** The spec-delta closure loop (ADR-020) ran on every slice — prompt `## Spec delta`, retro `## Spec delta — landed?`, narrative `## Document History` row — alongside the OpenSpec change folder for the one adopting BC. The two did not duplicate: OpenSpec change folder authoritative for SHALL form, narrative authoritative for journey prose.

---

## Cross-BC Integration Map

All M6 cross-BC flows wired and verified through Testcontainers Postgres + (test-stubbed) RabbitMQ. The Obligations + Relay view at M6 close:

```
Obligations-INBOUND:
  Settlement (M5)  ──► SettlementCompleted   ──► Obligations (M6)  (PostSaleCoordination saga starts)   ✅ S2/S3
                       (queue: obligations-settlement-events)

Obligations-OUTBOUND:
  Obligations (M6) ──► TrackingInfoProvided  ──► Relay (M6)   (winner tracking push)                    ✅ S6
  Obligations (M6) ──► ObligationFulfilled   ──► Relay (M6)   (completion push; Operations consumes M7) ✅ S6
  Obligations (M6) ──► DisputeOpened         ──► Relay (M6)   (staff alert; Operations consumes M7)     ✅ S6
  Obligations (M6) ──► DisputeResolved       ──► Relay (M6)   (participant notify; Operations M7)       ✅ S6
                       (queue: relay-obligations-events)

Relay-INBOUND (pure consumer — all routes inbound-only):
  Participants (M1) ──► relay-participants-events  ──► OperationsHub                                     ✅ S6
  Selling (M2)      ──► relay-selling-events       ──► OperationsHub                                     ✅ S6
  Auctions (M3/M4)  ──► relay-auctions-events      ──► BiddingHub + OperationsHub                        ✅ S5/S6
  Settlement (M5)   ──► relay-settlement-events     ──► BiddingHub (SellerPayoutIssued + SettlementCompleted) ✅ S5/S6
  Obligations (M6)  ──► relay-obligations-events    ──► BiddingHub + OperationsHub                        ✅ S6
  Listings (M2)     ──► relay-listings-events       ──► OperationsHub (watch count)                       ✅ S6
```

The post-sale **fan-out** — one `SettlementCompleted` driving the Obligations saga start *and* the Relay winner push as two independent sibling consumers (not a chain) — is the structural claim the S7 `PostSaleFanOutTestFixture` proves end-to-end. `MultipleHandlerBehavior.Separated` + `MessageIdentity.IdAndDestination` (mirroring `Program.cs`) are the bits that make the two handlers run as independent destinations off one publish; without `MessageIdentity` the durable inbox would dedupe the fanned-out copies.

Seven new RabbitMQ routes wired in M6: one Obligations inbound (`obligations-settlement-events`), one Obligations outbound / Relay inbound (`relay-obligations-events`), and five Relay inbound (`relay-participants-events`, `relay-selling-events`, `relay-auctions-events`, `relay-listings-events`, plus the `ListenTo` on the pre-wired `relay-settlement-events`).

---

## Test Count at M6 Close

| Project | Count | Δ from pre-M6 | M6 contributions |
|---|---|---|---|
| `CritterBids.Api.Tests` (+ composed fixture) | 2 | +1 | S7: `PostSaleFanOutTestFixture` end-to-end fan-out test |
| `CritterBids.Auctions.Tests` | 65 | — | — (M6 added a `RelayBcDiscoveryExclusion` but no net new tests) |
| `CritterBids.Contracts.Tests` | 1 | — | — |
| `CritterBids.Listings.Tests` | 20 | — | — |
| `CritterBids.Participants.Tests` | 6 | — | — |
| `CritterBids.Selling.Tests` | 36 | — | — |
| `CritterBids.Settlement.Tests` | 25 | — | — |
| `CritterBids.Obligations.Tests` | 13 | +13 | S2 scaffold (4) + S3 happy path (2) + S4 escalation/dispute (7) |
| `CritterBids.Relay.Tests` | 35 | +35 | S5 scaffold + core hub push (6) + S6 remaining routes / OperationsHub / history (29) |
| **Total** | **203** | **+49** | |

Test arc across the milestone: 154 (pre-M6) → 154 (S1, docs/contracts only) → 158 (S2) → 160 (S3) → 167 (S4) → 173 (S5) → 202 (S6) → **203 (S7)**. The S6 full suite was Docker-blocked in-session (Testcontainers endpoint unavailable); the 202 count was confirmed at S7 open with Docker available (server 29.4.3), and S7 added the single composed fan-out test for 203.

---

## Key Decisions Made in M6

| Identifier | Decision |
|---|---|
| [ADR-022](../decisions/022-obligations-saga-hosting.md) | **Wolverine Saga** (`PostSaleCoordinationSaga : Saga`) for the post-sale coordination workflow — confirms the ADR-019 Settlement precedent. Chosen over Process Managers via Handlers because the workflow is state-driven with **loop-back transitions** (non-terminal escalation, late-tracking recovery, dispute `Extension` reschedule) and **state-keyed scheduled-message cancellation** is its defining pattern. `ObligationId` is deterministic UUID v5 from `ListingId`. Handlers remain the right host for Relay's broadcast pipeline (the contrast case ADR-019 named). Authored at S1; no revisit through implementation. |
| [ADR-023](../decisions/023-relay-reactive-broadcast-architecture.md) | **Plain `Hub` + direct `IHubContext`** for Relay's SignalR architecture. Hubs are plain `Hub` subclasses mapped via `app.MapHub<T>()`; Wolverine handlers inject `IHubContext<THub>` and push explicitly, returning `Task` only (preserving Relay-never-publishes). Rejects the `WolverineFx.SignalR` transport (`opts.UseSignalR()` + `ToSignalR()`) because it targets `IHubContext<WolverineHub>`, not the mapped application hubs, so it never reaches clients at `/hub/bidding`. Supersedes the `wolverine-signalr` skill's `IHubContext`-injection caution for CritterBids Relay. Revisit trigger: bidirectional hub messaging or multi-node transport fan-out. |
| [ADR-020](../decisions/020-spec-delta-closure-loop.md) | **Spec-delta closure loop** (authored in M6-prep, `5bb2dfd`). Four-step per-session cadence operationalizing ADR-016: prompt declares `## Spec delta`; session executes; retro confirms `## Spec delta — landed?`; spec records the amendment in `## Document History`. Ran on every M6 slice. |
| [ADR-021](../decisions/021-openspec-cli-for-m6.md) | **OpenSpec CLI adoption for M6, per-BC opt-in** (authored in M6-prep, `5bb2dfd`). Obligations adopted (first lived change, `add-obligation-lifecycle`, archived 2026-05-29); Relay evaluated independently and proceeded narrative-anchored. M1–M5 BCs not retroactively adopted (false-provenance avoidance). |
| M6-D1 | **`ObligationsOptions` demo-mode timeout config** (W001-6 closed at S1). Configurable durations let the demo run reminder/escalation chains in seconds while production defaults stay day-scale. BC-internal — recorded in the S1 retro, not ADR-worthy. The S7 fan-out test relies on the day-scale production defaults so the scheduled timers never fire mid-test (no real-clock wait). |
| M6-D2 | **`internal static class DisputeResolutions`** (`Refund` / `Extension` / `Closed`) shared by the saga branch logic and the projection's `Apply(DisputeResolved)` — one source of truth for the magic strings without coupling the string-valued wire contract to an enum type (the `ListingPassed.Reason` precedent). |
| M6-D3 | **Additive optional payload fields for bidder targeting** (`TrackingInfoProvided.WinnerId`, `DisputeResolved.ParticipantId`) with safe fallback when absent (S6). Additive-only per ADR-005; avoids a breaking contract revision to route pushes to the right participant group. |

---

## Key Learnings — Cross-Session Patterns

These generalize across milestones. Session-local findings live in individual session retros.

### 1. Sibling-consumer fan-out needs `MessageIdentity.IdAndDestination`, not just `Separated`

The S7 fan-out test proves one `SettlementCompleted` drives two independent destinations. `MultipleHandlerBehavior.Separated` alone is insufficient: the durable inbox dedupes by message id, so without `Durability.MessageIdentity = IdAndDestination` only the first separated handler runs and the other is silently swallowed as a duplicate. Any future multi-consumer fan-out test (or production host that needs two BCs to independently consume one event) must set both. This is the inverse of the per-BC `*BcDiscoveryExclusion` fixtures — a deliberately *composed* host — and the two patterns must not be conflated.

### 2. A test project that references `Api` transitively discovers every BC's handlers

S5's transitive handler leak (`Relay.Tests → Api → every BC`, so Auctions' `BidPlacedHandler` co-consumed `BidPlaced` and faulted before Relay's push fired) is the same class of problem the M5 `*BcDiscoveryExclusion` pattern solves, encountered from the SignalR side. The tell: a shared event type's push test times out while a uniquely-consumed event's test passes (only 2 of 3 failed in S5 because `SettlementCompleted` had no competing host consumer). The fix is per-sibling-BC discovery exclusion in the test fixture, never weakening production discovery.

### 3. SignalR push assertions need a real-Kestrel host; Alba's in-memory `TestServer` cannot drive a `HubConnection`

Relay's push tests (S5/S6) and the S7 composed fixture all require real Kestrel because SignalR's WebSocket transport cannot run under Alba's in-memory `TestServer`. The Obligations-saga half of the same fan-out test needs Marten/Testcontainers. The composed `PostSaleFanOutTestFixture` is the merge of both needs. Future real-time BCs (Operations `OperationsHub` consumers in M7) inherit this constraint — plan the fixture as real-Kestrel + Testcontainers from the start.

### 4. Cancellable scheduled messages are saga-state-keyed, not envelope-tracked at the call site

The Obligations reminder chain (S3) is CritterBids' canonical lived example of `bus.ScheduleAsync()` + state-driven cancellation: the saga persists what it needs to cancel on its own state and cancels on `TrackingInfoProvided`, rather than threading a raw envelope id through handler signatures. This is the pattern ADR-022 cites as the saga-shape justification. Auctions' close-timer use of `ScheduleAsync` is the simpler (non-cancellable-chain) precedent; Obligations is the cancellable-chain reference for any future BC needing reminder/escalation timers.

### 5. A major engine bump is cheap when bootstrap is uniform

The Wolverine 6 / Marten 9 / JasperFx 2 upgrade landed as a single mid-milestone commit and produced no slice-level API-break finding. The uniform bootstrap (one `AddMarten()`, per-BC `ConfigureMarten()`, `IntegrateWithWolverine()`, `AutoApplyTransactions()`) means version churn concentrates in one place rather than scattering across eight BCs. This is evidence for keeping the canonical bootstrap sequence inviolate — it is what makes engine upgrades a chore instead of a milestone.

---

## ADR Candidate Review

| Finding | ADR warranted? | Rationale |
|---|---|---|
| Obligations saga hosting (Saga vs Handlers) | **Yes — ADR-022 at S1** | Has alternatives and project-specific reasoning; authored before implementation per the foundation-decision discipline |
| Relay SignalR architecture (plain Hub vs Wolverine transport) | **Yes — ADR-023 at S5** | A real fork with a rejected option (`WolverineFx.SignalR`) and a documented "why not"; supersedes a skill caution |
| `ObligationsOptions` demo-mode timeout config | **No** | BC-internal options shape; lives in the S1 retro + `ObligationsOptions` docstring |
| Deterministic UUID v5 `ObligationId` from `ListingId` | **No** | Application of ADR-007's convention to a BC with a natural business key; lives in `ObligationsIdentityNamespaces` |
| `DisputeResolutions` shared-consts pattern | **No** | Implementation idiom (the `ListingPassed.Reason` precedent); skill-file rule sufficient |
| Additive optional bidder-target payload fields | **No** | Application of ADR-005 additive-only contract policy |
| Wolverine 6 / Marten 9 upgrade | **No** | Dependency bump, not an architectural choice; recorded in this retro + the package files |

**ADR-022 + ADR-023 land in M6; ADR-020 + ADR-021 landed in M6-prep.** No additional ADRs warranted at M6 close.

---

## What Was Used, What Wasn't (vs M6-S1 plan)

| Foundation decision (M6-S1) | M6 lived outcome |
|---|---|
| Wolverine Saga for Obligations (ADR-022) | Used. State-driven saga with escalation loop-back, cancellable reminder chain, dispute sub-workflow; zero revisits |
| Deterministic UUID v5 `ObligationId` from `ListingId` | Used unchanged; proven structurally in the S7 fan-out test |
| Four `CritterBids.Contracts.Obligations.*` events (S1 stubs) | Used; all four emitted from the saga and published on `relay-obligations-events`. Two gained additive optional bidder-target fields at S6 |
| `ObligationsOptions` demo-mode config (W001-6) | Used; production day-scale defaults double as the S7 test's no-real-clock-wait mechanism |
| Plain `Hub` + `IHubContext` for Relay (ADR-023) | Used; `WolverineFx.SignalR` reference retained as a future `WolverineHub`-transport door but not wired |
| `*BcDiscoveryExclusion` isolation in Relay/Obligations test fixtures | Used throughout; S7 deliberately built the *inverse* (a composed host) without weakening the exclusions |
| OpenSpec CLI for Obligations (ADR-021) | Used; first lived change archived. Relay evaluated and declined (narrative-anchored) — the formal ledger row is still owed |

---

## Technical Debt and Deferred Items

| Item | Deferred in | Target |
|---|---|---|
| `docs/skills/wolverine-signalr/SKILL.md` lived-Relay update (plain `Hub` + `IHubContext` per ADR-023) | Owed since S5, reaffirmed S6/S7 | Next skills-maintenance pass |
| Relay OpenSpec decline/defer row in `openspec/README.md` adoption ledger | S7 (surfaced, not edited unilaterally) | M7 open or skills/docs housekeeping |
| Operations BC consumers of `relay-obligations-events` / `operations-obligations-events` and `operations-settlement-events` `ListenTo` | M6 milestone non-goals | M7 (Operations BC) |
| `OperationsObligationsView` / operator read models (lot board, saga-state panel, dispute queue) | M6 milestone non-goals; archived `add-obligation-lifecycle` task 8.3 | M7 (Operations BC) |
| `OperationsHub` push targeting refinement (S6 standardized to `Clients.All`; no staff-group variant) | S6 open-question #4 | M7 when the staff dashboard read-models land |
| Real carrier-tracking webhook (the `ProvideTracking` seam is an in-process stub) | M6 non-goals | Post-MVP |
| Dispute compensation / settlement reversal on `DisputeResolved` (`ResolutionType` carried, no reversal logic) | M6 non-goals | Post-MVP |
| Email / SMS / push delivery (Relay delivers SignalR only; stubs log but do not call external services) | M6 non-goals | Post-MVP |
| Relay HTTP endpoints + React `@microsoft/signalr` client wiring | M6 non-goals | M8 (frontend) |
| `[Authorize]` posture resumes (M6 is the last `[AllowAnonymous]` milestone per `CLAUDE.md`) | M6 convention | M7 (auth milestone) |
| Marten event-type alias / upgrade pass for prior namespace promotions | Carried from M5 | First-real-deploy retrospective |

---

## What's Next — M7

M6 closes the **post-sale + real-time push** layer. Two BCs remain unbuilt against the MVP's eight-BC scope: **Operations** (the one BC never yet present in `src/`) and the frontend SPAs (M8). The recommended next milestone is **M7 — Operations BC**, for concrete reasons:

- **Relay already pushes to `OperationsHub`, but nothing reads the operator read-models.** M6 wired the staff feed; M7 gives it the projections (lot board, saga-state panel, settlement queue, dispute queue) that make the ops dashboard meaningful. The `relay-obligations-events` / `operations-settlement-events` / `operations-obligations-events` consumer seams are already named and waiting.
- **The `[Authorize]` posture is scheduled to resume at M7.** `CLAUDE.md` pins `[AllowAnonymous]` "through M6"; M7 is where real authentication planning begins. This is a milestone-level convention change to flag in M7's opening session, not a silent flip.
- **M7 should open with a foundation-decisions slice** per the established M{n}-S1 pattern: Operations storage confirmation (Marten per ADR-011), OpenSpec adoption evaluation for Operations (per ADR-021), and the auth-posture decision (likely ADR-worthy — the first endpoints to lose `[AllowAnonymous]`).
- **Engine baseline is current.** M7 starts on Wolverine 6 / Marten 9 / JasperFx 2 with a clean 0-warning build — no inherited dependency debt to clear first.

The frontend (M8) remains independent and can parallelize once Operations' read-models exist, but the ops dashboard's data depends on M7 shipping first.

---

## Key Numbers at M6 Close

- **Tests:** 203 passing (up from 154 pre-M6; +49 in M6 — Obligations 13, Relay 35, 1 composed fan-out)
- **Sessions:** 7 (S1 through S7; no mid-flight splits)
- **PRs / commits on `main` for M6 work:** S1 (#47), S2 (#48), S3 (#49), S4 (#51), S5 (#53), S6 (#56), S7 (#57), plus M6-prep ADRs (#46), the engine upgrade (#55), and closeout-doc sync (#59, #60)
- **New ADRs:** 4 (020 + 021 in M6-prep; 022 at S1; 023 at S5)
- **New RabbitMQ routes:** 7 (1 Obligations inbound, 1 Obligations-out/Relay-in, 5 Relay inbound incl. the `relay-settlement-events` `ListenTo`)
- **New integration events:** 4 (`TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved`) + several minimal Relay-consumed contract types authored at S6 (`ListingRevised`, `ListingEndedEarly`, `LotWatchAdded`, `LotWatchRemoved`, `BidRejected`)
- **New Marten document types:** 3 (`PostSaleCoordinationSaga`, `ObligationStatusView` in the `obligations` schema; `NotificationHistoryView` in the `relay` schema)
- **First-of-kind in CritterBids:** first cancellable-scheduled-message production saga; first SignalR real-time push layer; first lived OpenSpec change; first mid-milestone major engine upgrade
- **Build:** 0 errors, **0 warnings** (NU1904 Marten 8.35.0 advisory cleared by the Marten 9 upgrade)

---

## What M7 Should Know

**At M6 close the solution has 203 tests passing across 9 test projects, covering the full Participants → Selling → Auctions → Settlement → Obligations journey plus the Relay real-time push surface, verified end-to-end through real Postgres + (test-stubbed) RabbitMQ + real-Kestrel SignalR.** Seven production BCs are implemented: Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay. The post-sale fan-out (`SettlementCompleted` → {Obligations saga start, Relay winner push}) is proven in one composed host.

**For M7 (Operations BC):**
- Relay's `OperationsHub` already pushes the staff event set; M7 builds the read-models behind it, not the hub. `OperationsHub` targeting is currently `Clients.All` (S6) — refine to staff groups when the dashboard lands.
- The Operations consumer seams are pre-named: `operations-obligations-events`, `operations-settlement-events` (pre-wired publish-only since M5-S6, still no consumer). M7 adds the `ListenToRabbitQueue()` calls — no upstream BC code change required.
- `[AllowAnonymous]` is the last-milestone posture; M7 is where auth planning resumes. Treat the first `[Authorize]` endpoints as an ADR-worthy decision in M7-S1.
- Engine baseline is Wolverine 6 / Marten 9 / JasperFx 2, 0-warning build. Watch for stale Marten-8 / Wolverine-5 code blocks in skill files when implementing against them.
- Two housekeeping items owed from M6 close: the `wolverine-signalr` skill lived-Relay update and the Relay OpenSpec decline row in `openspec/README.md`. Neither blocks M7 implementation; fold them into M7-S1 or a docs pass.

The shipping pattern at M6 close — one (or two paired) BCs per milestone, retro per slice + retro per milestone, ADR per architectural decision, narrative + (per-BC) OpenSpec as joint authority, spec-delta closure loop on every slice — is the discipline carrying into M7.
