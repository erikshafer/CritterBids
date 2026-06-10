import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import { LiveBidding } from "@/bidding/LiveBidding";
import type { CatalogListing } from "@/catalog/schema";

const listingId = "0192f1a0-1111-7000-8000-000000000001";
const winnerId = "0192f1a0-2222-7000-8000-000000000002";
const loserId = "0192f1a0-3333-7000-8000-000000000003";

vi.mock("@/session/SessionContext", () => ({
  useSession: vi.fn(() => ({ participantId: winnerId })),
}));

vi.mock("@/bidding/LiveActivity", () => ({
  LiveActivity: () => <div data-testid="live-activity" />,
}));

function seedListing(overrides: Partial<CatalogListing> = {}): CatalogListing {
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
    currentHighBid: 55,
    currentHighBidderId: winnerId,
    bidCount: 3,
    hammerPrice: null,
    winnerId: null,
    passedReason: null,
    finalHighestBid: null,
    closedAt: null,
    settledAt: null,
    sessionId: null,
    sessionStartedAt: null,
    ...overrides,
  };
}

function renderWith(listing: CatalogListing) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return render(<LiveBidding listing={listing} />, { wrapper });
}

describe("TerminalOutcome", () => {
  it("shows interim 'You won!' message for winner when status is Sold", () => {
    renderWith(
      seedListing({
        status: "Sold",
        winnerId,
        hammerPrice: 55,
        closedAt: "2026-06-08T12:05:15+00:00",
      }),
    );

    expect(screen.getByText(/you won!/i)).toBeInTheDocument();
    expect(screen.getByText(/settlement confirmation arrives next/i)).toBeInTheDocument();
  });

  it("shows confirmed charge for winner when status is Settled", () => {
    renderWith(
      seedListing({
        status: "Settled",
        winnerId,
        hammerPrice: 55,
        closedAt: "2026-06-08T12:05:15+00:00",
        settledAt: "2026-06-08T12:06:00+00:00",
      }),
    );

    expect(screen.getByText(/charged \$55\.00/i)).toBeInTheDocument();
    expect(screen.getByText(/it's yours!/i)).toBeInTheDocument();
    expect(screen.queryByText(/settlement confirmation/i)).not.toBeInTheDocument();
  });

  it("shows 'Sold' for non-winner when status is Sold", () => {
    renderWith(
      seedListing({
        status: "Sold",
        winnerId: loserId,
        hammerPrice: 55,
        closedAt: "2026-06-08T12:05:15+00:00",
      }),
    );

    expect(screen.getByText(/sold/i)).toBeInTheDocument();
    expect(screen.queryByText(/you won!/i)).not.toBeInTheDocument();
  });

  it("shows 'Sold' for non-winner when status is Settled", () => {
    renderWith(
      seedListing({
        status: "Settled",
        winnerId: loserId,
        hammerPrice: 55,
        closedAt: "2026-06-08T12:05:15+00:00",
        settledAt: "2026-06-08T12:06:00+00:00",
      }),
    );

    expect(screen.getByText(/sold/i)).toBeInTheDocument();
    expect(screen.queryByText(/charged/i)).not.toBeInTheDocument();
  });

  it("shows 'did not sell' for Passed status", () => {
    renderWith(
      seedListing({
        status: "Passed",
        closedAt: "2026-06-08T12:05:15+00:00",
        passedReason: "ReserveNotMet",
      }),
    );

    expect(screen.getByText(/did not sell/i)).toBeInTheDocument();
  });
});
