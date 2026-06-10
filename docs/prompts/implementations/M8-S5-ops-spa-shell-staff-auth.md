# M8-S5: Ops SPA Shell + Staff Auth + OperationsHub

**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — slice plan §7, row M8-S5
**Slice:** S5 of M8 (the ops-dashboard foundation slice; the second SPA, the staff auth dance, and the OperationsHub connection)
**Narrative:** `docs/narratives/008-operator-resolves-dispute-with-extension.md` (planned, not yet authored — the operator-vantage spec the ops dashboard renders; milestone §1 names it)
**Agent:** Claude Code
**Estimated scope:** one PR; **frontend-only** (`client/ops/` tree, plus the npm workspace wiring in `client/`) **plus one doc** (the retro). **No `.cs`, `.csproj`, or `.slnx` file is touched.**

---

## Preconditions

This prompt assumes:

- **M8-S4 has merged** — the bidder app's narrative arc is complete (session start → catalog → bid → outbid → extended bidding → gavel → settlement confirmation). `client/bidder/` is feature-complete for the bidder journey; 25 Vitest tests green across 8 files.
- **The backend operations surface is fully wired** (M7): seven `[Authorize(Policy = "StaffOnly")]` GET endpoints under `/api/operations/*` return the six operator read models (`LotBoardView`, `BidActivityEntry`, `SettlementQueueView`, `OperationsObligationsView` × 2 queues, `SessionActivityView`, `ParticipantActivityView`). All return `IReadOnlyList<T>` (empty array, never 404).
- **The `OperationsHub` is mapped and staff-gated** (M6-S5 / M7-S6): `[Authorize(Policy = "StaffOnly")]` on the hub class; connections enrol into the `ops:staff` group on connect; push handlers deliver `OperationsFeedNotification` (shape: `{ listingId?, eventType, payload, occurredAt }`) via `Clients.All` broadcast.
- **The `StaffToken` auth scheme is live** (ADR 024): `X-Staff-Token` header for HTTP, `access_token` query string for the `/hub/operations` negotiate. The token is bound from `OperationsAuth:StaffToken` in configuration. In dev (non-production), an empty/missing token authenticates nothing (401 on every staff request) — the ops app must provide a token to connect.
- **ADR 025 (SPA monorepo layout)** established the `client/` npm-workspace pattern. `client/bidder/` is the first workspace member. `client/ops/` joins as the second member in this slice.

## Goal

