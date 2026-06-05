# CritterBids — Bidder SPA (M8-S1 BiddingHub connection proof)

The public, anonymous bidder-facing SPA. At **M8-S1** it exists only as a **connection proof**: it
opens a single SignalR `HubConnection` to the anonymous `/hub/bidding` and renders the live
connection state. It is **not** a bidding UI — no catalog, no bid placement, no domain data. M8-S2
promotes this app in place into the real bidder shell.

See **ADR 013** (frontend core stack), **ADR 025** (this monorepo layout + the dev-server proxy),
and **ADR 023** (the `ReceiveMessage` payload contract).

## Stack

Vite + React 19 + TypeScript (strict) · Tailwind CSS v4 (`@tailwindcss/vite`) · `@microsoft/signalr`.

## Install & build

From the `client/` workspace root (npm workspaces, ADR 025):

```bash
cd client
npm install
npm run build --workspace @critterbids/bidder
```

`npm run build` runs `tsc --noEmit && vite build` and must succeed from a clean checkout.

## Run the proof (manual verification)

1. Start the API host so `/hub/bidding` is live (Postgres + RabbitMQ provisioned by Aspire):

   ```bash
   dotnet run --project src/CritterBids.AppHost --launch-profile http
   ```

   (or run the API directly: `dotnet run --project src/CritterBids.Api --launch-profile http`,
   which listens on `http://localhost:5180` — the dev-proxy target).

2. Start the bidder dev server:

   ```bash
   cd client
   npm run dev --workspace @critterbids/bidder
   ```

3. Open the printed Vite URL. The status pill should transition
   **Connecting → Connected** (green). The Vite dev server proxies `/hub/bidding` to the API host
   with `ws: true`, so the SignalR negotiate POST + WebSocket upgrade are same-origin — no CORS and
   no API-host change (ADR 025).

If the API host runs on a non-default port, set `CRITTERBIDS_API_URL` before `npm run dev`.

## Out of scope at M8-S1

Catalog/listing rendering, bid placement, optimistic updates, TanStack Query, the SignalR
integration pattern (ADR 014), the second (ops) SPA, the `OperationsHub` staff connection, and PWA
manifest/service-worker wiring — all are later-slice work.
