import { describe, expect, it, vi, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import {
  QueryClient,
  QueryClientProvider,
} from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import { z } from "zod";

import { SessionProvider } from "@/session/SessionContext";
import { SellerSignalRProvider } from "@/signalr/SignalRProvider";
import { ListingDetailPage } from "@/listings/ListingDetailPage";

class FakeHubConnection {
  state: HubConnectionState = HubConnectionState.Disconnected;
  on() {}
  onreconnecting() {}
  onreconnected() {}
  onclose() {}
  invoke = vi.fn(async () => {});
  async start() {
    this.state = HubConnectionState.Connected;
  }
  async stop() {
    this.state = HubConnectionState.Disconnected;
  }
}

function makeCatalogListing(overrides: Record<string, unknown> = {}) {
  return {
    id: "listing-001",
    sellerId: "seller-abc",
    title: "Vintage Mechanical Keyboard",
    format: "Flash",
    startingBid: 25,
    buyItNow: 100,
    duration: null,
    publishedAt: "2026-06-10T12:00:00Z",
    status: "Open",
    scheduledCloseAt: "2026-06-13T12:05:00Z",
    currentHighBid: 35,
    currentHighBidderId: "bidder-bold",
    bidCount: 2,
    hammerPrice: null,
    winnerId: null,
    passedReason: null,
    finalHighestBid: null,
    closedAt: null,
    settledAt: null,
    sessionId: "session-1",
    sessionStartedAt: "2026-06-13T12:00:00Z",
    ...overrides,
  };
}

function createTestRouter(searchParams: Record<string, unknown> = {}) {
  const rootRoute = createRootRoute();
  const detailRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/listings/$id",
    component: ListingDetailPage,
    validateSearch: z.object({
      reserve: z.number().optional(),
    }),
  });
  const listingsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/listings",
    component: () => <div>listings</div>,
  });
  const tree = rootRoute.addChildren([detailRoute, listingsRoute]);

  const search = Object.keys(searchParams).length > 0
    ? `?${new URLSearchParams(
        Object.entries(searchParams).map(([k, v]) => [k, String(v)]),
      ).toString()}`
    : "";

  return createRouter({
    routeTree: tree,
    history: createMemoryHistory({
      initialEntries: [`/listings/listing-001${search}`],
    }),
  });
}

const fakeConnection = new FakeHubConnection();

function Providers({
  children,
  router,
}: {
  children?: React.ReactNode;
  router: ReturnType<typeof createTestRouter>;
}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <SellerSignalRProvider
          createConnection={
            () => fakeConnection as unknown as HubConnection
          }
        >
          {children ?? <RouterProvider router={router} />}
        </SellerSignalRProvider>
      </SessionProvider>
    </QueryClientProvider>
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

function stubSession() {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (url === "/api/participants/session") {
        return {
          ok: true,
          status: 200,
          headers: new Headers({
            Location: "/api/participants/seller-p",
          }),
          json: async () => ({ value: "seller-p" }),
        };
      }
      if (url.startsWith("/api/listings/")) {
        return {
          ok: true,
          status: 200,
          headers: new Headers(),
          json: async () => makeCatalogListing(),
        };
      }
      return { ok: false, status: 404, headers: new Headers(), json: async () => ({}) };
    }),
  );
}

