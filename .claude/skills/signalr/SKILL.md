---
name: signalr
description: >-
  CritterBids React SignalR client conventions for the bidder and ops SPAs
  (client/bidder, client/ops). Covers the useBiddingHub connection-lifecycle hook,
  HubConnection build/start/reconnect/teardown, the ReceiveMessage raw-record
  contract (ADR-023 — NO CloudEvents envelope), per-hub auth (anonymous BiddingHub
  vs StaffToken OperationsHub access_token negotiate, ADR-024), same-origin /hub
  URLs via the Vite dev proxy (ADR-025), and the forthcoming SignalR → TanStack
  Query cache bridge (ADR-014, built M8-S3). Use when writing, reviewing, or
  debugging SignalR client code in the CritterBids frontend.
---

# CritterBids SignalR — React Client

> The **client** side of CritterBids real-time. The **server** side (Relay hubs,
> handlers, group targeting, the broadcast architecture) lives in the backend skill
> `docs/skills/wolverine-signalr/SKILL.md`. This skill documents only what runs in
> the browser: the `HubConnection`, its lifecycle, the wire contract the client
> deserializes, per-hub auth, and how pushes reach the UI.
>
> Generic `@microsoft/signalr` API mechanics are well-trained; reach for `find-docs`
> / Context7 for raw library questions. This skill captures only what is
> **CritterBids-specific** and the decisions a fresh agent would otherwise get wrong.

## When to apply

- Writing or editing any `HubConnection` code in `client/bidder/` or `client/ops/`.
- Adding a live-update hook (bid feed, outbid banner, ops board refresh).
- Wiring the `OperationsHub` staff-token connection (M8-S5).
- Bridging hub pushes into the TanStack Query cache (M8-S3+, ADR-014).
- Debugging "the connection drops" / "the payload is `undefined`" / "ghost connections".

Do **not** use this for backend hub/handler/group-routing work — that is
`docs/skills/wolverine-signalr/SKILL.md` and ADR-023.

## The two hubs

| Hub | URL | Audience | Client auth | Built in |
|---|---|---|---|---|
| `BiddingHub` | `/hub/bidding` | public bidders | **none** (anonymous) | `client/bidder/` — proof exists (M8-S1); live feed M8-S3 |
| `OperationsHub` | `/hub/operations` | staff/operators | `StaffToken` via `access_token` on negotiate (ADR-024) | `client/ops/` — M8-S5 (not yet built) |

Both are **outbound-only** in CritterBids: the server pushes, the client listens.
Client→server actions (placing a bid) go through **HTTP endpoints**, not the hub.
Do not reach for `connection.invoke(...)` / `connection.send(...)` to mutate state —
that is not how CritterBids is wired (ADR-023 §Request/reply posture).

## Connection lifecycle — the canonical hook

The established shape is `client/bidder/src/useBiddingHub.ts`. **One `HubConnection`
per hook, lifetime tied to the component** (start on mount, stop on unmount). Copy
this skeleton for any new live-update hook:

```typescript
useEffect(() => {
  const connection = new HubConnectionBuilder()
    .withUrl("/hub/bidding")          // same-origin; see URL rule below
    .withAutomaticReconnect()         // non-negotiable (ADR-013)
    .configureLogging(LogLevel.Information)
    .build();

  const syncStatus = () => setStatus(connection.state);

  connection.on("ReceiveMessage", (payload: unknown) => { /* see contract below */ });

  connection.onreconnecting(error => { setLastError(error?.message ?? "reconnecting"); syncStatus(); });
  connection.onreconnected(()    => { setLastError(null); syncStatus(); });
  connection.onclose(error       => { setLastError(error?.message ?? null); syncStatus(); });

  let cancelled = false;            // guards setState after unmount
  setStatus(HubConnectionState.Connecting);
  connection.start()
    .then(()    => { if (!cancelled) { setLastError(null); syncStatus(); } })
    .catch(err  => { if (!cancelled) { setLastError(/* message */); syncStatus(); } });

  return () => { cancelled = true; void connection.stop(); };  // MANDATORY cleanup
}, []);                              // empty deps — connection is created once
```

Non-negotiable lifecycle rules:

1. **Always return a cleanup that calls `connection.stop()`.** Without it, React
   Strict Mode's double-mount and every remount leak a live socket ("ghost
   connections"). This is the single most common SignalR-client bug.
