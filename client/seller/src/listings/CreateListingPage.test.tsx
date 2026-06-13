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

import { CreateListingPage } from "@/listings/CreateListingPage";
import { SessionProvider } from "@/session/SessionContext";

function stubFetch(overrides: Record<string, unknown> = {}): void {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      for (const [pattern, body] of Object.entries(overrides)) {
        if (url.includes(pattern)) {
          return {
            ok: true,
            status: init?.method === "POST" ? 201 : 200,
            headers: new Headers(
              pattern === "/api/listings/draft"
                ? { Location: "/api/listings/new-id" }
                : pattern === "/api/participants/session"
                  ? { Location: "/api/participants/test-seller-id" }
                  : {},
            ),
            json: async () => body,
            text: async () => JSON.stringify(body),
          } as unknown as Response;
        }
      }
      return {
        ok: true,
        status: 200,
        headers: new Headers(),
        json: async () => null,
        text: async () => "",
      } as unknown as Response;
    }),
  );
}

function renderCreateListingPage(): void {
  sessionStorage.setItem("critterbids.seller.participantId", "test-seller-id");
  sessionStorage.setItem("critterbids.seller.isRegisteredSeller", "true");

  const rootRoute = createRootRoute({ component: Outlet });
  const createRoute_ = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: CreateListingPage,
  });
  const listingsRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/listings",
    component: () => <div>Listings Page</div>,
  });
  const routeTree = rootRoute.addChildren([createRoute_, listingsRoute]);
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

describe("CreateListingPage", () => {
  it("renders all core form fields", async () => {
    stubFetch();
    renderCreateListingPage();

    expect(
      await screen.findByLabelText("Title"),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Auction Format")).toBeInTheDocument();
    expect(screen.getByLabelText("Starting Bid ($)")).toBeInTheDocument();
    expect(
      screen.getByLabelText("Reserve Price ($) — optional"),
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText("Buy It Now Price ($) — optional"),
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText("Enable extended bidding"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Create Draft" }),
    ).toBeInTheDocument();
  });

  it("shows duration field when Timed format is selected", async () => {
    stubFetch();
    renderCreateListingPage();
    const user = userEvent.setup();

    await screen.findByLabelText("Title");

    expect(screen.queryByLabelText("Duration")).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("Auction Format"), "Timed");

    expect(screen.getByLabelText("Duration")).toBeInTheDocument();
  });

  it("hides duration field when Flash format is selected", async () => {
    stubFetch();
    renderCreateListingPage();
    const user = userEvent.setup();

    await screen.findByLabelText("Title");

    await user.selectOptions(screen.getByLabelText("Auction Format"), "Timed");
    expect(screen.getByLabelText("Duration")).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText("Auction Format"), "Flash");
    expect(screen.queryByLabelText("Duration")).not.toBeInTheDocument();
  });

  it("shows extended-bidding fields when checkbox is checked", async () => {
    stubFetch();
    renderCreateListingPage();
    const user = userEvent.setup();

    await screen.findByLabelText("Title");

    expect(
      screen.queryByLabelText("Trigger Window"),
    ).not.toBeInTheDocument();

    await user.click(screen.getByLabelText("Enable extended bidding"));

    expect(screen.getByLabelText("Trigger Window")).toBeInTheDocument();
    expect(screen.getByLabelText("Extension")).toBeInTheDocument();
  });

  it("hides extended-bidding fields when checkbox is unchecked", async () => {
    stubFetch();
    renderCreateListingPage();
    const user = userEvent.setup();

    await screen.findByLabelText("Title");

    await user.click(screen.getByLabelText("Enable extended bidding"));
    expect(screen.getByLabelText("Trigger Window")).toBeInTheDocument();

    await user.click(screen.getByLabelText("Enable extended bidding"));
    expect(
      screen.queryByLabelText("Trigger Window"),
    ).not.toBeInTheDocument();
  });

  it("shows validation errors when submitting with empty required fields", async () => {
    stubFetch();
    renderCreateListingPage();
    const user = userEvent.setup();

    await screen.findByLabelText("Title");

    await user.click(screen.getByRole("button", { name: "Create Draft" }));

    expect(
      await screen.findByText("Title is required"),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Starting bid is required"),
    ).toBeInTheDocument();
  });

  it("has a cancel button that links back to listings", async () => {
    stubFetch();
    renderCreateListingPage();

    await screen.findByLabelText("Title");
    expect(
      screen.getByRole("button", { name: "Cancel" }),
    ).toBeInTheDocument();
  });
});
