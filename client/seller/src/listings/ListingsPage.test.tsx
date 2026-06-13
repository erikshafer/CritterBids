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

import { ListingsPage } from "@/listings/ListingsPage";
import { SessionProvider } from "@/session/SessionContext";

const validListing = {
  id: "0192f1a0-1111-7000-8000-000000000001",
  sellerId: "0192f1a0-2222-7000-8000-000000000002",
  title: "Vintage Mechanical Keyboard",
  format: "Flash",
  status: "Published",
  startingBid: 25,
  reservePrice: 50,
  buyItNowPrice: 100,
  createdAt: "2026-06-10T14:00:00+00:00",
  publishedAt: "2026-06-10T15:00:00+00:00",
};

function stubFetch(responses: Record<string, unknown>): void {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      for (const [pattern, body] of Object.entries(responses)) {
        if (url.includes(pattern)) {
          return {
            ok: true,
            status: 200,
            headers: new Headers(
              pattern === "/api/participants/session"
                ? { Location: "/api/participants/seller-test-id" }
                : {},
            ),
            json: async () => body,
          } as unknown as Response;
        }
      }
      return {
        ok: false,
        status: 404,
        headers: new Headers(),
        json: async () => null,
      } as unknown as Response;
    }),
  );
}

function renderListingsPage(): void {
  sessionStorage.setItem("critterbids.seller.participantId", "seller-test-id");
  sessionStorage.setItem("critterbids.seller.isRegisteredSeller", "true");

  const rootRoute = createRootRoute({ component: Outlet });
  const listingsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: ListingsPage,
  });
  const routeTree = rootRoute.addChildren([listingsRoute]);
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  render(
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <RouterProvider router={router} />
      </SessionProvider>
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ListingsPage", () => {
  it("renders listing cards from the seller query endpoint", async () => {
    stubFetch({
      "/api/selling/listings": [validListing],
    });
    renderListingsPage();

    expect(
      await screen.findByText("Vintage Mechanical Keyboard"),
    ).toBeInTheDocument();
    expect(screen.getByText("Published")).toBeInTheDocument();
    expect(screen.getByText("Flash")).toBeInTheDocument();
  });

  it("renders an empty state when the seller has no listings", async () => {
    stubFetch({
      "/api/selling/listings": [],
    });
    renderListingsPage();

    expect(
      await screen.findByText(/haven't created any listings/i),
    ).toBeInTheDocument();
  });

  it("renders an error state on fetch failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: false,
        status: 500,
        headers: new Headers(),
        json: async () => null,
      })),
    );
    renderListingsPage();

    expect(
      await screen.findByText(/something went wrong/i),
    ).toBeInTheDocument();
  });

  it("shows reserve and BIN when present", async () => {
    stubFetch({
      "/api/selling/listings": [validListing],
    });
    renderListingsPage();

    await screen.findByText("Vintage Mechanical Keyboard");
    expect(screen.getByText(/Reserve:/)).toBeInTheDocument();
    expect(screen.getByText(/BIN:/)).toBeInTheDocument();
  });

  it("hides reserve and BIN when null", async () => {
    const noExtras = {
      ...validListing,
      reservePrice: null,
      buyItNowPrice: null,
    };
    stubFetch({
      "/api/selling/listings": [noExtras],
    });
    renderListingsPage();

    await screen.findByText("Vintage Mechanical Keyboard");
    expect(screen.queryByText(/Reserve:/)).not.toBeInTheDocument();
    expect(screen.queryByText(/BIN:/)).not.toBeInTheDocument();
  });
});
