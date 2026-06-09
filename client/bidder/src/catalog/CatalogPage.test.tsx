import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router";

import { CatalogPage } from "@/catalog/CatalogPage";

const validListing = {
  id: "0192f1a0-1111-7000-8000-000000000001",
  sellerId: "0192f1a0-2222-7000-8000-000000000002",
  title: "Bold Ferret",
  format: "Flash",
  startingBid: 50,
  buyItNow: null,
  duration: "00:05:00",
  publishedAt: "2026-06-04T12:00:00+00:00",
  status: "Open",
  scheduledCloseAt: null,
  currentHighBid: 75,
  currentHighBidderId: "0192f1a0-3333-7000-8000-000000000003",
  bidCount: 3,
  hammerPrice: null,
  winnerId: null,
  passedReason: null,
  finalHighestBid: null,
  closedAt: null,
  settledAt: null,
  sessionId: null,
  sessionStartedAt: null,
};

function mockCatalogResponse(listings: unknown): void {
  vi.stubGlobal(
    "fetch",
    vi.fn(
      async () =>
        ({
          ok: true,
          status: 200,
          json: async () => listings,
        }) as unknown as Response,
    ),
  );
}

// Isolated test router: CatalogPage at "/", a stub listing route so its Link target resolves.
// Deliberately NOT the real AppShell root — keeps SignalR (ConnectionIndicator) and the session
// POST out of jsdom so this stays a focused catalog test.
function renderCatalog(): void {
  const rootRoute = createRootRoute({ component: Outlet });
  const indexRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: CatalogPage,
  });
  const listingRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/listing/$id",
    component: () => null,
  });
  const routeTree = rootRoute.addChildren([indexRoute, listingRoute]);
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("CatalogPage", () => {
  it("renders the published listings from GET /api/listings", async () => {
    mockCatalogResponse([validListing]);
    renderCatalog();

    expect(await screen.findByText("Bold Ferret")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
  });

  it("renders an empty state (not an error) when the catalog is empty", async () => {
    mockCatalogResponse([]);
    renderCatalog();

    expect(
      await screen.findByText(/no listings are published yet/i),
    ).toBeInTheDocument();
  });
});
