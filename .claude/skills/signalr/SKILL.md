---
name: signalr
description: >-
  CritterBids React SignalR client conventions for the bidder and ops SPAs
  (client/bidder, client/ops). Covers the ADR-026 integration pattern (app-wide
  SignalRProvider Context + useListen + TanStack Query cache bridge), the
  ReceiveMessage raw-record contract and its Zod normalization (ADR-023 — NO
  CloudEvents envelope, no wire discriminator), per-hub auth (anonymous BiddingHub
  vs StaffToken OperationsHub — accessTokenFactory + skipNegotiation, ADR-024),
  same-origin /hub URLs via the Vite dev proxy (ADR-025), and push-fed dedupe
  rules. Use when writing, reviewing, or debugging SignalR client code in the
  CritterBids frontend.
---

# CritterBids SignalR — React Client

> The **client** side of CritterBids real-time. The **server** side (Relay hubs,
> handlers, group targeting, the broadcast architecture) lives in the backend skill
> `docs/skills/wolverine-signalr/SKILL.md`. This skill documents only what runs in
> the browser: the connection ownership pattern, the wire contract the client
> deserializes, per-hub auth, and how pushes reach the UI.
>
> Generic `@microsoft/signalr` API mechanics are well-trained; reach for `find-docs`
> / Context7 for raw library questions. This skill captures only what is
> **CritterBids-specific** and the decisions a fresh agent would otherwise get wrong.

## When to apply

- Writing or editing any `HubConnection` code in `client/bidder/` or `client/ops/`.
- Adding a live-update surface (bid feed, outbid banner, ops board refresh).
- Touching the `OperationsHub` staff-token connection or the auth gate around it.
- Bridging hub pushes into the TanStack Query cache (ADR-026).
- Debugging "the connection drops" / "the payload is `undefined`" / "401 on connect" /
  "ghost connections" / duplicate feed entries.

Do **not** use this for backend hub/handler/group-routing work — that is
`docs/skills/wolverine-signalr/SKILL.md` and ADR-023.

## The two hubs

| Hub | URL | Audience | Client auth | Lived code |
|---|---|---|---|---|
| `BiddingHub` | `/hub/bidding` | public bidders | **none** (anonymous) | `client/bidder/src/signalr/` (full ADR-026 pattern, M8-S3b) |
| `OperationsHub` | `/hub/operations` | staff/operators | `StaffToken` via `accessTokenFactory` + `skipNegotiation` (see § Per-hub auth) | `client/ops/src/signalr/` (S5 plumbing; cache bridge arrives M8-S6) |

Both are **outbound-only** in CritterBids: the server pushes, the client listens.
Client→server actions (placing a bid) go through **HTTP endpoints**, not the hub.
Do not reach for `connection.invoke(...)` / `connection.send(...)` to mutate state —
the only invokes are the BiddingHub group joins (`JoinListingGroup` / `JoinBidderGroup`).

## The integration pattern (ADR-026) — one Provider, hooks, cache bridge

**One app-wide `HubConnection` per SPA, owned by a Provider Context mounted above the
router.** This superseded M8-S2's `useBiddingHub` (a connection per component) — do
not copy any per-component-connection hook; it predates ADR-026.

The three parts (bidder reference implementation in `client/bidder/src/signalr/`):

1. **`SignalRProvider`** (`SignalRProvider.tsx`) — builds the connection
   (`.withAutomaticReconnect()` non-negotiable, single `ReceiveMessage` handler),
   owns start/stop in one mount-scoped effect, re-joins watched groups on reconnect,
   exposes `status` / `lastError` and a `subscribe` fan-out. The connection factory
   is injectable (`createConnection` prop) so tests drive pushes through a fake
   connection without touching the production path.
2. **`useListen(handler)`** (`hooks.ts`) — component subscription to **parsed**
   messages, handler held in a ref so the subscription registers once. For
   **transient affordances only** (activity ticker, toast) — never to render payload
   fields as authoritative state. Companions: `useHubConnectionState()`,
   `useWatchListing(id)`.
3. **The TanStack Query cache bridge** (`cacheBridge.ts`) — the load-bearing rule:
   **a hub push is a "something changed, refetch" signal, never authoritative data.**
   `applyHubMessage(queryClient, message)` translates every parsed message into
   `invalidateQueries` calls (`["listing", id]`, `["catalog"]`); the query functions
   re-fetch the authoritative read model. The Provider runs the bridge **first**,
   then fans out to `useListen` subscribers — by the time a listener runs, the
   re-query is already in flight. No push field is ever written into the cache as
   truth. (Bidder optimistic updates are a separate concern: a *local* optimistic
   bid reconciled against the HTTP 200 and rolled back on rejection is your own
   write, not a hub payload.)

The ops app replicates the Provider shape with the staff credential
(`client/ops/src/signalr/SignalRProvider.tsx`); its parse surface + cache bridge are
M8-S6 work. When the ops app becomes the second full consumer, the shared pieces move
to `client/shared/` (ADR-025) — until then, small duplication (`RECEIVE_MESSAGE`) is
tolerated.

