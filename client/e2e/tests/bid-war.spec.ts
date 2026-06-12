import {
  test,
  expect,
  type APIRequestContext,
  type Browser,
  type Page,
} from "@playwright/test";

// The narrative-001 bid-war spine (Moments 1–7), automated as the ADR 013 Playwright use case:
// two ISOLATED browser contexts — two anonymous bidders — fight over one live Flash listing
// against the running Aspire stack. Assertions go through what a bidder SEES (the rendered,
// re-queried view), never into hub internals (ADR 026 applied to test assertions); the one
// API-level assertion (the extended close) exists because the lived read model carries that
// fact while the UI structurally cannot — see the Moment 6 comment. After the initial
// navigation neither page reloads; the bidder app has no polling, so every observed UI
// update is push → cache invalidation → re-query by construction.
//
// S6b smoke-harness lessons promoted to automation:
//   • the hub connection is asserted on BOTH contexts before any bid is placed (a dead
//     connection and a missing push are otherwise indistinguishable);
//   • the listing is seeded with a per-run unique title, so a stale row from an earlier run
//     can never satisfy an assertion.

/** Per-run unique seed title (S6b lesson — greppable acceptance criterion). */
const uniqueTitle = `E2E Bid War ${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

// Auction policy the seed applies (DemoSeedEndpoint defaults, narrative 001's keyboard):
// starting bid $25, reserve $50, extended bidding 30s window / 15s extension. Two minutes of
// wall-clock auction gives the war room to breathe before the trigger window opens.
const DURATION_MINUTES = 2;
const TRIGGER_WINDOW_MS = 30_000;

interface SeededListing {
  listingId: string;
  detailPath: string;
}

async function seedFlashListing(request: APIRequestContext): Promise<SeededListing> {
  // Reaches the API through the bidder dev server's Vite proxy (baseURL-relative), the same
  // path the app's own requests take. The seed endpoint drives the listing all the way to
  // Open, polling cross-BC read models between stages — give it a generous timeout.
  const response = await request.post("/api/dev/seed-flash", {
    data: { title: uniqueTitle, durationMinutes: DURATION_MINUTES },
    timeout: 90_000,
  });
  expect(response.ok(), `seed-flash failed: ${response.status()}`).toBe(true);
  return (await response.json()) as SeededListing;
}

async function scheduledCloseAt(
  request: APIRequestContext,
  listingId: string,
): Promise<number> {
  const response = await request.get(`/api/listings/${listingId}`);
  expect(response.ok()).toBe(true);
  const view = (await response.json()) as { scheduledCloseAt: string | null };
  expect(view.scheduledCloseAt, "listing should carry a scheduled close").not.toBeNull();
  return new Date(view.scheduledCloseAt!).getTime();
}

/**
 * Open an isolated bidder: fresh context (own sessionStorage → own anonymous session), navigate
 * to the listing, and gate on the two preconditions every bid depends on — the BiddingHub
 * connection (asserted BEFORE any bid; S6b lesson) and the established session (the bid input
 * stays disabled until the session POST resolves).
 */
async function openBidder(browser: Browser, detailPath: string): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto(detailPath);

  // Hub connection asserted before the first bid. "Connected" is case-exact and not a substring
  // of "Disconnected"/"Reconnecting", so toContainText cannot false-positive.
  await expect(page.getByTitle("BiddingHub connection")).toContainText("Connected");

  // Anonymous session established → the bid form is live.
  await expect(page.getByLabel("Your bid (USD)")).toBeEnabled();

  // We are on the per-run listing, not a stale row from an earlier run.
  await expect(page.getByText(uniqueTitle)).toBeVisible();

  return page;
}

async function placeBid(page: Page, amount: number): Promise<void> {
  await page.getByLabel("Your bid (USD)").fill(String(amount));
  await page.getByRole("button", { name: "Place bid" }).click();
}

/** The headline price — the re-queried CatalogListingView's currentHighBid, not feed text. */
function headlinePrice(page: Page) {
  return page.locator("p.text-3xl");
}

test("two anonymous bidders fight a live bid war to gavel-fall", async ({ browser, request }) => {
  // --- Reachability gate: a clear message beats ECONNREFUSED noise -----------------------
  const probe = await request.get("/api/listings").catch(() => null);
  if (probe === null || !probe.ok()) {
    throw new Error(
      "The CritterBids stack is not reachable on http://localhost:5173. " +
        "Start it first: dotnet run --project src/CritterBids.AppHost --launch-profile http",
    );
  }

  // --- Seed one Flash listing for this run (unique title, 2-minute close) ----------------
  const { listingId, detailPath } = await seedFlashListing(request);

  // --- Two isolated bidders, hub-connected and session-established BEFORE any bid --------
  const pageA = await openBidder(browser, detailPath);
  const pageB = await openBidder(browser, detailPath);

  // Two contexts minted two DISTINCT anonymous sessions (separate sessionStorage).
  const participantA = await pageA.evaluate(() =>
    sessionStorage.getItem("critterbids.participantId"),
  );
  const participantB = await pageB.evaluate(() =>
    sessionStorage.getItem("critterbids.participantId"),
  );
  expect(participantA).not.toBeNull();
  expect(participantB).not.toBeNull();
  expect(participantA).not.toBe(participantB);

  // --- Moment 4: bidder A opens at $30 ----------------------------------------------------
  await placeBid(pageA, 30);
  await expect(pageA.getByText("Bid accepted at $30.00.")).toBeVisible();
  await expect(pageA.getByText(/You.re the high bidder/)).toBeVisible();

  // --- Moment 5 (cross-context, live): B sees A's bid arrive — push, no reload ------------
  await expect(pageB.getByText("Current bid")).toBeVisible();
  await expect(headlinePrice(pageB)).toHaveText("$30.00");
  await expect(pageB.getByText(/1 bid\b/)).toBeVisible();

  // --- Moment 5 (the outbid): B overbids at $35; A sees the outbid state ------------------
  await placeBid(pageB, 35);
  await expect(pageB.getByText("Bid accepted at $35.00.")).toBeVisible();
  await expect(pageB.getByText(/You.re the high bidder/)).toBeVisible();

  await expect(pageA.getByRole("alert").filter({ hasText: /You.ve been outbid/ })).toBeVisible();
  await expect(headlinePrice(pageA)).toHaveText("$35.00");

  // --- Moment 6: A reclaims at $55 inside the trigger window ------------------------------
  // The extension trigger is timing-stable to automate because the backend anchors the new
  // close to ScheduledCloseAt + 15s for ANY bid inside the 30s window
  // (PlaceBidHandler.TryComputeExtension) — landing the bid anywhere in the window is
  // sufficient, and we aim for its middle by waiting against the authoritative
  // scheduledCloseAt rather than a guessed sleep.
  const closeAt = await scheduledCloseAt(request, listingId);
  const msUntilMidWindow = closeAt - Date.now() - TRIGGER_WINDOW_MS / 2;
  if (msUntilMidWindow > 0) {
    await pageA.waitForTimeout(msUntilMidWindow);
  }

  await placeBid(pageA, 55);
  // $55 crosses the $50 reserve — the acceptance message carries the ReserveMet outcome.
  await expect(pageA.getByText("Bid accepted at $55.00. Reserve met.")).toBeVisible();
  await expect(pageA.getByText(/You.re the high bidder/)).toBeVisible();

  // The reclaim propagates cross-context: B's page re-queries on the BidPlaced push.
  await expect(headlinePrice(pageB)).toHaveText("$55.00");

  // The "Extended bidding" BANNER is deliberately NOT asserted (M8-S7 recorded gap): the UI
  // derives it from the re-queried view's scheduledCloseAt moving later, but the lived
  // Listings BC has no ExtendedBiddingTriggered handler — CatalogListingView.ScheduledCloseAt
  // is written once at BiddingOpened and never advances, so the banner is structurally
  // unreachable from the lived backend surface (escalated; M8 has no sanctioned backend
  // exception left). The extension's EFFECT is asserted instead, against the authoritative
  // read model: the auction outlives its original close and sells only at the extended one.
  const msPastOriginalClose = closeAt - Date.now() + 5_000;
  await pageA.waitForTimeout(msPastOriginalClose);
  const afterOriginalClose = await request.get(`/api/listings/${listingId}`);
  expect(afterOriginalClose.ok()).toBe(true);
  const lateView = (await afterOriginalClose.json()) as { status: string };
  expect(
    lateView.status,
    "the in-window bid must have extended the close past the original one",
  ).toBe("Open");

  // --- Moment 7: the gavel falls — sold, winner correct in BOTH contexts ------------------
  // The extended close is ~45s out; allow for the closing saga + projection + push on top.
  // A is the winner ("You won!", or already "It's yours!" if settlement completed first);
  // B sees the listing sold at the hammer price, and never the winner affordance.
  await expect(pageA.getByText(/You won!|It's yours!/)).toBeVisible({ timeout: 120_000 });
  await expect(pageB.getByText("Sold — $55.00")).toBeVisible({ timeout: 30_000 });
  await expect(pageB.getByText(/You won!|It's yours!/)).not.toBeVisible();
  await expect(pageB.getByText(/did not sell/)).not.toBeVisible();

  await pageA.context().close();
  await pageB.context().close();
});
