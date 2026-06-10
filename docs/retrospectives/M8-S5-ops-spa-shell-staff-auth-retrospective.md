# M8-S5: Ops SPA Shell + Staff Auth + OperationsHub — Retrospective

**Date:** 2026-06-10
**Milestone:** M8 — React Frontend SPAs
**Slice:** S5 — Ops SPA Shell + Staff Auth + `OperationsHub` (the second SPA; the credential dance the S1 proof deliberately skipped)
**Agent:** Claude Code
**Prompt:** `docs/prompts/implementations/M8-S5-ops-spa-shell-staff-auth.md`

## Baseline

- `client/` workspace had **one member** (`bidder`); bidder suite green at **25 Vitest tests across 8 files**; `npm run build --workspace @critterbids/bidder` exit 0.
- Branch from `main` at `70ba8cd` (M8-S4 merged — the bidder narrative arc complete).
- Backend operations surface fully wired per M7: seven `StaffOnly` GETs under `/api/operations/*`, `OperationsHub` gated and mapped, `StaffToken` scheme live. Verified live this session: bare `GET /api/operations/lot-board` → **401**; with `X-Staff-Token` → **200 `[]`**.
- No `client/ops/` directory; `client/package.json` `workspaces: ["bidder"]`.
- No `.cs`/`.csproj`/`.slnx` change planned or made — the working tree's only backend interaction was read + run.

## Items completed

| Item | Description |
|------|-------------|
| S5.1 | `client/ops/` scaffolded as the second npm-workspace member: Vite + React 19 + TS-strict, `base: "/ops/"`, dev port **5174**, `/api` + `/hub` proxy (`ws: true`), PWA manifest, Vitest config; workspace root `package.json` gains `"ops"` |
| S5.2 | Staff-token surface: `AuthGate` (single password input, **validated against `GET /api/operations/lot-board` before storing**), `sessionStorage` key `critterbids.staffToken`, `createStaffFetch` attaching `X-Staff-Token` and funnelling every 401 into clear-token + re-gate |
| S5.3 | `OperationsSignalRProvider`: one app-wide `HubConnection` to `/hub/operations`, `accessTokenFactory` + **`skipNegotiation: true` + WebSockets transport** (see Discovery below), `.withAutomaticReconnect()`, `ReceiveMessage` registered + logged, status/lastError exposed |
| S5.4 | Ops shell: header "CritterBids Operations" + connection pill, sidebar nav with the six view placeholders (each naming its M8-S6 endpoint), TanStack Router `basepath: "/ops"`, always-dark + 18px rem root for projector legibility |
| S5.5 | Vitest: **16 tests across 3 files** — `X-Staff-Token` on HTTP (4), token-validation probe semantics (4), auth-gate render/401/unreachable/mid-session-401 (5), `accessTokenFactory`/`skipNegotiation`/transport on the **production** factory via partial module mock + `ReceiveMessage` registration + hub-401 clear (3) |
| S5.6 | This retrospective |

## S5.3: Discovery — `accessTokenFactory` does not put the token on the negotiate query string (v7+ client)

The prompt (and milestone §6) pin the hub credential as "`access_token` query string on the negotiate (via `accessTokenFactory`)". The installed client (`@microsoft/signalr` **10.0.0**) does something different:

- `dist/esm/AccessTokenHttpClient.js` line 33: every HTTP request the connection makes — **including the negotiate POST** — gets the factory token as `request.headers[HeaderNames.Authorization] = `Bearer ${token}`` (a .NET 7-era breaking change: "SignalR clients no longer send the access token in the query string for HTTP requests").
- `dist/esm/WebSocketTransport.js` line 50: only the **browser WebSocket upgrade** appends `access_token=<token>` to the query (browsers cannot set WS headers).

