import { describe, expect, it } from "vitest";

import {
  obligationStatusViewSchema,
  obligationStatusListSchema,
} from "@/obligations/schema";

const VALID_OBLIGATION = {
  id: "00000000-0000-0000-0000-000000000001",
  listingId: "00000000-0000-0000-0000-000000000002",
  winnerId: "00000000-0000-0000-0000-000000000003",
  sellerId: "00000000-0000-0000-0000-000000000004",
  hammerPrice: 55.0,
  status: "AwaitingShipment",
  shipByDeadline: "2026-06-14T12:00:00Z",
  trackingNumber: null,
  reminderSentAt: null,
  trackingProvidedAt: null,
  fulfilledAt: null,
  escalatedAt: null,
  disputeId: null,
  disputeReason: null,
  disputeOpenedAt: null,
  disputeResolution: null,
  disputeResolvedAt: null,
};

describe("obligationStatusViewSchema", () => {
  it("parses a valid AwaitingShipment obligation", () => {
    const result = obligationStatusViewSchema.safeParse(VALID_OBLIGATION);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.status).toBe("AwaitingShipment");
      expect(result.data.hammerPrice).toBe(55.0);
    }
  });

  it("parses a Shipped obligation with tracking", () => {
    const shipped = {
      ...VALID_OBLIGATION,
      status: "Shipped",
      trackingNumber: "1Z999AA10123456784",
      trackingProvidedAt: "2026-06-13T10:00:00Z",
    };
    const result = obligationStatusViewSchema.safeParse(shipped);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.trackingNumber).toBe("1Z999AA10123456784");
    }
  });

  it("parses a Fulfilled obligation", () => {
    const fulfilled = {
      ...VALID_OBLIGATION,
      status: "Fulfilled",
      trackingNumber: "TRACK123",
      trackingProvidedAt: "2026-06-13T10:00:00Z",
      fulfilledAt: "2026-06-13T12:00:00Z",
    };
    const result = obligationStatusViewSchema.safeParse(fulfilled);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.status).toBe("Fulfilled");
      expect(result.data.fulfilledAt).toBe("2026-06-13T12:00:00Z");
    }
  });

  it("parses an Escalated obligation", () => {
    const escalated = {
      ...VALID_OBLIGATION,
      status: "Escalated",
      escalatedAt: "2026-06-14T12:00:00Z",
    };
    const result = obligationStatusViewSchema.safeParse(escalated);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.status).toBe("Escalated");
    }
  });

  it("parses a Disputed obligation", () => {
    const disputed = {
      ...VALID_OBLIGATION,
      status: "Disputed",
      disputeId: "00000000-0000-0000-0000-000000000099",
      disputeReason: "NonDelivery",
      disputeOpenedAt: "2026-06-14T14:00:00Z",
    };
    const result = obligationStatusViewSchema.safeParse(disputed);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.disputeReason).toBe("NonDelivery");
    }
  });

  it("rejects an unknown status value", () => {
    const invalid = { ...VALID_OBLIGATION, status: "Unknown" };
    const result = obligationStatusViewSchema.safeParse(invalid);
    expect(result.success).toBe(false);
  });

  it("rejects missing required fields", () => {
    const result = obligationStatusViewSchema.safeParse({ id: "abc" });
    expect(result.success).toBe(false);
  });
});

describe("obligationStatusListSchema", () => {
  it("parses an array of obligations", () => {
    const result = obligationStatusListSchema.safeParse([
      VALID_OBLIGATION,
      { ...VALID_OBLIGATION, id: "00000000-0000-0000-0000-000000000099", status: "Fulfilled" },
    ]);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data).toHaveLength(2);
    }
  });

  it("parses an empty array", () => {
    const result = obligationStatusListSchema.safeParse([]);
    expect(result.success).toBe(true);
  });
});
