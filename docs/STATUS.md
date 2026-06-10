# CritterBids — Project Status Snapshot

**As of:** 2026-06-09 · `main` @ `bd71978` (post-PR #91; M8 in progress, Bug #2 fixed)
**Derived from:** [`retrospectives/M8-S3b-bidder-live-bidding-retrospective.md`](./retrospectives/M8-S3b-bidder-live-bidding-retrospective.md) (latest session retro), the M8 Bug #2 fix-and-follow-ups work (PRs #88–#91, executed interactively outside the prompt→retro loop — see [`research/jasperfx-escalation-bidplaced-cross-bc-delivery.md`](./research/jasperfx-escalation-bidplaced-cross-bc-delivery.md) and [`research/README.md`](./research/README.md)), [`milestones/M8-frontend-spas.md`](./milestones/M8-frontend-spas.md), [`decisions/README.md`](./decisions/README.md), `.github/workflows/ci.yml`

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

CritterBids is mid-**M8 (React frontend SPAs)**. The backend MVP (M1–M7, all 8 BCs) is complete;
M8 slices S1–S3b have shipped, and the long-open **Bug #2 (HTTP bids never reaching
Listings/Relay/Operations) is root-caused and FIXED** — the full live-bidding loop now works
end-to-end cross-client in a real browser (own bid holds after re-query; a second bidder's outbid
arrives via SignalR push with no reload).

- **Build state at last close:** 0 errors / 0 warnings; **298 tests, all green** (full
  `dotnet test CritterBids.slnx`).
