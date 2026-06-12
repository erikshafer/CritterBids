# M8 — React Frontend SPAs — Milestone Retrospective

**Date:** 2026-06-12
**Milestone:** M8 — React Frontend SPAs
**Sessions:** S1 → S7, with two sanctioned mid-flight inserts (S3a/S3b split → S3c; S6b) and the Bug #2 interlude — 11 PR-producing sessions
**Agents:** Claude Code throughout

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M8-frontend-spas.md` §1 (ticked there with the same annotations):

| Exit criterion | Status |
|---|---|
| ADR 013 (frontend core stack) accepted | ✅ S1 — accepted against the 2026 ecosystem; five Deferred Questions stayed deferred and were resolved in their owning slices (routing S2, ADR 026 S3b) |
| SPA monorepo-layout ADR authored and accepted | ✅ S1 — [ADR-025](../decisions/025-spa-monorepo-layout.md): npm-workspaces monorepo at `client/`, host-served static output, Vite dev proxy (no CORS, no API-host change) |
| Bidder-facing SPA renders catalog/detail and drives live bidding end-to-end for narrative 001's spine | ✅ S2/S3b/S4 — **annotation:** "outbid"/"extended bidding" are client-side derivations from view transitions (no server `Outbid` push); the extended-bidding *banner* is structurally unreachable from the lived read model (S7 finding, carried forward) — the extension itself works and the gavel falls at the extended close |
| Operations dashboard SPA: StaffToken auth, `OperationsHub`, six operator views, re-query-on-push | ✅ S5/S6 — completed to a push-only feed at S6b (22-value vocabulary, polling stopgap deleted, topology invariant in CI) |
| SignalR integration pattern recorded (ADR 026); both SPAs follow it | ✅ S3b — Provider + `useListen` + TanStack Query cache bridge |
| Real-time client conventions honored | ✅ — **annotation:** the criterion's "`access_token` on the negotiate" wording was overtaken by the lived SignalR 7+ transport; the lived shape is `skipNegotiation` + WebSockets with the token on the WS upgrade (S5) |
| Render-time `Title` join | ✅ S6 — dashboard resolves titles from `/api/listings/{id}` |
| PWA posture resolved per accepted ADR 013 | ✅ — both apps wire `vite-plugin-pwa` (manifest + SW); offline-*data* scope stays ADR-013-deferred |
| Clean-checkout `npm install` + `npm run build`, TS strict, no .NET breakage | ✅ — both apps + the e2e member; strict base config shared via `tsconfig.base.json` |
| .NET baseline unchanged — 0 errors / 0 warnings, "281 tests" | ✅ with the honest annotation: **307 at close** — grown, not broken, by the three sanctioned backend exceptions (S3a, S3c, S6b) + the Bug #2 interlude; zero unsanctioned backend changes across the milestone |
| Playwright multi-context two-bidder bid-war e2e against a running host | ✅ S7 — `client/e2e/tests/bid-war.spec.ts`, two consecutive green runs against the live Aspire stack |
| CI frontend build/test job | ✅ — bidder job mid-milestone; extended to the full workspace (ops build+test, e2e type-check) at S7; Playwright-in-CI **deferred by recorded decision** |
| `CLAUDE.md` frontend pointer | ✅ S1, refreshed S7 (ops live, `client/e2e/` added, `client/shared/` deferral recorded) |
| All slice retros + this milestone retrospective | ✅ — eleven session retros + this document |

All criteria honored; the three annotations above are recorded divergences, not silent edits.

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 (PR #78) | ADR 013 acceptance + ADR 025 + `BiddingHub` connection proof at `client/bidder/` | ✅ | Dev proxy made the pre-authorized "dev-only CORS allowance" unnecessary |
| S2 (PR #80) | Bidder shell + catalog + anonymous session; routing question resolved (TanStack Router) | ✅ | Wolverine.HTTP empty-record POST needs a `"{}"` body — found only by live smoke |
| S3a (PR #84) | **Sanctioned backend exception #1:** `POST /api/auctions/bids` over the existing `PlaceBid` DCB command | ✅ | Server-side credit-ceiling sourcing; milestone v0.2 amendment (S3 split) |
| S3b (PR #86) | Live bidding + ADR 026 | ✅ | "Outbid" reshaped to a client-side derivation — the lived Relay surface has no targeted `Outbid` push |
| *(interlude)* (PRs #88–#92) | **Bug #2**: cross-BC `BidPlaced` delivery failure → Wolverine Separated single-saga dispatch gap; dispatcher-bridge fix; ADR 027 authored; 409 middleware | ✅ | Engine-level root cause; upstream handoff authored; skills codified (PR #91) |
| S3c (PR #93) | **Sanctioned backend exception #2:** ADR 027 per-BC sticky queue bindings — exactly-once consumption | ✅ | Two masked races found and fixed (saga `IRevisioned`, order-tolerant catalog settle) |
| S4 (PR #94) | Bidder settlement outcome (confirmed charge) — closes the bidder narrative arc | ✅ | `remainingCredit` gap recorded (lived notification omits it) |
| S5 (PR #95) | Ops shell + staff auth gate + `OperationsHub` connection | ✅ | SignalR 7+ token-transport discovery → `skipNegotiation` + WS; HTTP-probe auth gate (WS 401 is an opaque 1006) |
| S6 (PR #98) | Six operator boards + ops parse surface + cache bridge + Title join | ✅ | Push-vocabulary gaps bridged by a declared polling stopgap; two open decisions recorded |
| S6b (PR #100) | **Sanctioned backend exception #3 + scope addition:** ops-feed completion (8 events, topology invariant, stopgap deleted) + dispute-resolution control | ✅ | Smoke found the numeric-enum wire defect (fixed in `Program.cs`); Node-WS credential caveat |
| S7 (this PR) | Playwright bid-war e2e + full-workspace CI + doc refresh + retros | ✅ | E2e's first run falsified the extended-bidding banner (Listings has no `ExtendedBiddingTriggered` handler) — escalated, not fixed |

The milestone ladder grew from seven planned slices to eleven sessions, every insert through the
recorded amendment path (milestone Document History v0.2/v0.4/v0.5) — none silent.

---

## Test Count at M8 Close

| Project | Count | Δ from M7 close | M8 contributions |
|---|---|---|---|
| `CritterBids.Auctions.Tests` | 77 | +12 | S3a endpoint contract/rejections; S3c sticky bindings + race fixes |
| `CritterBids.Api.Tests` | 46 | +5 | S3a HTTP surface; interlude 409 mapping |
| `CritterBids.Relay.Tests` | 45 | +9 | S6b topology invariant + per-event ops-push tests |
| `CritterBids.Operations.Tests` | 38 | — | — |
| `CritterBids.Selling.Tests` | 36 | — | — |
| `CritterBids.Settlement.Tests` | 25 | — | — |
| `CritterBids.Listings.Tests` | 20 | — | — |
| `CritterBids.Obligations.Tests` | 13 | — | — |
| `CritterBids.Participants.Tests` | 6 | — | — |
| `CritterBids.Contracts.Tests` | 1 | — | — |
| **Backend total** | **307** | **+26** | all from the three sanctioned exceptions + the Bug #2 interlude |
| **Frontend (Vitest)** | **72** | +72 | bidder 25 (S1–S4), ops 47 (S5–S6b) — first frontend tests in the repo |
| **E2e (Playwright)** | **1** | +1 | S7 bid-war (local-only by recorded decision) |

---

## Key Decisions Made in M8

| Identifier | Decision |
|---|---|
| ADR-013 (accepted S1) | Frontend core stack: TS strict, Zod at the wire, TanStack Query, Tailwind v4 + shadcn/ui, `@microsoft/signalr`, Vitest + Playwright, PWA from day one. |
| [ADR-025](../decisions/025-spa-monorepo-layout.md) | npm-workspaces monorepo at `client/`; host-served static output (bidder `/`, ops `/ops/`); Vite dev proxy with `ws: true` — no CORS, no API-host change. |
| [ADR-026](../decisions/026-signalr-integration-pattern.md) | One app-wide `SignalRProvider` + `useListen` + TanStack Query cache bridge; **a push is a re-query signal, never authoritative data.** Proven in both apps. |
| [ADR-027](../decisions/027-per-bc-sticky-queue-bindings.md) | Per-BC sticky queue bindings — every broker-fed consumer sticky to its BC's queue; exactly-once consumption per queue. Authored at the Bug #2 follow-ups, implemented at S3c. |
| M8-D1 (S7) | **`client/shared/` extraction deferred to M9.** The apps duplicate the pattern, not the bytes (different hubs, auth, vocabularies, bridges); the seller console is the third consumer that reveals the real shared subset. |
| M8-D2 (S7) | **Playwright e2e stays out of CI.** It needs the live Aspire stack; standing that up in Actions is its own infrastructure piece. CI gets the e2e type-check; the test runs locally pre-merge (`client/e2e/README.md`). |
| M8-D3 (S6b) | Ops-feed completion sanctioned via two independent architectural evaluations converging (`docs/research/ops-feed-completion-evaluation-comparison.md`) — the dual-evaluation method's first use. |

---

## Key Learnings — Cross-Milestone Patterns

### 1. The sanctioned-exception mechanism is what kept "no backend changes" true

M8 made exactly three backend changes in eleven sessions, each escalated to its own slice with
its own prompt before any code moved (S3a, S3c, S6b) — plus one defect interlude handled the
same way. The non-goal survived *because* it had a pressure valve; the alternative is silent
`.cs` touches inside frontend slices. The discipline is now encoded in the
`frontend-slice-discipline` skill (Rule 2).

### 2. Specs describe intent; only the lived surface is bindable

Every slice that read `src/` before writing client code shipped against reality; everything
bound to a narrative or milestone table alone eventually broke: the `Outbid` push that doesn't
exist (S3b), the `remainingCredit` field that isn't sent (S4), the negotiate-token wording the
client library outgrew (S5) — and, found last, the extended-bidding banner bound at S3b to a
Listings upsert that was never implemented (S7). The e2e is what finally audited that one,
which leads to:

### 3. Each verification tier caught a class the tier below could not

Unit tests caught response-handling regressions; the live smoke caught request-contract bugs
(S2's body-less POST, S6b's numeric enums — invisible while every board was empty); only the
**multi-context e2e** caught a derived-UI beat whose triggering view transition the backend
never produces. The ladder is unit → live smoke → e2e, and M8 paid once at each rung to learn
which bugs live there.

### 4. "Push = re-query" is proven — and has one structural blind spot

The ADR 026 contract survived the whole milestone (zero optimistic cache writes, polling
deleted at S6b, the e2e proves cross-context propagation live). The blind spot: the
push-triggered re-query can lose the race to a sibling-queue projection, and the **last event
of a burst** has no later push to self-heal (S7 Finding 2). Recorded with a hardening candidate
(delayed re-invalidate in the cache bridge).

### 5. Defects became infrastructure

Bug #2's root cause (an engine-level dispatch gap) produced ADR 027, two skills, an upstream
handoff, and the exactly-once topology that S6b's invariant test now guards in CI. The S6b
topology test generalizes the move: when an audit finds a gap, encode the *rule* (reflection
over handler signatures), not the list — the rule names future offenders by itself.

### 6. The frontend earned its own pattern library

M8 produced the first two frontend skills (`wolverine-http-frontend-contract`,
`frontend-slice-discipline`) plus the rewritten client `signalr` skill — each rule citing the
slice that paid for it. M9 (seller console) starts with the discipline M8 derived empirically.

---

## ADR Candidate Review

| Finding | ADR warranted? | Rationale |
|---|---|---|
| Frontend stack, layout, SignalR pattern, sticky bindings | **Yes — ADR 013/025/026/027** (all accepted in M8) | Each had weighed alternatives and cross-cutting consequences |
| `client/shared/` deferral | **No** | ADR 025 already records the planned member and the "when duplication becomes real" trigger; S7 supplies the evaluation, recorded here + `CLAUDE.md` |
| Playwright-in-CI deferral | **No** | Operational sequencing, no rejected architecture; comment at the CI job + S7 retro |
| Dual-evaluation decision method (S6b) | **Not yet** | Candidate *skill*, not ADR — one use so far (tracked as an idea) |

**Next unreserved ADR number: 028.**

---

## Technical Debt and Deferred Items (what M8 defers outward)

| Item | Deferred in | Target |
|---|---|---|
| **Seller console** (narratives 004/005/006: publish, watch-close, fulfill-obligation) | Milestone §3 (scoped out up front) | **M9** — needs its milestone-scoping session first (no `docs/milestones/M9-*.md` exists; the precondition gate applies) |
| **`client/shared/` extraction** | S7 (M8-D1) | M9 — extract against the third consumer |
| **Playwright e2e in CI** | S7 (M8-D2) | Re-evaluate with CI infrastructure work |
| **Listings `ExtendedBiddingTriggered` handler** (catalog close never advances; bidder extended-bidding banner + `"Extended"` status unreachable) | S7 e2e finding | Backend slice — first housekeeping candidate alongside M9 |
| Push-refetch vs projection race hardening (cache-bridge delayed re-invalidate) | S7 e2e finding | Frontend hardening; pairs with the row above |
| `remainingCredit` on the settlement outcome view; bidder display-name header | S4 / S2 | Backend read paths, future slices |
| `IRevisioned` + retry policies for `SettlementSaga`/`PostSaleCoordinationSaga`; settlement double-publish; saga-race regression test | S3c | Backend follow-ups |
| Wolverine upstream handoff (Separated single-saga gap + `FirstOrDefault` sticky starvation) — re-verify against 6.8.0 | Interlude / S3c | Satellite (JasperFx) |
| Full ledger | — | `docs/STATUS.md` §3 (v0.6) |

---

## Key Numbers at M8 Close

- **Tests:** 307 backend (+26 from M7's 281) · 72 frontend Vitest · 1 Playwright e2e — all green
- **Sessions:** 11 (7 planned + S3a/S3b split + S3c + S6b inserts + the Bug #2 interlude), every insert via a recorded milestone amendment
- **New ADRs:** 4 accepted (013 flipped, 025, 026, 027); next unreserved **028**
- **Frontend surface:** 2 SPAs + 1 e2e harness in a 3-member npm workspace; both SPAs Aspire-orchestrated dev children (bidder `:5173`, ops `:5174`)
- **Backend changes:** exactly 3 sanctioned exceptions + 1 defect interlude; unsanctioned: **0**
- **Ops feed:** 22 push eventTypes, topology-invariant-enforced, zero polling
- **Engine at close:** Wolverine 6.8.0 / .NET 10 / Aspire 13.4.3
- **CI:** backend matrix (8 BCs + Api) + unit jobs + full-workspace frontend job; one `CI` aggregator
- **Build:** 0 errors, 0 warnings throughout

---

## What M9 Should Know

**At M8 close the MVP demo loop is whole:** a single `dotnet run --project src/CritterBids.AppHost`
starts the full stack and both SPAs; an attendee can scan in, bid, lose, reclaim, and win while
the ops dashboard mirrors the engine live; and the whole spine is machine-verified by the
bid-war e2e (`npm run e2e` from `client/`, host running).

**For M9 (seller console, working name):**

- **Scope the milestone first.** No `docs/milestones/M9-*.md` exists; an M9-S1 prompt hard-gates
  on it. Inputs: narratives 004/005/006, this retro's deferred table, ADR 025 (the `client/shared/`
  plan), and the `frontend-slice-discipline` skill.
- **The seller console is the `client/shared/` trigger.** Evaluate extraction against three real
  consumers; the S7 evaluation (pattern-vs-bytes) is the starting analysis.
- **Seller-side backend surfaces are bus-only today.** The seller submit flow and operator
  attach/start commands have no public HTTP endpoints (the dev seed endpoint orchestrates them
  in-process precisely because of that) — expect S3a-style sanctioned-exception slices to expose
  whatever the console needs; budget them up front in the milestone doc.
- **Carry the verification ladder:** lived backend first, live smoke per slice, and extend the
  e2e — the harness, the seed endpoint pattern, and the hub-assert/unique-title conventions are
  ready to reuse.
- **First housekeeping candidates:** the Listings `ExtendedBiddingTriggered` handler (re-arm the
  banner assert the e2e left marked) and the cache-bridge burst-final hardening.