`StaffTokenAuthenticationHandler` reads **only** `Request.Query["access_token"]` on the hub path (ADR 024 item 6 — deliberate: the query read is a scoped exception). A default negotiate-first `start()` therefore 401s before any WebSocket opens. The M7-S6 hub auth tests never hit this because `StaffAuthTestFixture.BuildOperationsConnection` appends `?access_token=` **manually to the URL string** — the backend had never been exercised against a JS client's default credential transport.

**Resolution (frontend-only):** `withUrl(OPERATIONS_HUB_URL, { transport: HttpTransportType.WebSockets, skipNegotiation: true, accessTokenFactory })`. Skipping negotiation makes the WS upgrade — the one request that *does* carry the query credential — the connection's only request. This is exactly the credential path ADR 024 designed and M7-S6 integration-tested. Cost: no SSE/long-polling fallback, acceptable for a staff dashboard. Alternatives rejected:

- *Manually appending `?access_token=` to the hub URL* (the M7-S6 test fixture's move): bypasses `accessTokenFactory` (the prompt-pinned mechanism), bakes the credential into the connection's base URL, and with a factory also present produces a duplicate query parameter that comma-joins on the server and never matches.
- *Reading the `Authorization: Bearer` header in the backend scheme*: the correct long-term door (it is the `AddJwtBearer` convention ADR 024 names as the migration path) but a backend change — barred by this slice's scope.

The acceptance criterion "supplies the token as `access_token` **on the negotiate**" is therefore satisfied in spirit, not letter: there is no negotiate; the token rides the upgrade. Recorded here as the criterion's lived shape.

## S5.2: Auth-gate validation probe — why the gate does a real GET before storing

Browsers hide the WS upgrade status code: a 401-rejected upgrade surfaces as a generic transport error (verified live — close code **1006**, no reason). If the gate stored the token unvalidated, a typo would manifest as an opaque "Disconnected" pill instead of an auth error. The gate therefore probes `GET /api/operations/lot-board` with the candidate in `X-Staff-Token` **before** storing: 2xx → store + mount, 401 → "rejected (401)" at the gate, 5xx/network → "could not reach the API host" (a down host is reported distinctly from a wrong token). Side benefit: the "HTTP requests carry `X-Staff-Token`" acceptance criterion is *live* in S5 — without the probe, no staff HTTP request would exist until S6's views.

Mid-session 401s (token rotated server-side) route through `createStaffFetch` → clear `sessionStorage` → the gate re-shows with the rejection reason. The provider also pattern-matches `/401|unauthorized/i` on hub start failures as a belt-and-braces clear (unreachable in browsers, reachable in non-browser transports).

## S5.4: Provider order — the hub mounts inside the gate

`QueryClientProvider → StaffAuthProvider → AuthGate → OperationsSignalRProvider → RouterProvider`. The connection provider mounting **inside** the gate means: no token, no connection attempt (no 401 spam against the hub); clearing the token unmounts the provider, which stops the connection in its effect cleanup. A token change is a remount, so the connection effect runs exactly once per provider life and needs no token dependency. This differs from the bidder app (whose `SignalRProvider` mounts unconditionally — its hub is anonymous) and is the credential adaptation ADR 026's "hub-agnostic" consequence anticipated.

## Live smoke (real Kestrel host + real browser)

Per the M8-S2 lesson (mocked-fetch tests verify response handling, not request shape), the slice closed with a live pass — Aspire host with `OperationsAuth__StaffToken` set in the launching shell (**finding:** Aspire child projects inherit the AppHost process environment, so an env var set before `dotnet run` reaches the API without any AppHost change):

| Check | Result |
|---|---|
| Raw WS upgrade via Vite proxy, `?access_token=<valid>` (Node 22 built-in `WebSocket` — header-less like a browser) | Upgrade accepted; SignalR handshake reply `{}\x1e` |
| Same, no token / wrong token | Upgrade rejected, close 1006 (generic — confirms the gate-probe rationale) |
| Browser UI smoke (Edge via `playwright-core`, 7 checks) | Gate renders · dashboard hidden pre-auth · wrong token → "rejected (401)" + not stored · valid token → stored + dashboard · pill reaches **Connected** · placeholder routes under `/ops/` | all pass |
| Both SPAs simultaneously | bidder `:5173` → 200, ops `:5174/ops/` → 200 |

Two console messages during the UI smoke, both expected: the wrong-token probe's own 401, and `Failed to start the HttpConnection before stop() was called` — the React 19 StrictMode dev double-mount stopping the first connection mid-start (same artifact pattern as the bidder app; the second mount connected).

## Test results

| Phase | Suite | Result |
|-------|-------|--------|
| After S5.1–S5.4 (build) | `npm run build --workspace @critterbids/ops` | exit 0 (tsc strict + vite build + PWA) |
| After S5.5 | `@critterbids/ops` Vitest | **16/16 green** (3 files), first run |
| Regression | `@critterbids/bidder` Vitest + build | **25/25 green**, build exit 0 — unchanged |

Frontend totals: 25 → **41** Vitest tests across the workspace. Backend test count untouched (no backend diff).

## Build state at session close

- `npm run build` exit 0 for both workspace members; TypeScript strict, zero errors.
- Backend: **no `.cs`, `.csproj`, or `.slnx` file in the diff** (`git status` shows `client/` + `docs/retrospectives/` only).
- `client/bidder/` source: **0 files changed** (the root `package.json` workspaces array and `package-lock.json` are workspace wiring, not bidder code).
- Grep-able assertions: `X-Staff-Token` literal in `client/ops/src`: 1 (the `STAFF_TOKEN_HEADER` constant); `access_token` literals: 0 (supplied by the signalr client, never hand-built); `localStorage` usage: 0 (sessionStorage only); new npm dependencies beyond the bidder's set: 0 (zod deliberately omitted until S6's parse surface needs it).

