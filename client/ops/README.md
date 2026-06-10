# CritterBids — Operations Dashboard SPA (M8-S5 shell + staff auth + OperationsHub)

The staff-gated operations dashboard. At **M8-S5** it is the **shell and the plumbing**: the
`StaffToken` auth gate (ADR 024), the staff-credentialed `OperationsHub` connection, and a dark,
projector-legible layout with placeholder routes for the six dashboard views. The data surfaces
(lot board, bid activity, settlement queue, escalations, disputes, sessions/participants) land in
**M8-S6**.

See **ADR 013** (frontend core stack), **ADR 024** (staff auth — `X-Staff-Token` header for HTTP,
`access_token` query string for the hub), **ADR 025** (monorepo layout; this app builds at base
path `/ops/`), and **ADR 026** (the SignalR Provider pattern this app replicates).

## Stack

Vite + React 19 + TypeScript (strict) · Tailwind CSS v4 (`@tailwindcss/vite`) + shadcn/ui ·
TanStack Router + TanStack Query · `@microsoft/signalr` · Vitest.

## Install & build

From the `client/` workspace root (npm workspaces, ADR 025):

```bash
cd client
npm install
npm run build --workspace @critterbids/ops
npm run test --workspace @critterbids/ops
```

`npm run build` runs `tsc --noEmit && vite build` and must succeed from a clean checkout.

## Run (manual verification)

1. Start the API host **with a staff token configured** — an empty/missing
   `OperationsAuth:StaffToken` authenticates nothing (401 on every staff request, ADR 024):

   ```bash
   # e.g. via environment:  OperationsAuth__StaffToken=<your-token>
   dotnet run --project src/CritterBids.AppHost --launch-profile http
   ```

2. Start the ops dev server (port **5174** — the bidder app owns 5173; both run simultaneously):

   ```bash
   cd client
   npm run dev --workspace @critterbids/ops
   ```

3. Open `http://localhost:5174/ops/` (the app lives at the `/ops/` base path, ADR 025). Enter the
   staff token at the gate — it is validated against `GET /api/operations/lot-board` (with the
   `X-Staff-Token` header) before being stored in `sessionStorage`. The dashboard then opens the
   `OperationsHub` WebSocket, with the token riding the `access_token` query string on the
   upgrade; the header pill should transition **Connecting → Connected** (green).

A wrong token is rejected at the gate with the API's 401. Any later 401 on a staff request clears
the stored token and re-shows the gate.

> **Why the hub connection skips negotiate:** since v7, `@microsoft/signalr` sends
> `accessTokenFactory` tokens on HTTP requests (including the negotiate POST) as an
> `Authorization: Bearer` header — but the backend `StaffToken` scheme reads only the
> `access_token` **query string** on the hub path (ADR 024). `skipNegotiation: true` +
> the WebSockets transport makes the upgrade request (which *does* carry the query credential)
> the connection's only request. Details in `src/signalr/SignalRProvider.tsx`.

## Out of scope at M8-S5

The six dashboard data views, the `OperationsFeedNotification` parse surface + TanStack Query
cache bridge (`useListen`), the render-time `Title` join from `/api/listings/{id}`, and Playwright
e2e — all are M8-S6/S7 work.