Live beats are **derived from view transitions, not push payloads**: "outbid" =
the held participant was high bidder and the re-queried view's `currentHighBidderId`
flipped away; "extended bidding" = `scheduledCloseAt` moved later; "you won" =
terminal status + `winnerId` match. There is **no server `Outbid` push** — do not
wait for one.

## URL & dev-proxy rule (ADR-025)

**Always use a same-origin relative path: `/hub/bidding`, `/hub/operations`.**
Never hardcode `http://localhost:5180` or any absolute origin.

- **Dev:** each SPA's Vite dev server proxies `/api` and `/hub` (with `ws: true`) to
  the API host at `:5180`. The browser stays same-origin, so there is no CORS and the
  WebSocket upgrade tunnels through the proxy. This is why no `AddCors`/`UseCors`
  exists on the API host — do not add one to "fix" a dev connection problem; check
  the Vite proxy config instead. Both SPAs run simultaneously (bidder `:5173`, ops
  `:5174` at base `/ops/`).
- **Prod:** each SPA is served from the same host that maps the hubs, so the same
  relative path resolves directly.

## The `ReceiveMessage` contract (ADR-023) and its normalization (ADR-026)

The server method name is **`ReceiveMessage`** for every push, on both hubs.
**The payload is the raw notification record, delivered directly. There is NO
CloudEvents envelope** — no `.type`, no `.data`, nothing to `.split(".")`.

**There is also no wire discriminator.** The lived Relay contract is heterogeneous
(M8-S3b finding): some records carry an `eventType` string, others are distinguished
only structurally (presence of `bidId` vs `winnerId` vs `settlementId`). The client
therefore normalizes at the boundary — `parseHubMessage(payload)`
(`client/bidder/src/signalr/messages.ts`) tries Zod schemas most-specific-first and
returns one discriminated union with a client-assigned `kind`
(`bidPlaced | listingSold | settlementCompleted | bidderEvent | listingEvent`), or
**`null` for an unrecognized shape** — logged-and-ignored, never thrown, so a future
notification type cannot tear down the connection.

```typescript
// ✅ The Provider's single handler (ADR-026): parse → bridge → fan out.
connection.on(RECEIVE_MESSAGE, (payload: unknown) => {
  const message = parseHubMessage(payload);
  if (message === null) return;          // forward-compatible: ignore unknown shapes
  applyHubMessage(queryClient, message);  // re-query FIRST
  for (const listener of listeners) listener(message);
});
```

Wire facts: System.Text.Json web defaults — camelCase keys, decimals as JSON numbers,
Guids/DateTimeOffsets as strings. Confirm record shapes against
`src/CritterBids.Relay/Notifications/` before adding a schema; the rest of the app
switches on `kind` only.

## Per-hub auth

### BiddingHub — anonymous

No credential. The proof is a bare `.withUrl("/hub/bidding")`. Send no token.

### OperationsHub — StaffToken (ADR-024), lived shape from M8-S5

The backend `StaffToken` scheme reads the hub credential **only** from the
`access_token` query string on the `/hub/operations` path (and the `X-Staff-Token`
header on every other path; never a query string on HTTP).

**The v7+ client transport trap:** since SignalR 7, `@microsoft/signalr` delivers
`accessTokenFactory` tokens to HTTP requests — **including the negotiate POST** — as
an `Authorization: Bearer` header (`AccessTokenHttpClient`); the `access_token`
query parameter is appended **only to the browser WebSocket upgrade**, where headers
are impossible. Against a query-string-only scheme, a default negotiate-first
`start()` therefore **401s before any WebSocket opens**. The lived resolution:

```typescript
new HubConnectionBuilder()
  .withUrl("/hub/operations", {
    transport: HttpTransportType.WebSockets,
    skipNegotiation: true,                       // the WS upgrade — the request that
    accessTokenFactory: () => getStaffToken(),   // carries ?access_token= — is the
  })                                             // connection's ONLY request
  .withAutomaticReconnect()
  .build();
```

Trade-offs, accepted for the staff dashboard: no SSE/long-polling fallback, and
`skipNegotiation` is incompatible with Azure SignalR Service. The post-MVP
alternative is the backend scheme learning the Bearer-header read (the recorded
ADR-024 JWT migration path) — a backend change, escalated, never a frontend
workaround beyond the above.

Rules around the credential (lived in `client/ops/src/auth/`):

- **One secret, two transports:** the same token rides `X-Staff-Token` on staff HTTP
  (via `createStaffFetch`) and `access_token` on the hub upgrade. Never hardcode it;
  the operator enters it at the auth gate and it is held in `sessionStorage`.
