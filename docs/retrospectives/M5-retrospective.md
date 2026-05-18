# M5 — Settlement BC — Milestone Retrospective

**Date:** 2026-05-17
**Milestone:** M5 — Settlement BC
**Sessions:** S1–S6 (6 sessions; no mid-flight splits)
**Author:** Claude (PSA mode, explanatory output style)

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M5-settlement-bc.md` §1:

| Exit criterion | Status |
|---|---|
| Solution builds clean with `dotnet build` — 0 errors, 0 warnings | ✅ (0 errors; 24 pre-existing NU1904 Marten vulnerability warnings unchanged through M5) |
| Settlement BC implemented: `CritterBids.Settlement` + `CritterBids.Settlement.Tests`, `AddSettlementModule()`, Marten config per ADR 011 | ✅ S2 (`ccfbb0b`) |
| Settlement workflow hosting choice decided in S1 with rationale documented in ADR-019 | ✅ S1 (`056c3c7`) — Wolverine Saga chosen; Option A per ADR-019 |
| `PendingSettlement` projection — Marten document seeded from `ListingPublished`; lifecycle states managed per W003 Phase 1 Part 1 | ✅ S3 (`7a3bd32`) — tolerant-upsert + status-preservation; five integration-event lifecycle handlers |
| Settlement workflow happy-path implemented: §1-§6 decider scenarios green; §7 evolver scenarios green; §8 projection scenarios green; bidding-source §9 happy-path integration green | ✅ S4 (`d953469`) — §9.1 happy path; transitive coverage of §1-§8 via the integration test |
| `CritterBids.Contracts.Settlement.*` integration events authored — `SettlementCompleted`, `PaymentFailed`, `SellerPayoutIssued` | ✅ S1 (stubs) + S4/S5 (filled and emitted from the saga) |
| `[WriteAggregate]` (Saga) pattern applied with explicit `nameof` override | ✅ S4 — `[SagaIdentityFrom(nameof(CheckReserve.SettlementId))]` etc. on every saga handler |
| `BidderCreditView` projection implemented, updated on `WinnerCharged` | ✅ S5 (`9a361c5`) — also seeded from `ParticipantSessionStarted` (cross-BC contract promotion in same slice); idempotency via `LastChargedSettlementId`; lazy-init negative-credit sentinel |
| At least one dispatch test per Settlement command exercising the Wolverine routing path | ✅ S4 §9.1 integration test transitively exercises all five self-send commands; S5 adds explicit per-state-guard tests for §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 |
| ADR 007 Gate 4 honored | ✅ S6 — **closed by lived fact**; engine-default row IDs are the permanent posture per the three M5 event-row surfaces (`PendingSettlement`, financial event stream, `BidderCreditView`) shipping without surfaced incident |
| M5-S1 / M5-S2 / M5-S3 / M5-S4 / M5-S5 / M5-S6 retrospective docs written | ✅ All six slice retros land in the SHA on their respective commits |
| M5 retrospective doc written | ✅ This document |

All twelve exit criteria honored. No deferrals to post-M5.

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 (`056c3c7`) | Foundation decisions: workflow hosting (ADR-019), W003 amendments per narrative 002 findings F002/F004/F005, three contracts stubs (`SettlementCompleted`, `PaymentFailed`, `SellerPayoutIssued`), cutover-gate prompt with narrative 002 joint authority | ✅ | ADR-007 Gate 4 trigger fired here (re-deferred at M4-S1 with M5-S1 as the closing trigger); the gate's actual close lands at S6 |
| S2 (`ccfbb0b`) | Settlement BC scaffold — project, `AddSettlementModule()`, Marten config, schema isolation, empty saga shell | ✅ | None — precedent pattern from prior BCs applied cleanly |
| S3 (`7a3bd32`) | `PendingSettlement` projection — five integration-event handlers; tolerant-upsert; status-preservation guards (`if (existing.Status != Pending) return`) | ✅ | `marten-projections.md` "Handler-Driven Projections — Tolerant Upsert" amended with the M5-S3 example |
| S4 (`d953469`) | Settlement saga happy path (bidding source) — six-event financial stream; five self-send commands; `[SagaIdentityFrom]` discipline; deterministic UUID v5 `SettlementId`; §9.1 integration test | ✅ | `wolverine-sagas.md` "Multi-Phase Sagas with Self-Sent Continuation Commands" section authored; `SettlementsConcurrencyRetryPolicies` introduced for the `PendingSettlement`-not-found retry contract |
| S5 (`212e0a1`, `9a361c5`, `e28210a`) | Three workstreams: failure path (`FailSettlement` + `Handle(FailSettlement)` + §9.3 + six state-guard tests); BIN source (`StartSettlementSagaHandler` overload + §9.2 with absent-`ReserveCheckCompleted` audit signal); `BidderCreditView` projection (Participants contract promotion + lazy-init negative-credit sentinel + idempotency contract); 15 new tests | ✅ | Participants `ParticipantSessionStarted` promoted from internal record to `CritterBids.Contracts.Participants` cross-BC contract; `settlement-participants-events` queue added; `InternalsVisibleTo` pattern adopted for Settlement.Tests |
| S6 (`b61995a`, `7a68237`, this commit) | Three publish routes (`SettlementCompleted` → Listings; `SellerPayoutIssued` → Relay-stub; `PaymentFailed` → Operations-stub); `SettlementStatusHandler` (`"Sold"` → `"Settled"` transition); `CatalogListingView.SettledAt`; `ListingPublishedHandler` Q3 amendment (load-and-preserve); ADR-014 body authored (Path A formalized); ADR-007 Gate 4 closed by lived fact; M5 milestone retro | ✅ | Q1 / Q3 / Q-Add-1 / Q5 (Gate 4) all resolved to "Recommended" option; `tracked.Sent` vs `tracked.NoRoutes` framing rediscovery (Key Learning #1 below) |

No mid-flight splits in M5 — every slice closed at its planned scope. M3 saw two splits (S4→S4b, S5→S5b); M5's slices were sized closer to the one-session ceiling at planning time.

---

## Cross-BC Integration Map

All six M5 cross-BC integration flows wired and verified through Testcontainers Postgres + (test-stubbed) RabbitMQ. The full Settlement-side view at M5 close:

```
Settlement-INBOUND queues:
  Selling (M2)        ──► ListingPublished       ──► Settlement (M5)  (PendingSettlement seeded)        ✅ S3
  Selling (M2)        ──► ListingWithdrawn       ──► Settlement (M5)  (PendingSettlement → Expired)     ✅ S3
                          (queue: settlement-selling-events)

  Auctions (M3)       ──► ListingSold            ──► Settlement (M5)  (saga starts; bidding source)     ✅ S4
  Auctions (M3)       ──► BuyItNowPurchased      ──► Settlement (M5)  (saga starts; BIN source)         ✅ S5
  Auctions (M3)       ──► ListingPassed          ──► Settlement (M5)  (PendingSettlement → Expired)     ✅ S3
                          (queue: settlement-auctions-events)

  Participants (M1)   ──► ParticipantSessionStarted ──► Settlement (M5)  (BidderCreditView seeded)      ✅ S5
                          (queue: settlement-participants-events; contract promotion in same slice)

