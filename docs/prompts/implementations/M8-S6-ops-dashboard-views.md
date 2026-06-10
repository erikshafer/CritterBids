# M8-S6: Ops Dashboard Views

**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — slice plan §7, row M8-S6
**Slice:** S6 of M8 (the six operator data views; the ops app's parse surface, cache bridge, and `useListen` — ADR 026's second full consumer)
**Narrative:** `docs/narratives/008-operator-resolves-dispute-with-extension.md` (**accepted**, v1.0 since M6-S4 — the operator-vantage spec for the escalation/dispute queues this slice renders. Correction: the M8-S5 prompt and retro called this narrative "planned, not yet authored"; that was wrong — it has existed with `status: accepted` since PR #50. The S5 retro's "S6 should hard-gate on an unauthored narrative" bullet is superseded; the precondition is already satisfied.)
**Agent:** Claude Code
**Estimated scope:** one PR; **frontend-only** (`client/ops/` tree) **plus one doc** (the retro). **No `.cs`, `.csproj`, or `.slnx` file is touched.**

---

## Preconditions

This prompt assumes:

- **M8-S5 has merged** (PR #95) — `client/ops/` exists as the second npm-workspace member: staff auth gate (`sessionStorage` token, validate-before-store probe), `createStaffFetch` (attaches `X-Staff-Token`, funnels 401 → clear + re-gate), `OperationsSignalRProvider` (`skipNegotiation` + WebSockets + `accessTokenFactory`, mounted **inside** the gate), six placeholder routes under TanStack Router `basepath: "/ops"`, dev port 5174, 16 Vitest tests green (bidder: 25).
- **Narrative 008 is accepted** (see the Narrative line above) — Morgan's escalation queue and open-dispute queue are the spec this slice's obligations views render.
- **The backend operations surface is fully wired** (M7): seven `StaffOnly` GETs under `/api/operations/*` returning the six operator view records as `IReadOnlyList<T>` (`[]`, never 404); escalations and disputes are two filtered endpoints over `OperationsObligationsView`.
- **The `OperationsHub` push surface is live**: `OperationsFeedNotification { listingId?, eventType, payload, occurredAt }` — a single **homogeneous** record (unlike the bidder's five-record heterogeneous wire) broadcast `Clients.All` on `ReceiveMessage`. The S5 provider registers the handler with a `console.debug` placeholder body that this slice replaces.
- **The skills exist** (PRs #96/#97): `.claude/skills/frontend-slice-discipline`, `.claude/skills/signalr`, `docs/skills/wolverine-http-frontend-contract/SKILL.md`.

## Lived-backend findings this prompt is anchored to (read before scoping down)

Per frontend-slice-discipline Rule 1, the prompt author read the lived Relay/Operations surfaces. Three findings shape this slice:

1. **The ops-feed `eventType` vocabulary is exactly 14 values** (from the Relay handlers that target `IHubContext<OperationsHub>`): `BidPlacedOperations`, `ListingSoldOperations` (note the `Operations` suffix on these two), `ListingPublished`, `ListingRevised`, `ListingEndedEarly`, `ListingAttachedToSession`, `LotWatchAdded`, `LotWatchRemoved`, `DisputeOpened`, `DisputeResolved` (all with `listingId`), and `SessionCreated`, `SessionStarted`, `ParticipantSessionStarted`, `SellerRegistrationCompleted` (all with `listingId: null`).
2. **Push-coverage gaps.** Settlement events (`SettlementCompleted`, `SellerPayoutIssued`, `PaymentFailed`) and the Obligations events `DeadlineEscalated`, `TrackingInfoProvided`, `ObligationFulfilled` reach the **`BiddingHub` only** — nothing feeds them to the `OperationsHub`. Likewise the Auctions lifecycle events (`BiddingOpened`, `ExtendedBiddingTriggered`, `ListingPassed`, `ListingWithdrawn`, `BuyItNow*`). Consequences: the **settlement queue board has zero live-push coverage**; **escalation arrivals are not pushed** (a card *leaves* the escalation queue live via `DisputeOpened`, but never *arrives* live); the lot board misses some terminal transitions (`Passed`, `Withdrawn`). Per Rule 2 this slice **renders the lived subset and records the gap as a carry-forward** (a candidate sanctioned backend slice: Relay ops-feed completion) — it does not add Relay handlers, and it does not fake live-ness in the client beyond the sanctioned polling stopgap (Open question 1).
3. **`LotBoardView` already carries a nullable `Title`** (set-once from `ListingPublished`). The milestone §2's "lot board … receives `ListingId` only" is stale on that one view. The render-time Title join from `/api/listings/{id}` is therefore the **primary** title source for bid activity, settlement queue, and the two obligations queues (whose records genuinely carry no title), and a **null-fallback** on the lot board.

## Goal

Fill the ops dashboard with its six operator data views over `/api/operations/*` — TanStack Query through the existing `staffFetch`, Zod-parsed at the wire boundary — and complete the ADR 026 pattern for the ops app: the `OperationsFeedNotification` parse surface, the TanStack Query cache bridge keyed off `eventType`, and `useListen`, replacing the S5 `console.debug` placeholder. After S6, the projector view of narrative 008 works: a dispute card lands in Morgan's open-dispute queue live, and the boards render the operator read models with live re-query refresh. S7 (Playwright e2e + housekeeping) then closes the milestone.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M8-frontend-spas.md` | **Authoritative for scope.** §7 row M8-S6; §2 ops-dashboard surfaces; §3 non-goals. |
| `docs/narratives/008-operator-resolves-dispute-with-extension.md` | The operator-vantage spec — Morgan's queues (Moments 1–3) are what the obligations views render. |
| `.claude/skills/frontend-slice-discipline/SKILL.md` | The four working rules (lived-backend-first, carry-forwards, installed-toolchain check, live-smoke playbook). |
| `.claude/skills/signalr/SKILL.md` | ADR 026 pattern, per-hub auth, parse-normalize convention, dedupe rules. |
| `docs/skills/wolverine-http-frontend-contract/SKILL.md` | STJ wire shapes, `[]`-never-404, 404/retry rules, staff-token transport. |
| `src/CritterBids.Operations/` (endpoints + the six view records) | The lived HTTP surface: `OperationsQueryEndpoints.cs`, `LotBoardView`, `BidActivityEntry`, `SettlementQueueView`, `OperationsObligationsView`, `SessionActivityView`, `ParticipantActivityView`, and their status enums. Schemas bind to these, not to the milestone table. |
| `src/CritterBids.Relay/` (ops-feed surface) | `Notifications/OperationsFeedNotification.cs` + the handlers targeting `IHubContext<OperationsHub>` — the lived `eventType` vocabulary the cache bridge maps. |
| `client/bidder/src/signalr/` + `client/bidder/src/catalog/queries.ts` | The ADR 026 reference implementation (messages/cacheBridge/hooks) and the query-function idiom (Zod parse point, `ListingNotFoundError`, no-retry-on-404). |
| `client/ops/src/` | The S5 baseline this slice extends: `SignalRProvider.tsx`, `auth/staffApi.ts`, `auth/StaffAuthContext.tsx`, `router.tsx`. |

## In scope

1. **Zod parse surface for the ops feed.** Add `zod` to `client/ops/package.json` (same major as the bidder). One schema for the homogeneous `OperationsFeedNotification` wire shape; a parse function returning a normalized message or `null` for an unrecognized shape (logged-and-ignored, never thrown — the bidder's forward-compatibility convention). No discriminated union is needed: the wire is one record; `eventType` is a wire-carried string, not a client-assigned `kind`.

2. **The cache bridge.** A pure function over (queryClient, parsed message) translating `eventType` → query-key invalidations per the ADR 026 push-equals-re-query rule. Known `eventType`s invalidate the board families they affect (and the `["listing", id]` title entry where a `listingId` is present and the event implies a title-bearing change, e.g. `ListingRevised`); an **unknown `eventType` value invalidates all operations board keys** — the forward-compatible safe default, since the wire shape still parsed.

3. **Provider completion.** Replace the S5 `console.debug` body with the ADR 026 ordering: parse → cache bridge **first** → subscriber fan-out. Add a `subscribe` seam to the provider context and a `useListen` hook (handler-in-a-ref, registered once), mirroring the bidder's `hooks.ts`. Connection-state surface stays as S5 built it.

4. **The six data views, replacing the six placeholders.** Query functions through `staffFetch` (every staff request keeps the 401-funnel), Zod-parsed, `queryKey`s under an `["operations", …]` family:
   - **Lot board** (`/`): per-listing row — title (own `Title`, join fallback), status, current bid / bid count, hammer price / winner where terminal. Newest-first as served.
   - **Bid activity** (`/bid-activity`): append-style feed of accepted bids — joined title, bidder, amount, proxy flag, placed-at.
   - **Settlement queue** (`/settlement-queue`): settlement rows with status; `Failed` rows visually flagged (the `PaymentFailed` case the milestone names); amounts where present.
   - **Escalations** (`/escalations`) and **Disputes** (`/disputes`): the two `OperationsObligationsView` queues — narrative 008's surfaces. The dispute card carries what Moment 1 names: joined listing title, raiser, reason, opened-at (and the escalated-at history where present).
   - **Sessions & participants** (`/sessions`): both boards on the one existing route — session lineup (title, status, duration, attached-listing count) and participant activity (display name, bidder id, credit ceiling, started-at).
   - Every board renders a **designed empty state** (an empty array is a state, not an error) and a load/error state; high-contrast projector legibility per the S5 shell.

5. **Render-time Title join.** A shared title-resolution hook over `GET /api/listings/{id}` (an `[AllowAnonymous]` endpoint — plain fetch is acceptable; `staffFetch` is harmless), cached per `["listing", id]` (the key the cache bridge already invalidates), 404-tolerant (a missing listing renders a shortened-id fallback, no retry per the wire-contract skill), deduplicated across rows by TanStack Query's cache.

6. **Vitest coverage.** Prove: (a) the parse surface accepts the wire shape and returns `null` on junk; (b) the cache bridge maps representative `eventType`s to the right invalidations, including the unknown-eventType blanket default; (c) one board renders rows from a fake `staffFetch` and the empty state from `[]`; (d) `useListen` receives a parsed message after the bridge ran. Keep the S5 suites green and untouched in behavior.

7. **`docs/retrospectives/M8-S6-ops-dashboard-views-retrospective.md`** — written last; records the push-coverage gap carry-forward explicitly.

## Explicitly out of scope

- **Any backend / API-host change** — including adding Relay ops-feed handlers for the settlement/escalation/lifecycle events found missing (finding 2). That is a recorded carry-forward (candidate sanctioned backend slice), not a quiet `.cs` touch.
- **Dispute-resolution controls / any command surface.** This slice renders Morgan's queues; it does not wire `ResolveDispute`/`OpenDispute` actions. The milestone §7 row scopes S6 to the data views; acting on a dispute from the dashboard is unscoped M8 work to surface in the retro if it is wanted before milestone close.
- **`client/shared/` extraction** (ADR 025 housekeeping). The Zod-schema duplication this slice adds is the third duplication candidate — noted for S7, not a gate here.
- **Playwright e2e, the CI frontend job, `CLAUDE.md`/`STATUS.md`/`bounded-contexts.md` refreshes** — all M8-S7 housekeeping.
- **The bidder app** — no `client/bidder/` changes.
- **New dependencies beyond `zod`** (ADR 013's set is closed).

## Conventions to pin or follow

- **ADR 026 ordering:** the provider runs the cache bridge before the listener fan-out; by the time a `useListen` handler fires, the re-query is in flight. Push payloads are never rendered as authoritative state — `useListen` is for transient affordances only.
- **Wire-contract rules** (`wolverine-http-frontend-contract`): camelCase keys; decimals as JSON numbers; `z.string()` for Guids/timestamps (no format pinning); `[]` renders, never errors; no retry on a 404 title join; all staff HTTP through `staffFetch`.
- **Push-fed dedupe rule** (`signalr` skill): the at-least-once broker topology can duplicate pushes. The boards are naturally idempotent (invalidating twice is harmless); any transient push-fed UI added here must dedupe by identity, never key off timestamps.
- **Same-origin relative paths only** — `/api/...`, `/hub/operations`; the Vite proxy owns dev reachability.
- **Frontend-slice-discipline Rule 4:** the slice closes with a live smoke per the playbook (Aspire host + `$env:OperationsAuth__StaffToken` in the launching shell; Edge via `playwright-core` if the Playwright MCP's Chromium is unavailable; Node 22 `WebSocket` for raw hub checks). A skipped smoke is an explicitly unchecked criterion in the retro, never a silent pass.

## Open questions

1. **Polling stopgap for boards without push coverage** (finding 2). The settlement queue would otherwise only refresh on mount/focus. **Recommended resolution:** a modest `refetchInterval` (~15–30 s) on the settlement-queue and escalations queries only, recorded in the retro as the documented stopgap the carry-forward backend slice retires. This is a data-layer trigger choice — the re-query path stays authoritative — not a violation of push-equals-re-query. If rejected, ship without polling and let the retro carry the gap as-is.
2. **Lot-watch events on the bridge.** `LotWatchAdded`/`LotWatchRemoved` reach the ops feed but no current board renders watch counts. **Recommended resolution:** map them to the lot-board invalidation (cheap, harmless) and note that no surface consumes watch data yet; do not build a watch column this slice.
3. **Per-row vs batched title joins.** Many rows may share few listings. **Recommended resolution:** per-id `useQuery` with the shared `["listing", id]` key — TanStack Query deduplicates identical keys; no bespoke batching unless the live smoke shows a real problem (escalating a batch endpoint would be a backend change anyway).

## Acceptance criteria

- [ ] `zod` is in `client/ops/package.json`; no other new dependency
- [ ] A parse surface for `OperationsFeedNotification` exists; junk input yields `null` (test-proven), never a throw
- [ ] The provider's `ReceiveMessage` body is parse → cache bridge → fan-out (the `console.debug` placeholder is gone); a `useListen` hook exists
- [ ] The cache bridge maps the lived 14-value `eventType` vocabulary; unknown `eventType` values trigger the blanket operations invalidation (test-proven)
- [ ] All six placeholder routes are replaced with data-rendering boards; no `PlaceholderView` usage remains in the router
- [ ] Every operations query goes through `staffFetch`; no bare `fetch` against `/api/operations/*` in view code
- [ ] Titles render via the join on bid activity, settlement queue, escalations, disputes; lot board uses its own `Title` with the join as null-fallback; a 404'd listing renders a fallback without retry
- [ ] Empty boards render a designed empty state from `[]`
- [ ] Vitest: parse surface, bridge mapping (incl. unknown default), one board render + empty state, `useListen` — green alongside the S5 suites; bidder suite untouched and green
- [ ] `npm run build` exit 0 for both workspace members; TypeScript strict
- [ ] Live smoke performed per the playbook and recorded in the retro (boards render real rows; a live push re-queries a board)
- [ ] No backend change — no `.cs`, `.csproj`, `.slnx` in the diff; no `client/bidder/` change
- [ ] `docs/retrospectives/M8-S6-ops-dashboard-views-retrospective.md` written, push-coverage carry-forward recorded
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Spec delta

Per ADR 020: (1) **Narrative 008's operator surfaces land** — Moments 1–3's `UX-or-UI-detail` deferred item (the staff dashboard's open-dispute and escalation queues) renders for real; Morgan's queues exist on the projector. (2) **The milestone §1 exit criterion** "renders the operator read models (lot board, settlement queue, obligation/dispute queue, session/participant activity) with live 're-query on push' refresh" is satisfied to the lived push vocabulary, with the coverage gap recorded as a spec-visible carry-forward. (3) **ADR 026 completes its second full consumer** — parse surface + cache bridge + `useListen` against a second hub with a homogeneous wire, confirming the pattern holds when no client-side `kind` normalization is needed. The retro's `## Spec delta — landed?` paragraph measures these three.
