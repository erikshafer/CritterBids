# CritterBids — Project Status Snapshot

**As of:** 2026-06-12 · `main` @ `7b31147` + the M8-S7 close PR (regenerated inside that PR; **M8 complete**)
**Derived from:** [`retrospectives/M8-S7-end-to-end-housekeeping-retrospective.md`](./retrospectives/M8-S7-end-to-end-housekeeping-retrospective.md) + the M8 milestone retrospective (latest session close), [`milestones/M8-frontend-spas.md`](./milestones/M8-frontend-spas.md) (v0.6, ✅ Complete), [`decisions/README.md`](./decisions/README.md), [`research/README.md`](./research/README.md), `.github/workflows/ci.yml`

> **This document is a derived snapshot, not a source of truth.** The canonical session-close
> state is always the **most recent retrospective** in `docs/retrospectives/`. If this snapshot
> and a newer retrospective disagree, the retrospective wins. Any agent (Claude Code, GitHub
> Copilot, a local LLM) answering "where are we?" should read this file first, then verify
> against the newest retro before acting.
>
> **Regeneration rule:** refresh this file at session close (or whenever it is found stale) by
> re-deriving every section from the newest retrospective, the active milestone doc, the ADR
> index, and the deferred-items tables. Update the "As of" line with the date and `main` commit.

---

## 1. Where Are We?

**M8 (React frontend SPAs) is complete** — and with it the full MVP arc: the backend (M1–M7,
all 8 BCs) plus both frontend SPAs and the end-to-end proof. The bidder app renders narrative
001's full journey (anonymous session → catalog → live bid war → extended bidding → gavel →
settlement confirmation); the ops dashboard renders the six operator boards over a **complete,
push-only** ops feed (the S6b 22-value vocabulary, polling stopgap deleted, topology invariant
in CI); and M8-S7 closed the milestone with the **Playwright two-bidder bid-war e2e**
(`client/e2e/`, two isolated browser contexts against the live Aspire stack — the ADR 013
multi-context use case), the CI frontend job extended to the whole workspace, and the doc
surface refreshed to shipped reality.

- **Build state at close:** 0 errors / 0 warnings; **307 backend tests green** (full
  `dotnet test CritterBids.slnx`); **72 frontend Vitest** (`@critterbids/ops` 47,
  `@critterbids/bidder` 25); **1 Playwright e2e** (local-only, two consecutive green runs
  recorded in the S7 retro).
- **Engine baseline:** Wolverine **6.8.0** (Marten via `WolverineFx.Marten`) / .NET 10 /
  ASP.NET Core 10.0.9 / Aspire **13.4.3**. A single `dotnet run --project
  src/CritterBids.AppHost` starts Postgres, RabbitMQ, the API host, and **both SPA dev servers**
  (bidder `:5173`, ops `:5174`) as Aspire children (commit `7b31147`).
- **Frontend:** `client/` is a three-member npm-workspaces monorepo (ADR 025): `bidder/`,
  `ops/`, and `e2e/` (the M8-S7 Playwright harness). `shared/` stays a planned member —
  extraction **deferred to M9** (S7 decision: the apps duplicate the pattern, not the bytes;
  the seller console is the third consumer that reveals the real shared subset).
- **Notable S7 finding (escalated, not fixed):** the bidder app's extended-bidding *banner* is
  structurally unreachable — the Listings BC has no `ExtendedBiddingTriggered` handler, so
  `CatalogListingView.ScheduledCloseAt` never advances after `BiddingOpened`. The saga-side
  extension itself works (e2e-proven: the listing outlives its original close). Backend
  carry-forward; M8 had no sanctioned exception left.

### Milestone ladder

