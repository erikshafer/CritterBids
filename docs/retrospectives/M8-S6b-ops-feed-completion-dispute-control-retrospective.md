# M8-S6b: Relay Ops-Feed Completion + Dispute-Resolution Control - Retrospective

**Date:** 2026-06-11
**Milestone:** M8 - React Frontend SPAs
**Slice:** S6b - Relay ops-feed completion + narrative-008 dispute-resolution control (third sanctioned backend exception; precedent S3a/S3c)
**Agent:** Claude Code
**Prompt:** `docs/prompts/implementations/M8-S6b-ops-feed-completion-dispute-control.md`

## Baseline

- Clean `main` at `399625e`; both post-S6 open decisions closed (comparison doc §Decision + Addendum), M8-S6b row in the milestone §7 ladder (v0.5).
- Backend: 298 tests green across 10 projects (Relay.Tests: 36).
- Frontend: `@critterbids/ops` 35 Vitest green, `@critterbids/bidder` 25 green; ops boards polled the settlement queue + escalations at 20 s (`PUSH_GAP_REFETCH_INTERVAL_MS`).
- Lived ops-feed vocabulary: 14 `eventType` values; `OperationsHub` pushes missing for the settlement family, `DeadlineEscalated`, `ObligationFulfilled`, and the lot-board lifecycle terminals.
- Dispute card: read-only (narrative 008 Moment 2 deferred at S6).

## Items completed

| Item | Description |
|------|-------------|
| 1 | Inventory: Operations-consumed events vs lived ops-feed vocabulary — final gap count **8**, not the evaluations' 10 |
| 2 | Dual-push deltas in 4 existing Relay handlers + 2 new handler methods (`DeadlineEscalated`, `PaymentFailed`); 2 publish-route additions in `Program.cs` |
| 3 | Topology test (red-first, exactly the 8) + 8 per-event `OperationsHub` push integration tests |
| 4 | Cache-bridge mappings for all 8 new eventTypes; vocabulary comments updated to the 22-value surface; `ListingSoldOperations` proxy-signal note retired |
| 5 | `PUSH_GAP_REFETCH_INTERVAL_MS` + both `refetchInterval` usages + stale gap comments deleted |
| 6 | `onreconnected` one-shot `["operations"]`-family invalidation, unit-tested |
| 7 | "Resolve with extension" control on the dispute card (mutation seam `resolveDispute.ts`, pending-until-push-clears, ProblemDetails-aware error, 401 funnel intact), Vitest-covered |
| — | **Unplanned, smoke-discovered:** enum-as-string HTTP serialization fix in `Program.cs` (see §Finding 2) |

## Item 1: The invariant decided final membership — 8 events, not 10

