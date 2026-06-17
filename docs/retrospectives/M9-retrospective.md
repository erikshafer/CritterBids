# M9 — Seller Console — Milestone Retrospective

**Date:** 2026-06-17
**Milestone:** M9 — Seller Console
**Sessions:** S1 → S8, plus the M9-scoping session and one out-of-plan interlude (PR #111) — 10 PR-producing sessions
**Agents:** Claude Code throughout

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M9-seller-console.md` §1 (ticked there with the same annotations):

| Exit criterion | Status |
|---|---|
| Seller SPA at `client/seller/` rendering the seller journeys (listing management, live auction observation, obligation fulfillment) | ✅ S1 scaffold → S4a/S4b (narrative 004) → S5 (narrative 005) → S6 (narratives 006/007) |
| `client/shared/` extracted, consumed by all three SPAs | ✅ S1 — **annotation:** ADR 025 counted it as the "fourth" member; with `e2e` already landed in M8, `shared` is the **fifth**, leaving a five-member workspace. The extracted surface is the `createSignalRProvider<TMessage>()` factory, shared Zod wire schemas, and the Tailwind theme |
| Seller-side HTTP surface complete (no bus-only seller commands) | ✅ S2/S3 backend precursors — all five gaps from the §2 audit closed (submit, update-draft, my-listings query, obligation-status query, settlement-summary query) |
| Live auction observation on the seller's own listings via `BiddingHub` | ✅ S5 |
| Obligation management — `ObligationStatusView` + `ProvideTracking` | ✅ S6; full lifecycle e2e-verified at S8 |
| Listings `ExtendedBiddingTriggered` handler shipped | ✅ S3 — `CatalogListingView.ScheduledCloseAt` advances on extension (the M8-S7 carry-forward) |
| Cache-bridge burst-final hardening evaluated | ✅ S3 — evaluated, **deferred** with rationale (the fix belongs in the shared cache-bridge surface, which stayed app-local) |
| Clean-checkout `npm install` + `npm run build`, TS strict, no .NET breakage | ✅ — all five workspace members |
| .NET baseline unchanged beyond sanctioned exceptions — 0 errors / 0 net-new warnings | ✅ with the honest annotation: **328 at close** — grown from 307 by the M9-S2/S3/S7 backend slices, not broken; net-new warnings: **0** (held `NU1903` + `CS0108` baseline) |
| Playwright e2e extended with a seller-perspective test | ✅ S8 — `client/e2e/tests/seller-obligation.spec.ts`, two consecutive green runs against the live Aspire stack |
| CI `frontend` job covers the seller app | ✅ — landed at S1, restructured into the current build-test/typecheck matrix at PR #111; verified at S8 |
| `CLAUDE.md` §Frontend updated | ✅ S1, kept current across M9; verified at S8 |
| All slice retros + this milestone retrospective | ✅ — eight session retros + this document |

All criteria honored; the annotations above are recorded divergences, not silent edits.

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| Scoping (PR #103) | M9 milestone-scoping doc (cleared the precondition gate) | ✅ | Eight-slice ceiling planned (later renumbered) |
| S1 (PR #104) | `client/shared/` extraction (factory pattern) + seller SPA scaffold (`:5175`) | ✅ | `shared` is the fifth member, not the "fourth"; `vite-env.d.ts` needed for TS6 CSS side-effect imports |
| S2 (PR #105) | **Backend precursor #1:** seller listing endpoints (submit, update-draft, my-listings query) + `SellerListingSummary` projection | ✅ | OQ-2 resolved — a Selling-side document projection for "my listings" |
| S3 (PR #106) | **Backend precursor #2:** obligation-status + settlement-summary query endpoints; Listings `ExtendedBiddingTriggered` handler; cache-bridge hardening evaluated + deferred | ✅ | Two `SettlementCompleted` handlers coexist on one sticky queue under Separated dispatch |
| S4a (PR #107) | Seller registration + my-listings dashboard | ✅ | One-click `RegisterAsSeller` from the session; 409-tolerant |
| S4b (PR #108) | Seller listing management write ops (edit-draft, submit, withdraw) | ✅ | `shouldUnregister` form bug surfaced (fixed at #111) |
| *(interlude)* (PR #111) | `react-hook-form` `shouldUnregister` fix + CI frontend-matrix restructure | ✅ | Out of the slice plan; folded the matrix into build-test/typecheck per app |
| S5 (PR #109) | Seller live auction observation (`BiddingHub`) | ✅ | Reserve-crossing indicator from client-side draft fields |
| S6 (PR #110) | Seller obligation fulfillment (status view + provide-tracking form) | ✅ | `BidderGroupNotification` is the seller's obligation-push channel; seller Vitest → 117 |
| S7 (PR #112) | `CatalogListingView` cross-queue create race fix | ✅ | OQ-1 live spike **refuted** the planned `UseNumericRevisions`; fix is `Insert`-on-create + `DocumentAlreadyExistsException` retry. Took the "S7" number, renumbering the close to S8 |
| S8 (this PR) | Seller-obligation Playwright e2e + doc refresh + skills audit + retros | ✅ | Found `Obligations:DemoMode` off in the dev host → sanctioned `Obligations__DemoMode=true` AppHost config; seed-then-inject identity bridge |

The milestone ran eight slices plus the scoping session and the #111 interlude — the close
renumbered from the planned "S7" to S8 after the race fix took S7.

---

## Test Count at M9 Close

| Project | Count | Δ from M8 close | M9 contributions |
|---|---|---|---|
| `CritterBids.Auctions.Tests` | 77 | — | — |
| `CritterBids.Api.Tests` | 46 | — | — |
| `CritterBids.Selling.Tests` | 45 | +9 | S2 submit/update-draft endpoints + `SellerListingSummary` |
| `CritterBids.Relay.Tests` | 45 | — | — |
| `CritterBids.Operations.Tests` | 38 | — | — |
| `CritterBids.Settlement.Tests` | 30 | +5 | S3 `SellerSettlementSummary` projection + query |
| `CritterBids.Listings.Tests` | 24 | +4 | S3 `ExtendedBiddingTriggered` handler (+2); S7 race fix (+2) |
| `CritterBids.Obligations.Tests` | 16 | +3 | S3 seller obligation-status query endpoint |
| `CritterBids.Participants.Tests` | 6 | — | — |
| `CritterBids.Contracts.Tests` | 1 | — | — |
| **Backend total** | **328** | **+21** | all from the M9-S2/S3/S7 backend slices |
| **Frontend (Vitest)** | **189** | +117 | seller 117 (S1→S6), ops 47, bidder 25 — the seller app is M9's frontend contribution |
| **E2e (Playwright)** | **2** | +1 | S8 seller-obligation (local-only by recorded decision), joining the M8 bid-war |

---

## Key Decisions Made in M9

| Identifier | Decision |
|---|---|
| M9-D1 (S1) | **`client/shared/` extraction shape.** A `createSignalRProvider<TMessage>()` factory (per-app Context + typed hooks via configuration props), shared Zod wire schemas, and the Tailwind theme CSS. Message parsers and cache-bridge implementations stay app-local — they differ in substance, not configuration. Realizes ADR 025's planned member; operates under ADR 026 unchanged. |
| M9-D2 (S2) | **"My listings" query shape** (OQ-2): a lightweight Selling-side `SellerListingSummary` document projection, not a query over the aggregate stream. |
| M9-D3 (S7) | **`CatalogListingView` cross-queue race fix mechanism** (OQ-1): `Insert`-on-create across all twelve write methods + a `DocumentAlreadyExistsException` retry policy. A live spike **refuted** the planned `UseNumericRevisions` (a plain `Store` under numeric revisions still last-writer-wins, no exception) — numeric revisions only enforce on a revision-checked write path, which a read-model handler's `Store` does not have. |
| M9-D4 (S8) | **`Obligations:DemoMode` enabled on the live demo host** (`Obligations__DemoMode=true` in the AppHost). The documented conference-demo posture (lifecycle in seconds, not days) was un-reachable on the live stack; enabling it makes the full post-sale lifecycle run live and the seller-obligation e2e's terminal reachable. Production binds `DemoMode=false` by default. |
| M9-D5 (S8) | **Seller-console e2e identity bridge = seed-then-inject.** The dev seed creates a registered seller + open listing; the e2e injects that `sellerId` into the console's session storage. No backend change; operator session management stays out of the seller console (milestone §3 non-goal). |

---

## Key Learnings — Cross-Milestone Patterns

### 1. The third consumer is what makes a shared extraction honest

M8 deferred `client/shared/` precisely because two apps "duplicate the pattern, not the bytes."
The seller console — the third consumer — revealed the real shared subset at M9-S1: the SignalR
provider/hook/cache-bridge *factory*, the Zod wire schemas, and the theme. What stayed app-local
(parsers, cache bridges, group management) confirmed the boundary by *not* fitting. Extracting
against three real consumers, not two plus speculation, is what kept the boundary right.

### 2. Backend precursors as their own slices kept "frontend milestone" true again

Like M8, M9 was a frontend milestone with a budgeted backend pressure valve. The seller-facing
HTTP surface was bus-only at M8 close; M9-S2/S3 exposed it over HTTP as sanctioned, recorded
precursor slices *before* the frontend slices that needed it — six endpoints/projections, zero
new domain capability. The discipline (`frontend-slice-discipline` Rule 2) held: every gap took
"render the subset + carry-forward" or "escalate a sanctioned slice", never a silent `.cs` touch
inside a UI slice.

### 3. A lean is a hypothesis; the spike is the truth

The M9-S7 prompt's lean was `UseNumericRevisions`; a live Postgres spike refuted it (a plain
`Store` under numeric revisions still commits last-writer-wins). The fix became `Insert`-on-create
+ a PK-collision retry. The lesson generalizes the M8 "specs describe intent; only the lived
surface binds" rule to *mechanism*: when a concurrency fix's correctness depends on framework
semantics, prove the semantics against a live store before committing the design.

### 4. The lived surface includes the host's runtime configuration

M9-S8's decisive finding wasn't in any `src/` type — it was that `Obligations:DemoMode` was off in
the dev host, so the "demo posture" lifecycle ran in days, not seconds. A live smoke that rides a
timer has to confirm the host is configured for the timing it assumes. Encoded into
`frontend-slice-discipline` Rule 1: env-var/appsettings gaps are as real as missing endpoints.

### 5. The verification ladder's top rung catches its own class — including test bugs

M8 proved unit → live-smoke → e2e each catches a class the rung below cannot. M9-S8 added a
wrinkle: with the *product* correct end-to-end (the hard part — the demo-mode auto-confirm timer
firing live), the e2e's first run caught an over-broad `getByText("Fulfilled")` that a real DOM
exposed and a green build + type-check did not. The top rung still earned its place.

### 6. Read-model concurrency is a recurring shape, not a one-off

The M9-S7 `CatalogListingView` cross-queue race is the same multi-handler-upsert pattern the
Operations BC uses (`DisputeQueueView`, `SellerPerformanceView`). The fix (`Insert`-on-create + PK
retry) is now a template, and the Operations audit is an open carry-forward. When a race is found
in one read model fed by multiple sticky queues, assume its siblings share the shape.

---

## ADR Candidate Review

| Finding | ADR warranted? | Rationale |
|---|---|---|
| `client/shared/` extraction shape (factory) | **No** | ADR 025 already records the planned member; ADR 026 records the SignalR pattern. S1 realizes both; the factory is an implementation of the recorded pattern, documented in the S1 retro |
| `CatalogListingView` race fix (`Insert`-on-create + retry) | **No** | A read-model concurrency fix within the established Wolverine-retry idiom; documented in the M9-S7 retro with the refuted-lean reasoning |
| `Obligations:DemoMode` on the demo host | **No** | A dev-host config decision (the documented demo posture), not a rejected architecture; recorded in the M9-S8 retro + STATUS §3 |
| Seed-then-inject e2e identity bridge | **No** | A test-harness technique; recorded in the M9-S8 retro + `client/e2e/README.md` |

**Next unreserved ADR number: 028** (unchanged — M9 authored none).

---

## Technical Debt and Deferred Items (what M9 defers outward)

| Item | Deferred in | Target |
|---|---|---|
| **signalr-skill refresh** (fold in the seller consumer, the `createSignalRProvider` factory, the `BidderGroupNotification` obligation channel) | S8 skills audit | M10 |
| **`FakeHubConnection` shared test-util extraction** (three copies across the SPAs) | S6 / S8 skills audit | M10 housekeeping |
| **`CatalogListingView` update-update race** (residual after the S7 create-race fix) | S7 (OQ-3) | Backend chore (convert merges to `UpdateRevision` only if it surfaces) |
| **Operations BC multi-handler read-model audit** (same shape as the Listings race) | S7 | Backend evaluation |
| **Seller settlement-summary UI** (the `GET /api/settlement/summaries?sellerId=` endpoint shipped at S3b; no console surface renders it) | M9 | Future seller-console slice |
| **Cache-bridge burst-final hardening** (delayed re-invalidate) | S3 evaluation | Bake into `@critterbids/shared` if/when the cache bridge is extracted there |
| **Playwright e2e in CI** | M8-S7 decision, reaffirmed S8 | Re-evaluate with CI infra work |
| `IRevisioned` + retry for `SettlementSaga`/`PostSaleCoordinationSaga`; settlement double-publish | M8-S3c | Backend follow-ups |
| Seller-side dispute UI; Timed-listing seller path; carrier field on the tracking form | M9 non-goals | Post-MVP |
| Full ledger | — | `docs/STATUS.md` §3 (v0.7) |

---

## Key Numbers at M9 Close

- **Tests:** 328 backend (+21 from M8's 307) · 189 frontend Vitest (seller 117 / ops 47 / bidder 25) · 2 Playwright e2e — all green
- **Sessions:** 10 PR-producing (scoping + S1–S8 + the #111 interlude); the close renumbered S7→S8 after the race fix took S7
- **New ADRs:** 0 (M9 operated under ADR 013/025/026/027); next unreserved **028**
- **Frontend surface:** 3 SPAs + `shared` + `e2e` in a five-member npm workspace; three SPA dev servers as Aspire children (bidder `:5173`, ops `:5174`, seller `:5175`)
- **Seller-facing HTTP surface:** complete — submit / update-draft / withdraw / my-listings / obligation-status / settlement-summary / provide-tracking, all public
- **Backend changes:** the M9-S2/S3 endpoint precursors + the S7 race fix + the S8 `DemoMode` host-config; unsanctioned domain changes: **0**
- **Engine at close:** Wolverine 6.8.0 / .NET 10 / Aspire 13.4.3 (carried from M8; no M9 bump)
- **Build:** 0 errors, 0 net-new warnings throughout

---

## What M10 Should Know

**At M9 close all three demo perspectives are whole:** a single `dotnet run --project
src/CritterBids.AppHost` starts the full stack and all three SPAs; a bidder can bid and win, an
operator can watch the engine, and a **seller** can publish, watch their auction, and fulfill the
post-sale obligation — the last now machine-verified by the seller-obligation e2e (and the
obligation lifecycle runs live in demo seconds, since the AppHost enables `DemoMode`).

**For M10 (working name):**

- **Scope the milestone first.** No `docs/milestones/M10-*.md` exists; an M10-S1 prompt hard-gates
  on it. Inputs: this retro's deferred table, the STATUS §3 ledger, and the M9-S8 skills-audit
  carry-forwards.
- **Skills housekeeping is queued.** The signalr-skill refresh (seller + factory + obligation
  channel) and the `FakeHubConnection` shared extraction are recorded; fold them in before or early
  in M10.
- **Read-model concurrency audit.** The Operations BC shares the multi-handler read-model shape the
  M9-S7 fix hardened — an `Insert`-on-create audit there is the open analogue.
- **Two sagas still lack enforced optimistic concurrency** (`SettlementSaga`,
  `PostSaleCoordinationSaga`) — the `IRevisioned` + retry follow-up is still open.
- **Carry the verification ladder + the demo-host posture awareness:** lived backend (including
  host runtime config) first, live smoke per slice, extend the e2e — and remember the e2e is
  local-only, the `e2e` member type-checked but not executed in CI.
