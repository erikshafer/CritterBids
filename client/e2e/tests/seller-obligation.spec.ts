import {
  test,
  expect,
  type APIRequestContext,
  type Browser,
  type Page,
} from "@playwright/test";

// The narrative-006 obligation-fulfillment spine (Moments 1–4), automated from the SELLER's
// vantage — the M9 counterpart to the bid-war's bidder-vantage test. One seller's listing sells,
// the seller console surfaces the resulting post-sale obligation, the seller provides tracking
// THROUGH THE CONSOLE UI (the react-hook-form dialog from M9-S6), and the obligation auto-confirms
// to the "Completed" terminal. Assertions go through what the SELLER sees (the rendered, re-queried
// ObligationStatusView), never into hub internals (ADR 026 applied to test assertions).
//
// Two pieces of lived-backend reality shape this test (read before it was written):
//   • IDENTITY BRIDGE (M9-S8 OQ-1, resolved seed-then-inject). The seller console mints its own
//     anonymous session and the operator create-session/attach/start step is staff/bus-only, so the
//     console can't both publish AND open a listing on its own. POST /api/dev/seed-flash already
//     drives a server-minted *registered seller* through publish→attach→start to Open and returns
//     that sellerId; we inject it into the console's session storage so the console adopts the
//     seeded seller identity. No backend change — the console renders that seller's obligations via
//     GET /api/obligations/status?sellerId=. The sale itself is forced with the dev Buy-It-Now
//     trigger (a single deterministic purchase) rather than riding a wall-clock auction — the
//     auction mechanics are already the bid-war test's job; the obligation lifecycle is this one's.
//   • DEMO TIMERS (M9-S8). The Obligations post-sale timers are days in production and seconds in
//     demo mode; the AppHost sets Obligations__DemoMode=true for the orchestrated demo run, so the
//     ship-by deadline (10s) and the post-tracking auto-confirm window (10s) elapse live inside this
//     test. A consequence we assert around: the obligation may ESCALATE (Overdue) before we provide
//     tracking — the "Provide Tracking" affordance survives escalation (narrative 007's recovery
//     door), so we gate on the affordance, not on a not-yet-escalated status.
//
// Bid-war harness conventions carried verbatim:
//   • the seller console's BiddingHub connection is asserted BEFORE any obligation action (a dead
//     connection and a missing push are otherwise indistinguishable);
//   • the listing is seeded with a per-run unique title, so a stale row from an earlier run can
//     never satisfy an assertion;
//   • the contexts are torn down at the end.