## Key learnings

1. **`accessTokenFactory` ≠ query-string-on-negotiate since SignalR 7.** JS (and .NET) clients send the factory token as `Authorization: Bearer` on HTTP requests; only the browser WS upgrade gets the `access_token` query parameter. Any server scheme that reads only the query string must be consumed with `skipNegotiation: true` + WebSockets — or taught to read the header.
2. **A custom scheme that reads `access_token` only on the hub path is only ever exercised by clients that put it there.** The M7-S6 fixture appended the query manually, masking the client-default mismatch for two slices. When a backend contract has exactly one consumer shape in tests, verify the *default* client behavior against it before a frontend slice depends on it.
3. **Validate-before-store is the right gate shape when the socket hides auth failures.** Browsers report a 401 WS upgrade as close 1006 with no reason; an HTTP probe with the same credential is the only reliable 401 detector, and it makes the credential's HTTP transport testable in the same slice.
4. **Mounting a credentialed connection provider inside the auth gate** ties the connection lifecycle to credential possession for free — no token-change reconnect logic, no unauthenticated connection attempts, teardown on 401 via unmount.
5. **Aspire child projects inherit the AppHost's process environment** — `$env:OperationsAuth__StaffToken` set in the launching shell reached the API project with no `WithEnvironment` call, which is how a dev staff token gets into the demo host without touching the repo.

## Findings against narrative

The prompt's `Narrative:` line names `docs/narratives/008-operator-resolves-dispute-with-extension.md` as **planned, not yet authored**. No narrative file exists to measure against; the slice's surfaces (the gate, the shell, the six nav placeholders) are the milestone §2 operator-surface table rendered as information architecture, and the disputes placeholder cites narrative 008 forward. No drift to route; the narrative remains owed before/at M8-S6, which renders the queues it specifies.

## Spec delta — landed?