describe("ListingDetailPage", () => {
  it("renders listing title and format", async () => {
    stubSession();
    const router = createTestRouter();

    render(<Providers router={router} />);

    await waitFor(() =>
      expect(screen.getByText("Vintage Mechanical Keyboard")).toBeInTheDocument(),
    );
    expect(screen.getByText("Flash")).toBeInTheDocument();
  });

  it("renders auction status badge", async () => {
    stubSession();
    const router = createTestRouter();

    render(<Providers router={router} />);

    await waitFor(() =>
      expect(screen.getByText("Open")).toBeInTheDocument(),
    );
  });

  it("renders starting bid and buy-it-now", async () => {
    stubSession();
    const router = createTestRouter();

    render(<Providers router={router} />);

    await waitFor(() =>
      expect(screen.getByText("$25.00")).toBeInTheDocument(),
    );
    expect(screen.getByText("$100.00")).toBeInTheDocument();
  });

  it("renders current bid and bid count in live auction panel", async () => {
    stubSession();
    const router = createTestRouter();

    render(<Providers router={router} />);

    await waitFor(() =>
      expect(screen.getByText("$35.00")).toBeInTheDocument(),
    );
    expect(screen.getByText("2 bids")).toBeInTheDocument();
  });

  it("shows reserve-not-met indicator when reserve search param is passed", async () => {
    stubSession();
    const router = createTestRouter({ reserve: 50 });

    render(<Providers router={router} />);

    await waitFor(() =>
      expect(
        screen.getByText(/Reserve not met/),
      ).toBeInTheDocument(),
    );
  });

  it("shows reserve-met indicator when current bid exceeds reserve", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        if (url === "/api/participants/session") {
          return {
            ok: true,
            status: 200,
            headers: new Headers({
              Location: "/api/participants/seller-p",
            }),
            json: async () => ({ value: "seller-p" }),
          };
        }
        if (url.startsWith("/api/listings/")) {
          return {
            ok: true,
            status: 200,
            headers: new Headers(),
            json: async () => makeCatalogListing({ currentHighBid: 55 }),
          };
        }
        return { ok: false, status: 404, headers: new Headers(), json: async () => ({}) };
      }),
    );

    const router = createTestRouter({ reserve: 50 });
    render(<Providers router={router} />);

    await waitFor(() =>
      expect(
        screen.getByText(/Reserve met/),
      ).toBeInTheDocument(),
    );
  });

  it("shows sold outcome for terminal listing", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        if (url === "/api/participants/session") {
          return {
            ok: true,
            status: 200,
            headers: new Headers({
              Location: "/api/participants/seller-p",
            }),
            json: async () => ({ value: "seller-p" }),
          };
        }
        if (url.startsWith("/api/listings/")) {
          return {
            ok: true,
            status: 200,
            headers: new Headers(),
            json: async () =>
              makeCatalogListing({
                status: "Sold",
                hammerPrice: 55,
                winnerId: "bidder-swift",
                bidCount: 3,
              }),
          };
        }
        return { ok: false, status: 404, headers: new Headers(), json: async () => ({}) };
      }),
    );

    const router = createTestRouter();
    render(<Providers router={router} />);

    await waitFor(() =>
      expect(screen.getByText(/Sold for/)).toBeInTheDocument(),
    );
    expect(screen.getByText(/\$55\.00/)).toBeInTheDocument();
  });

  it("shows observer message when listing is open (no bid panel)", async () => {
    stubSession();
    const router = createTestRouter();

    render(<Providers router={router} />);

    await waitFor(() =>
      expect(
        screen.getByText(/You are observing this auction/),
      ).toBeInTheDocument(),
    );
  });

  it("shows not-found state for 404 listing", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        if (url === "/api/participants/session") {
          return {
            ok: true,
            status: 200,
            headers: new Headers({
              Location: "/api/participants/seller-p",
            }),
            json: async () => ({ value: "seller-p" }),
          };
        }
        if (url.startsWith("/api/listings/")) {
          return {
            ok: false,
            status: 404,
            headers: new Headers(),
            json: async () => ({}),
          };
        }
        return { ok: false, status: 500, headers: new Headers(), json: async () => ({}) };
      }),
    );

    const router = createTestRouter();
    render(<Providers router={router} />);

    await waitFor(() =>
      expect(
        screen.getByText(/doesn't exist/),
      ).toBeInTheDocument(),
    );
  });

  it("shows back link to my listings", async () => {
    stubSession();
    const router = createTestRouter();

    render(<Providers router={router} />);

    await waitFor(() =>
      expect(
        screen.getByText("← Back to my listings"),
      ).toBeInTheDocument(),
    );
  });
});
