import { describe, expect, it, vi, beforeEach } from "vitest";
import { QueryClient } from "@tanstack/react-query";

import { applyHubMessage } from "@/signalr/cacheBridge";
import type { HubMessage } from "@/signalr/messages";

describe("applyHubMessage", () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient();
    vi.spyOn(queryClient, "invalidateQueries");
  });

  it("invalidates listing detail query on bidPlaced", () => {
    const message: HubMessage = {
      kind: "bidPlaced",
      listingId: "listing-abc",
      bidId: "bid-1",
      bidderId: "bidder-1",
      amount: 30,
      bidCount: 1,
      occurredAt: "2026-06-13T12:00:00Z",
    };

    applyHubMessage(queryClient, message);

    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["listing", "listing-abc"],
    });
  });

  it("invalidates seller listings query on bidPlaced", () => {
    const message: HubMessage = {
      kind: "bidPlaced",
      listingId: "listing-abc",
      bidId: "bid-1",
      bidderId: "bidder-1",
      amount: 30,
      bidCount: 1,
      occurredAt: "2026-06-13T12:00:00Z",
    };

    applyHubMessage(queryClient, message);

    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["sellerListings"],
    });
  });

  it("invalidates on listingSold", () => {
    const message: HubMessage = {
      kind: "listingSold",
      listingId: "listing-def",
      winnerId: "winner-1",
      hammerPrice: 55,
      bidCount: 3,
      soldAt: "2026-06-13T12:05:00Z",
    };

    applyHubMessage(queryClient, message);

    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["listing", "listing-def"],
    });
    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["sellerListings"],
    });
  });

  it("invalidates on listingEvent", () => {
    const message: HubMessage = {
      kind: "listingEvent",
      listingId: "listing-ghi",
      eventType: "BiddingOpened",
      payload: "Bidding opened.",
      occurredAt: "2026-06-13T12:00:00Z",
    };

    applyHubMessage(queryClient, message);

    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["listing", "listing-ghi"],
    });
    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["sellerListings"],
    });
  });

  it("invalidates obligations on ObligationFulfilled bidderEvent", () => {
    const message: HubMessage = {
      kind: "bidderEvent",
      bidderId: "seller-1",
      listingId: "listing-abc",
      eventType: "ObligationFulfilled",
      payload: "Obligation fulfilled.",
      occurredAt: "2026-06-13T14:00:00Z",
    };

    applyHubMessage(queryClient, message);

    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["sellerObligations"],
    });
    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["listing", "listing-abc"],
    });
    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["sellerListings"],
    });
  });

  it("invalidates obligations on TrackingInfoProvided bidderEvent", () => {
    const message: HubMessage = {
      kind: "bidderEvent",
      bidderId: "seller-1",
      listingId: null,
      eventType: "TrackingInfoProvided",
      payload: "Tracking provided: TRACK123.",
      occurredAt: "2026-06-13T10:00:00Z",
    };

    applyHubMessage(queryClient, message);

    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["sellerObligations"],
    });
    expect(queryClient.invalidateQueries).toHaveBeenCalledWith({
      queryKey: ["sellerListings"],
    });
  });

  it("does not invalidate obligations on non-obligation bidderEvent", () => {
    const message: HubMessage = {
      kind: "bidderEvent",
      bidderId: "seller-1",
      listingId: "listing-abc",
      eventType: "ProxyBidExhausted",
      payload: "Proxy exhausted.",
      occurredAt: "2026-06-13T10:00:00Z",
    };

    applyHubMessage(queryClient, message);

    expect(queryClient.invalidateQueries).not.toHaveBeenCalledWith({
      queryKey: ["sellerObligations"],
    });
  });
});
