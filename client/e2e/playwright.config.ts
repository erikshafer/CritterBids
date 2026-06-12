import { defineConfig, devices } from "@playwright/test";

// E2e against the LIVE Aspire-orchestrated stack. There is deliberately no `webServer` block:
// the harness does not start the host — `dotnet run --project src/CritterBids.AppHost` is a
// prerequisite (see README.md). The bidder SPA dev server (pinned :5173 by the AppHost) is the
// entry origin; /api and /hub ride its Vite proxy (ADR 025), so the test exercises exactly the
// wire the conference demo does — proxy, WebSocket upgrade and all.
export default defineConfig({
  testDir: "./tests",
  // One worker, no parallelism: the bid-war test owns real wall-clock auction timing against a
  // shared backend; a sibling test mutating the same catalog would be cross-talk, not isolation.
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [["list"]],
  // The bid war spans a full 1-minute Flash auction plus a 15s extension plus settlement.
  timeout: 240_000,
  // Cross-context propagation is push → cache invalidation → re-query over RabbitMQ; allow for
  // broker + projection latency without masking a dead connection (the hub indicator is asserted
  // separately before any bid).
  expect: { timeout: 20_000 },
  use: {
    baseURL: process.env.CRITTERBIDS_BIDDER_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        // On machines where the Playwright browser download is unavailable, point at a system
        // browser instead, e.g. PLAYWRIGHT_BROWSER_CHANNEL=msedge (the M8-S5 fallback playbook).
        channel: process.env.PLAYWRIGHT_BROWSER_CHANNEL,
      },
    },
  ],
});
