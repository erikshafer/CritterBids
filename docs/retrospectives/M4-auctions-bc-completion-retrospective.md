# M4 — Auctions BC Completion — Milestone Retrospective

**Date:** 2026-05-21
**Milestone:** M4 — Auctions BC Completion
**Sessions:** S1 → S7 (7 sessions, zero splits — S4b, S5b, S6b pre-drafted slots all unused)
**Author:** Claude (PSA mode, explanatory output style)

---

## Baseline vs. Exit State

### Test-count arc

| Checkpoint | Total | Δ | Note |
|---|---|---|---|
| M3 close | 86 | — | 1 Api + 1 Contracts + 6 Participants + 11 Listings + 32 Selling + 35 Auctions |
| M4-S1 close | 86 | 0 | Docs only |
| M5 close (interleaved) | 115 | +29 | Settlement BC shipped during the M4 pause — +25 Settlement + 1 Auctions + 3 Listings |
| M4-S2 close | 120 | +5 | Selling `WithdrawListing` (+4) + Auctions cross-BC integration test (+1) |
| M4-S3 close | 125 | +5 | Proxy Bid Manager skeleton (+4 saga + 1 dispatch) |
| M4-S4 close | 134 | +9 | Proxy terminal paths + bidding war + `ParticipantCreditCeiling` projection |
| M4-S5 close | 148 | +14 | Session aggregate + fan-out + `PublishedListings` projection |
| M4-S6 close | 154 | +6 | Listings catalog session/withdrawn + cross-BC composition test |
| **M4-S7 close (this slice)** | **154** | **0** | Docs only |

### Build state at M4 close

- `dotnet build` — **0 errors**, 24 NU1904 NuGet vulnerability warnings (Marten 8.35.0 advisory, unchanged across M3 / M4 / M5)
- `dotnet test` — **154 passing**, 0 failing, 0 skipped
- 7 BCs implemented with PostgreSQL stores per ADR-011 (Participants, Selling, Auctions, Listings, Settlement, plus Api + Contracts assemblies)
- 9 application-level RabbitMQ queues live, 7 consumed (Operations + Relay queues are forward-spec with 0 consumers — see §"Operational Posture")

### End-to-end demo path at M4 close

The Flash demo journey runs end-to-end against real Postgres + RabbitMQ in tests, and `dotnet run --project src/CritterBids.AppHost` brings it up locally with all bindings live. Operationally:

- A Flash `Session` is created, listings attached, then `StartSession` cascades through `SessionStarted → BiddingOpened` fan-out to every attached listing simultaneously.
- A bidder registers a `RegisterProxyBid` with `MaxAmount` capped at the registered `BidderCreditCeiling`; a competing `BidPlaced` triggers the proxy's auto-bid via the M4-S3 dispatcher; the workshop-corrected three-way `Math.Min(amount + increment, MaxAmount, BidderCreditCeiling)` formula determines either continuation or `ProxyBidExhausted`.
- A two-proxy bidding war cascades within a single `SendMessageAndWaitAsync` cycle (eager, single-cycle per the M4-S4 finding).
- A seller `WithdrawListing` produces a real `ListingWithdrawn` integration event that terminates the Auction Closing saga and the Proxy Bid Manager saga, and the M4-S6 `Withdrawn`-preservation guard on `AuctionStatusHandler.Handle(BiddingOpened)` protects the catalog terminal state against a stale `BiddingOpened` arrival from a post-attach withdrawal.

Nothing in this set was demonstrable at M3 close.

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M4-auctions-bc-completion.md` §1:

| Exit criterion | Status |
|---|---|
| Solution builds clean with `dotnet build` — 0 errors, 0 warnings | ⚠️ See reconciliation below |
| Proxy Bid Manager saga implemented: Marten-document-backed `Saga` subclass with composite UUID v5 correlation on `ListingId + BidderId`; all 11 `002-scenarios.md` §4 scenarios green | ✅ S3 (5 scenarios) + S4 (6 scenarios) |
| Session aggregate implemented: `Session` event-sourced aggregate on Auctions, `CreateSession` / `AttachListingToSession` / `StartSession` command handlers; all 7 `002-scenarios.md` §5 scenarios green | ✅ S5 |
| `SessionStarted → BiddingOpened` fan-out handler implemented per Workshop 002 Phase 1 Option B — one `BiddingOpened` produced per attached listing | ✅ S5 (`SessionStartedHandler` appends to per-listing streams; `UseFastEventForwarding` fans out) |
| `CritterBids.Contracts.Auctions.*` extended — `RegisterProxyBid` (command), `ProxyBidRegistered`, `ProxyBidExhausted`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted` | ✅ S1 stubs with full L2 payload; consumed across S3 / S4 / S5 / S6 |
| `CritterBids.Contracts.Selling.ListingWithdrawn` authored; Selling BC produces it through a new `WithdrawListing` command + `SellerListing.Apply(ListingWithdrawn)` handler; 4 scenarios green | ✅ S1 contract extension + S2 producer |
| Auction Closing saga's `Handle(ListingWithdrawn)` exercised against the real Selling producer (integration test); the M3 test-fixture synthesis reduced to a unit-test shortcut | ✅ S2 (`RealSellingProducerSagaTerminationTests`); M3 fixture helper kept per M4-D6 |
| `[WriteAggregate]` with explicit `nameof` override on every M4 aggregate command (Session) and Selling command (`WithdrawListing`) from first commit | ✅ S2 (`WithdrawListing`) + S5 (`AttachListingToSession`, `StartSession`); `CreateSession` uses `MartenOps.StartStream<Session>` per the create-shape precedent |
| Listings BC catalog extended: `CatalogListingView` gains Session-membership fields and a `Withdrawn` status transition; implemented as a new sibling handler class per M3-D2 Path A (second application of the pattern) | ✅ S6 — three classes shipped per ADR-014 Sub-Option A (one sibling per source BC), strengthening the M3 precedent |
| ADR 014 — Cross-BC read-model extension shape — authored, documenting the pattern with the two lived applications as evidence | ✅ ADR 014 authored at M5-S6 (out of M4-S6 by date but in the spirit of M4-D3); amended at M4-S6 with the third lived application + Sub-Option A resolution + named-field-allow-list discipline |
| At least one dispatch test per new command (`RegisterProxyBid`, `CreateSession`, `AttachListingToSession`, `StartSession`, `WithdrawListing`) | ✅ S2 / S3 / S5 — five new dispatch tests |
| ADR 007 Gate 4 re-evaluated — closed with JasperFx input, or re-deferred with a new dated rationale that names the specific blocker | ✅ S1 — re-deferred with new trigger (M5-S1) and named owner (Erik); M5-S1 then re-deferred again with a further trigger per its retro |
| Aspire RabbitMQ management UI port exposed (per M3-S7 smoke-test observation); low-priority infrastructure fix bundled into S1 | ✅ S1 (`.WithManagementPlugin()` on the `AddRabbitMQ` builder); verified at this slice's smoke test (HTTP 200 at the Aspire-assigned host port) |
| `docs/skills/wolverine-sagas.md` updated retrospectively with the first in-repo composite-key saga example | ✅ S3 (`67b2252`) + S4 (`01d5c12`) — three new sections folded inline across two slices |
| `docs/skills/marten-projections.md` §7 reinforced with the second Path A application as a concrete example (from S6) | ✅ S6 (`7aebe37`) — §"View Extension Across Milestones" extended + new §"Status-Preservation Guards" subsection authored |
| M4 retrospective doc written | ✅ This document |