Settlement-OUTBOUND queues:
  Settlement (M5)     ──► SettlementCompleted    ──► Listings (M5)    (CatalogListingView.Status=Settled) ✅ S6
                          (queue: listings-settlement-events)

  Settlement (M5)     ──► SellerPayoutIssued     ──► (Relay BC post-M5)                                 ✅ S6 (route only)
                          (queue: relay-settlement-events; no consumer in M5)

  Settlement (M5)     ──► PaymentFailed          ──► (Operations BC post-M5)                            ✅ S6 (route only)
                          (queue: operations-settlement-events; no consumer in M5)
```

The full bidder-experience arc from M1 QR-scan through M5 settlement now runs end-to-end through real Postgres + (test-stubbed) RabbitMQ. The Settlement BC consumes events from **three** upstream BCs (Participants, Selling, Auctions) and publishes events to **three** downstream BCs (Listings live; Relay + Operations post-M5). Five new RabbitMQ queue routes wired in M5: three inbound (`settlement-selling-events`, `settlement-auctions-events`, `settlement-participants-events`), two outbound (`listings-settlement-events`, `relay-settlement-events`, `operations-settlement-events`).

---

## Test Count at M5 Close

| Project | Count | Δ from M4 paused | M5 contributions |
|---|---|---|---|
| `CritterBids.Api.Tests` | 1 | — | — |
| `CritterBids.Auctions.Tests` | 36 | — | — (M5 did not touch Auctions) |
| `CritterBids.Contracts.Tests` | 1 | — | — |
| `CritterBids.Listings.Tests` | 14 | +3 | S6: `SettlementStatusHandlerTests` (3) |
| `CritterBids.Participants.Tests` | 6 | — | S5: contract-promotion ripple (no net new tests; existing tests amended) |
| `CritterBids.Selling.Tests` | 32 | — | — |
| `CritterBids.Settlement.Tests` | 25 | +25 | S2 (1 module test) + S3 (6 projection tests) + S4 (2 saga tests) + S5 (15 tests across three workstreams) + S6 (1 publish-route test) |
| **Total** | **115** | **+29** | |

M5 added 29 tests across two BC test projects (Settlement and Listings). The Settlement count breaks down as: 1 module-scaffold smoke test (S2) + 6 `PendingSettlementHandler` tests (S3) + 2 saga integration tests (S4: §9.1 + §9.4) + 8 saga-failure-path tests (S5: §9.3 end-to-end + 6 state-guard + 1 lifecycle) + 2 BIN-source tests (S5: §9.2 + deterministic-id) + 5 `BidderCreditView` tests (S5) + 1 publish-route test (S6) = 25.

The §9.1 / §9.2 / §9.3 integration tests transitively cover Sections 1-8's happy + failure + BIN paths per the M5-S4 retro's transitive-coverage argument. No pure-function decider helpers were extracted in M5 implementation (ADR-019 Option C remained out of scope — no concrete friction signal during the saga's lived implementation).

---

## Key Decisions Made in M5

| Identifier | Decision |
|---|---|
| [ADR-019](../decisions/019-settlement-workflow-hosting.md) | **Wolverine Saga (Option A)** for the Settlement workflow. Phased-state-fit asymmetry: Settlement's seven phases share evolving state (HammerPrice, FeePercentage, FeeAmount materializing mid-workflow) which is exactly what the Saga primitive hosts. Process Managers via Handlers (Option B) remain right for future event-reactive BCs without phased state (Relay broadcast pipeline post-M5 is the canonical fit). Decider design lens (Option C) preserved at workshop level; helper extraction deferred unless implementation surfaces friction. Decided at S1 (`056c3c7`); no revisit through M5 implementation. |
| [ADR-014](../decisions/014-cross-bc-read-model-extension-shape.md) | **Path A — one view per logical entity, sibling handler classes per source BC, additive fields.** Authored at M5-S6 alongside the second lived application (`SettlementStatusHandler` extending `CatalogListingView` with `Status = "Settled"` + `SettledAt`). M3-S6's `AuctionStatusHandler` is the first lived example. Multi-source-sibling sub-question (Option A: one sibling per source BC; Option B: one sibling per logical feature) deferred to a third lived application — most likely M4-S6's `SessionMembershipHandler` if and when M4 resumes. Five binding constraints on future contributing BCs: one handler per source, tolerant-upsert, disjoint field sets, status-preservation guards, seed-handler load-and-preserve discipline. |
| [ADR-007 Gate 4](../decisions/007-uuid-strategy.md) | **Closed by lived fact at M5-S6.** Settlement shipped three event-row surfaces (`PendingSettlement` projection, financial event stream, `BidderCreditView` projection) under Marten's engine-default row IDs through M5-S3/S4/S5 without surfacing any row-ID-related friction. Engine default is the **permanent posture** across all CritterBids Marten BCs. Future row-ID strategy questions become separate ADRs if a production incident motivates them. Gate 4 stops being open; no further deferral. |
| M5-D1 | **Settlement is financially authoritative; Auctions' `ReserveMet` is UX-grade.** Already resolved in W001/W002/W003; reaffirmed in M5 implementation through the saga's `Handle(CheckReserve)` doing the authoritative reserve comparison against `PendingSettlement.ReservePrice` (not trusting any upstream signal). |
| M5-D2 | **`ParticipantSessionStarted` promoted from Participants-internal record to cross-BC contract at M5-S5.** Pre-step for `BidderCreditView`'s seed path. Replace-the-internal-record path (a) chosen over parallel-types (b) or type-forward (c) per S5 retro's grep evidence (no internal-only consumers depended on the access modifier). Marten event-type identity changed (namespace-prefix-sensitive); pre-production migration is invisible because tests clean Marten before each run; flagged for the first-real-deploy retrospective. |
| M5-D3 | **`PaymentFailed` publish route wired at M5-S6 despite no Operations BC consumer.** Flips the M5-S6 v0.1 prompt's "deferred to post-M5" stance based on the M5-S5 retro's queue-topology-completeness recommendation. The route is wired structurally; when Operations BC ships, its consumer drains the queue without requiring any Settlement-side change. Recorded as M5-S5 retro item #1 → M5-S6 commit 2. |
| M5-D4 | **`BidderCreditView` lazy-init negative-credit sentinel** as the no-prior-row signal. W003 Phase 1 Part 7's schema is `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)`; the lazy-init path on `WinnerCharged` writes `RemainingCredit = -Amount` when no prior `ParticipantSessionStarted` seed exists. The Relay broadcast handler (post-M5) reads `RemainingCredit` verbatim; a negative value renders as "balance unknown" or "credit deficit" without consumer-side branching. MVP simplification of the W003 design; not a design pivot. |

---

## Key Learnings — Cross-Session Patterns

These are generalizable across milestones. Session-local findings live in individual session retros.

### 1. `tracked.Sent` vs `tracked.NoRoutes` is a fixture-stance question, not just an assertion-target choice

The M3-S5b retro's Key Learning surfaced the basic pattern: cascaded messages with no production routing rule land in `NoRoutes`, not `Sent`. M5-S6 reproduced the same surprise from a different angle: `tracked.Sent` is **also** unreachable for external-routed messages when the fixture calls `DisableAllExternalWolverineTransports()`, because that call strips the external route entirely (doesn't stub it). `IncludeExternalTransports()` on the tracker doesn't help — there's no external transport to include.

Two codebase patterns work around this:
- **`tracked.NoRoutes` assertion** (M3-S5b Auctions + M5-S6 Settlement). Asserts the saga's emission contract; production route wiring asserted by code review.
- **Stub local-queue route via `IWolverineExtension`** (M1-S6 Participants — `SellingBcDiscoveryExclusion.Configure` adds a stub `ToLocalQueue` route alongside its handler-discovery exclusion). Asserts via Sent; production wiring still asserted by code review.

Neither pattern proves the production publish route end-to-end. End-to-end proof would require a real-RabbitMQ Testcontainers test — heavy, slow, not the codebase's lived idiom. The takeaway for future skill amendments: `critter-stack-testing-patterns.md` Problem 4 could be extended with the `IncludeExternalTransports`-vs-`DisableAllExternalWolverineTransports` interaction. Defer to skills-maintenance pass.

### 2. Cross-BC contract promotion changes Marten event-type identity (namespace-prefix-sensitive)

M5-S5's `ParticipantSessionStarted` promotion from `CritterBids.Participants.Features.StartParticipantSession.ParticipantSessionStarted` to `CritterBids.Contracts.Participants.ParticipantSessionStarted` is a single-line using-statement swap in source files but a **wire-format change** in Marten's event-type serialization. The six Participants tests passed after the promotion because tests clean Marten before each run. In production, this kind of contract promotion would need either `opts.Events.MapEventType<T>("<old-name>")` aliasing or an event-version upgrade pass.

M5 ships pre-production, so the migration is cosmetic — but the lesson is real. Any future contract promotion (M6+ frontend, post-MVP Relay BC) needs the alias or upgrade path planned at promotion time, not discovered at deploy time. Recorded for the first-real-deploy retrospective.

### 3. Multi-phase saga shape generalizes cleanly across source overloads

M5-S5's BIN-source `StartSettlementSagaHandler.Handle(BuyItNowPurchased)` was a 50-line implementation that copied the M5-S4 bidding overload's structure verbatim and changed exactly three places: initial `Status` and `ReserveWasMet`, `SettlementInitiated`'s `Source`/`Price`, and the first self-send (`ChargeWinner` not `CheckReserve`). The `PendingSettlement` load, deterministic-id derivation, idempotency check, and `OutgoingMessages` return shape were unchanged.

This is a strong signal that the M5-S4 start-handler shape generalized cleanly. Future source overloads (post-MVP payment-processor failure replay; offline ops-staff manual settlement initiation per W003 Phase 4 PARKED P-001) can follow the same pattern. The saga's `Handle(FailSettlement)` extension at S5 reinforced the point — adding the seventh phase to the saga was a mechanical extension of the established shape.

### 4. The status-preservation discipline cascades across handlers

M5-S3 introduced the pattern in `PendingSettlementHandler`: `if (existing.Status != PendingSettlementStatus.Pending) return`. M5-S5's `BidderCreditViewHandler` adopted it (`if (existing is { LastChargedSettlementId: not null }) return` for re-seeded rows). M5-S6's `SettlementStatusHandler` adopted it (`if (existing.Status != "Sold") return`). And M5-S6's amendment to the M2-S7 `ListingPublishedHandler` brought the discipline to the seed handler too (load + preserve any downstream-handler state on re-delivery).

Now codified in ADR-014's Decision §5 as one of the five binding constraints on Path A sibling handlers. The pattern is mechanical at this point — adding a new sibling handler is a matter of choosing the right pre-state status to gate the transition.

### 5. Direct-invocation tests for state-guard scenarios are 10× faster than Wolverine-harness tests

M5-S5's six `Should.Throw<InvalidSettlementTransitionException>` tests in `SettlementSagaFailurePathsTests.cs` run in single-digit milliseconds combined. The Wolverine-harness alternative (dispatch a self-send command into the inbox, wait for the handler to fire, catch the resulting message-bus exception) would add ~50-100ms per test for the saga lookup and dispatch overhead. For pure state-guard assertions, direct invocation is the right tool — and the saga's state-guard logic is unit-testable without DI, a property worth preserving when extracting pure-function decider helpers per ADR-019 Option C (if that materializes post-M5).

---

## ADR Candidate Review

Each M5 discovery was reviewed for ADR candidacy at M5-S6 close:

| Finding | ADR warranted? | Rationale |
|---|---|---|
| Cross-BC read-model extension shape (M3-D2 Path A) | **Yes — authored at M5-S6 as ADR-014** | Second lived application at M5-S6 met the M3-S7 deferral criterion. Body authored alongside the lived application |
| ADR-007 Gate 4 row-ID strategy | **Yes — closed at M5-S6 by lived-fact amendment to existing ADR** | Three M5 event-row surfaces shipped under engine default without incident. Closure recorded as one further amendment to ADR-007, not a new ADR |
| Settlement workflow hosting (Saga vs Handlers) | **Yes — authored at M5-S1 as ADR-019** | The choice has alternatives, trade-offs, and project-specific reasoning. Authored before implementation, not after, per the foundation-decision discipline |
| `BidderCreditView` lazy-init negative-credit sentinel | **No** | MVP simplification of a workshop-documented design. Not an architectural choice across BCs; lives in the `BidderCreditView` docstring + W003 Phase 1 Part 7 |
| Deterministic UUID v5 `SettlementId` derivation | **No** | Application of ADR-007's stream-ID convention to a BC with a natural business key (`ListingId`). Lives in `SettlementsIdentityNamespaces` + `wolverine-sagas.md` |
| Multi-phase saga with self-sent continuation commands | **No** | Wolverine convention. Skill-file rule sufficient (`wolverine-sagas.md` "Multi-Phase Sagas" section) |
| `tracked.Sent` vs `tracked.NoRoutes` second-encounter (M5-S6) | **No** | Reinforces the M3-S5b skill rule; skill-amendment pass when the next maintenance window arrives |
| Cross-BC contract promotion changing Marten event-type identity | **No** | Operational concern, not an architectural choice. Recorded for the first-real-deploy retrospective |

**ADR-014 + ADR-019 land in M5; ADR-007 amended by lived fact at M5 close.** No additional ADRs warranted at M5-S6 close. The discipline of "ADRs record project-specific architectural choices with alternatives; skill files record implementation patterns regardless of origin" was applied consistently across the M5 candidate review.

---

## What Was Used, What Wasn't (vs M5-S1 plan)

| Foundation decision (M5-S1) | M5 lived outcome |
|---|---|
| Wolverine Saga over Process Managers (ADR-019 Option A) | Used. Seven-phase saga; six commands; one terminal failure handler; zero ADR-019 revisits |
| Decider helper extraction (ADR-019 Option C) — conditional on M5-S4 friction | **Not used.** No concrete friction surfaced. Test surface used direct integration tests (§9.1/§9.2/§9.3) plus per-state-guard unit tests — sufficient without helpers |
| `BidderCreditView` projection initialized on `ParticipantSessionStarted` (W003 design) | Used with MVP simplification — lazy-init negative-credit sentinel on `WinnerCharged` covers the no-prior-row case |
| Three integration events in `CritterBids.Contracts.Settlement.*` (S1 stubs) | Used unchanged through M5; all three emitted from saga + all three published (S6); two consumers in-M5 (Listings via `SettlementCompleted`, internal `PendingSettlementHandler` via `PaymentFailed`/`SettlementCompleted`), three post-M5 (Relay, Operations, future seller-balance) |
| `[WriteAggregate]` pattern (or saga equivalent) on every Settlement aggregate command | Used as `[SagaIdentityFrom(nameof(...))]` on every continuation handler — the Saga-shape equivalent of the M2.5/M3 `[WriteAggregate]` convention |

---

## Technical Debt and Deferred Items

| Item | Deferred in | Target |
|---|---|---|
| Real payment-processor integration (Stripe / Braintree / banking call) | M5 plan / W003 § "Winner Charge" | Post-MVP (no concrete target slice) |
| Compensation paths beyond MVP credit-ledger | M5 plan / W003 Phase 1 Part 3 | Post-MVP |
| Relay BC `SettlementCompleted` + `SellerPayoutIssued` broadcast handlers | M5 plan / Relay BC scope | Post-M5 (when Relay BC is scheduled) |
| Operations BC `PaymentFailed` dashboard surfacing | M5 plan / Operations BC scope | Post-M5 (when Operations BC is scheduled) |
| `BidderCreditView` cleanup on session expiry | W003 Phase 1 Part 7 final paragraph | Post-MVP (per Participants' session-expiry convention) |
| Future bidder-balance HTTP endpoint | W003 Phase 1 Part 7 | Post-MVP (`BidderCreditView` is shaped to support it) |
| Marten event-type alias / upgrade pass for `ParticipantSessionStarted` namespace migration | M5-S5 Key Learning | First-real-deploy retrospective |
| Duplicate-`ListingSold`-after-Failed contract specification | M5-S5 retro item #3 | Post-M5 — surfaces only if production sees the pattern; no concrete trigger |
| `critter-stack-testing-patterns.md` Problem 4 amendment (`IncludeExternalTransports` vs `DisableAllExternalWolverineTransports`) | M5-S6 retro Key Learning #4 | Next skills-maintenance pass |
| `ProcessManager<TState>` framework primitive adoption | ADR-019 § Out of scope | Out of scope; separate ADR if JasperFx ships the primitive and CritterBids' direction shifts |

---

## Cumulative Findings Ledger at M5 Close

Narrative 002 (`docs/narratives/002-winner-clears-settlement.md`) — five findings tracked across M5:

| Finding | Status at M5 close | Resolution slice |
|---|---|---|
| F001 | ✅ | M5-S1 (PR #25) |
| F002 (Price/HammerPrice rename) | ✅ | M5-S1 — W003 amendment in `056c3c7` |
| F003 (minimum-scope) | ✅ | M5-S1 (PR #25) |
| F004 (SettlementInitiated payload mismatch) | ✅ | M5-S1 — W003 amendment in `056c3c7` |
| F005 (BidderCreditView projection name + shape) | ✅ | M5-S1 — W003 amendment in `056c3c7`; lived implementation in M5-S5 with the lazy-init MVP simplification recorded as `document-as-intentional` |

No new findings against narrative 002 during M5 implementation. Cumulative cross-BC drift: none recorded during M5 — every cross-BC contract change (the `ParticipantSessionStarted` promotion) carried its own retro disposition; no implicit drift detected during the M5-S6 close review.

---

## What's Next

Two routes after M5 close:

### Route 1 — Return to M4 to complete the Auctions BC

The Auctions BC paused after M4-S2. Remaining slices in the original M4 plan: Proxy Bid Manager saga (M4-S3), Session aggregate (M4-S4/S5), `SessionStarted → BiddingOpened` fan-out handler (M4-S5), and the `SessionMembershipHandler` sibling that was originally framed as the second ADR-014 application (M4-S6). With ADR-014 now Accepted, M4-S6 (when it ships) is bound by the already-accepted ADR; its scope is the third lived application of Path A and the natural moment to resolve ADR-014's deferred multi-source-sibling sub-question.

### Route 2 — Open M6 (frontend MVP)

The M6 milestone doc has not been authored. M6 would consume the catalog endpoint, the bid submission endpoint (Auctions BC at M3-M4), and (post-M6) the SignalR broadcast surface (Relay BC). The backend is sufficiently complete for catalog browsing + bidding scenarios; flash-format auctions require the M4 Session aggregate (not shipped).

### Recommended order

**Finish M4 before M6.** Concrete rationale:

- The flash-format scenarios from narrative 002 require the Session aggregate (M4-S4/S5); the bidder-experience arc M5 lived against assumed Sessions in narrative 002 but did not implement them
- ADR-014's multi-source sub-question deferral targets M4-S6 as the natural resolution slice; resolving it before opening M6 means the frontend doesn't have to absorb a mid-flight ADR amendment
- M6 frontend work is independent of M4's saga + aggregate work — it can be parallelized after M4 ships, but starting M6 first invites the M4-S6 ADR-014 resolution to land mid-frontend

The recommendation carries from the M5-S4 retro forward and is reaffirmed here.

---

## Key Numbers at M5 Close

- **Tests:** 115 passing (up from 86 at M3 close; +29 in M5)
- **Sessions:** 6 (S1 through S6; no mid-flight splits)
- **Commits:** 11 on `main` for the M5 work (+ 4 supporting commits for prompts/auth/test-SDK bumps)
- **PRs merged:** 4 (PR #25 M5-S1, PR #26 M5-S2, PR #27 M5-S3, PR #28 M5-S4); M5-S5 and M5-S6 landed directly on `main` after PR-style review
- **New ADRs authored in M5:** 2 (ADR-019 at S1, ADR-014 at S6); 1 amended (ADR-007 Gate 4 closed at S6)
- **New RabbitMQ queues:** 5 (`settlement-selling-events`, `settlement-auctions-events`, `settlement-participants-events`, `listings-settlement-events`, `relay-settlement-events`, `operations-settlement-events`)
- **New event types:** 6 internal (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`, `FinancialEventStream`, internal command types) + 3 integration (`SettlementCompleted`, `SellerPayoutIssued`, `PaymentFailed`) + 1 promoted (`ParticipantSessionStarted` from Participants-internal to cross-BC contract)
- **New Marten document types:** 4 in the `settlement` schema (`SettlementSaga`, `PendingSettlement`, `FinancialEventStream`, `BidderCreditView`)
- **New skill amendments:** 2 (`wolverine-sagas.md` "Multi-Phase Sagas" section at S4; `marten-projections.md §"View Extension Across Milestones"` second-example callout at S6)