- **Engine baseline:** Wolverine **6.5.1** / Marten **9.6.0** / JasperFx.Events **2.8.2** /
  .NET 10 / Aspire 13.2+ (upgraded across M8; STATUS v0.2's 6.2.2 line is historical).
- **Frontend:** `client/bidder/` (Vite + React SPA, ADR 012/013/025) is live against the API with
  the ADR 026 SignalR cache-bridge pattern. `client/ops/` is planned (M8-S5/S6).
- **Bug #2 outcome (the headline since v0.2):** the defect was a Wolverine ≤6.5.x consume-side
  dispatch gap (Separated-mode single-saga chains suppress the sticky-handler fan-out). Fixed
  app-side with the `AuctionClosingDispatchHandler` dispatcher bridge (PR #90); lessons codified
  as skills (PR #91, incl. the new `message-flow-diagnosis` skill); upstream Wolverine fix
  hand-off authored; ai-skills corrections PR'd (private repo, JasperFx/ai-skills#71).

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
| **M8** | **React frontend SPAs (bidder + ops dashboard)** | ⏳ **In progress** (S1–S3b done; S3c queued; S4–S6 remaining) |

### M8 slice progress

| Slice | Scope | Status |
|---|---|---|
| M8-S1 | Foundation: ADR 013 accepted, ADR 025 (monorepo layout), `client/bidder/` BiddingHub proof | ✅ Done |
| M8-S2 | Bidder SPA shell + catalog + anonymous session | ✅ Done (PR #80) |
| M8-S3a | Backend precursor: `POST /api/auctions/bids` over the existing PlaceBid DCB command | ✅ Done |
| M8-S3b | Bidder live bidding + ADR 026 (SignalR integration pattern) | ✅ Done (PR #86) |
| *(interlude)* | **Bug #2 root cause + fix** (dispatcher bridge, SPA session/feed fixes, skills, ADR 027 authored, 409 concurrency middleware, doc consolidation) | ✅ Done (PRs #88–#91 + follow-ups branch) |
| M8-S3c | **ADR 027 implementation**: per-BC sticky queue bindings + `auctions-auctions-events`; kills the N-copies fan-out + Bug #3-class dead-letter noise; live-verifies BIN/withdrawal flows | ⏳ Next (prompt authored) |
| M8-S4 | Bidder settlement outcome (won/charged confirmation; narrative 001 Moment 8 + narrative 002) | ⏳ Planned |
| M8-S5 | Ops SPA shell + staff auth + `OperationsHub` credential dance | ⏳ Planned |
| M8-S6 | Ops dashboard views (lot board, bid activity, settlement queue, obligations, sessions) | ⏳ Planned |

---

## 2. What's Up Next?

### Immediate: M8-S3c — ADR 027 sticky queue bindings (prompt: `docs/prompts/implementations/M8-S3c-adr027-sticky-queue-bindings.md`)

Then M8-S4 → S6 per the milestone doc. Satellite work running alongside (not session-gated):

- **Wolverine upstream fix** — agent work order ready at
  [`research/wolverine-upstream-saga-sticky-separation-handoff.md`](./research/wolverine-upstream-saga-sticky-separation-handoff.md)
  (launch in `C:\Code\JasperFx\wolverine`; cites prior art #3041/#3042). Includes the
  `describe-routing --explain` NRE as a separate small issue.
- **ai-skills PR #71** (private repo) — Separated sharp-edge + forwarding-API + diagnostics
  corrections; with Jeremy for review.

Key M8 concerns carried forward: render-time `Title` join (ops views return `ListingId` only);
Relay push = re-query signal (now *proven* in the bidder app, to be repeated in the ops app);
staff auth header/query-string dance for the ops SPA (ADR 024).

---

## 3. Deferred / Delayed Ledger

### Deferred with a target

| Item | Deferred at | Target |
|---|---|---|
| `BuyItNowPurchased` / `ListingWithdrawn` cross-BC delivery live-verification (predicted fixed by the bridge; never observed live) | Bug #2 verification pass | M8-S3c live-verification step |
| Bug #3-class saga-start dead-letter noise (`BiddingOpened`/`ListingSold`/`SettlementCompleted` start races under N-copies fan-out) | M8-S3b findings note | Eliminated by ADR 027 → M8-S3c |
| `LiveActivity` non-`bidPlaced` dedupe identity is `kind+occurredAt+text` (theoretically lossy, benign for a transient ticker) | Bug #2 verification pass | Revisit if a non-bid feed entry class gains volume |
| `OperationsHub` staff-group targeting (currently `Clients.All`) | M7-S1 fork #4 / ADR-024 item 6 | Post-MVP Relay edit |
| Render-time `Title` join (lot board / obligations view show `ListingId` only) | M7-S3/S4 | M8-S6 (frontend render concern) |
| `marten-projections` skill: non-monotone state-machine guard section | M7-S4 | Future skill-update session |
| `wolverine-http-auth` skill codifying default-scheme trap / hub-path credential patterns | M7-S6 gap | Future skill-authoring session |
| Marten event-type alias / upgrade pass for prior namespace promotions | M5 | First-real-deploy retrospective |

### Decided, not deferred (recorded dispositions)

| Item | Disposition |
|---|---|
| Mash-bidding UX (each click places a real +$1 bid via prefill auto-advance) | **Working as intended** (Erik, 2026-06-09) — one click = one bid is correct auction behavior; no debounce/confirm. |
| M8-S3a "DCB retry policy" deferral | **Resolved differently:** bus path already had `AuctionsConcurrencyRetryPolicies`; HTTP chains don't consume Wolverine failure rules at 6.5.1, so commit-time conflicts map to 409 via `ConcurrencyConflictMiddleware` (this session). Also flagged: the DCB blog's per-endpoint `Configure` retry guidance doesn't apply to HTTP chains at this version — follow up with Babu. |

### Post-MVP (explicitly out of scope)

| Item | Source |
|---|---|
| `Refund` settlement-reversal / compensation mechanics on `DisputeResolved` | M6/M7 non-goals |
| Real carrier-tracking webhook (`ProvideTracking` is an in-process stub) | M6 non-goals |
| Email / SMS / push delivery (Relay is SignalR-only; stubs log) | M6 non-goals |
| Per-user staff identity, roles, external IdP (swap behind unchanged `StaffOnly` policy) | ADR-024 revisit trigger |
| `DemoResetInitiated` cascade (MVP demo reset = Docker volume removal) | M7 non-goals |
| Long tail of "all significant events → Operations" beyond the M7-enumerated set | M7 §2 scope ceiling |
| Operations runbook / SRE docs (P-001) · Demo-script runbook (P-002) | `decisions/PARKED.md` |
| Transport swap demo (RabbitMQ → Azure Service Bus) | ADR 002 |

---

## 4. Current Risks

| # | Risk | Severity | Notes / Mitigation |
|---|---|---|---|
| 1 | **N-copies fan-out remains live until M8-S3c lands** — every multi-queue event still executes each consumer once per consuming queue, and saga-start dead letters keep accumulating in dev. | Medium (known, bounded) | ADR 027 accepted; M8-S3c prompt authored; idempotency guards + client dedupe absorb it meanwhile. |
| 2 | **The Wolverine single-saga fan-out defect ships in upstream 6.6.0 unless the fix lands** — CritterBids is immune post-bridge, but any new saga continue-handler on a shared event would silently regress. | Medium | Guard rails: `wolverine-sagas` skill rule + ADR 027 bindings make the shape impossible to reach accidentally; upstream handoff ready to execute. |
| 3 | **Eventual-consistency contract for the ops app** (push = re-query) is proven in the bidder app but not yet exercised against `OperationsHub`. | Low | ADR 026 pattern is reusable as-is; M8-S5 repeats the dance with staff auth. |
| 4 | **Occasional Testcontainers flakes** in Auctions and Settlement suites (container-startup timing). Pass on rerun. | Low | Inherent to Testcontainers startup variance. |
| 5 | **Doc nit:** the M7-S4 retro header says `**Date:** 2025-02-14` — wrong year/date (should be ~2026-05-31). | Trivial | Fix in the next docs PR. |

---

## 5. Key Numbers (at this snapshot)

- **Tests:** 298 passing — Auctions 77, Api 46, Operations 38, Selling 36, Relay 36,
  Settlement 25, Listings 20, Obligations 13, Participants 6, Contracts 1 — plus 17 frontend
  (Vitest) in `client/bidder/`
- **ADRs:** 27 authored (next unreserved: **028**); 003/008/010 superseded; 013/025/026/027 accepted in M8
- **BCs in `src/`:** 8 of 8 MVP backend BCs — all active; `client/bidder/` is the first frontend surface
- **PRs this M8 stretch:** #80 (S2), #86 (S3b), #88 (seed endpoint + findings), #89 (DCB research + escalation), #90 (**Bug #2 fix**), #91 (skills) — all merged
- **Satellite:** JasperFx/ai-skills#71 open (private); Wolverine upstream fix not yet filed (handoff ready)
- **OpenSpec adoption:** Obligations ✅ adopted · Relay ❌ declined · Operations ❌ declined

---

## 6. Where to Look (reference map for agents)

| Question | Read |
|---|---|
| What are the conventions / hard rules? | `CLAUDE.md` (root) |
| What happened in the last session? | Newest file in `docs/retrospectives/` (canonical state) |
| What is the current milestone's scope? | `docs/milestones/M8-frontend-spas.md` |
| What happened with Bug #2? | `docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md` (root cause) + `docs/research/README.md` (index) |
| A message was produced but a consumer never saw it? | `docs/skills/message-flow-diagnosis/SKILL.md` |
| Why is the architecture this way? | `docs/decisions/README.md` (ADR index) |
| What was deliberately deferred? | `docs/decisions/PARKED.md` + §3 of this file |
| How do I implement pattern X? | `docs/skills/README.md` (skill index) |
| How do sessions run? | `docs/prompts/README.md` + `docs/retrospectives/README.md` |
| BC boundaries and integration topology? | `docs/vision/bounded-contexts.md` |
| Event vocabulary? | `docs/vision/domain-events.md` |

---

## Document History

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