### Build-state reconciliation — "0 warnings" vs lived "24 NU1904 warnings"

The milestone doc's "0 errors, 0 warnings" criterion was authored at M4 plan-time when no NU1904 advisories existed. Marten 8.35.0's advisory (`GHSA-vmw2-qwm8-x84c`) landed before M4-S2 opened; the 24 warnings are NuGet vulnerability advisories surfaced on every `dotnet build`, not compiler warnings, and they have been **byte-stable across M3 / M4 / M5** — no M4 work introduced any new warnings. The criterion is satisfied in the compiler sense (no C# diagnostics) and is owed a one-off project-wide disposition (suppress / upgrade / accept-and-document) that does not belong to any single M4 slice. Flagged in §"Technical Debt and Deferred Items" with M5/M6 as the candidate slot.

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 | ADR-007 Gate 4 re-defer with named owner; resolve M4-D1 (proxy composite-key string form) + M4-D2 (Session UUID v7) + M4-D4 (Auctions-side duplicate `PublishedListings` projection); author 6 Auctions contract stubs + extend `Selling.ListingWithdrawn` to four fields; expose Aspire RabbitMQ management UI port | ✅ Docs + contracts only | `ListingWithdrawn` extension (ADR-005 additive) broke two M3-era positional-constructor call sites at compile time — wire-safe but source-breaking; fixup is item S1h. ADR-005's wire-safety guarantee is real but does not cover compile-time call sites. |
| S2 | Selling `WithdrawListing` command + handler; Selling-internal `ListingWithdrawn` domain event; `SellerListing.Apply(ListingWithdrawn)`; two Program.cs publish routes (`auctions-selling-events`, `listings-selling-events`); 4 tests + 1 cross-BC integration test | ✅ | Scope deviation: `SellingTestFixture` extended with `AuctionsBcDiscoveryExclusion` + `ListingsBcDiscoveryExclusion` to absorb `UnknownSagaException` from foreign-BC handler discovery under `MultipleHandlerBehavior.Separated`. New self-hosted Alba composition fixture for the single cross-BC integration test (`RealSellingProducerSagaTerminationTests`). |
| S3 | Proxy Bid Manager saga skeleton: `ProxyBidManagerSaga` (state-only at this point), `StartProxyBidManagerSagaHandler`, `UuidV5` helper, `AuctionsIdentityHelpers`. OQ1 Path C dispatcher pattern: `ProxyBidDispatchHandler.Handle(BidPlaced)` + `ProxyBidObserved` Auctions-internal command. 5 scenarios (4.1 / 4.2 / 4.4 / 4.5) + 1 dispatch test. Skill append: `wolverine-sagas.md` gained §"Composite-Key Correlation — the Dispatcher Pattern" + §"Multiple Handlers + Separated — Send, Don't Invoke" (commit `67b2252`). | ✅ | Two blockers: (1) `NoHandlerForEndpointException` on multi-handler `BidPlaced` `InvokeAsync` — fixed by switching test dispatch to `SendMessageAndWaitAsync`; (2) transient state pollution on the existing `PlaceBidDispatchTests` cleared after Blocker 1's fix. Git-history caveat: intermediate commits reference types whose files were not git-add'd at commit time; only the branch end-state is reviewable. |
| S4 | Proxy terminal paths + exhaustion + bidding war + register-while-outbid. `ParticipantCreditCeiling` projection (second application of M4-D4); `ParticipantCreditCeilingNotFoundException` + cooldown retry policy; new `auctions-participants-events` queue + listener; three wrapped commands (`ProxyListingSoldObserved` / `ProxyListingPassedObserved` / `ProxyListingWithdrawnObserved`) + three saga `Handle` methods + three `NotFound` absorbers; workshop-corrected three-way `Math.Min` exhaustion formula; six new scenarios (4.3, 4.6, 4.7, 4.8, 4.9, 4.10, 4.11) + 2 projection idempotency tests. Skill append: `wolverine-sagas.md` gained §"Saga-to-Saga Cascades — Eager / Single-Cycle Under SendMessageAndWaitAsync" (commit `01d5c12`). | ✅ | Two anticipated cross-cuts: (1) §4.10 bidding-war cascade halted at step one until the Listing stream was DCB-seeded (`SeedListingStreamAsync`); (2) four `AuctionClosingSagaTests.Close_*` assertions flipped `NoRoutes → Sent` for `ListingSold` / `ListingPassed` after the dispatcher's terminal handlers landed. Both folded into the skill file. OQ6a: `BuyItNowPurchased` proxy-termination deferred per the prompt's out-of-scope directive — orphan-saga risk acknowledged for post-MVP. |
| S5 | Session aggregate (`Session.cs` — first non-Listing Auctions aggregate, sealed-record functional-`Apply` shape) + three commands (`CreateSession`, `AttachListingToSession`, `StartSession`) + `SessionStartedHandler` fan-out. `PublishedListings` projection (third application of M4-D4) with full BiddingOpened-precursor payload (OQ1 Path A). `ListingPublishedHandler` Flash-listing guard. 14 new tests (7 aggregate + 2 fan-out + 3 dispatch + 2 projection). No skill append — nothing surfaced beyond M5-S3 / M4-S3 / M4-S4 coverage. | ✅ | Blocker: `CreateSession` dispatch test's `tracked.Sent`/`NoRoutes` was empty for the `IStartStream`-forwarded `SessionCreated` — `UseFastEventForwarding` runs async after the handler returns, so TrackActivity finalizes before forwarding lands. Fix: switch to `IMessageBus.InvokeAsync<CreationResponse<Guid>>` for typed-response capture. OQ2 framing distinction: milestone doc §6's "DCB-primary" idempotency wording conflated `BidConsistencyState` with stream-existence pre-query — the lived mechanism is the M3 `FetchStreamStateAsync` idiom. OQ3 Path α (withdrawn listing in `ListingIds`) deferred to S6 for cross-BC observation. |
| S6 | `CatalogListingView` extended with `SessionId` + `SessionStartedAt` + `Withdrawn` status. ADR-014 sub-question resolved to Sub-Option A: two new single-source siblings — `AuctionsSessionHandler` (Auctions-sourced) + `SellingListingWithdrawnHandler` (Selling-sourced). `AuctionStatusHandler.Handle(BiddingOpened)` gains a top-of-method `Withdrawn`-preservation guard. `ListingPublishedHandler` seed-handler extended with named-field allow-list for the two new fields. 4 §7 scenarios (with one `[Theory]` contributing 2 rows) + 1 cross-BC composition test (`BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus`) that pinned OQ3 Path α's Listings-side terminal to Path 3 (catalog handler is the source of truth). ADR-014 amended with the third lived application + Sub-Option A pin + named-field-allow-list discipline. Skill append: `marten-projections.md` §"View Extension Across Milestones" extended + new §"Status-Preservation Guards" subsection (commit `7aebe37`). | ✅ | M4-S6 prompt asserted the §"Status-Preservation Guards" subsection already existed from M5-S6's amendment — it did not. Authored fresh at M4-S6. The M5-S6 retrospective's claim is byte-frozen as historical record; the M4-S6 retro absorbs the correction durably (OQ5 Path B at this slice — see §"Open Question Dispositions"). |
| S7 | M4 milestone retro (this document); skill-consolidation null-call audit; Aspire smoke test; Narrative 001 Moment 3 disposition (Path B — Phase 2.5 stub at `phase2-5-narrative-001-moment-3-flash-cascade-audit.md` + Finding 013 in `001-findings.md`). No `.cs` / `Program.cs` / `CLAUDE.md` / milestone doc / ADR diff. | ✅ | This session |

---

## Cross-BC Integration Map at M4 Close

Six cross-BC integration hops live at M4 close — three carried from earlier milestones unchanged, three new or extended in M4. All verified against the Aspire-provisioned RabbitMQ container at this slice's smoke test:

```
Participants ─── SellerRegistrationCompleted ────────────► Selling                     [M1, unchanged]
              (queue: selling-participants-events)

Selling ─────── ListingPublished ────────────────────────► Listings                    [M2, unchanged]
              (queue: listings-selling-events)

Selling ─────── ListingPublished ────────────────────────► Auctions                    [M3, unchanged]
              (queue: auctions-selling-events)

Selling ─────── ListingWithdrawn ────────────────────────► Auctions                    [NEW M4-S2]
              (queue: auctions-selling-events — existing)   (AuctionClosingSaga + ProxyBidManagerSaga termination)

Selling ─────── ListingWithdrawn ────────────────────────► Listings                    [NEW M4-S2]
              (queue: listings-selling-events — existing)   (SellingListingWithdrawnHandler — Withdrawn status)

Auctions ────── BiddingOpened, BidPlaced,                ─► Listings                    [M3, extended in M4]
                BiddingClosed, ListingSold, ListingPassed,
                BuyItNowPurchased, SessionCreated,
                ListingAttachedToSession, SessionStarted
              (queue: listings-auctions-events)              (CatalogListingView — auction-status + session-membership fields)

Participants ── ParticipantSessionStarted ───────────────► Auctions                    [NEW M4-S4]
              (queue: auctions-participants-events — new)   (ParticipantCreditCeiling projection — credit-ceiling cache for proxy saga)
```

**Auctions BC-internal flows new at M4:**

```
Auctions  ────── SessionStarted                          ─► Auctions                    [NEW M4-S5]
                                                            (SessionStartedHandler fans out one
                                                             BiddingOpened per attached listing
                                                             via session.Events.StartStream<Listing>;
                                                             UseFastEventForwarding fans the appends out)

Auctions  ────── BidPlaced                               ─► Auctions                    [NEW M4-S3]
                                                            (ProxyBidDispatchHandler — second BidPlaced
                                                             subscriber alongside AuctionClosingSaga;
                                                             emits ProxyBidObserved per active proxy saga)

Auctions  ────── ListingSold / ListingPassed /           ─► Auctions                    [NEW M4-S4]
                ListingWithdrawn
                                                            (ProxyBidDispatchHandler terminal methods
                                                             emit ProxyListingXxxObserved per active proxy saga)
```

Settlement remains an active consumer of `ListingPublished`, `ListingWithdrawn`, `ListingSold`, `BuyItNowPurchased`, `ListingPassed`, and `ParticipantSessionStarted` (all wired in M5; the M4 contracts' L2 payloads were complete from S1 stubbing so no Settlement contract change was needed when M4-S2 / M4-S4 producers landed).

---

## Test Count at M4 Close

### Per-project breakdown

| Project | M3 Close | M4 Delta | M4 Close | Type |
|---|---|---|---|---|
| `CritterBids.Api.Tests` | 1 | 0 | 1 | Smoke |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 | Smoke |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 | Mixed |
| `CritterBids.Listings.Tests` | 11 | +9 | 20 | Integration (projection) |
| `CritterBids.Selling.Tests` | 32 | +4 | 36 | Mixed (aggregate + dispatch) |
| `CritterBids.Settlement.Tests` | 0 | +25 | 25 | All M5 (interleaved during the M4 pause) |
| `CritterBids.Auctions.Tests` | 35 | +30 | 65 | Integration (saga + aggregate + DCB + dispatch + projection + cross-BC) |
| **Total** | **86** | **+68** | **154** | |

### Plan-vs-actual reconciliation

| Source | Plan (M4 milestone doc §7) | Actual at M4 close | Δ vs plan | Rationale |
|---|---|---|---|---|
| Settlement BC | 0 (M4 plan did not anticipate M5 shipping during the pause) | 25 | +25 | M5 shipped six implementation slices between M4-S1 close (2026-04-20) and M4-S2 open (2026-05-18). All 25 Settlement tests landed during that pause. |
| Listings | +4 (S6 §7 scenarios) | +9 | +5 | +3 from M5-S5/S6 catalog-extension tests landing during the M5 pause; +1 from M4-S6's `[Theory]` xUnit-grain accounting (one Theory landing as two test methods); +1 from M4-S6's cross-BC composition test (not pre-sized into §7). |
| Selling | +4 (`WithdrawListing` scenarios) | +4 | 0 | Exact match. |
| Auctions | +24 (Session 12 + Proxy 12) | +30 | +6 | M4-S4 added 2 `ParticipantCreditCeiling` projection idempotency tests + M4-S4 added cap/bidding-war/register-while-outbid scenarios beyond the §7 base; M4-S5 added 2 `PublishedListings` projection idempotency tests not in §7; M4-S2 added 1 cross-BC integration test. |
| **Net** | **+32** | **+68** | **+36** | |

The +36 delta breaks cleanly into +29 from M5's interleaved work (which the M4 plan did not anticipate) and +7 from M4's own work expanding past the original sizing — chiefly the duplicate-projection idempotency tests at S4 and S5, plus M4-S6's `[Theory]` + cross-BC composition test. M5's planning hindsight is now part of the durable record: future milestone-doc sizing exercises that follow another milestone's interleaving should leave headroom for adjacent-milestone test growth, not just for the in-scope milestone's own work.

---

## Key Decisions Made in M4

| Identifier | Decision |
|---|---|
| [ADR-007 Gate 4](../decisions/007-uuid-strategy.md) | **Event row ID strategy — re-deferred at M4-S1.** JasperFx input still pending. New trigger: M5-S1 (last Marten BC foundation-decisions session). Named owner: Erik (JasperFx follow-up nudge). Bare re-deferral rejected. M5-S1 has since shipped and re-deferred again with a further trigger — see its retro for the latest status. |
| M4-D1 | **Proxy saga composite-key format — colon-delimited string `$"{ListingId}:{BidderId}"`.** Pinned at S1 in `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` with a dedicated namespace Guid (`abffa589-fb32-4b62-8ff7-ee1ca4f255ff`). Matches Workshop 002 §4.1 text verbatim; UUID v5 hashing is deterministic on either input so the hash-domain cost is identical to byte-concatenation. |
| M4-D2 | **Session aggregate stream ID — UUID v7 (`Guid.CreateVersion7()`).** Resolved at S1; implemented at S5. No natural business key exists (session titles are not unique); v7 provides insert locality via its Unix-ms prefix. Consistent with every other event-sourced aggregate in the codebase. |
| M4-D3 | **ADR-014 timing — author at S6 alongside the code that justifies it.** The author-date drifted out of M4 (the ADR was authored at M5-S6 — written by the Settlement team alongside their concurrent settlement-status sibling — and amended at M4-S6 with the Sub-Option A resolution). The drift did not affect S6's lived implementation; the ADR was Accepted and amended in the same M4-S6 PR. |
| M4-D4 | **Auctions-side duplicate `PublishedListings` projection for `AttachListingToSession` published-status check.** Resolved at S1; first lived application landed at S5. Preserves BC isolation (Auctions never reads a Listings-owned view). Same pattern at M4-S4 for `ParticipantCreditCeiling` (per-bidder credit-cache for the proxy saga). **Three lived applications across M4 + M5 — ADR-015 candidate, see §"ADR Candidate Review".** |
| M4-D5 | **`ListingWithdrawn` adds a new `"Withdrawn"` string to the catalog `Status` vocabulary (not an overload on `ClosedReason`).** Resolved at S6. The `Status` field is now seven strings — `"Published"`, `"Open"`, `"Closed"`, `"Sold"`, `"Passed"`, `"Settled"`, `"Withdrawn"` — six of them terminal. |
| M4-D6 | **M3 test-fixture `ListingWithdrawn` synthesis kept as unit-test-only shortcut.** Resolved at S2. `AuctionsTestFixture.AppendListingWithdrawnAsync` retains the hand-crafted-event shape for saga tests that only need the event shape; integration paths now exercise the real Selling producer via `RealSellingProducerSagaTerminationTests`. The helper's docstring names the real producer as the canonical reference. |
| ADR-014 amendment (M4-S6) | **Sub-Option A pinned unconditionally: one handler class per source BC, single-source per sibling.** Source-prefix naming convention (`Auctions*Handler`, `Selling*Handler`, `Settlement*Handler`); grandfathered names retained. §"Decision" §1 strengthened from conditional to unconditional. §"Decision" §5 records the named-field-allow-list discipline in seed handlers (`ListingPublishedHandler` adds one preservation line per new downstream-sibling field). |

---

## Key Learnings — Cross-Session Patterns

These generalize across more than one M4 session or carry forward into M5+ work. Session-local findings remain in individual session retros.

1. **Composite-key saga correlation under `MultipleHandlerBehavior.Separated` is solved by the dispatcher pattern.** `[SagaIdentityFrom]` requires a property-pull from the inbound message; for a composite key derived from two values where one inbound (`BidPlaced`) targets many saga instances, a non-saga `Handle(BidPlaced)` queries active sagas and emits a wrapped command (`ProxyBidObserved`) per match. The saga's reactive handler pulls the SagaId from the wrapper. Verified at M4-S3 against the Wolverine source (`PullSagaIdFromMessageFrame.cs`); folded into `wolverine-sagas.md`.

2. **Saga-to-saga cascades under `SendMessageAndWaitAsync` are eager and single-cycle.** A single dispatch waits for the full recursive cascade to drain — including saga reactions, their `OutgoingMessages`, the dispatcher's fan-out, and downstream saga reactions to those. The §4.10 two-proxy bidding war (~10 hops) completes in ~1 second within one tracked invocation. Folded into `wolverine-sagas.md` at M4-S4. Implication for test design: prefer `SendMessageAndWaitAsync` for multi-handler dispatch; `InvokeMessageAndWaitAsync` is correct only for single-handler messages.

3. **`[WriteAggregate]` from first commit pays off at dispatch-test time.** Every M4 aggregate command (`AttachListingToSession`, `StartSession`, `WithdrawListing`) carried `[WriteAggregate(nameof(...))]` from its first commit per the M2.5 / M3 precedent. Marten 8 + sealed-record + static `Create` + instance `Apply` returning new-via-`with` composes cleanly with `[WriteAggregate]` + `LiveStreamAggregation<T>()` — no schema ceremony needed (verified at M4-S5 with the first non-Listing Auctions aggregate).

4. **The duplicate-projection pattern is now a load-bearing modular-monolith primitive with three lived applications.** `Settlement.BidderCreditView` (M5-S5), `Auctions.ParticipantCreditCeiling` (M4-S4), `Auctions.PublishedListings` (M4-S5) all share the shape: each consuming BC subscribes to an upstream contract event, projects a small Marten document keyed by the entity id, and applies tolerant-upsert with terminal-status preservation. Preserves BC isolation; trades one extra projection per consuming BC for the ability to read upstream state without crossing BC boundaries at hot-path time. The threshold-of-three for ADR authorship is now crossed — see §"ADR Candidate Review".

5. **`UseFastEventForwarding` + `IStartStream` cascade does not land in `tracked.Sent` synchronously.** Forwarded events from `IStartStream` returns run asynchronously after the handler returns; `TrackActivity` has already finalized its capture. Use `bus.InvokeAsync<TResponse>` for typed-response capture, or `AggregateStreamAsync` for aggregate-state assertions. Direct-`OutgoingMessages` cascades (like `StartProxyBidManagerSagaHandler` emitting `ProxyBidRegistered`) still land in `tracked.NoRoutes` synchronously. Discovered at M4-S5; mental-model note for future dispatch tests.

6. **ADR-014's single-source-per-sibling rule is now unconditional.** M3-S6 + M5-S6 + M4-S6 establish three lived applications of the per-source-BC sibling-class convention; the third application (M4-S6) strengthened §"Decision" §1 from conditional to unconditional. Future BC catalog extensions (Obligations status surfaces, Relay status surfaces, Operations status surfaces) inherit "per-source sibling unless proven otherwise" as the default. Re-confirmation: any future slice considering a multi-source sibling must amend ADR-014 with new evidence justifying the exception for that specific application.

7. **Named-field allow-list discipline in seed handlers is now a Listings-BC-specific load-bearing rule.** Every new sibling-handler field below the M2 block adds its own preservation line in `ListingPublishedHandler` — not implicit record-`with` semantics. Pinned in the handler's XML comment and in ADR-014 §"Decision" §5. The rule lives in code and in the ADR; whether it warrants `CLAUDE.md`-level promotion is a forward question (recommend defer — the rule is Listings-BC specific until a second BC adopts a sibling-handler topology).

8. **Skill-fold discipline at M4 close is the inverse of M3 close.** M3-S7 ran a six-finding bulk pass because S1-S6 had accumulated findings without inline folds. M4-S3, M4-S4, and M4-S6 all folded inline at slice close (commits `67b2252`, `01d5c12`, `7aebe37`); M4-S1, M4-S2, M4-S5 each made explicit null-call audits ("nothing new surfaced beyond the existing skill section"). M4-S7 ships with zero accumulated findings — a null-call milestone retro audit (see §"Skill Consolidation Review"). Hypothesis: per-session skill-fold discipline tightened across M3-S7 → M4-S1 such that every implementation session either folds inline or makes an explicit no-append decision in its retro. The pattern is now the M4-established default.

---

## ADR Candidate Review

| Finding | ADR warranted? | Rationale |
|---|---|---|
| Composite-key dispatcher pattern (M4-S3) | **No** | Consumer of Wolverine's `Separated` + `[SagaIdentityFrom]` primitives — no CritterBids-side architectural choice with alternatives. Skill-file rule sufficient. |
| Saga-to-saga eager-cascade timing (M4-S4) | **No** | Observed behaviour of Wolverine's tracked-session + sticky-queue routing. Skill-file rule sufficient. |
| `UseFastEventForwarding` async vs `tracked.*` (M4-S5) | **No** | Wolverine framework behaviour. Skill-file / retro-only learning. |
| Sub-Option A single-source-per-sibling (M4-S6) | **Already ADR-014** | The amendment landed at M4-S6 — third lived application + strengthened §"Decision" §1. |
| Named-field-allow-list seed-handler discipline (M4-S6) | **No (defer)** | Listings-BC-specific. Earn `CLAUDE.md` promotion when a second BC adopts a sibling-handler topology and faces the same convention. |
| **Duplicate-projection pattern (M4-D4 — three lived applications)** | **Yes — ADR-015 candidate** | Three lived applications at M4 close (`Settlement.BidderCreditView` M5-S5, `Auctions.ParticipantCreditCeiling` M4-S4, `Auctions.PublishedListings` M4-S5). The threshold-of-three is symmetric with ADR-014's authoring rationale (M3-S6 + M5-S6 + M4-S6 lived applications earned ADR-014 its body). The M4 milestone doc §8 M4-D4 row originally said "no ADR trigger"; that disposition is now overtaken by lived evidence. **Proposed:** ADR 015 — "Duplicate Projections for BC-Isolation-Preserving Cross-BC Reads". One-paragraph framing: a consuming BC subscribes to an upstream BC's integration event, projects a small Marten document keyed by the entity id (no fields beyond what the hot-path handler needs, except where workshop scenario vocabulary justifies an extended payload as at M4-S5 Path A), and applies tolerant-upsert with terminal-status preservation. Trades one extra projection per consuming BC for the elimination of cross-BC hot-path reads. Three M4+M5 applications form the evidence; the consequences mirror integration-messaging L2 (full payload at first commit). **Flag, do not author this slice** — keep the author-when-it-justifies-the-decision discipline consistent with M3-S7 / M4-S6. |

**Recommend Path A for the duplicate-projection question** (flag with proposed number ADR-015, one-paragraph framing here, defer authorship to a follow-up docs slice). Authoring belongs to a session that can sit alongside the third application's lived code; M4-S7 is too far from `Auctions.PublishedListings` (M4-S5) to bring the authoring up to ADR-014-grade fidelity at this slice's scope.

---

## Technical Debt and Deferred Items

| Item | Deferred in | Target |
|---|---|---|
| ADR-007 Gate 4 — event row ID strategy | M4-S1 (re-deferred from M3-S1); M5-S1 (re-deferred again) | Next foundation-decisions session that touches Marten event-row storage; owner Erik. |
| The M3 fixture-synthesized `ListingWithdrawn` shortcut | M4-S2 / M4-D6 | Kept indefinitely as unit-test shortcut; the integration path is real. No removal trigger named. |
| Defensive pre-filtering at `StartSession` time of listings withdrawn since attach | M4-S5 / M4 milestone doc §3 | Post-MVP hardening. M4-S6 pinned the Listings-side terminal (Path 3 — catalog handler preserves Withdrawn); Auctions-side terminal remains unobserved (see next row). |
| Auctions-side OQ3 Path α terminal observation | M4-S5 (deferred to S6); M4-S6 (deferred to S7); **M4-S7 (deferred to M6 frontend ship)** | M6 frontend ship surfaces the bidder-facing terminal; if the Listings-side guard is sufficient for user-facing posture, the Auctions-side observation may never need to land. Named trigger: M6 frontend; named owner: Erik for the trigger evaluation when the M6 frontend slice opens. |
| `BuyItNowPurchased` as proxy-saga terminal | M4-S4 / OQ6a | Post-MVP. Orphan-saga risk acknowledged; not observable in the current M6 demo path (no live proxy paths exercised by BIN). Re-evaluate when an Operations BC "active proxies count" surface ships. |
| `BuyItNowPurchased` cascade-bucket / Send-vs-Invoke pre-emptive fix | M4-S4 / OQ6a | Same defer trigger as the BIN-as-terminal row above. |
| Operations BC `SessionCatalog` view (per-session summary) | `SessionCreated` contract docstring; M4-S6 retro carry-forward | Post-M5 Operations BC milestone. Named candidate slice. |
| `POST /api/listings/submit` HTTP endpoint (`[WriteAggregate]` stream-ID verification) | Carried since M2 | M6 frontend milestone. |
| Bid-increment helper extraction | M3-S4 / M4-S3 / M4-S4 / M4-S5 / M4-S6 retros | Threshold of three uncrossed across all of M3 + M4 + M5; two co-located inline copies persist (`PlaceBidHandler` + `ProxyBidManagerSaga`). Extract whenever a third user genuinely surfaces — likely an M6 UI hint surface needing client-side increment math. |
| Narrative 001 Moment 3 `defer → green` finding (the cascade lived-code audit) | M4-S7 | Stubbed at this slice as `phase2-5-narrative-001-moment-3-flash-cascade-audit.md` per OQ1 Path B; Finding 013 added to `001-findings.md`. Narrative amendment lands in a Phase 2.5 PR. |
| ADR-015 — Duplicate Projections for BC-Isolation-Preserving Cross-BC Reads | M4-S7 / §"ADR Candidate Review" | Flagged with proposed number + one-paragraph framing; authorship deferred to a follow-up docs slice. Suggested authoring date: pair with the next slice that exercises duplicate-projection logic (M6 Operations BC if it adds a fourth lived application; or a post-M5 docs-cleanup slot if the existing three suffice). |
| 24 NU1904 NuGet vulnerability warnings on Marten 8.35.0 | Carried since M3 baseline | Owed a one-off project-wide disposition (suppress / upgrade / accept-and-document). Candidate slot: M5+ infrastructure-hygiene docs slice; or paired with whatever Marten version upgrade lands first. |
| `UseFastEventForwarding`-vs-`tracked.Sent` async-capture mental model | M4-S5 retro | Captured in the retro and as a key learning here; not a skill-file addition because the M5-S3 PendingSettlement section + the wolverine-sagas Send-vs-Invoke section together cover the pattern. Re-evaluate at the next time a `CreationResponse<T>` + cascade scenario surfaces. |

---

## Skill Consolidation Review — Null-Call Audit

Per the M4-S7 prompt's item 2, this is a null-call audit confirming the three inline-folded skill updates and the three no-append decisions across M4's six implementation slices. **No gap surfaced; no new skill-file commit ships in M4-S7.**

| Slice | Skill activity | Verification |
|---|---|---|
| M4-S1 | Foundation decisions + contract stubs; no skill change owed. | Retro §"Verification checklist" — `wolverine-message-handlers.md` and `integration-messaging.md` byte-identical at session close. |
| M4-S2 | Selling `WithdrawListing` + cross-BC integration test; no skill change owed. | Retro §"Key learnings" §4 — "introduces no novel pattern and the existing patterns documented in those skill files were applied verbatim". |
| M4-S3 | `wolverine-sagas.md` gained §"Composite-Key Correlation — the Dispatcher Pattern" + §"Multiple Handlers + `MultipleHandlerBehavior.Separated` — Send, Don't Invoke" (commit **`67b2252`**). | Retro §S3l — both sections folded inline at session close. |
| M4-S4 | `wolverine-sagas.md` gained §"Saga-to-Saga Cascades — Eager / Single-Cycle Under SendMessageAndWaitAsync" (commit **`01d5c12`**). | Retro §S4w — subsection appended at session close. |
| M4-S5 | No skill change owed; nothing new surfaced beyond M5-S3 PendingSettlement + M4-S3 Send-vs-Invoke coverage. | Retro §"Skill append discipline" — explicit no-append call with rationale. |
| M4-S6 | `marten-projections.md` §"View Extension Across Milestones" extended with the third application + new §"Status-Preservation Guards" subsection authored fresh (commit **`7aebe37`**). | Retro §"Decisions inheriting forward" §"Status-preservation guards are a named pattern". |
| M4-S7 | Null-call audit — no fold, no append, no commit. | This subsection. |

The three inline-folded updates and the three no-append decisions together demonstrate the M4-established skill-fold discipline: every implementation slice either folds findings inline at session close or makes an explicit no-append decision in its retro. Close-out sessions inherit the discipline as a null call rather than a bulk pass — see Key Learning 8 above for the cross-session pattern.

---

## Operational Smoke-Test Outcome

**Scope.** Verify the Aspire-provisioned RabbitMQ container against the wired publish bindings and listen queues at M4 close. Confirm M4-S1's `.WithManagementPlugin()` exposure of the RabbitMQ management UI. Confirm all four M4-promised queues / routes are visible with consumers attached.

**Method.** `dotnet run --project src/CritterBids.AppHost --launch-profile http`; Aspire dashboard reachable at `http://localhost:15237`; `rabbitmq-zyprqdjn` + `postgres-yyemxwsu` containers spawned under the `critterbids` Docker Compose label; queue / binding inspection via `docker exec rabbitmq-zyprqdjn rabbitmqctl list_queues` and `list_bindings`; management UI probe via `curl http://localhost:57302` (host port dynamically assigned by Aspire from container port 15672).

**Result: PASSED, no anomalies.**

```
name                                              messages  consumers  M4 role
listings-auctions-events                          0         1          M3 — extended M4-S5 (Session trio)
selling-participants-events                       0         1          M1 (unchanged)
settlement-selling-events                         0         1          M5 (Settlement consumes Selling)
wolverine-dead-letter-queue                       0         0          Standard
operations-settlement-events                      0         0          M5 (post-M5 consumer; 0 consumers as expected)
listings-selling-events                           0         1          M2 — extended M4-S2 (ListingWithdrawn)
auctions-participants-events                      0         1          NEW M4-S4 (Auctions consumes Participants)
settlement-participants-events                    0         1          M5
wolverine.response.<guid>                         0         1          Standard
relay-settlement-events                           0         0          M5 (post-M5 Relay consumer; 0 consumers as expected)
auctions-selling-events                           0         1          M3 — extended M4-S2 (ListingWithdrawn)
settlement-auctions-events                        0         1          M5
listings-settlement-events                        0         1          M5
```

**Per the M4 milestone doc §Appendix promise table:**

| M4-promised route | Source | Destination queue | Consumer attached at smoke test? |
|---|---|---|---|
| `ListingWithdrawn` → Auctions (saga termination) | Selling | `auctions-selling-events` | ✅ 1 consumer |
| `ListingWithdrawn` → Listings (catalog Withdrawn status) | Selling | `listings-selling-events` | ✅ 1 consumer |
| Session trio → Listings (catalog session-membership) | Auctions | `listings-auctions-events` | ✅ 1 consumer |
| `ParticipantSessionStarted` → Auctions (`ParticipantCreditCeiling` projection) | Participants | `auctions-participants-events` | ✅ 1 consumer |

**Management UI reachability:** HTTP 200 at the Aspire-assigned host port (M4-S1's `.WithManagementPlugin()` deliverable verified; M3-S7's operational gap is closed). The `rabbitmqctl` via `docker exec` path remains available as the M3-precedent fallback.

**Anomalies observed:** none. Dead-letter queue clean at zero messages. Two forward-spec queues (`operations-settlement-events`, `relay-settlement-events`) sit at 0 consumers as expected — Operations and Relay BCs are post-M5 per the project plan. No M4-promised queue or binding is missing. No M5-side regression.

**Posture at M4 close:** the integration surface is wired in both dev (Aspire) and tests (Testcontainers), confirmed against real RabbitMQ, with no known gaps. Queue-level ordering within each queue is guaranteed; cross-queue ordering is not (per ADR-014's expand-additively rule and the M3-established tolerant-upsert pattern).

---

## Narrative 001 Moment 3 Disposition

Per OQ1 (recommended Path B; accepted at session open): the narrative finding is routed to a Phase 2.5 follow-up stub rather than amended inline at this slice.

- **Stub prompt:** `docs/prompts/implementations/phase2-5-narrative-001-moment-3-flash-cascade-audit.md` — v0.1 authored at this slice. Names the lived two-class topology (`AuctionsSessionHandler` + `SellingListingWithdrawnHandler` per ADR-014 Sub-Option A) and the narrated single `SessionMembershipHandler`; proposes the narrative-amendment shape (replace the class name, drop the Moment 3 `defer` line, point at this resolution).
- **Finding ledger:** `docs/narratives/001-findings.md` Finding 013 added — routed `narrative-update`, surfaced at Moment 3 (lived-code audit ran at M4-S7), resolution pointer to the Phase 2.5 stub.
- **No `.cs` change.** The lived implementation is correct; only the narrative drifts. ADR-014 Sub-Option A is the canonical reference for the source-prefix naming convention.

The rationale for Path B over Path A (inline amendment at M4-S7): ADR 016 Phase 2 discipline separates audit-and-fix from author-and-ship; M4-S7's scope is the milestone retrospective + skill audit + smoke test, not narrative authoring. The Phase 2.5 precedent for `narrative-update` findings surfaced at milestone close was established with Finding 011 (`phase2-5-extension-calculation-fix.md`).

---

## Session-Split Retrospective — Zero Splits at M4

M4 ran exactly seven sessions with no splits. Three pre-drafted slots (S4b, S5b, S6b) were authored at planning time as preemptive safety valves and never exercised. This is the inverse of M3's two-split outcome (S4b + S5b actually fired). The zero-split outcome warrants a brief reflection given its symmetry to M3's reflection on the value of preemptive split slots.

**Hypothesis 1 — better milestone-doc sizing post-M3 retro.** The M3-S7 retro recommended preemptive split-slot drafting as a discipline ("when a session prompt's acceptance criteria count approaches 20, preemptively draft an Xb continuation slot"). M4 took the recommendation seriously and authored three; the discipline turned out to be more valuable as a *forcing function for prompt-author discipline* than as an actual execution-time tool. Knowing an Xb slot existed gave each session prompt a clear "if it goes long, here's where to put the residual" outlet, which paradoxically reduced the pressure to over-scope each base session.

**Hypothesis 2 — the M4 surface area was naturally more bounded than M3's.** M3 introduced two new patterns (DCB boundary model, first saga); M4 extended two patterns (composite-key second saga, second event-sourced aggregate type) and lifted existing primitives. Pattern-lift work is more predictable than pattern-introduction work, and predictability shows up as smaller variance in actual-vs-planned slice sizing.

**Hypothesis 3 — the M4-S5 prompt absorbed the OQ-2 framing-mismatch surprise without splitting.** M4-S5's milestone-doc §6 named "DCB-primary" idempotency that turned out to be a different mechanism (stream-existence pre-query) at lived-implementation time. Under M3 norms this could have triggered an S5b. Under M4 the framing discrepancy resolved inside the base session because the OQ was lifted to session-open at the user's gate, the implementation chose the lived primitive without ceremony, and the retro recorded the framing distinction. Discipline of "halt-and-consult at OQ-resolution time, not at mid-session debug time" pays off.

**For M6+ planning.** The zero-split outcome should not lead to dropping the preemptive Xb discipline — its value is as a forcing function for sizing clarity, not as an execution-time tool. Continue to draft Xb slots when a session's acceptance-criteria count approaches the M3 threshold of 20. Three pre-drafted slots becoming zero used is a feature, not a planning over-shoot.

---

## What M5 / M6 Should Know

At M4 close the solution has **154 tests** passing across seven test projects, with seven BCs implemented on PostgreSQL via Marten per ADR-011. Six cross-BC integration hops are live (three carried from earlier milestones, three new or extended in M4) and three Auctions-internal cascade flows are wired (`SessionStarted` fan-out via stream-append, `BidPlaced` → proxy dispatcher fan-out, three terminal events → proxy dispatcher terminal handlers). The duplicate-projection pattern (M4-D4) has earned three lived applications and is now an ADR-015 candidate; the read-model-extension Sub-Option A pattern (ADR-014) is unconditional and is the binding rule for every future BC catalog extension.

**M5 has already shipped during the M4 pause.** Settlement BC is fully wired (25 tests, six integration hops with Settlement as consumer, the `BidderCreditView` duplicate-projection for the settlement saga's credit-ceiling reference). Settlement closes the financial loop on `ListingSold` / `BuyItNowPurchased`; M5's retros (S1 through S6) are the durable reference for that work. This M4 retro touches M5 only in two reconciliation surfaces: (a) the test-count plan-vs-actual table records M5's interleaved +29 tests so M6's planning hindsight has the data; (b) the operational smoke test verifies M5's queues are not regressed by anything M4 lands.

**M6 inherits a stable Auctions + Listings + Settlement integration surface and adds three things on top:**

1. **Real authentication** — lifting `[AllowAnonymous]` everywhere. The project stance through M5 has been `[AllowAnonymous]` on all endpoints; M6 introduces a real auth scheme and gates HTTP commands behind it. Nothing about the M4 surface contradicts this — all M4 commands are bus-dispatched test-only.
2. **HTTP endpoint surface for Auctions commands.** `RegisterProxyBid`, `CreateSession`, `AttachListingToSession`, `StartSession`, and `WithdrawListing` are all `[WriteAggregate]`-shaped and dispatch-test-covered but have no HTTP surface today. M6 adds `[WolverinePost]` (or equivalent) to each, alongside the M2-deferred `POST /api/listings/submit`. The M4-D4 `PublishedListings` projection's published-status check is HTTP-ready at the command-handler grain.
3. **Frontend rendering of the 21-field `CatalogListingView`.** The catalog read model is stable at 21 fields (8 M2-S7 + 10 M3-S6 + 1 M5-S6 + 2 M4-S6) with a seven-string `Status` vocabulary, six of them terminal. M6's `critterbids-web` and `critterbids-ops` frontends consume this shape transparently; no projection-shape change is needed for the M6 frontend ship. The Auctions-side OQ3 Path α terminal observation becomes user-facing at this point and is the natural trigger for either pinning the Auctions-side terminal (by adding telemetry / a composition test) or confirming that the Listings-side guard is sufficient for the UX.

**What is stable and ready to build on:** the Auctions BC contract surface (15 contracts authored; all carry L2 payloads for every named future consumer); the Listings catalog projection shape; the saga-and-aggregate primitives (`[WriteAggregate]`, `[SagaIdentityFrom]`, dispatcher pattern for composite-key correlation, duplicate-projection pattern for BC-isolation-preserving reads); the testing primitives (`*BcDiscoveryExclusion` for foreign-BC handler isolation; `TrackActivity` for cascade-completion timing; `IMessageBus.InvokeAsync<TResponse>` for `IStartStream`-shaped dispatch tests).

**What is flagged fragile (carry-forward):** the 24 NU1904 NuGet vulnerability warnings (Marten 8.35.0 advisory) need a project-wide disposition; the `BuyItNowPurchased` proxy-termination gap will surface when any Operations BC "active proxies count" view ships; the Auctions-side OQ3 Path α terminal is unobserved and may become user-visible at M6 frontend ship time; the bid-increment helper's two co-located inline copies remain below the extraction threshold but will likely cross it when an M6 UI hint surface needs client-side increment math.

**Sub-Option A future-application clause re-confirmation.** ADR-014 §"Decision" §1 is unconditional at M4 close: single-source-per-sibling, source-prefix naming (`Auctions*Handler`, `Selling*Handler`, `Settlement*Handler`). Future Obligations / Relay / Operations status-field extensions follow this rule by default; any multi-source-sibling proposal must amend ADR-014 with new evidence justifying the exception for the specific application. Default response: "per-source sibling unless proven otherwise."

**Named-field-allow-list discipline disposition.** The rule lives in `ListingPublishedHandler.cs`'s XML comment block and in ADR-014 §"Decision" §5. Recommend defer on `CLAUDE.md` promotion — the rule is Listings-BC-specific until a second BC adopts a sibling-handler topology. Re-evaluate when M6+ Operations BC adds its own multi-source catalog reflection (or when any BC sees a fifth sibling on a shared read model).
