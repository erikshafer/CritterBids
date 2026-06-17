# CritterBids — Project Status Snapshot

**As of:** 2026-06-17 · `main` @ `78443c1` + the M9-S8 close PR (regenerated inside that PR; **M9 complete**)
**Derived from:** [`retrospectives/M9-S8-end-to-end-housekeeping-retrospective.md`](./retrospectives/M9-S8-end-to-end-housekeeping-retrospective.md) + the M9 milestone retrospective (latest session close), [`milestones/M9-seller-console.md`](./milestones/M9-seller-console.md) (✅ Complete), [`decisions/README.md`](./decisions/README.md), `.github/workflows/ci.yml`

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

**M9 (Seller Console) is complete** — the third React SPA ships, and with it the platform's third
perspective on the same engine. The seller console (`client/seller/`, anonymous `BiddingHub`, base
`/seller/`) renders the seller-perspective journeys: registration + listing management (narratives
004), live auction observation (narrative 005), and post-sale obligation fulfillment (narratives
006/007). M9 also realized the `client/shared/` extraction ADR 025 had deferred to the third
consumer, and absorbed the first housekeeping backend slices from M8's deferred ledger.

- **Build state at close:** 0 errors / 0 net-new warnings (the `NU1903` MessagePack advisory and
  the two `CS0108` saga-`Version` hides are the held baseline); **328 backend tests green** (full
  `dotnet test CritterBids.slnx`); **189 frontend Vitest** (`@critterbids/seller` 117,
  `@critterbids/ops` 47, `@critterbids/bidder` 25); **2 Playwright e2e** (local-only by recorded
  decision — the M8 bid-war + the new M9-S8 seller-obligation test, each run twice green against
  the live Aspire stack).
- **Engine baseline:** Wolverine **6.8.0** (Marten via `WolverineFx.Marten`) / .NET 10 / Aspire
  **13.4.3** — carried from M8 close; M9 added no engine bump. A single `dotnet run --project
  src/CritterBids.AppHost` starts Postgres, RabbitMQ, the API host, and **all three SPA dev
  servers** (bidder `:5173`, ops `:5174`, seller `:5175`) as Aspire children. The AppHost now also
  sets `Obligations__DemoMode=true` (M9-S8) so the post-sale lifecycle runs live in demo seconds.
- **Frontend:** `client/` is a **five-member** npm-workspaces monorepo (ADR 025): `shared/`
  (`@critterbids/shared` — the SignalR provider/hook/cache-bridge factory, shared Zod wire schemas,
  and the Tailwind theme, extracted at M9-S1), `bidder/`, `ops/`, `seller/`, and `e2e/` (Playwright).
- **Backend surface:** all seller-facing flows now have public HTTP endpoints (the M9-S2/S3
  precursor slices); the M8-flagged bus-only seller commands are gone. The Listings
  `ExtendedBiddingTriggered` handler shipped (M9-S3), re-arming the extended-bidding banner; and the
  `CatalogListingView` cross-queue create race is fixed (M9-S7, `Insert`-on-create + retry).

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
| M8 | React frontend SPAs (bidder + ops dashboard) + e2e | ✅ Complete (307 backend / 72 Vitest / 1 e2e at close) |
| **M9** | **Seller console (third SPA) + `client/shared/` + housekeeping** | ✅ **Complete** (328 backend / 189 Vitest / 2 e2e at close) |
| M10 | (working name) — **not yet scoped** | 🔭 Next — needs its milestone-scoping session |

### M9 slice ledger (final)

