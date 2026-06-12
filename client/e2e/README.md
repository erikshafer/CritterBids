# @critterbids/e2e — end-to-end tests

Playwright multi-context e2e tests (ADR 013) that drive the real SPAs against the **live
Aspire-orchestrated stack**. The headline test is the narrative-001 bid war: two anonymous
bidders in isolated browser contexts fight over one Flash listing through outbid, extended
bidding, and gavel-fall.

These tests are **not run in CI** (recorded M8-S7 deferral — they need Postgres, RabbitMQ, the
API host, and the bidder dev server all running). They are a local, pre-merge verification tool.

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

Or from this directory: `npm test`. A single run takes **3–4 minutes of wall-clock time** — the
bid-war test rides a real 2-minute Flash auction plus its 15-second extended-bidding extension
and settlement; that is the point, not a defect.

Each run seeds its own listing with a unique title, so repeated runs against the same database
do not interfere with each other. Earlier runs' listings remain in the dev database; that is
harmless.

## Conventions

- Assertions go through the UI (what a bidder sees) — never into hub internals. A push is a
  signal; the rendered re-query result is the observable (ADR 026).
- Each browser context asserts its `BiddingHub` connection **before** placing any bid, and every
  seeded listing title is per-run unique (the M8-S6b smoke-harness lessons, promoted from
  playbook to automation).
- E2e dependencies live only in this workspace member; neither app's production build or
  type-check sees them (ADR 025 dependency hygiene).
