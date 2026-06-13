import { describe, expect, it } from "vitest";

import { parseHubMessage, listingIdOf } from "@/signalr/messages";

describe("parseHubMessage", () => {
  it("parses BidPlacedNotification", () => {
    const payload = {
      listingId: "abc-123",
      bidId: "bid-001",
      bidderId: "bidder-1",
      amount: 30,
      bidCount: 1,
      occurredAt: "2026-06-13T12:00:00Z",
    };

    const result = parseHubMessage(payload);

    expect(result).toEqual({ kind: "bidPlaced", ...payload });
  });

  it("parses ListingSoldNotification", () => {
    const payload = {
      listingId: "abc-123",
      winnerId: "bidder-1",
      hammerPrice: 55,
      bidCount: 3,
      soldAt: "2026-06-13T12:05:00Z",
    };

    const result = parseHubMessage(payload);

    expect(result).toEqual({ kind: "listingSold", ...payload });
  });

  it("parses ListingGroupNotification", () => {
    const payload = {
      listingId: "abc-123",
      eventType: "BiddingOpened",
      payload: "Bidding opened at starting bid 25.",
      occurredAt: "2026-06-13T12:00:00Z",
    };

    const result = parseHubMessage(payload);

    expect(result).toEqual({ kind: "listingEvent", ...payload });
  });

  it("returns null for unrecognized payloads", () => {
    expect(parseHubMessage({})).toBeNull();
    expect(parseHubMessage({ foo: "bar" })).toBeNull();
    expect(parseHubMessage(null)).toBeNull();
    expect(parseHubMessage(42)).toBeNull();
  });

  it("parses BidderGroupNotification as bidderEvent", () => {
    const payload = {
      bidderId: "bidder-1",
      listingId: "abc-123",
      eventType: "ObligationFulfilled",
      payload: "Obligation fulfilled.",
      occurredAt: "2026-06-13T12:00:00Z",
    };

    const result = parseHubMessage(payload);

    expect(result).toEqual({
      kind: "bidderEvent",
      bidderId: "bidder-1",
      listingId: "abc-123",
      eventType: "ObligationFulfilled",
      payload: "Obligation fulfilled.",
      occurredAt: "2026-06-13T12:00:00Z",
    });
  });

  it("parses BidderGroupNotification with null listingId", () => {
    const payload = {
      bidderId: "bidder-1",
      listingId: null,
      eventType: "TrackingInfoProvided",
      payload: "Tracking provided: TRACK123.",
      occurredAt: "2026-06-13T10:00:00Z",
    };

    const result = parseHubMessage(payload);

    expect(result).toEqual({
      kind: "bidderEvent",
      bidderId: "bidder-1",
      listingId: null,
      eventType: "TrackingInfoProvided",
      payload: "Tracking provided: TRACK123.",
      occurredAt: "2026-06-13T10:00:00Z",
    });
  });

  it("discriminates bidPlaced before listingEvent (bidId is the distinguisher)", () => {
    // A BidPlaced payload also satisfies the listingGroup schema shape, but bidPlaced
    // is tried first (most-specific-first) because it has the bidId field.
    const payload = {
      listingId: "abc-123",
      bidId: "bid-001",
      bidderId: "bidder-1",
      amount: 30,
      bidCount: 1,
      occurredAt: "2026-06-13T12:00:00Z",
    };

    const result = parseHubMessage(payload);
    expect(result?.kind).toBe("bidPlaced");
  });
});

describe("listingIdOf", () => {
  it("returns the listing id from auction message kinds", () => {
    expect(
      listingIdOf({
        kind: "bidPlaced",
        listingId: "l-1",
        bidId: "b",
        bidderId: "b",
        amount: 1,
        bidCount: 1,
        occurredAt: "",
      }),
    ).toBe("l-1");

    expect(
      listingIdOf({
        kind: "listingSold",
        listingId: "l-2",
        winnerId: "w",
        hammerPrice: 50,
        bidCount: 3,
        soldAt: "",
      }),
    ).toBe("l-2");

    expect(
      listingIdOf({
        kind: "listingEvent",
        listingId: "l-3",
        eventType: "ReserveMet",
        payload: "Reserve met.",
        occurredAt: "",
      }),
    ).toBe("l-3");
  });

  it("returns null for bidderEvent with no listing", () => {
    expect(
      listingIdOf({
        kind: "bidderEvent",
        bidderId: "b-1",
        listingId: null,
        eventType: "ObligationFulfilled",
        payload: "Fulfilled.",
        occurredAt: "",
      }),
    ).toBeNull();
  });

  it("returns the listing id from bidderEvent when present", () => {
    expect(
      listingIdOf({
        kind: "bidderEvent",
        bidderId: "b-1",
        listingId: "l-4",
        eventType: "TrackingInfoProvided",
        payload: "Tracking provided.",
        occurredAt: "",
      }),
    ).toBe("l-4");
  });
});
