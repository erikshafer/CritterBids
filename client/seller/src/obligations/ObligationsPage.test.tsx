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

import { ObligationsPage } from "@/obligations/ObligationsPage";
import { SessionProvider } from "@/session/SessionContext";

const BASE_OBLIGATION = {
  id: "0192f1a0-1111-7000-8000-000000000001",
  listingId: "0192f1a0-2222-7000-8000-000000000002",
  winnerId: "0192f1a0-3333-7000-8000-000000000003",
  sellerId: "seller-test-id",
  hammerPrice: 55.0,
  shipByDeadline: new Date(Date.now() + 86_400_000).toISOString(),
  trackingNumber: null,
  reminderSentAt: null,
  trackingProvidedAt: null,
  fulfilledAt: null,
  escalatedAt: null,
  disputeId: null,
  disputeReason: null,
  disputeOpenedAt: null,
  disputeResolution: null,
  disputeResolvedAt: null,
};

function stubFetch(obligations: unknown[]): void {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (typeof url === "string" && url.includes("/api/obligations/status")) {
        return {
          ok: true,
          status: 200,
          headers: new Headers(),
          json: async () => obligations,
          text: async () => JSON.stringify(obligations),
        } as unknown as Response;
      }
      if (typeof url === "string" && url.includes("/api/selling/listings")) {
        return {
          ok: true,
          status: 200,
          headers: new Headers(),
          json: async () => [],
          text: async () => "[]",
        } as unknown as Response;
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

function renderObligationsPage(): void {
  sessionStorage.setItem("critterbids.seller.participantId", "seller-test-id");
  sessionStorage.setItem("critterbids.seller.isRegisteredSeller", "true");

  const rootRoute = createRootRoute({ component: Outlet });
  const obligationsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: ObligationsPage,
  });
  const routeTree = rootRoute.addChildren([obligationsRoute]);
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

describe("ObligationsPage", () => {
  it("renders an empty state when no obligations exist", async () => {
    stubFetch([]);
    renderObligationsPage();

    expect(
      await screen.findByText(/no post-sale obligations/i),
    ).toBeInTheDocument();
  });

  it("renders AwaitingShipment with deadline and Provide Tracking button", async () => {
    stubFetch([{ ...BASE_OBLIGATION, status: "AwaitingShipment" }]);
    renderObligationsPage();

    expect(
      await screen.findByText(/ship your item by/i),
    ).toBeInTheDocument();
    expect(screen.getByText("Awaiting Shipment")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Provide Tracking" }),
    ).toBeInTheDocument();
  });

  it("renders reminder banner when reminderSentAt is set", async () => {
    stubFetch([
      {
        ...BASE_OBLIGATION,
        status: "AwaitingShipment",
        reminderSentAt: "2026-06-13T10:00:00Z",
      },
    ]);
    renderObligationsPage();

    expect(
      await screen.findByText(/reminder sent/i),
    ).toBeInTheDocument();
  });

  it("renders Escalated status with overdue messaging and tracking button", async () => {
    stubFetch([
      {
        ...BASE_OBLIGATION,
        status: "Escalated",
        escalatedAt: "2026-06-14T12:00:00Z",
      },
    ]);
    renderObligationsPage();

    expect(
      await screen.findByText(/your deadline passed/i),
    ).toBeInTheDocument();
    expect(screen.getByText("Overdue")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Provide Tracking" }),
    ).toBeInTheDocument();
  });

  it("renders Shipped status with tracking number", async () => {
    stubFetch([
      {
        ...BASE_OBLIGATION,
        status: "Shipped",
        trackingNumber: "1Z999AA10123456784",
        trackingProvidedAt: "2026-06-13T10:00:00Z",
      },
    ]);
    renderObligationsPage();

    expect(
      await screen.findByText(/1Z999AA10123456784/),
    ).toBeInTheDocument();
    expect(screen.getByText("Shipped")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Provide Tracking" }),
    ).not.toBeInTheDocument();
  });

  it("renders Fulfilled status as completed", async () => {
    stubFetch([
      {
        ...BASE_OBLIGATION,
        status: "Fulfilled",
        trackingNumber: "TRACK123",
        trackingProvidedAt: "2026-06-13T10:00:00Z",
        fulfilledAt: "2026-06-13T12:00:00Z",
      },
    ]);
    renderObligationsPage();

    expect(await screen.findByText("Completed.")).toBeInTheDocument();
    expect(screen.getByText("Fulfilled")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Provide Tracking" }),
    ).not.toBeInTheDocument();
  });

  it("renders Disputed status with reason", async () => {
    stubFetch([
      {
        ...BASE_OBLIGATION,
        status: "Disputed",
        disputeId: "00000000-0000-0000-0000-000000000099",
        disputeReason: "NonDelivery",
        disputeOpenedAt: "2026-06-14T14:00:00Z",
      },
    ]);
    renderObligationsPage();

    expect(
      await screen.findByText(/dispute open.*nondelivery/i),
    ).toBeInTheDocument();
    expect(screen.getByText("Disputed")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Provide Tracking" }),
    ).not.toBeInTheDocument();
  });

  it("renders resolved dispute status", async () => {
    stubFetch([
      {
        ...BASE_OBLIGATION,
        status: "Disputed",
        disputeId: "00000000-0000-0000-0000-000000000099",
        disputeReason: "NonDelivery",
        disputeOpenedAt: "2026-06-14T14:00:00Z",
        disputeResolution: "Refund",
        disputeResolvedAt: "2026-06-15T10:00:00Z",
      },
    ]);
    renderObligationsPage();

    expect(
      await screen.findByText(/dispute resolved.*refund/i),
    ).toBeInTheDocument();
  });

  it("renders hammer price on obligation card", async () => {
    stubFetch([{ ...BASE_OBLIGATION, status: "AwaitingShipment" }]);
    renderObligationsPage();

    expect(await screen.findByText("$55.00 sale")).toBeInTheDocument();
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
    renderObligationsPage();

    expect(
      await screen.findByText(/something went wrong/i),
    ).toBeInTheDocument();
  });

  it("shows overdue deadline text when shipByDeadline is in the past", async () => {
    stubFetch([
      {
        ...BASE_OBLIGATION,
        status: "AwaitingShipment",
        shipByDeadline: new Date(Date.now() - 60_000).toISOString(),
      },
    ]);
    renderObligationsPage();

    expect(await screen.findByText(/overdue/i)).toBeInTheDocument();
  });
});