Landed with one documented divergence. (1) **The ops SPA exists** — `client/ops/` is the second workspace member, builds and runs at `/ops/` on port 5174 alongside the bidder. (2) **The staff-auth client dance lands end-to-end** — proven from a real browser against the real host: `X-Staff-Token` on HTTP (gate probe + `staffFetch`), `access_token` on the hub connection. *Divergence:* the credential reaches the hub on the **WebSocket upgrade, not the negotiate** — the v7+ client sends `Authorization: Bearer` on negotiate, which the ADR 024 scheme deliberately doesn't read, so the connection skips negotiation (S5.3 Discovery). ADR 024's posture is confirmed working from a browser client; the milestone §6 phrase "`access_token` query string for the `OperationsHub` negotiate" describes the wire as it was believed, not as the v10 client ships it. (3) **The ADR 026 pattern replicates** — Provider + connection-state hook against a second hub with the credential adaptation, confirming the ADR's "hub-agnostic" consequence (its `useListen`/cache-bridge thirds arrive with S6's views, per the slice's explicit out-of-scope). No ADR text was amended this slice; if M8-S6 wires `useListen` against this provider unchanged, ADR 026 needs no edit, and the negotiate-vs-upgrade wording is a candidate one-line clarification to ADR 024's references when next touched.

## Verification checklist

- [x] `client/ops/` exists as a Vite + React + TS-strict workspace member; `npm install` + `npm run build` succeed from the `client/` workspace
- [x] Ops app on dev port 5174; bidder `:5173` and ops `:5174/ops/` verified responding simultaneously
- [x] Staff-auth gate prompts for the token; token stored in `sessionStorage` (`critterbids.staffToken`)
- [x] HTTP requests to `/api/operations/*` carry `X-Staff-Token` (gate probe + `createStaffFetch`; asserted in tests and live)
- [x] `OperationsHub` connection supplies the token via `accessTokenFactory` as `access_token` on the connection's query string (on the WS upgrade — no negotiate exists under `skipNegotiation`; divergence documented in S5.3)
- [x] Hub connection state rendered (Connected/Connecting/Reconnecting/Disconnected pill + failure reason; auth errors surface at the gate with the 401 reason)
- [x] 401 responses clear the stored token and re-show the gate (mid-session test + gate-probe test + live wrong-token check)
- [x] Dark, high-contrast, projector-legible shell (always-dark, 18px rem root) with nav placeholders for the six views
- [x] Vitest covers (a) token header on HTTP, (b) `accessTokenFactory` usage on the production factory, (c) auth-gate render + 401 handling — 16 tests green
- [x] No backend change — zero `.cs`/`.csproj`/`.slnx` in the diff
- [x] No `client/bidder/` change (workspace root manifest + lockfile only)
- [x] This retrospective
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M8-S6 (in milestone scope):** the six data views over `/api/operations/*` via TanStack Query through `staffFetch`; the `OperationsFeedNotification` Zod parse surface + cache bridge + `useListen` (the provider's `ReceiveMessage` body is a `console.debug` placeholder to replace); the render-time `Title` join from `/api/listings/{id}`; zod gets added to `client/ops` dependencies at that point.
- **Narrative 008** is still unauthored; S6 renders its dispute/escalation queues and should hard-gate on it or escalate, per the milestone-doc-precondition pattern.
- **`client/shared/` extraction** (ADR 025 housekeeping): `RECEIVE_MESSAGE` and the shadcn theme/primitives are now duplicated across the two apps — tolerated by the prompt; worth extracting when S6 adds the Zod schemas (a third duplication candidate).
- **`CLAUDE.md` frontend table** still annotates the ops row "*(planned, M8-S5)*" — now stale; the prompt scoped this slice to the client tree + retro, so the one-line refresh belongs to S7 housekeeping with the other doc updates (`STATUS.md`, `bounded-contexts.md`).
- **CI frontend job** (milestone housekeeping, S7): should run both workspace members' build + test.
- **Candidate ADR 024 clarification** (one line, next touch): the hub credential rides the WS upgrade, not the negotiate, under v7+ clients — or, post-MVP, the scheme learns the `Authorization: Bearer` read as the recorded JWT migration path.
