# ADR 026 — SignalR Integration Pattern (Provider + `useListen` + TanStack Query Cache Bridge)

**Status:** ✅ Accepted
**Date:** 2026-06-08 (M8-S3b)
**Deciders:** @PSA, @UXE
**Resolves:** ADR 013 Deferred Question — "SignalR integration pattern"

---

## Context

ADR 013 chose `@microsoft/signalr` as the real-time client library but deliberately **deferred the
integration pattern** — the React shape that wraps a `HubConnection` — until a real hub was wired
from a client. That precondition is now met: M8-S1 stood up a live `BiddingHub` connection proof,
M8-S2 promoted it into the bidder shell (a single `useBiddingHub` hook owning one connection inside
the connection-indicator component), and M8-S3a exposed `POST /api/auctions/bids` so the bidder app
has both halves of the live-bidding loop — a command surface and a push surface.

This ADR pins how the bidder app (and, later, the ops app) consumes SignalR. It builds **on top of**
two existing decisions and does not restate them:

- **ADR 023** owns the wire contract: plain `Hub` subclasses, `IHubContext`-injection from Wolverine
  handlers (path b), the single `ReceiveMessage` client method, and the raw notification record as
  the payload (no CloudEvents envelope).
- **The `wolverine-signalr` skill** owns the client-library conventions: `HubConnectionBuilder`,
  `.withAutomaticReconnect()`, the `listing:{id}` / `bidder:{id}` group keys, and the
  `JoinListingGroup` / `JoinBidderGroup` deterministic-join invocations.

What was **not** decided until now is the React composition: who owns the connection lifecycle, how
components subscribe, and — most importantly — what a push *means* to the UI.

### Two findings from the lived contract that shape this ADR

1. **The push wire is heterogeneous — there is no uniform `type` discriminator.** Four notification
   records reach a bidder client (`src/CritterBids.Relay/Notifications/`): `BidPlacedNotification`
   and `ListingSoldNotification` carry **no** `eventType` (they are distinguished structurally by
   `bidId` vs `winnerId`), while `ListingGroupNotification` and `BidderGroupNotification` carry an
   `eventType` string plus a human-readable `payload`. The client must normalize all four into one
   discriminated shape at the wire boundary.
2. **There is no `Outbid` server push.** Narrative 001 Moment 5 describes a targeted "Outbid" push,
   but the lived Relay surface fans out `BidPlaced` only. "Outbid" is therefore a **client-side
   derivation** (the held participant dropped from high bidder to not-high), not a server event. The
   pattern below makes this kind of derivation natural by treating the re-queried read model — not
   the push — as the source of truth.

---

## Decision

The integration pattern has three parts, plus a normalization layer and a derivation discipline.

### 1. `SignalRProvider` — one app-wide connection in React Context

A single `SignalRProvider` Context owns **one** `HubConnection` for the whole bidder app, mounted
above the router so it survives navigation (`client/bidder/src/signalr/SignalRProvider.tsx`). It:

- builds the connection with the ADR 023 conventions (anonymous `/hub/bidding`,
  `.withAutomaticReconnect()`, the single `ReceiveMessage` handler);
- enrols the held `ParticipantId` into its `bidder:{id}` group once the session is known, and
  re-joins watched `listing:{id}` groups on every (re)connect;
- exposes connection `status` / `lastError`, a `watchListing(id)` enrolment method, and a
  `subscribe(listener)` fan-out — consumed only through the hooks below.

The connection factory is injectable (`createConnection` prop) so tests drive the channel with a
fake `HubConnection` without touching the production path. This **supersedes M8-S2's `useBiddingHub`**,
which opened its own connection inside a single component; the app now has exactly one connection.

### 2. `useListen(handler)` — component subscription to parsed messages