2. **`.withAutomaticReconnect()` always** (ADR-013). Conference Wi-Fi drops; the
   client must recover without a page reload.
3. **Guard async `setState` with a `cancelled` flag.** `start()` resolves
   asynchronously; the component may already be unmounted. Setting state on an
   unmounted component is a bug the flag prevents.
4. **Empty dependency array.** The connection is built once for the component's
   life. If the URL/group must change (e.g. switch which listing you watch),
   prefer tearing down and rebuilding via a keyed remount over mutating a live
   connection.
5. **Surface `connection.state` to the UI.** Bidders need to see Connecting /
   Connected / Reconnecting / Disconnected — a silent dead socket erodes trust in
   a live-bidding UX.

## URL & dev-proxy rule (ADR-025)

**Always use a same-origin relative path: `/hub/bidding`, `/hub/operations`.**
Never hardcode `http://localhost:5180` or any absolute origin.

- **Dev:** the Vite dev server proxies `/api` and `/hub` (with `ws: true`) to the
  API host at `:5180`. The browser stays same-origin, so there is no CORS and the
  WebSocket upgrade tunnels through the proxy. This is why no `AddCors`/`UseCors`
  exists on the API host — do not add one to "fix" a dev connection problem;
  check the Vite proxy config instead.
- **Prod:** each SPA is served from the same host that maps the hubs, so the same
  relative path resolves directly.

## The `ReceiveMessage` contract (ADR-023) — read carefully

The server method name is **`ReceiveMessage`** for every push, on both hubs.

**The payload is the raw notification record, delivered directly. There is NO
CloudEvents envelope.** ADR-023 chose plain-Hub direct `IHubContext` push (path b)
and explicitly rejected the Wolverine SignalR *transport* (path a) that would have
wrapped messages in a CloudEvents envelope.

```typescript
// ✅ CORRECT (ADR-023 path b) — the handler argument IS the domain record
connection.on("ReceiveMessage", (notification: unknown) => {
  // validate notification through a Zod schema (ADR-013), then use it
});
```

```typescript
// ❌ WRONG — this is path-(a) envelope code that ADR-023 REJECTED.
// CritterBids payloads have no `.type` and no `.data`; both are undefined.
connection.on("ReceiveMessage", cloudEvent => {
  const typeName = (cloudEvent.type ?? "").split(".").pop();  // always undefined here
  const data = cloudEvent.data;                                // always undefined here
});
```

> ⚠️ The backend skill `docs/skills/wolverine-signalr/SKILL.md` still contains a
> stale "CloudEvents type format" client snippet using `cloudEvent.type` /
> `.split(".").pop()`. That snippet predates ADR-023's path-(b) decision and is
> wrong for the CritterBids client. **ADR-023 is the authority.** Do not copy that
> snippet; if you see it cited, prefer this skill + ADR-023.

### Open question — message-type discrimination (M8-S3)

Because there is no envelope `type`, how the client distinguishes a `BidPlaced`
from a `BidderOutbid` (both arriving on `ReceiveMessage`) is **not yet pinned
down** — the proof hook captures the payload untyped on purpose. The discriminator
(a field on the record, or distinct hub-method names) **plus** the Zod schema that
validates each shape at the wire boundary is **M8-S3 live-bidding work**. Do not
invent a discrimination scheme here; resolve it in M8-S3 against the actual Relay
notification records and record it in ADR-014. When in doubt, confirm the record
shapes against `CritterBids.Relay`'s notification types and ADR-023.

## Per-hub auth

### BiddingHub — anonymous

No credential. The hub is `[AllowAnonymous]`; the proof connects with a bare
`.withUrl("/hub/bidding")`. Send no token.

### OperationsHub — StaffToken (ADR-024), built M8-S5

SignalR clients **cannot set custom headers on the negotiate POST**, so the staff
credential rides as the `access_token` **query string** on negotiate. The
`@microsoft/signalr` client does this for you via `accessTokenFactory`:

```typescript
new HubConnectionBuilder()
  .withUrl("/hub/operations", { accessTokenFactory: () => getStaffToken() })
  .withAutomaticReconnect()
  .build();
```

Rules:

