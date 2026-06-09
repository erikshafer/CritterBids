import { describe, expect, it } from "vitest";

import { parseHubMessage } from "@/signalr/messages";

const listingId = "0192f1a0-1111-7000-8000-000000000001";
const bidderId = "0192f1a0-2222-7000-8000-000000000002";

// The four heterogeneous wire shapes Relay delivers (camelCase, no uniform discriminator).
describe("parseHubMessage", () => {
  it("normalizes a BidPlacedNotification (no eventType, has bidId)", () => {
    const message = parseHubMessage({
      listingId,
      bidId: "0192f1a0-3333-7000-8000-000000000003",
      bidderId,
      amount: 35,
      bidCount: 2,
      occurredAt: "2026-06-08T12:00:00+00:00",
    });
    expect(message).toMatchObject({ kind: "bidPlaced", amount: 35, bidCount: 2 });
  });

  it("normalizes a ListingSoldNotification (winnerId + hammerPrice)", () => {
    const message = parseHubMessage({
      listingId,
      winnerId: bidderId,
      hammerPrice: 55,
      bidCount: 3,
      soldAt: "2026-06-08T12:05:00+00:00",
    });
    expect(message).toMatchObject({ kind: "listingSold", hammerPrice: 55 });
  });

  it("normalizes an eventType-tagged listing notification (ExtendedBiddingTriggered)", () => {
    const message = parseHubMessage({
      listingId,
      eventType: "ExtendedBiddingTriggered",
      payload: "Close moved from … to ….",
      occurredAt: "2026-06-08T12:04:30+00:00",
    });
    expect(message).toMatchObject({
      kind: "listingEvent",
      eventType: "ExtendedBiddingTriggered",
    });
  });

  it("normalizes a bidder-group notification (bidderId + eventType wins over the listing shape)", () => {
    const message = parseHubMessage({
      bidderId,
      listingId,
      eventType: "ProxyBidExhausted",
      payload: "Proxy exhausted at max 100.",
      occurredAt: "2026-06-08T12:03:00+00:00",
    });
    expect(message).toMatchObject({ kind: "bidderEvent", bidderId });
  });

  it("returns null for an unrecognized payload (forward-compatible, never throws)", () => {
    expect(parseHubMessage({ foo: "bar" })).toBeNull();
    expect(parseHubMessage(null)).toBeNull();
  });
});
