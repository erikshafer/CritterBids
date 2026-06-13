import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router";
import { z } from "zod";

import { ListingsPage } from "@/listings/ListingsPage";
import { SessionProvider } from "@/session/SessionContext";

const draftListing = {
  id: "0192f1a0-1111-7000-8000-000000000001",
  sellerId: "0192f1a0-2222-7000-8000-000000000002",
  title: "Vintage Mechanical Keyboard",
  format: "Flash",
  status: "Draft",
  startingBid: 25,
  reservePrice: 50,
  buyItNowPrice: 100,
  createdAt: "2026-06-10T14:00:00+00:00",
  publishedAt: null,
};

const publishedListing = {
  ...draftListing,
  id: "0192f1a0-3333-7000-8000-000000000003",
  title: "Vintage Folding Camera",
  status: "Published",
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
            text: async () => JSON.stringify(body),
          } as unknown as Response;
        }
      }
      return {
        ok: false,
        status: 404,
        headers: new Headers(),
        json: async () => null,
        text: async () => "",
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
  const createRoute_ = createRoute({
    getParentRoute: () => rootRoute,
    path: "/listings/new",
    component: () => <div>Create Listing Page</div>,
  });
  const detailRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/listings/$id",
    component: () => <div>Listing Detail Page</div>,
    validateSearch: z.object({
      reserve: z.number().optional(),
    }),
  });
  const routeTree = rootRoute.addChildren([
    listingsRoute,
    createRoute_,
    detailRoute,
  ]);
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
      "/api/selling/listings": [draftListing],
    });
    renderListingsPage();

    expect(
      await screen.findByText("Vintage Mechanical Keyboard"),
    ).toBeInTheDocument();
    expect(screen.getByText("Draft")).toBeInTheDocument();
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
        text: async () => "",
      })),
    );
    renderListingsPage();

    expect(
      await screen.findByText(/something went wrong/i),
    ).toBeInTheDocument();
  });

  it("shows reserve and BIN when present", async () => {
    stubFetch({
      "/api/selling/listings": [draftListing],
    });
    renderListingsPage();

    await screen.findByText("Vintage Mechanical Keyboard");
    expect(screen.getByText(/Reserve:/)).toBeInTheDocument();
    expect(screen.getByText(/BIN:/)).toBeInTheDocument();
  });

  it("hides reserve and BIN when null", async () => {
    const noExtras = {
      ...draftListing,
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

  it("shows Edit and Submit buttons on Draft listings", async () => {
    stubFetch({
      "/api/selling/listings": [draftListing],
    });
    renderListingsPage();

    await screen.findByText("Vintage Mechanical Keyboard");
    expect(screen.getByRole("button", { name: "Edit" })).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Submit for Publication" }),
    ).toBeInTheDocument();
  });

  it("hides Edit and Submit buttons on non-Draft listings", async () => {
    stubFetch({
      "/api/selling/listings": [publishedListing],
    });
    renderListingsPage();

    await screen.findByText("Vintage Folding Camera");
    expect(
      screen.queryByRole("button", { name: "Edit" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Submit for Publication" }),
    ).not.toBeInTheDocument();
  });

  it("renders the Create Listing button", async () => {
    stubFetch({
      "/api/selling/listings": [],
    });
    renderListingsPage();

    expect(
      await screen.findByRole("link", { name: "Create Listing" }),
    ).toBeInTheDocument();
  });

  it("opens edit dialog when Edit button is clicked", async () => {
    stubFetch({
      "/api/selling/listings": [draftListing],
    });
    renderListingsPage();
    const user = userEvent.setup();

    await screen.findByText("Vintage Mechanical Keyboard");
    await user.click(screen.getByRole("button", { name: "Edit" }));

    expect(
      screen.getByRole("dialog", { name: /Edit Vintage Mechanical Keyboard/i }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Title")).toBeInTheDocument();
  });

  it("calls submit endpoint when Submit for Publication is clicked", async () => {
    const fetchMock = vi.fn(async (url: string) => {
      if (url.includes("/api/selling/listings?")) {
        return {
          ok: true,
          status: 200,
          headers: new Headers(),
          json: async () => [draftListing],
          text: async () => JSON.stringify([draftListing]),
        } as unknown as Response;
      }
      return {
        ok: true,
        status: 202,
        headers: new Headers(),
        json: async () => null,
        text: async () => "",
      } as unknown as Response;
    });
    vi.stubGlobal("fetch", fetchMock);
    renderListingsPage();
    const user = userEvent.setup();

    await screen.findByText("Vintage Mechanical Keyboard");
    await user.click(
      screen.getByRole("button", { name: "Submit for Publication" }),
    );

    await vi.waitFor(() => {
      const submitCall = fetchMock.mock.calls.find(
        (c) =>
          typeof c[0] === "string" &&
          c[0].includes("/api/selling/listings/submit"),
      );
      expect(submitCall).toBeDefined();
    });
  });

  it("shows View button on Published listings", async () => {
    stubFetch({
      "/api/selling/listings": [publishedListing],
    });
    renderListingsPage();

    await screen.findByText("Vintage Folding Camera");
    expect(screen.getByRole("button", { name: "View" })).toBeInTheDocument();
  });

  it("does not show View button on Draft listings", async () => {
    stubFetch({
      "/api/selling/listings": [draftListing],
    });
    renderListingsPage();

    await screen.findByText("Vintage Mechanical Keyboard");
    expect(
      screen.queryByRole("button", { name: "View" }),
    ).not.toBeInTheDocument();
  });
});
