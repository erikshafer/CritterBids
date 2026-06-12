import { describe, expect, it, vi } from "vitest";
import { QueryClient } from "@tanstack/react-query";

import { applyOperationsFeedMessage } from "@/signalr/cacheBridge";
import type { OperationsFeedMessage } from "@/signalr/messages";

// The bridge is a pure function over (queryClient, message) — directly testable without a hub.
// Push = re-query (ADR 026): every assertion is about which keys get invalidated, never about
// payload data landing in the cache.

function message(
  eventType: string,
  listingId: string | null = "11111111-0000-0000-0000-000000000001",
): OperationsFeedMessage {
  return {
    listingId,
    eventType,
    payload: "human-readable",
    occurredAt: "2026-06-10T15:04:05Z",
  };
}

function invalidatedKeys(
  spy: ReturnType<typeof vi.fn>,
): ReadonlyArray<readonly string[]> {
  return spy.mock.calls.map(
    (call) => (call[0] as { queryKey: readonly string[] }).queryKey,
  );
}

function setup() {
  const queryClient = new QueryClient();
  const spy = vi
    .spyOn(queryClient, "invalidateQueries")
    .mockResolvedValue(undefined);
  return { queryClient, spy };
}

describe("applyOperationsFeedMessage", () => {
  it("maps BidPlacedOperations to the lot board and the bid-activity feed", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("BidPlacedOperations"));
    expect(invalidatedKeys(spy)).toEqual([
      ["operations", "lot-board"],
      ["operations", "bid-activity"],
    ]);
  });

  it("maps ListingSoldOperations to the lot board only (direct settlement pushes superseded the proxy invalidation at M8-S6b)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("ListingSoldOperations"));
    expect(invalidatedKeys(spy)).toEqual([["operations", "lot-board"]]);
  });

  it("maps the lot-board lifecycle events to the lot board (M8-S6b feed completion)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("BiddingOpened"));
    applyOperationsFeedMessage(queryClient, message("ListingPassed"));
    applyOperationsFeedMessage(queryClient, message("ListingWithdrawn"));
    expect(invalidatedKeys(spy)).toEqual([
      ["operations", "lot-board"],
      ["operations", "lot-board"],
      ["operations", "lot-board"],
    ]);
  });

  it("maps the settlement family to the settlement queue (M8-S6b — the gavel→charged beat moves live)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("SettlementCompleted"));
    applyOperationsFeedMessage(queryClient, message("PaymentFailed"));
    applyOperationsFeedMessage(queryClient, message("SellerPayoutIssued", null));
    expect(invalidatedKeys(spy)).toEqual([
      ["operations", "settlement-queue"],
      ["operations", "settlement-queue"],
      ["operations", "settlement-queue"],
    ]);
  });

  it("maps DeadlineEscalated to the escalation queue (M8-S6b — arrivals are now pushed)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("DeadlineEscalated"));
    expect(invalidatedKeys(spy)).toEqual([["operations", "escalations"]]);
  });

  it("maps the dispute events and ObligationFulfilled to both obligations queues (cards move between and out of them)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("DisputeOpened"));
    applyOperationsFeedMessage(queryClient, message("DisputeResolved"));
    applyOperationsFeedMessage(queryClient, message("ObligationFulfilled"));
    expect(invalidatedKeys(spy)).toEqual([
      ["operations", "escalations"],
      ["operations", "disputes"],
      ["operations", "escalations"],
      ["operations", "disputes"],
      ["operations", "escalations"],
      ["operations", "disputes"],
    ]);
  });

  it("refreshes the title-join entry on ListingRevised (the vocabulary's title-bearing change)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(
      queryClient,
      message("ListingRevised", "22222222-0000-0000-0000-000000000002"),
    );
    expect(invalidatedKeys(spy)).toEqual([
      ["operations", "lot-board"],
      ["listing", "22222222-0000-0000-0000-000000000002"],
    ]);
  });

  it("maps the session and participant events (listingId null) to their boards", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("SessionCreated", null));
    applyOperationsFeedMessage(
      queryClient,
      message("ParticipantSessionStarted", null),
    );
    expect(invalidatedKeys(spy)).toEqual([
      ["operations", "sessions"],
      ["operations", "participants"],
    ]);
  });

  it("blanket-invalidates the whole operations family for an unknown eventType (forward-compatible default)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("SomeFutureEvent"));
    expect(invalidatedKeys(spy)).toEqual([["operations"]]);
  });
});
