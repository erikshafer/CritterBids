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

import { ProvideTrackingDialog } from "@/obligations/ProvideTrackingDialog";
import type { ObligationStatusView } from "@/obligations/schema";
import { SessionProvider } from "@/session/SessionContext";

const OBLIGATION: ObligationStatusView = {
  id: "0192f1a0-1111-7000-8000-000000000001",
  listingId: "0192f1a0-2222-7000-8000-000000000002",
  winnerId: "0192f1a0-3333-7000-8000-000000000003",
  sellerId: "seller-test-id",
  hammerPrice: 55.0,
  status: "AwaitingShipment",
  shipByDeadline: "2026-06-14T12:00:00Z",
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

function renderDialog(onClose = vi.fn()): {
  onClose: ReturnType<typeof vi.fn>;
  fetchMock: ReturnType<typeof vi.fn>;
} {
  sessionStorage.setItem("critterbids.seller.participantId", "seller-test-id");
  sessionStorage.setItem("critterbids.seller.isRegisteredSeller", "true");

  const fetchMock = vi.fn(async () => ({
    ok: true,
    status: 202,
    headers: new Headers(),
    json: async () => null,
    text: async () => "",
  }));
  vi.stubGlobal("fetch", fetchMock);

  const rootRoute = createRootRoute({ component: Outlet });
  const homeRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: () => (
      <ProvideTrackingDialog obligation={OBLIGATION} onClose={onClose} />
    ),
  });
  const routeTree = rootRoute.addChildren([homeRoute]);
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

  return { onClose, fetchMock };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ProvideTrackingDialog", () => {
  it("renders the dialog with tracking number input", async () => {
    renderDialog();

    expect(
      await screen.findByRole("dialog", { name: /provide tracking/i }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Tracking Number")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Submit Tracking" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Cancel" }),
    ).toBeInTheDocument();
  });

  it("shows the obligation context (hammer price)", async () => {
    renderDialog();

    expect(await screen.findByText(/\$55\.00 sale/)).toBeInTheDocument();
  });

  it("validates that tracking number is required", async () => {
    renderDialog();
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: "Submit Tracking" }),
    );

    expect(
      await screen.findByText(/tracking number is required/i),
    ).toBeInTheDocument();
  });

  it("submits tracking number to the endpoint", async () => {
    const { fetchMock } = renderDialog();
    const user = userEvent.setup();

    await user.type(
      await screen.findByLabelText("Tracking Number"),
      "1Z999AA10123456784",
    );
    await user.click(screen.getByRole("button", { name: "Submit Tracking" }));

    await vi.waitFor(() => {
      const trackingCall = fetchMock.mock.calls.find(
        (c) =>
          typeof c[0] === "string" &&
          c[0].includes("/api/obligations/tracking"),
      );
      expect(trackingCall).toBeDefined();
      const body = JSON.parse(
        (trackingCall![1] as { body: string }).body,
      ) as { obligationId: string; trackingNumber: string };
      expect(body.obligationId).toBe(OBLIGATION.id);
      expect(body.trackingNumber).toBe("1Z999AA10123456784");
    });
  });

  it("calls onClose on successful submit", async () => {
    const onClose = vi.fn();
    renderDialog(onClose);
    const user = userEvent.setup();

    await user.type(
      await screen.findByLabelText("Tracking Number"),
      "TRACK-XYZ",
    );
    await user.click(screen.getByRole("button", { name: "Submit Tracking" }));

    await vi.waitFor(() => {
      expect(onClose).toHaveBeenCalled();
    });
  });

  it("calls onClose when Cancel is clicked", async () => {
    const onClose = vi.fn();
    renderDialog(onClose);
    const user = userEvent.setup();

    await user.click(
      await screen.findByRole("button", { name: "Cancel" }),
    );

    expect(onClose).toHaveBeenCalled();
  });

  it("shows error on mutation failure", async () => {
    const { fetchMock } = renderDialog();
    fetchMock.mockResolvedValue({
      ok: false,
      status: 400,
      headers: new Headers(),
      json: async () => null,
      text: async () => "Invalid tracking number",
    });
    const user = userEvent.setup();

    await user.type(
      await screen.findByLabelText("Tracking Number"),
      "BAD-TRACK",
    );
    await user.click(screen.getByRole("button", { name: "Submit Tracking" }));

    expect(
      await screen.findByText("Invalid tracking number"),
    ).toBeInTheDocument();
  });
});
