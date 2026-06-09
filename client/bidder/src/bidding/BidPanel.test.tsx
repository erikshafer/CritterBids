import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { BidPanel } from "@/bidding/BidPanel";
import type { CatalogListing } from "@/catalog/schema";

const listingId = "0192f1a0-1111-7000-8000-000000000001";
const participantId = "0192f1a0-2222-7000-8000-000000000002";

function seedListing(): CatalogListing {
  return {
    id: listingId,
    sellerId: "0192f1a0-7777-7000-8000-000000000007",
    title: "Vintage Keyboard",
    format: "Flash",
    startingBid: 25,
    buyItNow: 100,
    duration: "00:05:00",
    publishedAt: "2026-06-08T12:00:00+00:00",
    status: "Open",
    scheduledCloseAt: "2026-06-08T12:05:00+00:00",
    currentHighBid: 30,
    currentHighBidderId: "0192f1a0-9999-7000-8000-000000000009",
    bidCount: 1,
    hammerPrice: null,
    winnerId: null,
    passedReason: null,
    finalHighestBid: null,
    closedAt: null,
    settledAt: null,
    sessionId: null,
    sessionStartedAt: null,
  };
}

function renderPanel() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  queryClient.setQueryData(["listing", listingId], seedListing());
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  render(<BidPanel listing={seedListing()} participantId={participantId} />, {
    wrapper,
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("BidPanel", () => {
  it("surfaces the ProblemDetails reason when a bid is rejected", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: false,
        status: 400,
        json: async () => ({
          reason: "ExceedsCreditCeiling",
          currentHighBid: 30,
        }),
      })) as unknown as typeof fetch,
    );

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /place bid/i }));

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent(/exceeds your available credit/i);
    expect(alert).toHaveTextContent(/current high bid is \$30/i);
  });

  it("confirms acceptance on a 200 response", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        status: 200,
        json: async () => ({
          bidId: "0192f1a0-3333-7000-8000-000000000003",
          listingId,
          bidderId: participantId,
          amount: 31,
          bidCount: 2,
          currentHighBid: 31,
          reserveMet: true,
          extendedBidding: null,
        }),
      })) as unknown as typeof fetch,
    );

    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /place bid/i }));

    expect(await screen.findByText(/bid accepted/i)).toBeInTheDocument();
    expect(screen.getByText(/reserve met/i)).toBeInTheDocument();
  });
});
