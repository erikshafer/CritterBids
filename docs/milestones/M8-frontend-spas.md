# M8 — React Frontend SPAs

**Status:** ✅ Complete (closed at M8-S7, 2026-06-12 — eleven slices S1…S7 incl. the sanctioned S3a/S3c/S6b backend exceptions and the Bug #2 interlude; see the M8 milestone retrospective)
**Scope:** The two React single-page applications that render the CritterBids journeys against the now-complete backend — a public, anonymous **bidder-facing app** (catalog + live bidding via `BiddingHub`) and a staff-gated **operations dashboard** (operator read models + live feed via `OperationsHub`). Both ship as static Vite SPAs (ADR 012) pointing at the same `CritterBids.Api` host. M8 is the **first milestone with a frontend code surface** — the eight backend BCs are done; M8 is the renderer. It is also the milestone that flips ADR 013 (frontend core stack) from `Proposed` to `Accepted` and authors the SPA monorepo-layout ADR.
**Companion docs:** [`../vision/bounded-contexts.md`](../vision/bounded-contexts.md) (§Relay, §Operations, §"Two SignalR hubs", §"separate React SPAs pointing at the same API host") · [`../narratives/001-bidder-wins-flash-auction.md`](../narratives/001-bidder-wins-flash-auction.md) (the bidder spine the public app renders) · [`../narratives/002-winner-clears-settlement.md`](../narratives/002-winner-clears-settlement.md) · [`../narratives/003-bidder-starts-anonymous-session.md`](../narratives/003-bidder-starts-anonymous-session.md) · [`../narratives/008-operator-resolves-dispute-with-extension.md`](../narratives/008-operator-resolves-dispute-with-extension.md) (the operator-vantage spec the ops dashboard renders) · [`../decisions/004-react-frontend.md`](../decisions/004-react-frontend.md) · [`../decisions/012-frontend-spa-vite.md`](../decisions/012-frontend-spa-vite.md) · [`../decisions/013-frontend-core-stack.md`](../decisions/013-frontend-core-stack.md) · [`../decisions/023-relay-reactive-broadcast-architecture.md`](../decisions/023-relay-reactive-broadcast-architecture.md) · [`../decisions/024-staff-token-authentication.md`](../decisions/024-staff-token-authentication.md) · [`../skills/wolverine-signalr/SKILL.md`](../skills/wolverine-signalr/SKILL.md) · [`../decisions/README.md`](../decisions/README.md)

---

## 1. Goal & Exit Criteria

### Goal

Deliver the **two React SPAs** that make the CritterBids backend visible and operable: the bidder-facing public app and the staff operations dashboard, each a static Vite + React + TypeScript build served against the existing API host. The backend is feature-complete at M7 close (281 tests, all eight BCs, all integration routes, all staff surfaces gated) — M8 originates **no backend BC work** beyond a single sanctioned exception (**M8-S3a**: exposing the *existing* internal `PlaceBid` command over HTTP, because bid placement has no HTTP surface and the live-bidding journey cannot ship without it — see §3 and §7). Its job is otherwise the renderer and the real-time client wiring.

The milestone's spine is the conference-demo story narrative 001 dramatizes: an attendee scans a QR code, the bidder app mints an anonymous session, the catalog renders, a Flash session opens, bids fly over `BiddingHub` in real time, extended bidding triggers, the gavel falls, and settlement completes — while on the same projector a second browser window runs the ops dashboard, the "look at the engine running" view (`bounded-contexts.md` §Operations), showing the lot board, settlement queue, and obligation/dispute pipeline update live over `OperationsHub`.

M8 also carries two **foundation decisions** that gate every later frontend slice and are therefore made first, in M8-S1, and recorded durably rather than discovered mid-flight:

1. **Accept (or revise on acceptance) ADR 013 — Frontend Core Stack.** ADR 013 has been `Proposed` since 2026-04-19. M8-S1 flips it to `Accepted` against the 2026 ecosystem as it actually stands at acceptance time. Its five Deferred Questions (routing library, UI state beyond server state, auth client pattern, the SignalR *integration* pattern → ADR 026, PWA offline scope) stay deferred; they are resolved in the later slices that need them, not pre-empted by acceptance.
2. **Author the SPA monorepo-layout ADR (next unreserved number).** Where the SPA source lives, whether the two apps are independent Vite projects / a shared workspace / a monorepo, how each build's static output relates to `CritterBids.Api`, and the local dev-server story (proxy vs CORS) for reaching the API and the hubs. This is hard-to-reverse and cross-cutting — it shapes how every later M8 slice is structured — which is why it is an ADR, not a milestone-doc note.

### Exit criteria

- [x] **ADR 013 accepted.** `013-frontend-core-stack.md` status is `Accepted` (dated, with any on-acceptance revision recorded and its reason); the five Deferred Questions remain listed as deferred; the ADR index row is re-badged `✅ Accepted` *(M8-S1)*
- [x] **SPA monorepo-layout ADR authored and accepted.** Settles source layout, build-output integration with the .NET host, and the dev-server (proxy vs CORS) story — each with alternatives weighed; the ADR index gains its row and the next-unreserved-number pointer advances *(M8-S1 — ADR 025)*
- [x] **Bidder-facing SPA** exists at the layout the ADR decides, renders the public catalog and a listing detail (read-only) from the existing `[AllowAnonymous]` query endpoints, and drives live bidding through `BiddingHub` end-to-end for narrative 001's spine (session start → catalog → bid → outbid → extended bidding → sold → settlement outcome) *(M8-S2/S3b/S4; annotation: the "outbid" and "extended bidding" beats are client-side derivations from view transitions — there is no server `Outbid` push, and the extended-bidding banner is structurally unreachable from the lived read model because Listings carries no `ExtendedBiddingTriggered` handler — the M8-S7 e2e finding, recorded as a carry-forward; the extension itself works and the gavel falls at the extended close)*
- [x] **Operations dashboard SPA** exists, authenticates with the `StaffToken` posture (ADR 024), connects to `OperationsHub`, and renders the operator read models (lot board, settlement queue, obligation/dispute queue, session/participant activity) with live "re-query on push" refresh *(M8-S5/S6; push vocabulary completed and the polling stopgap deleted at M8-S6b)*
- [x] **SignalR integration pattern** decided and recorded (ADR 026 — Provider + hook + TanStack Query cache bridge) once the first real hub is wired from the client; both SPAs follow it *(M8-S3b)*
- [x] **Real-time client conventions honored:** `@microsoft/signalr` with `.withAutomaticReconnect()`; the `ReceiveMessage` client method per ADR 023; the staff token read from config, never hardcoded *(annotation: the "`access_token` query string for the `OperationsHub` negotiate" wording was overtaken by the lived SignalR 7+ transport — the client sends factory tokens as a Bearer header on the negotiate, so the lived shape is `skipNegotiation` + WebSockets with the token on the WS upgrade, M8-S5; `X-Staff-Token` on staff HTTP as written)*
- [x] **Render-time `Title` join** implemented: lot board / obligations views receive `ListingId` only, and the dashboard resolves display titles from `/api/listings/{id}` *(M8-S6)*
- [x] **PWA posture** resolved consistent with accepted ADR 013 (manifest + service-worker registration on both apps, or an explicit recorded deferral with carry-forward — see §6) *(both apps wire `vite-plugin-pwa`; offline-data scope stays ADR-013-deferred)*
- [x] Each SPA `npm install` + `npm run build` (or the layout ADR's named equivalents) succeed from a clean checkout; TypeScript **strict** is on; the builds do not break `dotnet build CritterBids.slnx` / `dotnet test CritterBids.slnx`
- [x] Existing .NET baseline unchanged by M8 — 0 errors / 0 warnings held throughout *(annotation: the "281 backend tests" baseline is **307 at close** — grown, not broken, by the three sanctioned backend exceptions and their fixes: S3a bid endpoint, S3c ADR 027 sticky bindings, S6b ops-feed completion + the Bug #2 interlude; no unsanctioned backend change shipped)*
- [x] At least one Playwright multi-context end-to-end test exercising a live two-bidder bid war against a running API host (the ADR 013 use case for Playwright) *(M8-S7 — `client/e2e/tests/bid-war.spec.ts`, two green runs recorded in the S7 retro)*
- [x] CI extended with a **frontend build/test job** — landed as housekeeping within M8 (not necessarily S1). The backend integration matrix already covers all eight BC suites + Api (`.github/workflows/ci.yml`, PR #77); no backend matrix work is owed *(bidder-only job first; extended to the full workspace — ops build+test, e2e type-check — at M8-S7; the Playwright e2e itself stays out of CI, a recorded S7 deferral)*
- [x] `CLAUDE.md` gains a short frontend-layout pointer reflecting the layout ADR (a pointer, not a duplication) *(M8-S1; refreshed at M8-S7 — ops no longer planned, `client/e2e/` added, `client/shared/` deferral recorded)*
- [x] M8-S1 through final-session retrospective docs written; M8 retrospective doc written *(M8-S7)*

---

## 2. In Scope

### The two SPAs

| App | Directory home (ADR 025) | Audience | Auth | Live channel | Renders |
|---|---|---|---|---|---|
| Bidder-facing app | `client/bidder/` | Public conference attendees | Anonymous (`[AllowAnonymous]`) | `BiddingHub` (`/hub/bidding`) | Narratives 001, 002, 003 — anonymous session start, catalog, listing detail, live bidding, settlement outcome |
| Operations dashboard | `client/ops/` | Staff / operators / projector | `StaffToken` (ADR 024) | `OperationsHub` (`/hub/operations`) | The six operator views + narrative 008's dispute/escalation queue |

Both are static Vite SPAs (ADR 012), React + TypeScript strict (ADR 004 + 013), pointing at the same `CritterBids.Api` host. The directory homes and project topology (an npm-workspaces monorepo rooted at `client/`) were decided by ADR 025 in M8-S1 and both apps shipped at those homes (bidder S2–S4, ops S5–S6b); M8-S7 added a third workspace member, `client/e2e/`, for the Playwright harness.

### Bidder-facing app — surfaces

| Surface | What it renders | Source |
|---|---|---|
| Landing / QR entry | Anonymous session start (`POST /api/participants/session`), display-name header | Narrative 001 Moment 1; narrative 003 |
| Catalog | Published-listing list from the `[AllowAnonymous]` catalog query endpoint | Narrative 001 Moment 2; `CatalogListingView` |
| Listing detail | Single-listing read view (`/api/listings/{id}`) | Narrative 001 Moment 2; `ListingDetailView` |
| Live bidding | Real-time bid feed, bid placement with optimistic update + rollback, outbid push, extended-bidding banner, gavel-fall, all over `BiddingHub` | Narrative 001 Moments 3–7; `BidPlaced`/`Outbid`/`ExtendedBiddingTriggered`/`ListingSold` |
| Settlement outcome | Won/charged confirmation view | Narrative 001 Moment 8; narrative 002 |

### Operations dashboard — surfaces

| Surface | What it renders | Source |
|---|---|---|
| Staff auth gate | `StaffToken` entry (config-fed, never hardcoded); `X-Staff-Token` HTTP + `access_token` hub negotiate | ADR 024 |
| Lot board | Current high bid + status per listing, live | `LotBoardView`; `/api/operations/*` |
| Bid-activity feed | Append-style live bid feed | `BidActivityEntry` |
| Settlement queue | In-flight + failed settlements (`PaymentFailed` flagged) | `SettlementQueueView` |
| Obligation / dispute queue | Escalation queue + open-dispute queue | `OperationsObligationsView`; narrative 008 |
| Session & participant activity | Session lineup + participant-session activity | `SessionActivityView`, `ParticipantActivityView` |

### Foundation decisions (M8-S1)

ADR 013 acceptance and the SPA monorepo-layout ADR are the headline S1 deliverables (see §1 Goal and §7). They are made first because every later slice's structure depends on them; discovering the layout or the stack mid-bidder-app would be expensive rework. M8-S1 also stands up a single minimal `BiddingHub` connection-proof app — the cheapest falsifiable test of the riskiest integration assumption (that a static SPA's dev server can reach the hub's WebSocket upgrade) — and nothing more of the bidder app than that proof.

### Cross-cutting frontend concerns carried from M7

- **Render-time `Title` join.** Operations API returns `ListingId` only for lot board / obligations views; the dashboard resolves titles from `/api/listings/{id}`.
- **Relay push = re-query signal.** `OperationsHub` (and `BiddingHub`) pushes are "something changed, refetch" notifications, **not** authoritative payloads and **not** read-your-own-write. The SPA re-queries the query endpoint on push rather than rendering the push body as truth (`M7-operations-bc.md` §5).
- **Staff token transport.** `X-Staff-Token` header for HTTP; `access_token` query string for the `OperationsHub` negotiate (SignalR clients cannot set custom headers on the negotiate POST). ADR 024.

### CI: frontend job (housekeeping, in-milestone)

The backend integration matrix already runs all eight BC suites + Api (`.github/workflows/ci.yml`, closed by PR #77 — this supersedes the M7 retro's "~44% local only" note, which is now stale). What M8 owed CI was a **frontend build/test job** — landed mid-milestone for `@critterbids/bidder` and **extended to the full workspace at M8-S7** (ops build + Vitest, e2e type-check). The optional Playwright e2e stage was evaluated at S7 and **deferred as a recorded decision** (the e2e needs the full Aspire stack live; see the comment at the `frontend` job and the S7 retro) — it runs locally pre-merge per `client/e2e/README.md`.

---

## 3. Explicit Non-Goals

- **Backend / API-host behavior changes.** No new endpoints, no hub changes, no auth changes, no new BC code. M8 consumes the M7 surface as-is. The single tolerable touch is a trivial **dev-only** CORS allowance if the layout ADR's dev-server choice strictly requires it; anything larger is escalated to its own decision, not made silently. **Sanctioned exception (M8-S3a, 2026-06-08):** wiring `POST /api/auctions/bids` — a thin `[AllowAnonymous]` HTTP entry over the *existing* internal `PlaceBid` DCB command — is the one backend change M8 takes on, because bid placement (narrative 001 Moment 4) has no HTTP surface and the live-bidding journey cannot ship without it. This is exactly the "escalated to its own decision" path this non-goal prescribes: it is its **own slice (M8-S3a)**, not a silent touch, and it adds **no new domain capability** — the command, the DCB consistency boundary, the rejection rules, and the bid-increment policy already exist in `PlaceBidHandler`; the slice only designs the HTTP contract over them. No *other* backend change is sanctioned.
- **New backend domain capability.** Settlement reversal, dispute compensation, the `DemoResetInitiated` cascade, per-user staff identity — all remain post-MVP and untouched by M8.
- **Resolving ADR 013's Deferred Questions at S1.** Routing library, UI state management, auth client pattern, the SignalR integration pattern (ADR 026), and PWA offline scope stay parked at acceptance. They are resolved in the later slices that need them (routing at the first bidder-shell slice; ADR 026 when the first hub is wired from the client), not by the S1 acceptance note.
- **Seller console.** The seller-perspective journeys (narratives 004/005/006 — publish, watch-close, fulfill-obligation) describe a seller console: a real, planned surface — but **out of M8 scope**, deferred to a future milestone (working name M9). M8's committed frontend surface is the **bidder** journey and the **operator** dashboard, full stop. No seller-authoring or seller-console UI ships in M8, and the bidder app does not grow seller surfaces. (The narratives exist; they are the spec the future seller-console milestone will render, not M8 scope creep.)
- **Native wrappers.** Capacitor / Tauri / native shells are an ADR 013 revisit trigger, not M8 scope. The PWA is the installability story.
- **Visual design system / branding / accessibility audit.** ADR 013 explicitly scopes these out; M8 uses Tailwind v4 + shadcn/ui defaults, not a bespoke design language. A projector-legible high-contrast ops layout is in scope as a layout concern, not as a branded design system.
- **shadcn/ui full component-catalog scaffolding up front.** Components are copied in as surfaces need them (the shadcn model), not bulk-scaffolded.
- **A third app or a unified app.** Exactly two SPAs. Merging bidder + ops into one app contradicts the `bounded-contexts.md` two-window projector framing and the anonymous-vs-staff auth split.

---

## 4. Solution Layout

### New surface added in M8

The first frontend code surface in the repository. Its root location and project topology are the **layout ADR's decision** (M8-S1); the working assumption the milestone plans around is a `client/` peer directory holding the two SPAs:

```
client/                          ← NEW IN M8 (root location + topology per the layout ADR)
├── bidder/                      ← bidder-facing public SPA (Vite + React + TS strict)
│   ├── package.json             ← @microsoft/signalr, TanStack Query, Tailwind v4, shadcn/ui, react-hook-form, Zod, Vitest, Playwright, vite-plugin-pwa
│   ├── vite.config.ts
│   ├── tsconfig.json            ← strict
│   └── src/
└── ops/                         ← operations dashboard SPA (same stack; staff-gated)
    └── …
```

Whether `bidder/` and `ops/` are independent Vite projects, a shared pnpm/npm workspace, or a monorepo with shared config (`tsconfig` base, Tailwind preset, ESLint/Prettier, a shared `api-client`/Zod-schema package) is exactly what the layout ADR settles. The build-output relationship to `CritterBids.Api` (host-served static files vs separate static deploy vs build-time copy) is likewise the ADR's call, constrained by ADR 012's static-SPA posture (no meta-framework, no server-rendered route ownership).

### Backend layout — unchanged

`src/` and `tests/` are untouched by M8 except (at most) a trivial dev-only CORS allowance in `Program.cs` if the dev-server choice requires it. The eight-BC `src/` layout from M7 stands as-is. No new `.csproj`, no new BC, no contract change.

### Full solution layout at M8 close

```
src/                 ← unchanged from M7 (8 BCs + Api + AppHost + Contracts)
tests/               ← unchanged from M7 (10 test projects) + CI matrix extended
client/              ← NEW: bidder/ + ops/ SPAs
docs/decisions/      ← ADR 013 accepted; SPA-layout ADR + ADR 026 authored
```

---

## 5. Infrastructure, Build & Dev

### Build integration with the .NET host

Each SPA is a static Vite build. How the static output ships relative to `CritterBids.Api` (served by the host as static files, a separate static deploy, or a build-time copy) is the layout ADR's decision, consistent with ADR 012. M8 does not introduce a server-rendered or meta-framework path.

### Local dev-server story

During development each SPA runs the Vite dev server and must reach the API and the SignalR hubs. The choice between a **Vite dev-server proxy** (the SPA proxies `/api` and `/hub` to the API host; no API CORS change) and **API-host CORS** (the API allows the dev-server origin) is the layout ADR's call. The proxy path is preferred where it works because it keeps the API host's CORS/auth surface untouched; the SignalR negotiate + WebSocket upgrade must be reachable through whichever mechanism is chosen, and if reaching the hub requires more than a trivial dev-only allowance on the API host, that is escalated rather than widening the API surface.

### Real-time client wiring

`@microsoft/signalr` is the client (ADR 013). Connections use `.withAutomaticReconnect()` and register the `ReceiveMessage` client method (the raw notification record is the payload — no CloudEvents envelope, per ADR 023). The `BiddingHub` connection is anonymous; the `OperationsHub` connection carries the staff credential as an `access_token` query-string parameter on the negotiate (ADR 024). The **integration pattern** wrapping these — a `SignalRProvider` Context, a `useListen(event, handler)` hook, and the bridge into the TanStack Query cache — is **ADR 026**, authored once the first hub is wired from the client (M8-S3), not pinned in advance.

### No new backend infrastructure

No new database, container, transport, or queue. M8 adds frontend build tooling (Node/npm) and a frontend CI job; the backend infra (Postgres + RabbitMQ via Aspire) is unchanged.

### Testing infrastructure

Per ADR 013: **Vitest + React Testing Library** for unit/component tests (Vite-native config), **Playwright** for end-to-end. Playwright's multi-context capability is the reason it was chosen — at least one M8 e2e test drives a live two-bidder bid war against a running API host, catching reconciliation/race bugs unit tests cannot. Backend tests are unchanged; the CI integration matrix already covers all eight backend suites + Api (PR #77) and gains only the **frontend** job in M8.

---

## 6. Conventions Pinned

### Frontend stack — ADR 013 (accepted at S1)

Once accepted in M8-S1, ADR 013 owns the library composition: TypeScript strict, Zod at the wire boundary, TanStack Query for server state, Tailwind v4 + shadcn/ui, `react-hook-form` + Zod for forms, `@microsoft/signalr`, Vitest + Playwright. Slices do not introduce libraries outside this set without an ADR. The five Deferred Questions are resolved in their owning slices, not silently.

### SPA layout & build integration — the S1 layout ADR

The layout ADR is the first and authoritative encoding of the `client/` layout, the build-output integration, and the dev-server story. Later M8 slices **point at it** rather than re-deciding. `CLAUDE.md` gains a short pointer to it (the way ADR 024 earned a `CLAUDE.md` follow-on).

### SignalR — ADR 023 (contract) + ADR 026 (integration pattern)

ADR 023 owns the `ReceiveMessage` payload contract and the plain-hub broadcast architecture; the `wolverine-signalr` skill owns the client-side `HubConnection` conventions. ADR 026 (authored M8-S3) owns the React integration pattern. Slices point at these rather than restating them.

### Static-SPA posture — ADR 012

Both SPAs ship as static Vite output; the backend owns all contracts. No Next.js / Remix framework mode / TanStack Start, no server-rendered route ownership. This constrains the layout ADR's options.

### Staff auth from the SPA — ADR 024

The ops dashboard reads the `StaffToken` from config (never hardcoded), sends it as `X-Staff-Token` on HTTP and `access_token` on the `OperationsHub` negotiate. The bidder app sends no credential — its surfaces are `[AllowAnonymous]`. 401 (no/invalid token) is handled; 403 is structurally unreachable under the single shared secret.

### Relay push = re-query, not render-the-payload

Both SPAs treat hub pushes as cache-invalidation signals and re-query the authoritative endpoint, honoring the M7 §5 eventual-consistency contract. No SPA renders a push payload as authoritative state.

### PWA posture — resolved against accepted ADR 013

ADR 013 commits to `vite-plugin-pwa` "from day one." Whether the **S1 proof app** wires the manifest + service worker immediately or defers PWA wiring to the first real SPA-shell slice is an S1 open question to settle against the accepted ADR — lean toward the ADR's day-one stance; if deferring on the minimal proof, record why and carry it forward so the first real bidder-shell slice honors it. The PWA *offline scope* (app-shell only vs caching listings vs queueing offline bids) stays an ADR-013-deferred question captured in a frontend skill, not M8 milestone scope.

### Internal-doc prose conventions

ADRs, prompts, retros, and this milestone doc follow the project's internal-doc conventions; em-dash hygiene is external-prose-only and does not apply here.

---

## 7. Slice Breakdown

M8 is planned as seven slices: a foundation-decisions slice (the only one whose decision density is in ADRs, not code), three bidder-app slices, two ops-dashboard slices, and an end-to-end + housekeeping close. The breakdown is a **scope ceiling refined in per-slice prompts**, mirroring the M5–M7 cadence; the deferred ADR decisions (routing, ADR 026) are resolved inside the slices that need them, not pre-decided here.

| Slice | Title | Scope |
|---|---|---|
| M8-S1 | Frontend Foundation Decisions — ADR 013 acceptance + SPA monorepo-layout ADR + `BiddingHub` connection proof | Flip ADR 013 `Proposed` → `Accepted` (revising only on a recorded 2026-ecosystem reason; five Deferred Questions stay deferred). Author the SPA monorepo-layout ADR (source layout, build-output integration, dev-server proxy-vs-CORS) — `Accepted`, alternatives weighed. Scaffold **one** minimal Vite + React + TS-strict proof app at the ADR's path, Tailwind v4 base wired, `@microsoft/signalr` pinned, opening a live `HubConnection` to the anonymous `/hub/bidding`, registering `ReceiveMessage`, `.withAutomaticReconnect()`, rendering connection state. The proof is a *connection*, not a journey. `CLAUDE.md` frontend pointer added. No second SPA, no `OperationsHub`, no catalog/bid/TanStack data wiring. |
| M8-S2 | Bidder SPA Shell + Catalog | Promote/scaffold the bidder app at its `client/bidder/` home; resolve ADR 013's **routing** deferred question (record the pick); app shell + layout; Tailwind v4 + shadcn/ui baseline; PWA wiring per the accepted ADR; anonymous session start; TanStack Query wired to the public catalog + listing-detail endpoints (read-only). Narratives 001 (Moments 1–2), 003. No live bidding yet. |
| M8-S3a | Bid Placement Endpoint (backend precursor) | **Sanctioned backend exception (see §3).** Wire `POST /api/auctions/bids` — a thin `[AllowAnonymous]` HTTP entry over the *existing* internal `PlaceBid` DCB command — returning bid **acceptance vs rejection** (reason → ProblemDetails) so the frontend can optimistic-update + roll back. The DCB handler, rejection rules (below-minimum, exceeds-ceiling, listing-closed, seller-cannot-bid), and the $1/$5 bid-increment policy already exist (`PlaceBidHandler`); this slice designs the HTTP contract over them — including a result path (the bus handler currently returns void) and **server-side credit-ceiling sourcing** (the browser must not supply its own) — plus integration tests. No frontend. |
| M8-S3b | Bidder Live Bidding + ADR 026 (frontend) | Author **ADR 026** (SignalR integration pattern: Provider + `useListen` hook + TanStack Query cache bridge) now that the first hub is wired from the client. Wire `BiddingHub`: live bid feed, bid placement (against the M8-S3a endpoint) with optimistic update + rollback, outbid push, extended-bidding banner, gavel-fall. Narrative 001 Moments 3–7. |
| M8-S3c | ADR 027 Sticky Queue Bindings (backend housekeeping) | **Sanctioned backend exception, inserted after S3b** (precedent: the v0.2 S3a/S3b split) — implements **ADR 027**, accepted at the Bug #2 follow-ups session. Bind every broker-fed contract-event consumer sticky to its BC's own queue (`[StickyHandler]`; method-level where one class spans queues) so each integration event is consumed **exactly once, on the queue its BC owns** — eliminating the Separated fan-out's N-copies behavior and the Bug #3 saga-start dead-letter class. Adds the `auctions-auctions-events` **and** `settlement-settlement-events` self-consumption queues (the audit found Settlement consumes its own `SettlementCompleted`/`PaymentFailed`); removes two consumer-less routes; consolidates four same-BC same-event handler pairs (Wolverine executes one sticky chain per message type + endpoint). First-ever live verification of the BIN and withdrawal journeys. No contract changes, no journey changes — behavior-preserving except duplicate-delivery side effects disappearing (and two races those duplicates had been masking, fixed: saga `IRevisioned` enforcement, order-tolerant catalog settlement transition). |
| M8-S4 | Bidder Settlement Outcome | Settlement-result view (won/charged confirmation) from the bidder vantage. Narrative 001 Moment 8 + narrative 002. No seller surfaces (the seller console is out of M8 per §3). |
| M8-S5 | Ops SPA Shell + Staff Auth + `OperationsHub` | Scaffold the ops app at `client/ops/`; `StaffToken` config + `X-Staff-Token` HTTP + `access_token` hub negotiate (ADR 024); projector-legible high-contrast shell; staff-gated `OperationsHub` connection (the credential dance the S1 proof deliberately skipped). |
| M8-S6 | Ops Dashboard Views | Lot board, bid-activity feed, settlement queue, obligation/dispute queue (narrative 008), session/participant activity over `/api/operations/*`; render-time `Title` join from `/api/listings/{id}`; Relay-push = re-query wiring. |
| M8-S6b | Relay Ops-Feed Completion + Dispute-Resolution Control | **Sanctioned backend exception (third; precedent S3a/S3c) plus a sanctioned scope addition, inserted before S7** — the two open decisions recorded at M8-S6 close, decided 2026-06-10/11 (`docs/research/ops-feed-completion-evaluation-comparison.md` §Decision + Addendum). **(a) Ops-feed completion:** every integration event consumed by an Operations BC handler gains a corresponding `OperationsFeedNotification` publication in Relay (dual-push template; the evaluations' 10-event gap inventory is the starting list, the invariant decides final membership), enforced going forward by a topology test; cache-bridge mappings added; the `PUSH_GAP_REFETCH_INTERVAL_MS` polling stopgap deleted; an `onreconnected` one-shot `["operations"]` invalidation replaces the polling's accidental reconnection recovery. **(b) Dispute-resolution control (narrative 008 Moment 2):** a frontend-only "Resolve with extension" action on the dispute card posting the existing `StaffOnly` `POST /api/obligations/disputes/resolve` (202 Accepted; the `DisputeResolved` push already re-queries both queues). `Refund`/`Closed` stay un-surfaced (refund compensation is an M6/M7 non-goal). No contract changes, no new domain capability. |
| M8-S7 | End-to-End + Housekeeping | Playwright multi-context two-bidder bid-war e2e against a running API host; CI **frontend** build/test job (the backend matrix is already complete per PR #77); `bounded-contexts.md`/`STATUS.md` updates; test-baseline update; M8 retrospective. |

---

## Document History

- **v0.6** (2026-06-12): Closed at M8-S7. Status flipped to ✅ Complete; §1 exit criteria all ticked with honest annotations — the 281→307 backend-test growth attributed to the three sanctioned exceptions (S3a/S3c/S6b) + the Bug #2 interlude; the `access_token`-on-negotiate wording marked overtaken by the lived SignalR 7+ transport (M8-S5); the extended-bidding *banner* recorded as structurally unreachable from the lived read model (Listings has no `ExtendedBiddingTriggered` handler — M8-S7 e2e finding, carried forward). §2 refreshed: SPA homes no longer "working names"; the CI paragraph reflects the full-workspace frontend job and the recorded Playwright-in-CI deferral. The M8 milestone retrospective is the canonical close record.
- **v0.5** (2026-06-11): Added the **M8-S6b** row to the §7 slice ladder, closing both open decisions recorded at M8-S6 close. **Decision 1** (Relay ops-feed completion vs. living with the polling stopgap): sanctioned 2026-06-10 after two independent architectural evaluations converged on sanction (`docs/research/ops-feed-completion-evaluation*.md`); runs pre-S7 as its own slice with Evaluation A's `onreconnected` reconciliation and enforced topology invariant in scope and Evaluation B's 10-event gap inventory as the scope count. **Decision 2** (dispute-resolution controls vs. view-only M8): sanctioned 2026-06-11 (Erik; recorded in the comparison doc's Addendum) — a frontend-only Extension-resolution control over the already-proven `POST /api/obligations/disputes/resolve`, folded into the same S6b slice rather than running as its own S6c. S7 is unchanged and lands against the finished posture.
- **v0.4** (2026-06-09): Amended at the M8-S3c implementation session per the S3c prompt's first deliverable. Added the **M8-S3c** row to the §7 slice ladder — a backend-housekeeping slice inserted after S3b (precedent: the v0.2 amendment that split S3 into S3a/S3b) implementing **ADR 027** (per-BC sticky queue bindings), which was accepted at the 2026-06-09 Bug #2 follow-ups session. The §1 "seven slices" framing is superseded by the ladder itself (now nine rows); S4–S7 are unchanged. The slice is the milestone-sanctioned second backend exception: infrastructure truth-restoration only — no contract change, no new domain capability, no frontend change.
- **v0.3** (2026-06-08): Corrected the stale `ADR 014` references for the SignalR integration pattern to **`ADR 026`** at the M8-S3b implementation step. The number `014` was already taken — ADR 014 is the accepted *Cross-BC Read-Model Extension Shape* (M5-S6) — so every `ADR 014` / `ADR-014` reference in this doc (§1, exit criteria, §3, §4 solution layout, §5, §6 heading + body, §7 slice ladder + the M8-S3b row, and the v0.1/v0.2 history rows) was a forward-reference collision. M8-S3b authored the pattern as **ADR 026** (`SignalRProvider` + `useListen` + TanStack Query cache bridge), the next unreserved number per `docs/decisions/README.md`, and resolved ADR 013's matching Deferred Question to the same number. Corrected in this doc verbatim (number only; the surrounding prose, which describes deferring the pattern to its own ADR, was already accurate). No scope change.
- **v0.2** (2026-06-08): Amended at the M8-S3 planning step. A backend-reality finding — bid placement (narrative 001 Moment 4) has **no HTTP endpoint**; `PlaceBid` exists only as an internal Wolverine/DCB command, while the live-bidding *read* side is fully wired on `BiddingHub` — surfaced a conflict with the §3 "no new endpoints" non-goal. Resolved (Erik, 2026-06-08) by splitting the original **M8-S3** into a **backend precursor M8-S3a** (a thin `[AllowAnonymous]` `POST /api/auctions/bids` over the existing `PlaceBid` DCB command, the one sanctioned backend exception) and the **frontend M8-S3b** (live bidding + ADR-026). §1, §3, and the §7 slice ladder updated; S4–S7 unchanged. No new domain capability is added — only an HTTP contract over existing behavior.
- **v0.1** (2026-06-04): Authored as the M8 opening artifact (milestone-scoping session), after the `M8-S1-frontend-foundation-decisions` prompt's Preconditions gate flagged the absence of this doc and the work was escalated. Scope derived from the M7 retrospective's "What's Next — M8" / "What M8 Should Know" sections, the M7→M8 handoff §3+§5 (orientation, not scope authority), `bounded-contexts.md` (§"two SignalR hubs", §"separate React SPAs pointing at the same API host", §Operations projector framing), narratives 001/002/003 (bidder spine) and 008 (operator vantage), and ADRs 004/012/013/023/024. Scope reviewed against the M5–M7 seven-slice cadence: the two foundation decisions (ADR 013 acceptance + the SPA-layout ADR) were framed as the headline S1 deliverables rather than pre-decided here; ADR 013's five Deferred Questions and ADR 026 were left to the slices that need them; the **seller console was scoped fully out of M8** (a planned future milestone, not an M8 default-defer), leaving the committed surface as the bidder journey + operator dashboard; the CI work owed by M8 was narrowed to a **frontend build/test job only** after confirming the backend integration matrix already covers all eight BC suites + Api (`.github/workflows/ci.yml`, PR #77 — the M7 retro's "~44% local only" note is stale). Status `Planned`; M8-S1 not yet started.
