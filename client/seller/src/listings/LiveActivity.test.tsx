import { describe, expect, it, vi } from "vitest";
import { render, screen, act } from "@testing-library/react";

import { LiveActivity } from "@/listings/LiveActivity";

const listeners: Array<(message: unknown) => void> = [];
vi.mock("@/signalr/hooks", () => ({
  useListen: (handler: (message: unknown) => void) => {
    listeners.push(handler);
  },
}));

function pushMessage(message: unknown) {
  for (const listener of listeners) {
    listener(message);
  }
}

describe("LiveActivity", () => {
  it("shows waiting message when no activity", () => {
    listeners.length = 0;
    render(<LiveActivity listingId="listing-1" />);

    expect(screen.getByText(/Waiting for live activity/)).toBeInTheDocument();
  });

  it("renders bid-placed messages", () => {
    listeners.length = 0;
    render(<LiveActivity listingId="listing-1" />);

    act(() => {
      pushMessage({
        kind: "bidPlaced",
        listingId: "listing-1",
        bidId: "bid-1",
        bidderId: "bidder-1",
        amount: 30,
        bidCount: 1,
        occurredAt: "2026-06-13T12:00:00Z",
      });
    });

    expect(screen.getByText("New bid $30.00")).toBeInTheDocument();
  });

  it("renders listing-sold messages", () => {
    listeners.length = 0;
    render(<LiveActivity listingId="listing-1" />);

    act(() => {
      pushMessage({
        kind: "listingSold",
        listingId: "listing-1",
        winnerId: "bidder-1",
        hammerPrice: 55,
        bidCount: 3,
        soldAt: "2026-06-13T12:05:00Z",
      });
    });

    expect(screen.getByText("Sold for $55.00")).toBeInTheDocument();
  });

  it("renders listing-event messages with payload text", () => {
    listeners.length = 0;
    render(<LiveActivity listingId="listing-1" />);

    act(() => {
      pushMessage({
        kind: "listingEvent",
        listingId: "listing-1",
        eventType: "ReserveMet",
        payload: "Reserve met at 55.",
        occurredAt: "2026-06-13T12:04:00Z",
      });
    });

    expect(screen.getByText("Reserve met at 55.")).toBeInTheDocument();
  });

  it("ignores messages for other listings", () => {
    listeners.length = 0;
    render(<LiveActivity listingId="listing-1" />);

    act(() => {
      pushMessage({
        kind: "bidPlaced",
        listingId: "listing-OTHER",
        bidId: "bid-1",
        bidderId: "bidder-1",
        amount: 30,
        bidCount: 1,
        occurredAt: "2026-06-13T12:00:00Z",
      });
    });

    expect(screen.getByText(/Waiting for live activity/)).toBeInTheDocument();
  });

  it("deduplicates bid messages by bidId", () => {
    listeners.length = 0;
    render(<LiveActivity listingId="listing-1" />);

    act(() => {
      pushMessage({
        kind: "bidPlaced",
        listingId: "listing-1",
        bidId: "bid-1",
        bidderId: "bidder-1",
        amount: 30,
        bidCount: 1,
        occurredAt: "2026-06-13T12:00:00Z",
      });
      pushMessage({
        kind: "bidPlaced",
        listingId: "listing-1",
        bidId: "bid-1",
        bidderId: "bidder-1",
        amount: 30,
        bidCount: 1,
        occurredAt: "2026-06-13T12:00:00Z",
      });
    });

    const items = screen.getAllByText("New bid $30.00");
    expect(items).toHaveLength(1);
  });
});