---

## What M4 (and M6) Should Know

**At M5 close the solution has 115 tests passing across 7 test projects, covering the full Participants → Selling → Auctions → Settlement journey end-to-end through real Postgres + (test-stubbed) RabbitMQ.** Five production BCs are implemented end-to-end: Participants, Selling, Auctions, Listings, and Settlement. Six cross-BC integration flows are live and verified through Testcontainers. The bidder-experience arc from M1 QR-scan through M5 settlement runs end-to-end; the seller-experience arc is partial (listing publication + settlement notification yes; flash auction Sessions no).

**For M4 (when it resumes):** ADR-014 is now Accepted; M4-S6's `SessionMembershipHandler` is bound by Path A's five binding constraints (one handler per source, tolerant-upsert, disjoint fields, status-preservation guards, seed-handler load-and-preserve). The multi-source-sibling sub-question is M4-S6's to resolve with its own lived evidence. The `ListingPublishedHandler` Q3 amendment at M5-S6 means the Listings catalog's seed handler now load-and-preserves; any M4 amendments to that handler must continue the discipline. The Auctions BC's M4-paused work (Proxy Bid Manager, Session aggregate) has not been touched by M5 — the M3-S5b lived ground is the foundation; the M5 retro carries no Auctions-side amendments.

**For M6 (when it opens):** The bid submission HTTP endpoint and the catalog endpoint exist; the SignalR broadcast surface does not (Relay BC is post-M5; the M6 frontend can stub its way around the absence). The `BidderCreditView` projection is queryable but has no cross-BC consumer yet — its `RemainingCredit` field is intended for the Relay broadcast composing the `SettlementCompleted` push payload; M6 can render it from a future bidder-balance endpoint or wait for Relay. The most significant load-bearing M6 dependency is the Sessions aggregate (M4-S4/S5) for flash-format auctions; consider finishing M4 before opening M6, per the recommendation above.

The Settlement BC is the last Marten BC in CritterBids' M5 scope. Future BCs (Relay, Obligations, Operations) ship post-M5 with their own milestone docs. The shipping pattern at M5 close — one BC per milestone, retro per slice + retro per milestone, ADR per architectural decision, narrative as joint authority per the cutover gate — is the discipline carrying into post-M5 work.
