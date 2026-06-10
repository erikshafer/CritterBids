import { describe, expect, it } from "vitest";

import { parseOperationsFeedMessage } from "@/signalr/messages";

// The ops feed is homogeneous: one wire shape, eventType carried as a string. The shape is the
// contract; the eventType VALUE is not validated here (an unknown value flows to the cache
// bridge's blanket default).

describe("parseOperationsFeedMessage", () => {
  it("parses the wire shape with a listingId", () => {
    const message = parseOperationsFeedMessage({
      listingId: "8b6ae0a1-0000-0000-0000-000000000001",
      eventType: "BidPlacedOperations",
      payload: "Bid placed: $42.00",
      occurredAt: "2026-06-10T15:04:05.000+00:00",
    });

    expect(message).toEqual({
      listingId: "8b6ae0a1-0000-0000-0000-000000000001",
      eventType: "BidPlacedOperations",
      payload: "Bid placed: $42.00",
      occurredAt: "2026-06-10T15:04:05.000+00:00",
    });
  });

  it("normalizes a null or absent listingId to null (the session/participant events)", () => {
    const withNull = parseOperationsFeedMessage({
      listingId: null,
      eventType: "SessionStarted",
      payload: "Session started.",
      occurredAt: "2026-06-10T15:04:05Z",
    });
    expect(withNull?.listingId).toBeNull();

    const absent = parseOperationsFeedMessage({
      eventType: "ParticipantSessionStarted",
      payload: "Participant joined.",
      occurredAt: "2026-06-10T15:04:05Z",
    });
    expect(absent?.listingId).toBeNull();
    expect(absent?.eventType).toBe("ParticipantSessionStarted");
  });

  it("parses an eventType outside the lived vocabulary (shape is the contract)", () => {
    const message = parseOperationsFeedMessage({
      listingId: null,
      eventType: "SomeFutureEvent",
      payload: "?",
      occurredAt: "2026-06-10T15:04:05Z",
    });
    expect(message?.eventType).toBe("SomeFutureEvent");
  });

  it("returns null for junk, never throws (forward compatibility)", () => {
    expect(parseOperationsFeedMessage(null)).toBeNull();
    expect(parseOperationsFeedMessage("string")).toBeNull();
    expect(parseOperationsFeedMessage(42)).toBeNull();
    expect(parseOperationsFeedMessage({})).toBeNull();
    expect(
      // a bidder-wire shape arriving on the wrong hub
      parseOperationsFeedMessage({
        listingId: "x",
        bidId: "y",
        bidderId: "z",
        amount: 10,
        bidCount: 1,
        occurredAt: "2026-06-10T15:04:05Z",
      }),
    ).toBeNull();
    expect(
      // missing occurredAt
      parseOperationsFeedMessage({ eventType: "X", payload: "p" }),
    ).toBeNull();
  });
});
