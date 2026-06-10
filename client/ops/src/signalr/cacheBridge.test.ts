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

  it("maps ListingSoldOperations to the lot board and the settlement queue (the sale seeds the settlement row)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("ListingSoldOperations"));
    expect(invalidatedKeys(spy)).toEqual([
      ["operations", "lot-board"],
      ["operations", "settlement-queue"],
    ]);
  });

  it("maps the dispute events to both obligations queues (cards move between them)", () => {
    const { queryClient, spy } = setup();
    applyOperationsFeedMessage(queryClient, message("DisputeOpened"));
    applyOperationsFeedMessage(queryClient, message("DisputeResolved"));
    expect(invalidatedKeys(spy)).toEqual([
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