/** Per-run unique seed title (bid-war lesson — greppable acceptance criterion). */
const uniqueTitle = `E2E Seller Obligation ${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

// The seed's Buy-It-Now default is $100, so the sale's hammer price — and the obligation card's
// headline — is deterministic.
const BUY_IT_NOW = 100;

// The seller console runs on its own origin (:5175, base /seller/); /api and /hub ride its Vite
// proxy exactly as the bidder app's do. The `request` fixture stays on the bidder origin (:5173,
// the config baseURL) — both proxy to the same API host, so API calls are origin-agnostic.
const SELLER_URL = process.env.CRITTERBIDS_SELLER_URL ?? "http://localhost:5175";

// The seller console keys its session off these (seller-specific, distinct from the bidder app's
// `critterbids.participantId`). Pre-seeding both makes SessionProvider boot straight to an
// established, registered-seller session with no session POST and no register-seller call.
const SELLER_PID_KEY = "critterbids.seller.participantId";
const SELLER_REGISTERED_KEY = "critterbids.seller.isRegisteredSeller";

interface SeededListing {
  listingId: string;
  sellerId: string;
}

async function seedFlashListing(request: APIRequestContext): Promise<SeededListing> {
  // The seed drives the listing all the way to Open, polling cross-BC read models between stages —
  // give it a generous timeout. We keep the default 5-minute Flash duration: the sale is forced via
  // Buy-It-Now, so the auction never has to close on its own.
  const response = await request.post("/api/dev/seed-flash", {
    data: { title: uniqueTitle },
    timeout: 90_000,
  });
  expect(response.ok(), `seed-flash failed: ${response.status()}`).toBe(true);
  const body = (await response.json()) as { listingId: string; sellerId: string };
  expect(body.listingId, "seed must return a listingId").toBeTruthy();
  expect(body.sellerId, "seed must return the seeded sellerId").toBeTruthy();
  return { listingId: body.listingId, sellerId: body.sellerId };
}

/** Mint a fresh anonymous participant to play the winning buyer. Returns its id. */
async function mintBuyer(request: APIRequestContext): Promise<string> {
  const response = await request.post("/api/participants/session", {
    data: {},
    headers: { "Content-Type": "application/json" },
  });
  expect(response.ok(), `session start failed: ${response.status()}`).toBe(true);
  // CreationResponse<Guid>: id is the last Location segment, with the body's `value` as a fallback.
  const location = response.headers()["location"];
  const fromLocation = location?.split("/").filter(Boolean).pop();
  if (fromLocation) return fromLocation;
  const body = (await response.json()) as { value?: string };
  expect(body.value, "session response must carry a participant id").toBeTruthy();
  return body.value!;
}

/** The seeded seller's obligations as the read model currently holds them. */
async function fetchObligations(
  request: APIRequestContext,
  sellerId: string,
): Promise<Array<{ id: string; status: string; hammerPrice: number }>> {
  const response = await request.get(`/api/obligations/status?sellerId=${sellerId}`);
  expect(response.ok(), `obligations query failed: ${response.status()}`).toBe(true);
  return (await response.json()) as Array<{ id: string; status: string; hammerPrice: number }>;
}

/**
 * Poll the obligations read model until `predicate` holds, returning the matching obligation. The
 * read model is fed asynchronously over RabbitMQ (settlement → obligation start; then the demo-mode
 * timers), so this is a readiness gate, not the assertion — the assertion is what the seller SEES
 * after we reload the console (the bid-war's scheduledCloseAt-gate pattern).
 */
async function waitForObligation(
  request: APIRequestContext,
  sellerId: string,
  predicate: (o: { status: string }) => boolean,
  timeoutMs: number,
): Promise<{ id: string; status: string; hammerPrice: number }> {
  const deadline = Date.now() + timeoutMs;
  let last: Array<{ id: string; status: string; hammerPrice: number }> = [];
  while (Date.now() < deadline) {
    last = await fetchObligations(request, sellerId);
    const match = last.find(predicate);
    if (match) return match;
    await new Promise((r) => setTimeout(r, 500));
  }
  throw new Error(
    `obligation predicate not met within ${timeoutMs}ms; last read model state: ${JSON.stringify(last)}`,
  );
}

/**
 * Open the seller console AS the seeded seller: a fresh context whose session storage is pre-seeded
 * with the seeded sellerId (and the registered-seller flag) before any app script runs, so the
 * console adopts that identity instead of minting a new anonymous one. Gate on the BiddingHub
 * connection before any obligation action (the bid-war lesson).
 */
async function openSellerConsole(browser: Browser, sellerId: string): Promise<Page> {
  const context = await browser.newContext();
  await context.addInitScript(
    ([pidKey, registeredKey, pid]) => {
      sessionStorage.setItem(pidKey, pid);
      sessionStorage.setItem(registeredKey, "true");
    },
    [SELLER_PID_KEY, SELLER_REGISTERED_KEY, sellerId] as const,
  );

  const page = await context.newPage();
  await page.goto(`${SELLER_URL}/seller/obligations`);

  // Hub connection asserted before any obligation traffic. "Connected" is case-exact and not a
  // substring of "Disconnected"/"Reconnecting", so toContainText cannot false-positive.
  await expect(page.getByTitle("BiddingHub connection")).toContainText("Connected");

  return page;
}

test("a seller fulfills a post-sale obligation through the console", async ({ browser, request }) => {
  // --- Reachability gate: a clear message beats ECONNREFUSED noise -----------------------
  const probe = await request.get("/api/listings").catch(() => null);
  if (probe === null || !probe.ok()) {
    throw new Error(
      "The CritterBids stack is not reachable on http://localhost:5173. " +
        "Start it first: dotnet run --project src/CritterBids.AppHost --launch-profile http " +
        "(the seller dev server on :5175 must be up too — it launches as an Aspire child).",
    );
  }

  // --- Seed one Flash listing for this run (server-minted registered seller, unique title) ---
  const { listingId, sellerId } = await seedFlashListing(request);

  // --- Open the seller console AS the seeded seller; assert it starts with no obligations -----
  const sellerPage = await openSellerConsole(browser, sellerId);
  await expect(sellerPage.getByText("No post-sale obligations yet.")).toBeVisible();

  // --- Force the sale: a fresh buyer takes the listing at Buy-It-Now ($100) -------------------
  // BuyNow has no public HTTP surface (buyer-side BIN UI is a future milestone), so we drive it
  // through the dev trigger the same way the seed drives the bus-only seller/operator commands.
  const buyerId = await mintBuyer(request);
  const buyNow = await request.post("/api/dev/buy-now", {
    data: { listingId, buyerId },
  });
  expect(buyNow.ok(), `buy-now failed: ${buyNow.status()}`).toBe(true);

  // --- Settlement → obligation: wait for the post-sale obligation to materialize --------------
  // It starts AwaitingShipment; under demo timers it may already have escalated (Overdue) by the
  // time we observe it — either is an actionable, tracking-providable state.
  const pending = await waitForObligation(
    request,
    sellerId,
    (o) => o.status === "AwaitingShipment" || o.status === "Escalated",
    60_000,
  );
  expect(pending.hammerPrice).toBe(BUY_IT_NOW);

  // --- Moment 1–2: the seller SEES the obligation in the console (re-query on reload) ---------
  // The console's obligations query cached the empty result above; reload to re-fetch now that the
  // read model carries the row (the burst-final push race, M8-S7 Finding 2, makes a reload more
  // deterministic than waiting on the creation push for the FIRST observation).
  await sellerPage.reload();
  await expect(sellerPage.getByText(`$${BUY_IT_NOW}.00 sale`)).toBeVisible();
  const provideTracking = sellerPage.getByRole("button", { name: "Provide Tracking" });
  await expect(provideTracking).toBeVisible();

  // --- Moment 3: the seller provides tracking THROUGH THE CONSOLE (react-hook-form dialog) -----
  await provideTracking.click();
  const dialog = sellerPage.getByRole("dialog", { name: "Provide tracking information" });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel("Tracking Number").fill("1Z999AA10123456784");
  await dialog.getByRole("button", { name: "Submit Tracking" }).click();
  // The dialog closes on a successful mutation.
  await expect(dialog).toBeHidden();

  // --- Moment 4: delivery auto-confirms; the seller SEES "Completed" --------------------------
  // Post-tracking the obligation goes Shipped, then auto-confirms after the demo AutoConfirmWindow
  // (10s) to Fulfilled. Gate on the read model reaching Fulfilled, then reload and assert the
  // terminal through the UI — the seller's window shows "Completed."
  await waitForObligation(request, sellerId, (o) => o.status === "Fulfilled", 40_000);
  await sellerPage.reload();
  await expect(sellerPage.getByText("Completed.")).toBeVisible();
  // `exact` pins the status badge; without it this also matches the "Fulfilled {timestamp}" line.
  await expect(sellerPage.getByText("Fulfilled", { exact: true })).toBeVisible();

  await sellerPage.context().close();
});