| Milestone | Scope | Status |
|---|---|---|
| M1 | Skeleton + Participants BC | ✅ Complete |
| M2 / M2.5 | Selling + Listings pipeline | ✅ Complete |
| M3 | Auctions BC core (DCB, Auction Closing saga) | ✅ Complete |
| M4 | Auctions completion (Proxy Bid saga, Session aggregate, WithdrawListing) | ✅ Complete |
| Foundation refresh | Spec-anchored development retrofit (ADR 016–018), narratives, workshops | ✅ Complete |
| M5 | Settlement BC (payment saga, financial event stream) | ✅ Complete |
| M6 | Obligations BC (post-sale saga) + Relay BC (SignalR push) | ✅ Complete |
| M7 | Operations BC (operator read models + staff auth) | ✅ Complete (281 tests at close) |
| **M8** | **React frontend SPAs (bidder + ops dashboard) + e2e** | ✅ **Complete** (307 backend / 72 Vitest / 1 e2e at close) |
| M9 | Seller console (working name) | 🔭 Next — **not yet scoped**; needs its milestone-scoping session first |

### M8 slice ledger (final)

| Slice | Scope | Status |
|---|---|---|
| M8-S1 | Foundation: ADR 013 accepted, ADR 025 (monorepo layout), `client/bidder/` BiddingHub proof | ✅ Done |
| M8-S2 | Bidder SPA shell + catalog + anonymous session | ✅ Done (PR #80) |
| M8-S3a | Backend precursor: `POST /api/auctions/bids` over the existing PlaceBid DCB command | ✅ Done (PR #84) |
| M8-S3b | Bidder live bidding + ADR 026 (SignalR integration pattern) | ✅ Done (PR #86) |
| *(interlude)* | Bug #2 root cause + fix (dispatcher bridge, ADR 027 authored, 409 middleware, skills) | ✅ Done (PRs #88–#92) |
| M8-S3c | ADR 027 implementation: per-BC sticky queue bindings, exactly-once consumption | ✅ Done (PR #93) |
| M8-S4 | Bidder settlement outcome (confirmed-charge view) | ✅ Done (PR #94) |
| M8-S5 | Ops SPA shell + staff auth gate + `OperationsHub` credential dance | ✅ Done (PR #95) |
| M8-S6 | Ops dashboard views (six boards, ADR 026 cache bridge, Title join) | ✅ Done (PR #98) |
| M8-S6b | Relay ops-feed completion (8 events, topology invariant, stopgap deleted) + dispute-resolution control | ✅ Done (PR #100) |
| M8-S7 | Playwright two-bidder bid-war e2e + full-workspace CI frontend job + doc refresh + retros | ✅ Done (this PR) |

---

## 2. What's Up Next?

### Immediate: scope M9 (seller console)

M8's milestone retrospective defers the seller-perspective journeys (narratives 004/005/006 —
publish, watch-close, fulfill-obligation) outward to a working-name **M9 seller console**.
**No `docs/milestones/M9-*.md` exists yet** — per the milestone-doc precondition gate, the next
step is a milestone-scoping session that authors it; an M9-S1 implementation prompt would
hard-gate on its absence. Inputs: the M8 milestone retro's "what M8 defers outward", narratives
004/005/006, and the `client/shared/` extraction decision (M9 is its recorded trigger).

Satellite work running alongside (not session-gated):

- **Wolverine upstream fix** — agent work order at
  [`research/wolverine-upstream-saga-sticky-separation-handoff.md`](./research/wolverine-upstream-saga-sticky-separation-handoff.md)
  (launch in `C:\Code\JasperFx\wolverine`); includes the `FirstOrDefault` sticky-sibling
  starvation finding and the `describe-routing --explain` NRE. Verify against the 6.8.0 line
  before filing — the handoff predates two engine bumps.
- **ai-skills PR #71** (private repo) — with Jeremy for review.

---

## 3. Deferred / Delayed Ledger

### Deferred with a target

| Item | Deferred at | Target |
|---|---|---|
| **Listings `ExtendedBiddingTriggered` handler** (CatalogListingView.ScheduledCloseAt never advances → the bidder extended-bidding banner and the "Extended" status are unreachable; narrative 001 Moment 6's Listings-side upsert is still forward-spec) | M8-S7 e2e finding | Backend slice (first M9-adjacent housekeeping candidate) |
| **`client/shared/` extraction** (Zod wire schemas, SignalR provider pattern) | M8-S7 decision | M9 — the seller console is the third consumer |
| **Playwright e2e in CI** (needs the full Aspire stack in Actions — its own infrastructure piece) | M8-S7 decision (comment at the `frontend` CI job) | Re-evaluate when CI infra work is on the table |
| **Push-refetch vs projection race** (a hub push can trigger the re-query before the projection applies; the UI converges only at the next push — observed once in the S7 e2e's first run on the close-move; benign for bids, structural for the last event of a burst) | M8-S7 e2e finding | Frontend hardening candidate (e.g. delayed re-invalidate in the cache bridge) — pairs with the row above |
| Sold → Settled banner transition distinguished in e2e (the bid-war test accepts either terminal text; the Settled-specific "It's yours!" beat is not separately asserted) | M8-S4 → S7 | Future e2e extension |
| `IRevisioned` + `ConcurrencyException` retry policies for `SettlementSaga` / `PostSaleCoordinationSaga` | M8-S3c retro | Follow-up slice/chore |
| `SettlementCompleted`/`PaymentFailed` double-publish (saga appends AND returns via `OutgoingMessages` → 2 envelopes per queue) | M8-S3c retro | Follow-up — pick one canonical publish path |
| Deterministic regression test for the saga lost-update race | M8-S3c retro | Future testing session |
| Dev-only StrictMode SignalR console artifact (one benign negotiation-stop log per page load) | M8-S3c browser smoke | Cosmetic; whenever convenient |
| `remainingCredit` display on the bidder settlement-outcome view (lived notification omits it) | M8-S4 retro | Requires backend change (credit read endpoint or notification enrichment) |
| Bidder display-name header (no anonymous read endpoint surfaces the generated name) | M8-S2 retro | Backend read path, future slice |
| `LiveActivity` non-`bidPlaced` dedupe identity is `kind+occurredAt+text` (lossy but benign for a transient ticker) | Bug #2 verification pass | Revisit if a non-bid feed class gains volume |
| Transient push-fed ops *feed* surface would need bounded seen-set dedupe (at-least-once duplicates observed live) | M8-S6b retro | If/when a ticker-style ops surface ships |
| `OperationsHub` staff-group targeting (currently `Clients.All`) | M7-S1 fork #4 / ADR-024 item 6 | Post-MVP Relay edit |
| `marten-projections` skill: non-monotone state-machine guard section | M7-S4 | Future skill-update session |
| `wolverine-http-auth` skill codifying default-scheme trap / hub-path credential patterns | M7-S6 gap | Future skill-authoring session |
| Marten event-type alias / upgrade pass for prior namespace promotions | M5 | First-real-deploy retrospective |

### Decided, not deferred (recorded dispositions)

| Item | Disposition |
|---|---|
| Mash-bidding UX (each click places a real +$1 bid via prefill auto-advance) | **Working as intended** (Erik, 2026-06-09). |
| M8-S3a "DCB retry policy" deferral | **Resolved differently:** bus path had `AuctionsConcurrencyRetryPolicies`; HTTP commit-time conflicts map to 409 via `ConcurrencyConflictMiddleware`. |
| Ops-feed completion vs polling stopgap | **Sanctioned and shipped at M8-S6b** (two independent evaluations; `docs/research/ops-feed-completion-evaluation-comparison.md`). |
| `BidPlacedOperations`/`ListingSoldOperations` suffixed wire values | **Stay as-is** — renaming wire values is churn (S6b). |

### Post-MVP (explicitly out of scope)

| Item | Source |
|---|---|
| `Refund` settlement-reversal / compensation mechanics on `DisputeResolved`; `Refund`/`Closed` dispute controls | M6/M7 non-goals (re-affirmed S6b/S7) |
| Buyer "report a problem" form; notification-history expansion for ops-feed publications | M8-S6/S6b retros |
| Real carrier-tracking webhook (`ProvideTracking` is an in-process stub) | M6 non-goals |
| Email / SMS / push delivery (Relay is SignalR-only; stubs log) | M6 non-goals |
| Per-user staff identity, roles, external IdP (swap behind unchanged `StaffOnly` policy) | ADR-024 revisit trigger |
| `DemoResetInitiated` cascade (MVP demo reset = Docker volume removal) | M7 non-goals |
| Operations runbook / SRE docs (P-001) · Demo-script runbook (P-002) | `decisions/PARKED.md` |
| Transport swap demo (RabbitMQ → Azure Service Bus) | ADR 002 |

---

## 4. Current Risks

| # | Risk | Severity | Notes / Mitigation |
|---|---|---|---|
| 1 | **Two sagas still have unenforced optimistic concurrency** (`SettlementSaga`, `PostSaleCoordinationSaga`). | Medium | Follow-up ships `IRevisioned` + matching retry policies together. |
| 2 | **Wolverine upstream defects unfixed** (Separated single-saga fan-out suppression; `FirstOrDefault` sticky-sibling starvation) — CritterBids is insulated by ADR 027, but new saga continue-handlers on shared events remain a sharp edge. Engine has since moved 6.5.1 → 6.8.0; the handoff's repro claims need re-verification before filing. | Medium | `wolverine-sagas` skill rule + ADR 027 bindings + audit discipline; upstream handoff ready. |
| 2b | **New-consumer drift under sticky bindings** (consumer without a binding, or route without a consumer). | Low-Medium | Convention in the S3c retro + handler docstrings; topology test now guards the Relay-ops side (S6b). |
| 3 | **Push-refetch can lose the race to the projection** for the last event of a burst — the UI shows stale data until the next push (S7 e2e observed it once on the close-move; the boards' re-query idempotency absorbs it everywhere else today). | Low | Recorded with a frontend-hardening candidate (delayed re-invalidate); becomes Medium if a no-further-push surface starts depending on burst-final events. |
| 4 | **Occasional Testcontainers flakes** in Auctions and Settlement suites. Pass on rerun. | Low | Inherent startup variance. |
| 5 | **Doc nit:** the M7-S4 retro header says `**Date:** 2025-02-14` (should be ~2026-05-31). | Trivial | Fix in a docs PR. |

---

## 5. Key Numbers (at this snapshot)

- **Tests:** **307 backend** passing — Auctions 77, Api 46, Relay 45, Operations 38, Selling 36,
  Settlement 25, Listings 20, Obligations 13, Participants 6, Contracts 1 — plus **72 frontend
  Vitest** (`@critterbids/ops` 47, `@critterbids/bidder` 25) and **1 Playwright e2e**
  (local-only by recorded decision)
- **CI:** backend matrix (8 BC suites + Api) + unit tests + a `frontend` job covering **both**
  SPA builds + Vitest suites + the e2e type-check; single `CI` aggregator as the required check
- **Ops feed:** 22 push eventTypes, all cache-bridge-mapped; `OperationsFeedTopologyTests`
  enforces Operations-consumed ⇒ ops-pushed; polling stopgap deleted (S6b)
- **Sticky bindings (ADR 027):** per-BC sticky consumption across 6 BCs; 23 RabbitMQ listeners;
  exactly-once consumption per queue (S3c) — S6b added 2 publish routes to existing queues
- **ADRs:** 27 authored (next unreserved: **028**); 013/025/026/027 accepted in M8
- **Workspaces:** 8 backend BCs in `src/`; 3 npm workspace members in `client/`
  (`bidder`, `ops`, `e2e`); `shared/` planned (M9)
- **PRs this M8 stretch:** #80, #83–#86, #88–#100 + the Aspire SPA-orchestration commit
  (`7b31147`) + the S7 close PR — all merged or in flight
- **Satellite:** JasperFx/ai-skills#71 open (private); Wolverine upstream handoff ready
  (re-verify against 6.8.0)
- **OpenSpec adoption:** Obligations ✅ adopted · Relay ❌ declined · Operations ❌ declined

---

## 6. Where to Look (reference map for agents)

| Question | Read |
|---|---|
| What are the conventions / hard rules? | `CLAUDE.md` (root) |
| What happened in the last session? | Newest file in `docs/retrospectives/` (canonical state) |
| What did M8 ship, end to end? | The M8 milestone retrospective in `docs/retrospectives/` |
| How do I run the e2e? | `client/e2e/README.md` (requires the live Aspire stack) |
| What happened with Bug #2? | `docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md` + `docs/research/README.md` |
| A message was produced but a consumer never saw it? | `docs/skills/message-flow-diagnosis/SKILL.md` |
| Why is the architecture this way? | `docs/decisions/README.md` (ADR index) |
| What was deliberately deferred? | `docs/decisions/PARKED.md` + §3 of this file |
| How do I implement pattern X? | `docs/skills/README.md` (skill index) |
| How do sessions run? | `docs/prompts/README.md` + `docs/retrospectives/README.md` |
| BC boundaries and integration topology? | `docs/vision/bounded-contexts.md` |
| Event vocabulary? | `docs/vision/domain-events.md` |

---

## Document History

- **v0.6** (2026-06-12): Regenerated at M8 close (M8-S7). M8 flipped to ✅ Complete across the
  ladder and slice tables (S5 #95, S6 #98, S6b #100, S7 rows added); headline rewritten to the
  milestone-close posture (both SPAs shipped, ops feed complete and push-only, bid-war e2e green
  twice, full-workspace frontend CI). Engine baseline 6.8.0 / Aspire 13.4.3 with both SPAs as
  Aspire children. Deferred ledger re-derived: added the Listings `ExtendedBiddingTriggered`
  handler gap (S7 e2e finding), the `client/shared/` → M9 decision, the Playwright-in-CI
  deferral, and the push-refetch race; S4's live-smoke item resolved into the e2e with the
  Sold-vs-Settled distinction carried. "What's next" pivots to the M9 scoping session
  (milestone-doc precondition gate). Key numbers: 307 backend / 72 Vitest / 1 e2e.
- **v0.5** (2026-06-10): Regenerated at M8-S4 merge. Bidder app narrative arc complete (S1–S4
  done); slice table flips S3c and S4 to done, adds the M8-S7 row; "What's next" pivots to
  M8-S5 (ops dashboard shell); deferred ledger gains `remainingCredit` display gap and S4 live
  smoke test; frontend test count updated (17 → 25); PR ledger updated (S3c #93, S4 #94 merged).
- **v0.4** (2026-06-09): Regenerated at M8-S3c close (ADR 027 implementation). Slice table flips
  S3c to done; headline records exactly-once delivery, the two new self-consumption queues, the
  first live BIN/withdrawal verification, and the two masked races found-and-fixed (saga
  `IRevisioned` enforcement, order-tolerant catalog settle). Deferred ledger: two items closed,
  four added (Settlement/PostSale `IRevisioned` pairing, settlement double-publish, race
  regression test, StrictMode console artifact). Risks re-derived: N-copies risk retired,
  saga-concurrency gap and sticky-drift convention added; upstream handoff extended with the
  `FirstOrDefault` sticky-sibling starvation finding.
- **v0.3** (2026-06-09): Regenerated after the M8 Bug #2 fix-and-follow-ups stretch (PRs #88–#91 +
  the follow-ups branch). M8 ladder/slice tables added (S1–S3b done, S3c queued); Bug #2 outcome,
  ADR 027, the 409 concurrency middleware, research-folder consolidation, and the satellite
  JasperFx work items recorded; risks and key numbers re-derived (298 backend tests; engine
  baseline corrected to Wolverine 6.5.1 / Marten 9.6.0).
- **v0.2** (2026-06-03): Regenerated at M7 close (all 7 slices complete, 281 tests). Derived
  from the M7-S7 retro, M7 milestone retro, M7 milestone doc, ADR index, PARKED.md, and the
  CI workflow. Milestone ladder, risks, key numbers, and deferred items fully updated.
- **v0.1** (2026-05-31): Authored at the Copilot → Claude Code tooling transition, immediately
  after M7-S4 merged (PR #70). Derived from the M7-S4 retro, M6 milestone retro, M7 milestone
  doc, ADR index, PARKED.md, and the CI workflow. First snapshot of its kind; regeneration rule
  recorded in the header.