Components subscribe to parsed hub messages via `useListen((message) => …)`
(`client/bidder/src/signalr/hooks.ts`). The handler is held in a ref so the subscription registers
once and never drops a message across a re-render. `useListen` is for **transient affordances** — a
live activity ticker, a toast — never for rendering payload fields as authoritative state.
Companion hooks: `useHubConnectionState()` (status UI) and `useWatchListing(id)` (group enrolment for
a route's lifetime).

### 3. The TanStack Query cache bridge — push = re-query, never render-the-payload

**This is the load-bearing rule** (milestone §6 / M7 §5). A hub push is a "something changed,
refetch" signal, not an authoritative payload. The bridge (`client/bidder/src/signalr/cacheBridge.ts`)
is a pure function `applyHubMessage(queryClient, message)` that translates every parsed message into
TanStack Query **invalidations** (`["listing", id]`, and the `["catalog"]` list incidentally). The
existing query functions then re-fetch the authoritative read model (`CatalogListingView` over
`GET /api/listings/{id}`). No push field is ever written into the cache as truth.

The `SignalRProvider`'s single `ReceiveMessage` handler runs the bridge **first**, then fans the
message out to `useListen` subscribers — so by the time a component's listener runs, the re-query is
already in flight and the component reads fresh data from the query, not from the message.

### 4. Wire normalization at the boundary (Zod)

`parseHubMessage(payload)` (`client/bidder/src/signalr/messages.ts`) parses the four heterogeneous
records — via Zod schemas tried most-specific-first — into one discriminated `HubMessage` union
(`bidPlaced | listingSold | listingEvent | bidderEvent`). An unrecognized payload returns `null`
(logged-and-ignored, never fatal) so a future notification type cannot tear down the connection. This
is the ADR 013 "Zod at the wire boundary" rule applied to the push surface, mirroring the HTTP-body
schemas.

### 5. Live affordances are derived from view transitions, not from push payloads

Because the re-queried `CatalogListingView` is the source of truth, every live beat is derived from
how that view **changes** between re-queries, keeping payloads out of authoritative state:

| Beat | Derivation (from the re-queried view) |
|---|---|
| Outbid | held participant was high bidder, then `currentHighBidderId` flips away while still Open |
| Extended bidding | `scheduledCloseAt` moves later than the previously-observed value |
| Gavel / sold | `status` reaches a terminal value; "you won" iff `winnerId === ParticipantId` |

The optimistic bid-placement update (`usePlaceBid`) reconciles against the 200 `PlaceBidResponse`
and rolls back on any non-2xx; the subsequent push-driven re-query confirms against the read model.
Ordering floor: **200 reconciles immediately, the push re-query confirms** — both converge on the one
authoritative view, so the bidder sees no flicker or double-count.

---

## Alternatives Considered

- **Render the push payload directly as UI state (rejected).** Treating `BidPlacedNotification.amount`
  as the current bid is simpler and lower-latency, but it violates the M7 §5 eventual-consistency
  contract: the push is a fire-and-forget broadcast, not read-your-own-write, and a dropped or
  reordered push would leave the UI showing a wrong, un-reconcilable number. The cache bridge makes
  the authoritative read endpoint the single source of truth and tolerates at-least-once delivery.
- **One connection per component / per hook (rejected).** M8-S2's `useBiddingHub` opened a connection
  inside the indicator component. Scaling that to the detail view, the feed, and the catalog would
  open several redundant `/hub/bidding` connections per client. A single app-wide Provider connection
  with group enrolment per route is the standard SignalR shape and what ADR 023's group model assumes.
- **A dedicated `WolverineFx.SignalR` transport (already rejected upstream).** ADR 023 rejected the
  Wolverine SignalR transport because it targets `IHubContext<WolverineHub>`, not the mapped
  application hubs clients connect to. This ADR is the *client* counterpart and inherits that
  rejection; nothing here reopens it.
- **An event-emitter / pub-sub library instead of Context (rejected).** Zustand or a bare emitter
  could carry the fan-out, but React Context + refs needs no new dependency (UI-state management
  remains an ADR-013-parked question) and keeps the connection lifecycle tied to the React tree.
- **A server-side `Outbid` event (rejected / escalation-only).** Rather than add a backend push for
  the outbid beat (which M8's non-goals forbid), it is derived client-side from the `BidPlaced` +
  view transition. If a future requirement genuinely needs a targeted server `Outbid`, that is its
  own backend slice to escalate — not a frontend workaround.

---

## Consequences

**Positive**

- One connection, one source of truth. The cache is the live mirror of the read model; the UI is
  resilient to dropped/reordered/at-least-once pushes.
- `useListen` + the cache bridge cleanly separate "transient activity" from "authoritative state,"
  making the render path easy to reason about and grep-verify (the query is the render source).
- The pattern is hub-agnostic: M8-S5's ops app reuses the same Provider/hook/bridge shape against
  `OperationsHub` (adding the `access_token` negotiate credential per ADR 024).

**Negative / costs**

- A push costs a re-query round-trip (latency + a request) rather than rendering the payload inline.
  For a conference-scale demo this is negligible; if it ever isn't, a future optimization could let
  selected pushes seed the cache *and* invalidate — but only behind a reconciliation guarantee.
- The heterogeneous wire forces structural discrimination in `parseHubMessage`; a future server-side
  move to a uniform discriminator field would let it simplify (a candidate ADR-023 follow-up).

**Neutral**

- The optimistic-update reconciliation ordering is a UX detail owned per-surface; this ADR fixes only
  the floor (200 reconciles, push confirms).

---

## Relationship to Other ADRs

| ADR | Effect |
|---|---|
| ADR 013 — Frontend Core Stack | **Resolves** its deferred "SignalR integration pattern" question (amended in place). |
| ADR 023 — Relay Reactive-Broadcast Architecture | **Depends on.** Owns the `ReceiveMessage` payload contract + group model this pattern consumes. |
| ADR 024 — Staff Auth Posture | **Forward-looking.** The ops-app reuse adds the `access_token` negotiate credential; the bidder connection is anonymous. |
| ADR 025 — SPA Monorepo Layout | **Depends on.** Same-origin `/hub/bidding` via the dev proxy; the pattern will live in `client/shared/` when the ops app adopts it. |

---

## References

- `client/bidder/src/signalr/` — `SignalRProvider.tsx`, `hooks.ts`, `messages.ts`, `cacheBridge.ts`, `hub.ts`
- `client/bidder/src/bidding/usePlaceBid.ts` — the optimistic-update/rollback mutation that pairs with the bridge
- `src/CritterBids.Relay/Notifications/` — the four wire-record shapes normalized at the boundary
- `docs/skills/wolverine-signalr/SKILL.md` — client `HubConnection` conventions (not restated here)
- ADR 023 — Relay Reactive-Broadcast Architecture
- Narrative 001 Moments 3–7 — the live-bidding beats this pattern renders

---

## Document History

- **2026-06-08** — `M8-S3b-bidder-live-bidding`: **Authored and Accepted.** The first hub wired from a
  client (M8-S1 proof → M8-S2 shell → M8-S3a bid endpoint) met ADR 013's precondition for resolving
  the deferred integration-pattern question. Pins the `SignalRProvider` + `useListen` + TanStack Query
  cache-bridge composition, the Zod wire-normalization of Relay's heterogeneous push surface, and the
  "push = re-query, derive beats from view transitions" discipline (including outbid-is-derived, since
  no server `Outbid` push exists). Resolves the milestone's stale `ADR 014` references for this pattern
  (014 is the accepted Cross-BC Read-Model Extension Shape; 026 is the next unreserved number).