The evaluations' 10-event starting list was diffed against the lived handler surface
(`src/CritterBids.Operations/*Handler.cs`: 17 distinct consumed integration events). Two dropped
out because **no Operations BC handler consumes them** — `TrackingInfoProvided` (Relay pushes it
to the winner's BiddingHub group only; no ops read model changes on it) and
`ExtendedBiddingTriggered` (lot board does not track close-time moves). Open question 2 resolved
unambiguously the same way: `BuyItNowPurchased`/`BuyItNowOptionRemoved` have BiddingHub handlers
only, no Operations consumption — out of scope, no escalation. The final gap set:

`SettlementCompleted`, `SellerPayoutIssued`, `PaymentFailed`, `DeadlineEscalated`,
`ObligationFulfilled`, `BiddingOpened`, `ListingPassed`, `ListingWithdrawn`.

No Operations-consumed event was missing from the evaluations' combined lists in the other
direction (`ParticipantSessionStarted`, the session trio, `ListingPublished`, `BidPlaced`,
`ListingSold`, `DisputeOpened`/`DisputeResolved` already had pushes).

## Item 2: Dual-push deltas — where each landed and why

| Event | Where | Shape |
|---|---|---|
| `BiddingOpened`, `ListingPassed`, `ListingWithdrawn` | existing `AuctionsBiddingHandler` methods | ops push added beside the BiddingHub push (`Task.WhenAll`) — sticky dispatch runs one handler class per (message type, endpoint), so the ops push must ride the same method (the S3c `BidPlacedHandler` precedent) |
| `ObligationFulfilled` | existing `ObligationsRelayHandler` method | third send in the existing `WhenAll` |
| `DeadlineEscalated` | **new method** in `ObligationsRelayHandler` | ops-feed only — operator-only fact, no participant push, no history entry |
| `SettlementCompleted` | existing `SettlementCompletedHandler` | ops push beside the winner-group push |
| `SellerPayoutIssued` | existing `SellerPayoutIssuedHandler` | ops push; `listingId: null` (the contract carries no ListingId) |
| `PaymentFailed` | **new class** `SettlementOperationsHandler` | first Relay consumer of the event; ops-feed only; new class is safe — no other class handles `PaymentFailed` on `relay-settlement-events` |

Routing: the `DeadlineEscalated → relay-obligations-events` route **removed at M8-S3c as
consumer-less** was restored exactly as that removal comment prescribed ("restore the route
together with the Relay handler if it ships"); `PaymentFailed → relay-settlement-events` is a
parallel publish-route addition to an existing ADR 027 queue. No new queues, no Contracts change
— Open question 1 never tripped.

EventType naming: `nameof(Event)` for all 8 (the majority precedent). The
`BidPlacedOperations`/`ListingSoldOperations` suffixed values predate this slice and stay as-is —
renaming wire values is churn the slice's no-payload-change rule forbids in spirit.

## Item 3: The topology test — red first, by design

`tests/CritterBids.Relay.Tests/OperationsFeedTopologyTests.cs` derives both sides by reflection:
Operations-consumed = first parameter of every `Handle` method in `CritterBids.Operations` whose
type lives in `CritterBids.Contracts`; ops-published = every Relay `Handle` receiving the event
**and injecting `IHubContext<OperationsHub>`** (in the dual-push template, taking the ops hub
context is what a publication looks like; the per-event push tests cover the actual send). A
≥15-count sanity floor guards against the reflection going stale and "passing" on an empty scan.

Run against the unmodified tree it failed with exactly the inventory:

```
missing should be empty but had 8 items and was
["BiddingOpened", "DeadlineEscalated", "ListingPassed", "ListingWithdrawn",
 "ObligationFulfilled", "PaymentFailed", "SellerPayoutIssued", "SettlementCompleted"]
```

— the acceptance criterion's "demonstrated once during development", and a cross-check that test
mechanics and manual inventory agree. A future Operations consumer added without a Relay ops push
now fails CI with a named list.

## Finding 1 (smoke): enums crossed the HTTP wire as numbers — a latent S6 defect

First smoke run: the settlement row materialized in the read model but never rendered. Raw wire
showed `"status": 2` — **STJ-default numeric enums** on every Operations view
(`SettlementQueueStatus`, `LotBoardStatus`, `QueueState`, `SessionActivityStatus`), while the
documented wire contract (`docs/skills/wolverine-http-frontend-contract` §3: "enums → string
name") and the S6 Zod schemas + status switches expect names. Invisible until now because **S6's
live smoke only ever saw empty boards, and `[]` parses identically either way** — the first real
row on any staff board failed its Zod parse and turned the board into an error state.

Resolution (deliberate scope addition, surfaced here and in the PR): one registration in
`Program.cs` —

```csharp
builder.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
```

— the canonical Wolverine.HTTP knob (verified via Context7; Wolverine HTTP shares the Minimal-API
`JsonOptions`). Backend over frontend fix because the documented contract is names, all existing
client code (both apps' schemas, the ops status switches) already speaks names, and a client-side
numeric mapping would duplicate backend enum ordering brittly. The bidder app carries no enum
fields, so its wire is unchanged. Full backend suite re-run green after the change (307/307 — no
test asserted the numeric wire).

## Finding 2 (smoke): the Node SignalR client is NOT faithful to the browser credential transport

A diagnostic Node listener using `accessTokenFactory` failed its WS upgrade against
`/hub/operations` ("WebSocket failed to connect…"). Root cause: in Node the `@microsoft/signalr`
CJS build rides the `ws` package, which **can** set headers — so the factory token goes out as an
`Authorization` header (which the query-string-only StaffToken scheme does not read), not as
`?access_token=`. This refines the S5-era playbook note ("Node reproduces browser credential
transport faithfully" — true only for Node's built-in header-less WebSocket, not when `ws` is
resolvable, as it is inside `client/`): **to reproduce the browser wire from Node, pin the token
in the URL** (`/hub/operations?access_token=…`) instead of using the factory. With that form the
listener connected and captured all three new push families live, including the expected
at-least-once duplicates (two copies of `SettlementCompleted` per settlement — absorbed by the
bridge's idempotent re-query, per the signalr skill's dedupe doctrine). Skill-correction
carry-forward recorded below.

## Item 7: The control — pending until the push clears the card

`resolveDispute.ts` posts `{ obligationId, disputeId, resolutionType: "Extension" }` through
`staffFetch` and treats 202-no-body as success **without a parse attempt**; failures throw a
`ResolveDisputeError` carrying ProblemDetails `detail`/`title` (tolerant `safeParse` — a bare 5xx
gets a generic retry message). The component holds per-row `pendingIds`/`rowErrors` state:

- click → row enters pending ("Granting extension…", disabled) — **and stays pending after the
  202**, because 202 means "accepted", not "resolved"; the `DisputeResolved` push re-queries both
  queues and the card leaving the data is the success signal (no `onSuccess` cache write, no row
  removal — ADR 026 verbatim);
- failure → row returns to actionable with the error visible on the card (`role="alert"`); a 401
  additionally funnels through `staffFetch` to the re-gate;
- the control renders only when `disputeId` is non-null — a disputeId-less row is a projection
  gap that gets "—", never a synthesized id (Open question 3; the live smoke surfaced no null
  `disputeId`).

Injectable seams throughout (the component takes a `resolveDispute` prop; the seam takes
`staffFetch`) — no module mocks, matching the S6 testing conventions.

## Live smoke (real Aspire host + real browser, per the playbook)

Host: `$env:OperationsAuth__StaffToken` + `$env:Obligations__DemoMode = "true"` in the launching
shell; ops dev server on 5174; Edge via `playwright-core` (`channel: "msedge"`, headless);
self-contained driver (`/api/dev/seed-flash` → session → bid → 1-minute close → settlement →
demo-mode escalation at +10 s → `POST /api/obligations/disputes` → dashboard action).

| Check | Result |
|---|---|
| Staff HTTP contract: no token → 401; valid → 200 `[]` | pass |
| Auth gate → dashboard → hub indicator **Connected** (asserted before seeding) | pass |
| **(a)** Settlement row appeared on the settlement queue **live via `SettlementCompleted` push** — stopgap deleted, so any update is push-driven by construction; no reload | pass |
| **(b)** Escalation card **arrived** live (`DeadlineEscalated` push — the gap that motivated the slice) | pass |
| **(c)** Narrative 008 Moment 2 end-to-end: dispute card arrived (`DisputeOpened`), "Resolve with extension" clicked, control pending after 202, card **cleared live** via `DisputeResolved` push re-query; dispute queue confirmed empty of the obligation via staff API | pass |
| Raw-wire pushes captured independently (Node listener): `SettlementCompleted`, `SellerPayoutIssued`, `DeadlineEscalated` with expected at-least-once duplicates | pass |

Smoke-harness lessons paid for en route: (1) assert the hub indicator **before** seeding — a
board with no poll has no fallback trigger, so an unasserted dead connection looks identical to a
missing push; (2) per-run unique seed titles — earlier runs' rows sit in the same queues, and a
reused title lets `waitForSelector` match a stale row instead of the live arrival (one run's
(a)/(b) "passes" were invalidated this way and re-proven with unique titles). Hosts/dev server
torn down and confirmed unreachable after.

## Test results

| Phase | Suite | Result |
|-------|-------|--------|
| Topology test vs unmodified tree | Relay.Tests (filtered) | **red** — exactly the 8 (verbatim above) |
| After backend deltas | Relay.Tests (filtered) | 23/23 (topology + 22 push tests) |
| After frontend items 4–7 | `@critterbids/ops` Vitest | **47/47** (was 35; +12: 5 seam, 3 control, 1 reconnect, 3 bridge) |
| Regression | `@critterbids/bidder` Vitest + build | **25/25**, build exit 0 — untouched |
| Full backend, after all changes incl. the enum fix | `dotnet test` (solution) | **307/307** (was 298; Relay.Tests 36 → 45) |
| Type-check | `npm run build` (ops) | exit 0 (tests inside `tsc --noEmit` strict) |

## Build state at session close

- `dotnet build` solution: 0 errors, 0 warnings. Both SPA builds exit 0.
- `PUSH_GAP_REFETCH_INTERVAL_MS` over `client/ops/`: **0**. `refetchInterval` over `client/ops/`: **0** (acceptance-criterion grep).
- Ops-feed vocabulary: **22** eventTypes, every one mapped in `cacheBridge.ts` with board-key targets; `messages.ts` comment matches the lived handler surface.
- `IHubContext<OperationsHub>` injections in Relay handlers: **17** methods (was 9) — one per published eventType, minus the dual-`eventType` BidPlaced/ListingSold classes' single methods.
- New `CritterBids.Contracts` types: **0**. `OperationsFeedNotification` shape changes: **0**. New queues: **0** (two publish routes added to existing queues).
- Optimistic cache writes in the mutation path: **0** (`setQueryData` in `resolveDispute.ts`/`ObligationsQueues.tsx`: 0).

## Key learnings

1. **An invariant beats a list.** The evaluations' "10 events" became 8 the moment the rule
   ("Operations-consumed ⇒ ops push") was applied to the lived handler surface — two events on
   the list had no consumer, and none were missing. Write the rule into a test and the list can
   never go stale again.
2. **Empty-state smokes verify the empty state only.** S6's "all boards render `[]`" pass hid a
   wire-contract defect (numeric enums) that the first populated board exposed. A smoke that
   never puts a real row through the parse boundary has not verified the parse boundary.
3. **Reflection-over-signatures turns a convention into CI.** In Wolverine's method-parameter DI
   idiom, *what a handler injects* is a reliable proxy for *what it does* — cheap to assert, and
   it names future offenders precisely.
4. **Node-vs-browser SignalR credential transport differs by WebSocket implementation, not
   platform.** The signalr client uses headers whenever the WS implementation supports them
   (`ws` in Node) and the query string only when it can't (browsers). Faithful browser
   reproduction from Node = token pinned in the URL.
5. **Push-only boards need their connection asserted before any traffic is generated.** With
   polling gone, a dead hub connection and a missing publication are indistinguishable from the
   board's silence; the smoke must separate them (indicator gate + raw listener).

## Findings against narrative

Narrative 008 Moment 2 implemented **as drafted**: the operator's single action
(`ResolveDispute { ObligationId, DisputeId, ResolutionType: "Extension" }` from the staff
dashboard), the non-terminal continuation (the obligation left both queues and re-entered the
active set — confirmed via staff API post-resolve), and the staff-board clear via the
`DisputeResolved` broadcast all match the narrative's Moment 2 Response verbatim. Lane:
`narrative-update` (resolved in this PR) — the v1.2 Document History row records the landing and
closes the v1.1-recorded `DeadlineEscalated` push gap. No drift in the narrative's domain
understanding; the buyer's "Report a problem" form stays deferred as v1.1 recorded.

## Spec delta - landed?

**Landed as written, all three consequences, plus one surfaced addition.** (1) Narrative 008
gained the v1.2 Document History row (Moment 2 implemented; card clears live). (2) ADR 026's
push-equals-re-query contract is now total for the ops app: the polling stopgap is deleted and
the topology invariant is mechanically enforced (`OperationsFeedTopologyTests`). (3) The M8 exit
criterion's "lived push vocabulary" qualifier is removed in fact: the settlement queue, escalation
arrivals, and lot-board terminal transitions all update live (smoke-proven). **Addition:** the
enum-as-string serialization fix restores `wolverine-http-frontend-contract` §3's documented
wire shape for the whole HTTP surface — the skill text needed no edit (it was already right; the
host was wrong), so no Document History row beyond the `Program.cs` comment and this retro.

## Verification checklist

- [x] Topology test exists, failed red with the 8 missing events (verbatim output above), passes on the final tree
- [x] `PUSH_GAP_REFETCH_INTERVAL_MS` / `refetchInterval` over `client/ops/`: zero matches
- [x] Every final-inventory event mapped in `cacheBridge.ts`; `messages.ts` vocabulary comment matches the lived 22-value surface
- [x] `onreconnected` → exactly one `["operations"]`-family invalidation per reconnect; unit-tested
- [x] Extension control renders only on non-null `disputeId`; 202 → card leaves via push-driven re-query; no manual cache write
- [x] Failed resolve surfaces visibly on the card (ProblemDetails-aware); 401 funnels to the staff re-gate
- [x] `dotnet build` + `dotnet test` green (307/307); `npm test` green in ops (47/47) and bidder (25/25, untouched); ops tests inside the production type-check
- [x] Live smoke against the Aspire host recorded above: one dispute resolved end-to-end from the dashboard; one settlement event observed moving the settlement queue live with the stopgap gone
- [x] This retrospective; narrative 008 v1.2 Document History row

## What remains / next session should verify

- **M8-S7 (in milestone scope, unchanged):** Playwright two-bidder e2e — now validating the
  *finished* feed; CI frontend job (72 Vitest tests across the workspace); doc refreshes
  (CLAUDE.md ops row, milestone §2 staleness, `bounded-contexts.md`, STATUS.md); `client/shared/`
  extraction decision.
- **Skill correction carry-forward:** `.claude/skills/frontend-slice-discipline` §smoke playbook
  step 3 ("Node 22's built-in WebSocket is header-less like a browser") — true only when the `ws`
  package is not resolvable; from inside `client/` the signalr client picks up `ws` and sends an
  Authorization header instead. Add the pin-token-in-URL form as the faithful Node reproduction.
- **At-least-once duplicates observed live** (two copies of several ops pushes per event):
  expected broker behavior, absorbed by re-query idempotency; if a transient push-fed ops *feed*
  surface is ever added (ticker-style), it needs the bounded seen-set dedupe per the signalr
  skill.
- **Out of scope, tracked elsewhere:** `Refund`/`Closed` controls (M6/M7 non-goal), buyer's
  "Report a problem" form, notification-history expansion for ops-feed publications.