Scaffold the **operations dashboard SPA** at `client/ops/` and establish the staff-auth + `OperationsHub` connection — the ops-dashboard equivalent of what M8-S1/S2 did for the bidder app. After S5, the ops app has: a running Vite dev server, the `StaffToken` credential wired for both HTTP and the hub, a live `OperationsHub` connection rendering connection state, and a high-contrast projector-legible shell. S5 delivers the **plumbing**; S6 fills the dashboard views.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M8-frontend-spas.md` | **Authoritative for scope.** §7 row M8-S5; §2 ops-dashboard surfaces; §3 non-goals; §6 staff auth from the SPA convention. |
| `docs/decisions/024-staff-auth-posture-resumption.md` | ADR 024 — the staff-auth scheme. `X-Staff-Token` header for HTTP; `access_token` query string for the hub negotiate; `OperationsAuth:StaffToken` config key; constant-time validation; 401 on absence/mismatch. |
| `docs/decisions/025-spa-monorepo-layout.md` | ADR 025 — the `client/` npm-workspace layout. `client/ops/` is the planned second workspace member. |
| `docs/decisions/026-signalr-integration-pattern.md` | ADR 026 — the Provider + `useListen` + cache bridge pattern. S5 repeats this for the `OperationsHub` (with the staff-auth credential dance added). |
| `docs/decisions/013-frontend-core-stack.md` | ADR 013 — accepted library set. TypeScript strict, Zod, TanStack Query, Tailwind v4 + shadcn/ui, `@microsoft/signalr`, Vitest. |
| `src/CritterBids.Relay/Hubs/OperationsHub.cs` | The hub class — `[Authorize(Policy = "StaffOnly")]`; `OnConnectedAsync` enrols into `ops:staff`. |
| `src/CritterBids.Api/Auth/StaffAuthConstants.cs` | The constants: scheme name, header name, query-string key, config key, hub path. |
| `src/CritterBids.Api/Auth/StaffTokenAuthenticationHandler.cs` | The handler — reads `access_token` from query string for the hub path; reads `X-Staff-Token` header for all other paths. |
| `src/CritterBids.Relay/Notifications/OperationsFeedNotification.cs` | The single notification shape: `{ listingId?, eventType, payload, occurredAt }`. |
| `src/CritterBids.Operations/OperationsQueryEndpoints.cs` | All seven staff-gated GET endpoints — the HTTP surface the ops app queries. |
| `client/bidder/src/signalr/SignalRProvider.tsx` | The bidder's ADR 026 implementation — the pattern S5 follows (with staff-auth credential added). |
| `client/bidder/vite.config.ts` | The bidder's Vite config — proxy setup, Vitest config, PWA wiring. S5 follows the same structure. |
| `CLAUDE.md` | Global conventions. |

## In scope

1. **Scaffold `client/ops/` as the second npm-workspace member.** Same Vite + React + TS-strict stack as `client/bidder/` (ADR 013). Wire it into the workspace root (`client/package.json`), with its own `package.json`, `vite.config.ts`, `tsconfig.json`, and Vite dev-server proxy (same `/api` and `/hub` targets as the bidder). The ops app runs on a **different dev port** (e.g. 5174) so both SPAs can run simultaneously.

2. **Staff token configuration surface.** The ops app needs a staff token to call the `[Authorize(Policy = "StaffOnly")]` endpoints and to connect to the `OperationsHub`. In the MVP, the token is entered by the operator on a simple auth-gate screen (a text input + submit, not a full login flow) and held in `sessionStorage` (same pattern as the bidder's `participantId`). The stored token is:
   - Sent as `X-Staff-Token` on every HTTP request to `/api/operations/*`.
   - Sent as `access_token` query-string parameter on the `OperationsHub` negotiate.
   The auth gate should handle 401 responses gracefully (clear the stored token, re-show the gate).

3. **`OperationsHub` connection with staff credential.** A `SignalRProvider` (or equivalent) that opens a `HubConnection` to `/hub/operations` with `.withAutomaticReconnect()` and passes the staff token via the `accessTokenFactory` option (the `@microsoft/signalr` client sends this as `access_token` on the negotiate). Register the `ReceiveMessage` client method. Render connection state (connected/connecting/disconnected). Follow the ADR 026 pattern from the bidder app, adapted for the staff-auth credential.

4. **Ops app shell.** A minimal layout with: a header showing "CritterBids Operations" + connection status indicator, a nav (sidebar or top) with placeholder links for the six dashboard views (lot board, bid activity, settlement queue, escalations, disputes, sessions/participants), and a content area. **High-contrast, projector-legible** — dark background, bright text, generous font sizes (the ops dashboard is projected on a conference screen alongside the bidder app). Tailwind v4 + shadcn/ui, dark mode by default.

5. **Vitest coverage.** Prove: (a) the staff token is sent as `X-Staff-Token` on HTTP requests; (b) the `OperationsHub` connection uses the `accessTokenFactory` to supply the token; (c) the auth gate renders and clears on 401. Keep it focused — the S6 slice owns the dashboard-view tests.

6. **`docs/retrospectives/M8-S5-ops-spa-shell-staff-auth-retrospective.md`** — written last.

## Explicitly out of scope

- **Any backend / API-host change.** No new endpoints, no auth scheme change, no Relay handler change, no `.cs`/`.csproj`/`.slnx` touch. The ops app consumes the M7 backend surface as-is.
- **Dashboard data views.** The six operator views (lot board, bid activity, settlement queue, escalations, disputes, sessions/participants) are **M8-S6** scope. S5 scaffolds the shell and the plumbing; S6 fills it with TanStack Query-backed data surfaces.
- **The `OperationsHub` cache bridge / `useListen` wiring.** The ops app will need its own parse surface for `OperationsFeedNotification` and a cache bridge — that lands in S6 alongside the views it invalidates. S5 may register `ReceiveMessage` and log it, but the operational cache bridge is S6.
- **Render-time `Title` join.** The lot board / obligations views receive `ListingId` only; the dashboard resolves titles from `/api/listings/{id}`. This is S6 rendering logic.
- **Playwright e2e** (M8-S7).
- **The bidder app / `client/bidder/`** — no changes to the bidder SPA.
- **`client/shared/` extraction.** ADR 025 plans a shared workspace member for wire contracts both apps share (Zod schemas, SignalR integration). If the ops app duplicates a small amount from the bidder (e.g. the `RECEIVE_MESSAGE` constant), that's acceptable for S5; the shared extraction is a housekeeping task, not a gate.

## Conventions to pin or follow

- **ADR 024 credential transport rules (non-negotiable):**
  - HTTP: `X-Staff-Token` header on every staff-gated request. NOT a query-string parameter on HTTP paths.
  - Hub: `access_token` query-string parameter on the negotiate (via `accessTokenFactory`). NOT a custom header (the WebSocket transport can't carry one).
  - The token is read from configuration on the backend; the ops SPA reads it from `sessionStorage` after the operator enters it.
  - An empty or missing token on the backend authenticates nothing (returns 401). The frontend must handle 401 by clearing the stored token and re-prompting.

- **ADR 026 (push = re-query, never render-the-payload):** The same rule as the bidder app. The `OperationsHub`'s `OperationsFeedNotification` triggers cache invalidations; the dashboard re-queries the authoritative `/api/operations/*` endpoints. No push field is rendered as truth.

- **In-set libraries only:** ADR 013's accepted set. No new dependencies beyond what `client/bidder/` already uses.

- **Vite dev-server proxy:** Same pattern as the bidder (`/api` → API host, `/hub` → API host with `ws: true`). The ops app runs on a different port so both can dev simultaneously.

## Open questions

1. **Token storage.** `sessionStorage` mirrors the bidder's `participantId` pattern and means the token is cleared on tab close. Alternatively, `localStorage` would persist across tabs/sessions. **Recommended resolution:** `sessionStorage` — the operator re-enters the token per projector session; persistence across tabs is a convenience that doesn't outweigh the simplicity of a clean session model. The token is a single shared secret, not a per-user credential.

2. **Vite dev port.** The bidder runs on Vite's default `:5173`. The ops app should use a different port. **Recommended resolution:** configure `server.port: 5174` in the ops Vite config. No collision; both SPAs proxy to the same API host.

3. **Dark-mode-by-default approach.** shadcn/ui supports dark mode via a `dark` class on `<html>`. **Recommended resolution:** set `class="dark"` on the root `<html>` element in the ops app's `index.html`. No theme toggle needed — the ops dashboard is always dark for projector legibility.

## Acceptance criteria

- [ ] `client/ops/` exists as a Vite + React + TS-strict workspace member; `npm install` + `npm run build` succeed from a clean checkout in the `client/` workspace
- [ ] The ops app runs on a dev port separate from the bidder app (both can run simultaneously)
- [ ] A staff-auth gate prompts for the token; the token is stored in `sessionStorage`
- [ ] HTTP requests to `/api/operations/*` carry the `X-Staff-Token` header
- [ ] The `OperationsHub` connection uses `accessTokenFactory` to supply the token as `access_token` on the negotiate
- [ ] The hub connection state is rendered (connected / connecting / disconnected / auth error)
- [ ] 401 responses clear the stored token and re-show the auth gate
- [ ] The shell has a dark, high-contrast, projector-legible layout with nav placeholders for the six dashboard views
- [ ] Vitest covers: (a) token header on HTTP; (b) `accessTokenFactory` usage; (c) auth gate render + 401 handling
- [ ] No backend change — no `.cs`, `.csproj`, `.slnx` touch
- [ ] No `client/bidder/` change
- [ ] `docs/retrospectives/M8-S5-ops-spa-shell-staff-auth-retrospective.md` written
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Spec delta

Per ADR 020, this slice's spec consequences are: (1) **the ops SPA exists** — the second frontend surface named in the milestone §1 exit criteria is scaffolded and running. (2) **The staff-auth client dance lands** — the `StaffToken` credential flow (header for HTTP, query string for the hub) is wired end-to-end from the frontend, proving the ADR 024 posture works from a browser client. (3) **The ADR 026 pattern is replicated** — the `OperationsHub` connection follows the same Provider pattern as the bidder's `BiddingHub`, with the credential adaptation, confirming the pattern's reusability across hubs with different auth requirements. The retro's `## Spec delta -- landed?` paragraph confirms these three.