| Slice | Scope | Status |
|---|---|---|
| (scoping) | M9 milestone-scoping doc | ✅ Done (PR #103) |
| M9-S1 | Foundation: `client/shared/` extraction + seller SPA scaffold (`:5175`) | ✅ Done (PR #104) |
| M9-S2 | Backend precursor: seller listing endpoints (submit, update-draft, my-listings query) | ✅ Done (PR #105) |
| M9-S3 | Backend precursor: seller query endpoints (obligation status, settlement summary) + Listings `ExtendedBiddingTriggered` handler + cache-bridge hardening (deferred) | ✅ Done (PR #106) |
| M9-S4a | Seller registration + my-listings dashboard | ✅ Done (PR #107) |
| M9-S4b | Seller listing management write operations (edit-draft, submit, withdraw) | ✅ Done (PR #108) |
| M9-S5 | Seller live auction observation (`BiddingHub`) | ✅ Done (PR #109) |
| M9-S6 | Seller obligation fulfillment (status view + provide-tracking form) | ✅ Done (PR #110) |
| M9-S7 | Listings cross-queue race fix (`CatalogListingView` `Insert`-on-create + retry) | ✅ Done (PR #112) |
| M9-S8 | End-to-end + housekeeping (close): seller-obligation Playwright e2e + doc refresh + skills audit + M9 retro | ✅ Done (this PR) |

> Numbering note: the close was planned as "M9-S7"; the cross-queue race fix took S7 mid-milestone,
> so the close renumbered to S8. (PR #111 — a `react-hook-form` `shouldUnregister` fix + the CI
> frontend-matrix restructure — landed between S6 and S7 outside the slice plan.)

---

## 2. What's Up Next?

### Immediate: scope M10

M9 closes the seller console and the third demo perspective. **No `docs/milestones/M10-*.md` exists
yet** — per the milestone-doc precondition gate, the next step is a milestone-scoping session that
authors it; an M10-S1 implementation prompt would hard-gate on its absence. Candidate inputs: the
M9 milestone retrospective's "what M9 defers outward", the deferred ledger below, and the M9-S8
pre-M10 skills-audit findings (recorded in the M9-S8 retro).

Satellite work running alongside (not session-gated):

- **Wolverine upstream fix** — agent work order at
  [`research/wolverine-upstream-saga-sticky-separation-handoff.md`](./research/wolverine-upstream-saga-sticky-separation-handoff.md)
  (the `FirstOrDefault` sticky-sibling starvation finding + the `describe-routing --explain` NRE).
  Re-verify against the current engine line before filing.
- **ai-skills PR** (private repo) — with Jeremy for review.

---

## 3. Deferred / Delayed Ledger

### Deferred with a target

| Item | Deferred at | Target |
|---|---|---|
| **Cache-bridge burst-final hardening** (a hub push can trigger the re-query before the projection applies; the UI converges only at the next push — structural for the last event of a burst) | M8-S7 → evaluated + deferred M9-S3 | Bake the delayed re-invalidate into the `@critterbids/shared` cache-bridge surface when/if that is extracted (cache bridges stayed app-local at M9-S1) |
| **`CatalogListingView` update-update race** (two handlers both load an *existing* row and `Store`; last-writer-wins on a contended field — never the observed bug, fields largely monotone) | M9-S7 (OQ-3) | Backend chore; convert merge writes to `UpdateRevision` only if it surfaces |
| **Operations BC analogous multi-handler read-model race** (`DisputeQueueView`, `SellerPerformanceView` share the multi-handler upsert pattern the Listings race fix addressed) | M9-S7 | Separate evaluation; pairs with the row above |
| **`FakeHubConnection` shared test-util extraction** (three copies across the three SPAs) | M9-S6 → skills review | Extract to `@critterbids/shared` test utilities (M10 housekeeping) |
| **Seller settlement-summary UI** (the `GET /api/settlement/summaries?sellerId=` endpoint shipped at M9-S3b; no seller-console surface renders it yet) | M9 | Future seller-console slice (was a cross-reference surface, not a core exit criterion) |
| **Playwright e2e in CI** (needs the full Aspire stack in Actions; the `e2e` member is type-checked in CI, not executed) | M8-S7 decision; reaffirmed M9-S8 | Re-evaluate when CI infra work is on the table |
| `IRevisioned` + `ConcurrencyException` retry policies for `SettlementSaga` / `PostSaleCoordinationSaga` | M8-S3c | Follow-up slice/chore |
| `SettlementCompleted`/`PaymentFailed` double-publish (saga appends AND returns via `OutgoingMessages`) | M8-S3c | Follow-up — pick one canonical publish path |
| `remainingCredit` on the bidder settlement-outcome view; bidder display-name header | M8-S4 / S2 | Backend read paths, future slices |
| `OperationsHub` staff-group targeting (currently `Clients.All`) | M7-S1 / ADR-024 item 6 | Post-MVP Relay edit |
| Marten event-type alias / upgrade pass for prior namespace promotions | M5 | First-real-deploy retrospective |

### Decided, not deferred (recorded dispositions)

| Item | Disposition |
|---|---|
| **Obligations `DemoMode` on the live host** | **Enabled at M9-S8** (`Obligations__DemoMode=true` in the AppHost) — the documented conference-demo posture; production binds `DemoMode=false` by default. Makes the full post-sale lifecycle run live (and the M9-S8 e2e's "Completed" terminal reachable). |
| **Seller-console e2e identity bridge** | **Seed-then-inject** (M9-S8): the dev `seed-flash` endpoint creates a registered seller + open listing; the e2e injects that `sellerId` into the console's session storage. No backend change; the operator session-start step stays out of the seller console (milestone §3 non-goal). |
| `client/shared/` extraction (M8-D1 deferral) | **Realized at M9-S1** — factory `createSignalRProvider<TMessage>()` + shared Zod schemas + Tailwind theme; consumed by all three SPAs. |
| Listings `ExtendedBiddingTriggered` handler (M8-S7 finding) | **Shipped at M9-S3** — `CatalogListingView.ScheduledCloseAt` now advances on extension. |
| Mash-bidding UX; M8-S3a "DCB retry policy"; ops-feed completion; suffixed wire values | Recorded in M8 (unchanged). |

### Post-MVP (explicitly out of scope)

| Item | Source |
|---|---|
| Seller-side dispute UI (viewing/responding to an open dispute) | M9 non-goal |
| Timed-listing seller-console path (Flash is the demo story) | M9 non-goal |
| `Refund` settlement-reversal / compensation; `Refund`/`Closed` dispute controls | M6/M7 non-goals |
| Real carrier-tracking webhook (`ProvideTracking` is an in-process stub); carrier field on the tracking form | M6 non-goals / narrative 006 |
| Email / SMS / push delivery (Relay is SignalR-only) | M6 non-goals |
| Per-user staff / seller identity, roles, external IdP | ADR-024 revisit trigger |
| Production static-file serving for the SPAs (Aspire-served in dev) | M8/M9 non-goals |

---

## 4. Current Risks

| # | Risk | Severity | Notes / Mitigation |
|---|---|---|---|
| 1 | **Two sagas still have unenforced optimistic concurrency** (`SettlementSaga`, `PostSaleCoordinationSaga`). | Medium | Follow-up ships `IRevisioned` + matching retry policies together. |
| 2 | **Wolverine upstream defects unfixed** (Separated single-saga fan-out suppression; `FirstOrDefault` sticky-sibling starvation) — CritterBids is insulated by ADR 027, but new saga continue-handlers on shared events remain a sharp edge. | Medium | `wolverine-sagas` skill rule + ADR 027 bindings + audit discipline; upstream handoff ready (re-verify against the current engine line). |
| 3 | **Read-model multi-handler races beyond Listings** — the Operations BC shares the multi-handler upsert pattern the M9-S7 fix hardened; an `Insert`-on-create audit there is open. | Low-Medium | M9-S7 carry-forward; the Listings fix is the template. |
| 4 | **Push-refetch can lose the race to the projection** for the last event of a burst — the UI shows stale data until the next push. | Low | Recorded with a frontend-hardening candidate (delayed re-invalidate in the shared cache bridge); the seller e2e sidesteps it with a read-model readiness gate + reload. |
| 5 | **Occasional Testcontainers flakes** — a full-suite run spins up ~10 Postgres containers near-simultaneously; one BC occasionally loses the startup race. Pass on isolated re-run. | Low | Inherent startup variance; re-run the single project before treating a single full-suite failure as a regression. |

---

## 5. Key Numbers (at this snapshot)

- **Tests:** **328 backend** passing (full `dotnet test CritterBids.slnx`) — plus **189 frontend
  Vitest** (`@critterbids/seller` 117, `@critterbids/ops` 47, `@critterbids/bidder` 25) and **2
  Playwright e2e** (bid-war + seller-obligation; local-only by recorded decision)
- **CI:** backend matrix (8 BC suites + Api + Contracts) + a `frontend` matrix job covering
  `seller`/`bidder`/`ops` (build + Vitest) and `shared`/`e2e` (type-check); single `CI` aggregator
  as the required check. The frontend matrix has covered `seller`+`shared` since M9-S1; PR #111
  restructured it into its current build-test/type-check form.
- **Frontend:** five npm-workspace members (`shared`, `bidder`, `ops`, `seller`, `e2e`); three SPAs
  Aspire-orchestrated dev children (bidder `:5173`, ops `:5174`, seller `:5175`)
- **Seller-facing HTTP surface:** complete — submit / update-draft / withdraw / my-listings query /
  obligation-status query / settlement-summary query / provide-tracking, all public endpoints
- **ADRs:** 27 authored (next unreserved: **028**); M9 added none — it operated under ADR 013/025/026
- **OpenSpec adoption:** Obligations ✅ adopted · Relay ❌ declined · Operations ❌ declined

---

## 6. Where to Look (reference map for agents)

| Question | Read |
|---|---|
| What are the conventions / hard rules? | `CLAUDE.md` (root) |
| What happened in the last session? | Newest file in `docs/retrospectives/` (canonical state) |
| What did M9 ship, end to end? | The M9 milestone retrospective in `docs/retrospectives/` |
| How do I run the e2e? | `client/e2e/README.md` (requires the live Aspire stack) |
| How do I work a frontend slice? | `.claude/skills/frontend-slice-discipline/SKILL.md` |
| Why is the architecture this way? | `docs/decisions/README.md` (ADR index) |
| What was deliberately deferred? | `docs/decisions/PARKED.md` + §3 of this file |
| How do I implement pattern X? | `docs/skills/README.md` (skill index) |
| How do sessions run? | `docs/prompts/README.md` + `docs/retrospectives/README.md` |
| BC boundaries and integration topology? | `docs/vision/bounded-contexts.md` |
| Event vocabulary? | `docs/vision/domain-events.md` |

---

## Document History

- **v0.7** (2026-06-17): Regenerated at M9 close (M9-S8). M9 flipped to ✅ Complete across the
  ladder and a final M9 slice ledger added (scoping #103 → S8). Headline rewritten to the
  seller-console-close posture (three SPAs, five-member `client/` workspace, `client/shared/`
  realized at S1, all seller-facing endpoints public, the `ExtendedBiddingTriggered` handler and
  the `CatalogListingView` create-race fix shipped). Key numbers: 328 backend / 189 Vitest / 2 e2e.
  Deferred ledger re-derived: closed the `client/shared/` extraction and the `ExtendedBiddingTriggered`
  handler (both shipped in M9); added the `CatalogListingView` update-update race (OQ-3), the
  Operations multi-handler read-model audit, the `FakeHubConnection` shared extraction, and the
  seller settlement-summary UI gap; recorded the `Obligations DemoMode` host-config decision and the
  seed-then-inject e2e identity bridge. "What's next" pivots to the M10 scoping session
  (milestone-doc precondition gate).
- **v0.6** (2026-06-12): Regenerated at M8 close (M8-S7). M8 ✅ Complete; both SPAs shipped, ops
  feed complete and push-only, bid-war e2e green twice, full-workspace frontend CI. Engine baseline
  6.8.0 / Aspire 13.4.3. Deferred ledger added the Listings `ExtendedBiddingTriggered` gap, the
  `client/shared/` → M9 decision, the Playwright-in-CI deferral, and the push-refetch race.
- **v0.5** (2026-06-10): Regenerated at M8-S4 merge (bidder narrative arc complete).
- **v0.4** (2026-06-09): Regenerated at M8-S3c close (ADR 027 implementation).
- **v0.3** (2026-06-09): Regenerated after the M8 Bug #2 fix-and-follow-ups stretch.
- **v0.2** (2026-06-03): Regenerated at M7 close (281 tests).
- **v0.1** (2026-05-31): Authored at the Copilot → Claude Code tooling transition.