- **Validate before storing.** Browsers report a 401 WS upgrade as an opaque
  `close 1006` with no status — the hub cannot tell you "wrong token." The gate
  therefore probes a real staff GET (`/api/operations/lot-board`) with the candidate
  in `X-Staff-Token` **before** storing it: 2xx → store, 401 → "rejected" at the
  gate, 5xx/network → "API unreachable" (distinct from a wrong token).
- **Mount the credentialed Provider inside the auth gate.** No token → no connection
  attempt; clearing the token unmounts the Provider, whose effect cleanup stops the
  connection. No token-change reconnect logic needed — a token change is a remount.
- **Every 401 clears the stored token and re-shows the gate** (`createStaffFetch`
  funnels them). **403 is structurally unreachable** under the single-shared-secret
  posture — don't build a 403 branch.

## Push-fed surfaces must dedupe (at-least-once reaches the browser)

The broker topology can deliver an integration event to Relay more than once, and a
SignalR push has no server-side absorption — duplicates reach the browser. For any
transient push-fed UI (feeds, tickers):

- **Dedupe by the notification's identity field** (`BidPlacedNotification.BidId`),
  bounded (capped seen-set — see `client/bidder/src/bidding/LiveActivity.tsx`).
  A timestamp is NOT an identity: duplicate copies share `occurredAt`.
- **Never derive React list keys from timestamps or list length** — duplicate keys
  corrupt keyed reconciliation and strand ghost DOM nodes.
- Authoritative surfaces are naturally idempotent: the cache bridge re-queries the
  read model, and invalidating twice is harmless.

## Pitfalls

- **Default negotiate against the `OperationsHub`** → 401 before any socket opens;
  `accessTokenFactory` puts a Bearer header on negotiate, which the scheme does not
  read. Use `skipNegotiation: true` + WebSockets (§ Per-hub auth).
- **Treating a hub start failure as an auth signal** → a browser 401 upgrade is an
  opaque 1006; auth detection belongs on an HTTP probe with `X-Staff-Token`.
- **A connection per component / per hook** → superseded by the ADR-026 app-wide
  Provider; multiple redundant sockets per client.
- **Missing `connection.stop()` in the Provider's effect cleanup** → ghost
  connections. (In dev, StrictMode's double-mount logs one benign
  `Failed to start the HttpConnection before stop() was called` from the torn-down
  first connection — expected noise, not a bug, provided the cleanup exists.)
- **Copying any `cloudEvent.type` / `.split(".")` snippet** → `undefined` on every
  branch; the payload is the raw record (ADR-023).
- **Switching on a wire `type`/discriminator field** → none exists; parse through
  `parseHubMessage` and switch on the client-assigned `kind`.
- **Rendering a push payload as authoritative state** → violates the re-query
  contract; the bridge invalidates and the query re-fetches.
- **Hardcoding `http://localhost:5180`** → breaks the same-origin dev proxy and prod;
  use relative `/hub/...`.
- **Adding `AddCors`/`UseCors` to the API host** to fix a dev connection → wrong
  layer; the Vite proxy (ADR-025) is the mechanism.
- **`connection.invoke(...)` to mutate state** → commands go over HTTP; the hubs are
  outbound-only (group joins are the only invokes).
- **Timestamp- or length-derived React keys on push-fed lists** → duplicate keys;
  dedupe by message identity.

## See also

- **`docs/skills/wolverine-signalr/SKILL.md`** — the server side (Relay hubs,
  handlers, group keys, broadcast architecture). Same wire-contract story; keep the
  two in sync.
- **ADR-023** (`docs/decisions/023-relay-reactive-broadcast-architecture.md`) — plain
  Hub + direct `IHubContext`, raw record on `ReceiveMessage`. The wire-contract
  authority.
- **ADR-026** (`docs/decisions/026-signalr-integration-pattern.md`) — the Provider +
  `useListen` + cache-bridge pattern this skill summarizes; resolved ADR-013's
  deferred question (early docs called it "ADR-014" — that number belongs to the
  Cross-BC Read-Model Extension Shape; 026 is correct).
- **ADR-024** (`docs/decisions/024-staff-auth-posture-resumption.md`) — StaffToken:
  `X-Staff-Token` header for HTTP, `access_token` query string for the hub path.
- **ADR-025** (`docs/decisions/025-spa-monorepo-layout.md`) — `client/` layout, Vite
  dev proxy (`ws: true`), base paths, the planned `client/shared/` home.
- **Lived reference code:** `client/bidder/src/signalr/` (full pattern),
  `client/ops/src/signalr/` + `client/ops/src/auth/` (credentialed variant + gate).
- **M8-S5 retro** (`docs/retrospectives/M8-S5-ops-spa-shell-staff-auth-retrospective.md`)
  § S5.3 Discovery — the full accessTokenFactory transport analysis with package-source
  evidence.
- **Global skills** that auto-activate alongside this one: `tanstack-query-best-practices`
  (the cache bridge), `zod` (wire-boundary validation), `vercel-react-best-practices`
  (hook/effect correctness), `vitest` / `playwright` (testing the live connection).
