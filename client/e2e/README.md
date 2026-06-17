# @critterbids/e2e — end-to-end tests

Playwright e2e tests (ADR 013) that drive the real SPAs against the **live Aspire-orchestrated
stack**. Two tests live here:

- **`bid-war.spec.ts`** (narrative 001, bidder vantage) — two anonymous bidders in isolated
  browser contexts fight over one Flash listing through outbid, extended bidding, and gavel-fall.
- **`seller-obligation.spec.ts`** (narrative 006, seller vantage) — a seller's listing sells, the
  **seller console** surfaces the post-sale obligation, the seller provides tracking through the
  console UI, and the obligation auto-confirms to "Completed". This test drives the seller SPA on
  `:5175` and forces the sale with the dev Buy-It-Now trigger (the auction mechanics are the
  bid-war test's job; the obligation lifecycle is this one's). See "Seller-obligation specifics"
  below for the two backend facts it depends on.

These tests are **not run in CI** (recorded M8-S7 deferral — they need Postgres, RabbitMQ, the
API host, and the SPA dev servers all running; the e2e member is type-checked in CI but not
executed). They are a local, pre-merge verification tool.

## Prerequisites

1. **The full stack running** (Postgres + RabbitMQ + API + both SPA dev servers):

   ```bash
   dotnet run --project src/CritterBids.AppHost --launch-profile http
   ```

   Wait until the bidder dev server answers on `http://localhost:5173`. The API host must be in
   the `Development` environment — the tests seed data through the dev-only
   `POST /api/dev/seed-flash` endpoint.

2. **A browser for Playwright** (one-time):

   ```bash
   npx playwright install chromium
   ```

   If the browser download is unavailable on your machine, point the harness at a system
   browser instead: set `PLAYWRIGHT_BROWSER_CHANNEL=msedge` (or `chrome`) when running.

## Running

From `client/`:

```bash
npm run e2e
```

Or from this directory: `npm test`. Run a single spec with
`npx playwright test seller-obligation` (or `bid-war`).

Timings: the **bid-war** test takes **3–4 minutes** — it rides a real 2-minute Flash auction plus
its 15-second extended-bidding extension and settlement; that is the point, not a defect. The
**seller-obligation** test takes **under a minute** — it forces the sale with Buy-It-Now (no
auction wait) and rides only the demo-mode obligation timers (a ~10s ship-by window and a ~10s
post-tracking auto-confirm window).

Each run seeds its own listing with a unique title, so repeated runs against the same database
do not interfere with each other. Earlier runs' listings remain in the dev database; that is
harmless.

## Seller-obligation specifics

The seller-obligation test depends on two facts about the **live host**, both satisfied by the
Aspire-orchestrated run:

1. **Demo-mode obligation timers.** The Obligations post-sale timers are days in production and
   seconds in demo mode (`ObligationsOptions.DemoMode`). The AppHost sets
   `Obligations__DemoMode=true` for the orchestrated run, so the full lifecycle
   (tracking → shipped → auto-confirmed → fulfilled) completes live. Without it the auto-confirm
   is 3 days out and the test's "Completed" assertion can never pass.
2. **Seed-then-inject identity bridge.** The seller console mints its own anonymous session, but
   opening a listing for bidding is a staff/bus-only operator step. `POST /api/dev/seed-flash`
   already creates a *registered seller* and drives the listing to Open; the test injects that
   `sellerId` into the console's session storage (`critterbids.seller.participantId` +
   `critterbids.seller.isRegisteredSeller`) via `context.addInitScript` so the console adopts the
   seeded identity. No backend change, no operator UI in the seller console.

## Conventions

- Assertions go through the UI (what a bidder sees) — never into hub internals. A push is a
  signal; the rendered re-query result is the observable (ADR 026).
- Each browser context asserts its `BiddingHub` connection **before** placing any bid, and every
  seeded listing title is per-run unique (the M8-S6b smoke-harness lessons, promoted from
  playbook to automation).
- E2e dependencies live only in this workspace member; neither app's production build or
  type-check sees them (ADR 025 dependency hygiene).