- The staff token comes **from config, never hardcoded** (ADR-024). `getStaffToken()`
  reads it from the app's configured source; it is the same `StaffToken` used as the
  `X-Staff-Token` **header** on staff HTTP calls.
- Header for HTTP, `access_token` query string for the hub negotiate — two transports,
  one secret.
- Handle **401** (missing/invalid token). **403 is structurally unreachable** under
  the single-shared-secret posture, so don't build a 403 branch.

(`client/ops/` does not exist yet — this is the prescribed shape for M8-S5, grounded
in ADR-024 and the library's documented `accessTokenFactory`, not yet lived code.)

## SignalR → TanStack Query cache bridge — STUB (ADR-014, M8-S3)

**Not yet designed or built. Do not author the bridge from this skill.**

Current reality: the proof hook holds messages in local `useState`; `client/bidder/`
has **no TanStack Query dependency** at all. The integration pattern — a
`SignalRProvider` Context, a `useListen(event, handler)` hook, and the bridge into
the TanStack Query cache — is **deferred to ADR-014** (ADR-013 §Deferred Questions),
authored at **M8-S3** when the first hub is wired into the real app. Its code will
live in the planned `client/shared/` (the frontend analogue of `CritterBids.Contracts`).

The **one constraint already decided** that the bridge must honor:

> **A hub push is a "re-query" signal, not authoritative data.** Treat
> `ReceiveMessage` as cache invalidation — `queryClient.invalidateQueries(...)` for
> the affected key — and refetch the authoritative HTTP endpoint. **Do not render
> the push payload as truth** and do not treat it as read-your-own-write. This is the
> M7 §5 eventual-consistency contract between Relay push and the read models
> (`docs/milestones/M7-operations-bc.md` §5; M8 milestone §"Relay push = re-query").
>
> (Bidder optimistic updates are a separate concern: a *local* optimistic bid with
> rollback on HTTP rejection is fine; that is your own write, not a hub payload.)

When M8-S3 lands ADR-014, replace this stub with the lived pattern and cross-link it.

## Pitfalls

- **Missing `connection.stop()` cleanup** → ghost connections (the #1 bug).
- **Copying the backend skill's `cloudEvent.type` snippet** → `undefined` on every
  branch; the payload is the raw record (ADR-023).
- **Hardcoding `http://localhost:5180`** → breaks the same-origin dev proxy and prod;
  use relative `/hub/...`.
- **Adding `AddCors`/`UseCors` to the API host** to fix a dev connection → wrong layer;
  the Vite proxy (ADR-025) is the mechanism. A backend CORS change is escalated, not
  made silently.
- **Hardcoding the staff token** → ADR-024 violation; read from config.
- **Rendering a push payload as authoritative state** → violates the M7 §5 re-query
  contract; invalidate + refetch instead.
- **`connection.invoke(...)` to place a bid** → bids go over HTTP; the hub is
  outbound-only.
- **Inventing a message-type discriminator** before M8-S3 → confirm the real Relay
  record shapes first.

## See also

- **`docs/skills/wolverine-signalr/SKILL.md`** — the server side (Relay hubs,
  handlers, group keys, broadcast architecture). Its CloudEvents *client* snippet is
  stale per ADR-023; everything server-side is authoritative.
- **ADR-023** (`docs/decisions/023-relay-reactive-broadcast-architecture.md`) — plain
  Hub + direct `IHubContext`, no CloudEvents envelope. The wire-contract authority.
- **ADR-024** (`docs/decisions/024-staff-token-authentication.md`) — StaffToken,
  `X-Staff-Token` header vs `access_token` negotiate query string.
- **ADR-025** (`docs/decisions/025-spa-monorepo-layout.md`) — `client/` layout, Vite
  dev proxy (`ws: true`), build-output integration.
- **ADR-013** (`docs/decisions/013-frontend-core-stack.md`) — `@microsoft/signalr`,
  TanStack Query, Zod-at-the-boundary; defers the integration pattern to ADR-014.
- **ADR-014** — SignalR integration pattern (Provider + `useListen` + cache bridge).
  *Forthcoming, M8-S3.* Update this skill's bridge stub when it lands.
- **Global skills** that auto-activate alongside this one: `tanstack-query-best-practices`
  (the cache bridge), `zod` (wire-boundary validation), `vercel-react-best-practices`
  (hook/effect correctness), `vitest` / `playwright` (testing the live connection).
