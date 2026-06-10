# M8-S6: Ops Dashboard Views — Retrospective

**Date:** 2026-06-10
**Milestone:** M8 — React Frontend SPAs
**Slice:** S6 — Ops Dashboard Views (the six operator data views; ADR 026's second full consumer)
**Agent:** Claude Code
**Prompt:** `docs/prompts/implementations/M8-S6-ops-dashboard-views.md`

## Baseline

- `client/` workspace at S5 close: ops **16 Vitest tests across 3 files**, bidder **25 across 8**; `npm run build` exit 0 for both members.
- Branch from `main` at `b607c9b` (M8-S5 + the two skills PRs merged).
- Ops SPA: auth gate + `createStaffFetch` + `OperationsSignalRProvider` with a `console.debug` `ReceiveMessage` placeholder; six `PlaceholderView` routes; zod deliberately absent from `client/ops/package.json`.
- Backend surface verified live this session: bare `GET /api/operations/lot-board` → **401**; with `X-Staff-Token` → **200 `[]`**; all seven endpoints 200; unknown `/api/listings/{id}` → 404.
- **Narrative 008 exists and is `accepted` (v1.0, M6-S4, PR #50)** — correcting the S5 prompt/retro claim that it was "planned, not yet authored". The S6 prompt anchors to it; no authoring gate applied.

## Items completed

| Item | Description |
|------|-------------|
| S6.1 | `zod@^4.4.3` added to `client/ops`; `parseOperationsFeedMessage` over the homogeneous `OperationsFeedNotification` wire — one schema, `null` on shape mismatch (never a throw), `listingId` normalized to `string \| null` |
| S6.2 | `applyOperationsFeedMessage` cache bridge: the lived 14-value `eventType` vocabulary mapped to board-family invalidations; **unknown eventType → blanket `["operations"]` invalidation** (shape parsed, vocabulary didn't — refresh everything rather than nothing); `ListingRevised` additionally refreshes the `["listing", id]` title entry |
| S6.3 | Provider completion: `ReceiveMessage` body is now parse → bridge → fan-out (ADR 026 ordering); `subscribe` seam on the context (ref'd Set, stable identity); `useListen` hook mirroring the bidder's `hooks.ts` |
| S6.4 | The six boards replace the six placeholders: lot board, bid activity, settlement queue, escalations, disputes, sessions & participants (two boards, one route) — all through `staffFetch`-injected query-options factories under the `["operations", …]` key family, with designed empty/load/error states and the S5 shell's projector-legible styling |
| S6.5 | Render-time Title join: `useListingTitle` over anonymous `GET /api/listings/{id}`, cached per `["listing", id]`, `null` (id fallback, no retry) on 404 — primary on bid activity / settlement / obligations, **null-fallback on the lot board's own `Title`** (prompt finding 3) |
| S6.6 | Vitest 16 → **35 across 7 files**: parse surface (4), bridge mapping incl. unknown default (6), query functions + title join via fake `staffFetch`/`fetchImpl` (6), lot-board render + empty state on a seeded cache (2), provider parse→bridge→fan-out ordering + junk-ignore (1 new, 3 S5 tests adapted to the now-required `QueryClientProvider`) |
| S6.7 | This retrospective + narrative 008 Document History v1.1 row (the spec-delta landing) |

## S6.1/S6.2: The homogeneous wire — why no `kind`, and why unknown eventTypes invalidate everything

The bidder's parse surface exists because its wire is heterogeneous with no discriminator — five records, structural discrimination, a client-assigned `kind`. The ops feed needed none of that: one record, `eventType` carried as a server-named string. `parseOperationsFeedMessage` is therefore a single `safeParse` + null-normalization, and the rest of the app switches on `message.eventType` directly. Inventing a `kind` union over the 14 lived values was rejected: the vocabulary is open (any new Relay handler extends it), and a closed client union would turn every backend addition into a frontend type break.

That openness is also why the bridge's default branch is a **blanket `["operations"]` family invalidation** rather than a no-op: a payload that parsed but names an unknown `eventType` is a real signal from a newer backend; refreshing every board over-fetches slightly, refreshing none silently hides the change the push announced. The shape mismatch case (parse `null`) stays a no-op — that's noise, not signal.

## S6.4: Push-coverage gaps — the slice's load-bearing finding (made at prompt time, not mid-flight)

Reading the Relay handler topology during prompt authoring (frontend-slice-discipline Rule 1, extended to the push surface) found that **the ops feed is a strict subset of what the boards render**:

- Settlement events (`SettlementCompleted`, `SellerPayoutIssued`, `PaymentFailed`) reach `IHubContext<BiddingHub>` only — the **settlement queue board has zero live-push coverage**.
- `DeadlineEscalated`, `TrackingInfoProvided`, `ObligationFulfilled` likewise never reach the `OperationsHub` — **escalation queue arrivals are not pushed** (a card *leaves* live via `DisputeOpened`; it never *arrives* live).
- The Auctions lifecycle events (`BiddingOpened`, `ExtendedBiddingTriggered`, `ListingPassed`, `ListingWithdrawn`, `BuyItNow*`) are bidder-hub-only — the lot board misses the Passed/Withdrawn terminal transitions live (it does get `BidPlacedOperations`, `ListingSoldOperations`, and the Selling events).

Per Rule 2 the slice took move 1: **render the lived subset, record the carry-forward** — a candidate sanctioned backend slice ("Relay ops-feed completion") that adds `OperationsFeedNotification` publications for the missing events. The frontend stopgap (prompt open question 1, recommended resolution taken): a modest `refetchInterval` (20 s) on exactly the two affected queries (settlement queue, escalations), documented in `queries.ts` with the finding reference. This is a trigger choice, not a truth-path change — push = re-query stays intact, and the stopgap is retired by deleting one constant when the backend slice lands. `ListingSoldOperations` additionally invalidates the settlement queue as a proxy signal (the sale is what seeds the settlement row).

## S6.5: The lot board's `Title` — milestone text vs lived view

`LotBoardView` carries a nullable `Title` (set-once from `ListingPublished`), so the milestone §2 sentence "lot board … receives `ListingId` only" is stale on that one view. The board renders `row.title ?? <ListingTitle/>` — own field first, join fallback for pre-publish rows. The other title-less records (`BidActivityEntry`, `SettlementQueueView`, `OperationsObligationsView`) use the join as primary. The join itself: per-row `useQuery` on the shared `["listing", id]` key (TanStack Query deduplicates identical keys across rows — prompt open question 3's recommendation; no bespoke batching), `staleTime` 5 min with `ListingRevised` invalidating explicitly, 404 → `null` → shortened-id fallback, never retried. The milestone-text correction rides S7's doc-refresh housekeeping with the stale CLAUDE.md ops row.

## S6.6: Seeded-cache render tests — no fetch stub at all

Board render tests seed the `QueryClient` (`setQueryData` for the board key and the title-join key) under `staleTime: Infinity`, so no query function ever fires and neither global-fetch stubbing nor module mocks are involved; the request side is covered separately by query-function tests against the injectable `staffFetch`/`fetchImpl` seams. The S5 provider tests needed one mechanical adaptation: the provider now calls `useQueryClient`, so every render gains a `QueryClientProvider` wrapper.

## Live smoke (real Aspire host + real browser)

Per the playbook: Aspire host launched with `$env:OperationsAuth__StaffToken` in the shell (child-project inheritance, no repo change); ops dev server on 5174; Edge via `playwright-core` (`channel: "msedge"`, headless).

| Check | Result |
|---|---|
| HTTP contract: no/wrong token → 401; valid → 200 `[]` on all seven endpoints; unknown listing → 404 | pass |
| Auth gate renders; wrong token → "rejected (401)", not stored | pass |
| Valid token stored; dashboard mounts; pill reaches **Connected** | pass |
| All six routes render their boards, no error state | pass |
| `POST /api/participants/session` (body `"{}"`) → 201 | pass |
| **Push → re-query closes live:** `ParticipantSessionStarted` ops push made the participants board gain the row **without reload** (0 → 1) | pass |

The last row is the slice's acceptance spine proven end-to-end: Relay → `OperationsHub` → parse → bridge → `["operations", "participants"]` invalidation → `staffFetch` re-query → rendered row. Expected console noise, both understood: the wrong-token probe's own 401, and **seven** `Failed to start the HttpConnection before stop() was called` — one per hard `page.goto()` with a held token (the StrictMode dev double-mount artifact scales with the smoke's full page loads; a real operator SPA-navigates and sees one). Harness torn down; host stopped and confirmed unreachable.

## Test results

| Phase | Suite | Result |
|-------|-------|--------|
| After S6.1–S6.5 (build) | `npm run build --workspace @critterbids/ops` | exit 0 (tsc strict + vite build + PWA) |
| After S6.6 | `@critterbids/ops` Vitest | **35/35 green** (7 files) |
| Regression | `@critterbids/bidder` Vitest + build | **25/25 green**, build exit 0 — unchanged |

Frontend totals: 41 → **60** Vitest tests across the workspace. Backend test count untouched (no backend diff).

## Build state at session close

- `npm run build` exit 0 for both workspace members; TypeScript strict, zero errors.
- **No `.cs`, `.csproj`, or `.slnx` file in the diff**; `client/bidder/` source: 0 files changed (lockfile only).
- Grep-able assertions: `PlaceholderView` references: **0** (component deleted); bare `fetch(` against `/api/operations/`: **0** (all board reads through `staffFetch`; the title join's bare fetch targets the anonymous `/api/listings/{id}` deliberately); `connection.invoke(`: **0** (outbound-only hub); `localStorage`: **0**; new dependencies beyond `zod`: **0**; `console.debug` in the provider's message path: only the unrecognized-payload branch.

## Key learnings

1. **Rule 1 applies to the push surface, not just HTTP.** Grepping the Relay handlers for `IHubContext<OperationsHub>` at *prompt-authoring* time found the coverage gaps before any view was designed around a push that never comes — the cheapest possible time to find them.
2. **A homogeneous wire needs no client discriminator.** When the server names the event (`eventType`), parse the shape and switch on the server's string; reserve the bidder-style `kind` union for wires with no discriminator. Corollary: treat the vocabulary as open — unknown values are a refresh-everything signal, not an error.
3. **Polling as a documented stopgap is compatible with push = re-query** when it's framed as a trigger substitution on named queries with the retiring backend slice recorded — the truth path (re-query the read model) never changes.
4. **Seeding the QueryClient beats stubbing fetch for board render tests** — the request side lives in query-function tests against the injectable seam, and the render tests touch no network machinery at all.
5. **The StrictMode connection-noise line scales with hard navigations**: one benign `Failed to start the HttpConnection before stop() was called` per full page load with a mounted provider. Seven gotos → seven lines. Expected with the cleanup present; a bug without it.

## Findings against narrative

Narrative 008 implemented **as drafted** for the surfaces this slice owns: the open-dispute queue card carries exactly Moment 1's named fields (listing title via the join, raiser, reason, opened-at, the "escalated, no tracking on file" history line), arrives live via `DisputeOpened`, and clears live via `DisputeResolved` — both pushes lived in Relay since M6-S4. No drift in the narrative's domain understanding. Two adjacent corrections, both routed:

- The S5-era claim that narrative 008 was unauthored was wrong (handoff correction, confirmed at prompt authoring); the S6 prompt anchors to the accepted narrative. Lane: superseded-claim correction recorded in the prompt's Narrative line — no narrative text was wrong.
- `narrative-update` (resolved in this PR): narrative 008 gains the **v1.1 Document History row** recording the partial `UX-or-UI-detail` landing — queues render; the dispute-resolution controls (Moment 2's action) and the buyer's report-a-problem form remain deferred, and the `DeadlineEscalated` push gap is named there.

## Spec delta — landed?

**Landed as written, all three consequences.** (1) **Narrative 008's operator surfaces render** — the open-dispute and escalation queues are lived UI; `docs/narratives/008-operator-resolves-dispute-with-extension.md` § Document History v1.1 is the matching spec row. (2) **The milestone §1 exit criterion** ("renders the operator read models … with live 're-query on push' refresh") is satisfied to the lived push vocabulary — proven live by the smoke's push → re-query row — with the coverage gap recorded as a spec-visible carry-forward here and in the v1.1 row rather than silently absorbed. (3) **ADR 026 completes its second full consumer**: parse surface + cache bridge + `useListen` against the `OperationsHub`, confirming the pattern needs no amendment for a homogeneous wire (no client `kind`, same Provider ordering) — ADR 026's text required no edit, validating the S5 retro's prediction that S6 wiring against the unchanged provider would close the question.

## Verification checklist

- [x] `zod@^4.4.3` in `client/ops/package.json`; no other new dependency
- [x] `parseOperationsFeedMessage` exists; junk yields `null` (4 tests), never a throw
- [x] Provider `ReceiveMessage` is parse → bridge → fan-out (ordering asserted in-test via invalidation count at listener time); `useListen` exists
- [x] Bridge maps the lived 14-value vocabulary; unknown eventType → blanket `["operations"]` (test-proven)
- [x] Six placeholder routes replaced; `PlaceholderView` deleted, 0 references
- [x] Every operations query through `staffFetch`; bare `fetch` against `/api/operations/*`: 0
- [x] Titles join on bid activity / settlement / escalations / disputes; lot board own-`Title`-first with join fallback; 404 → id fallback, no retry
- [x] `[]` renders designed empty states (live: all seven endpoints returned `[]` and every board rendered its empty state)
- [x] Vitest: parse, bridge incl. unknown default, board render + empty state, `useListen` via the provider fan-out test — 35/35 ops, 25/25 bidder
- [x] `npm run build` exit 0, both members, TS strict
- [x] Live smoke performed and recorded above (boards render; a live push re-queried the participants board without reload)
- [x] No `.cs`/`.csproj`/`.slnx` in the diff; no `client/bidder/` change
- [x] This retrospective, push-coverage carry-forward recorded
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M8-S7 (in milestone scope):** Playwright multi-context two-bidder e2e; CI frontend build/test job (now 60 Vitest tests across two members); doc refreshes — CLAUDE.md ops row ("*planned, M8-S5*" is two slices stale), milestone §2's "lot board receives ListingId only" (stale per S6.5), `bounded-contexts.md`, STATUS.md regeneration; `client/shared/` extraction decision (duplications now: `RECEIVE_MESSAGE`, the shadcn theme/primitives, the parse-at-boundary idiom — schemas themselves diverge per hub and don't extract).
- **Carry-forward (backend, candidate sanctioned slice): Relay ops-feed completion.** Publish `OperationsFeedNotification` for the settlement events (`SettlementCompleted`, `SellerPayoutIssued`, `PaymentFailed`), `DeadlineEscalated`, and the Auctions lifecycle terminals (`ListingPassed`, `ListingWithdrawn`) so the settlement queue and escalation arrivals go push-fed; retire `PUSH_GAP_REFETCH_INTERVAL_MS` in `client/ops/src/operations/queries.ts` when it lands. Until then the two boards poll at 20 s by design.
- **Dispute-resolution controls** (narrative 008 Moment 2's `ResolveDispute` from the dashboard) remain unscoped in M8 — surface as a deliberate scope decision before milestone close if the demo wants Morgan to *act*, not just see.
- **Demo-data smoke breadth:** the live smoke proved the full loop on the participants board (the cheapest journey that exercises push → re-query). The bid-war boards (lot board / bid activity gaining rows live) ride M8-S7's two-bidder e2e, which generates exactly that traffic against a seeded catalog.
