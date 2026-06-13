import { describe, expect, it } from "vitest";

import {
  sellerListingSummarySchema,
  sellerListingsListSchema,
} from "@/listings/schema";

const validSummary = {
  id: "0192f1a0-1111-7000-8000-000000000001",
  sellerId: "0192f1a0-2222-7000-8000-000000000002",
  title: "Vintage Mechanical Keyboard",
  format: "Flash",
  status: "Draft",
  startingBid: 25,
  reservePrice: 50,
  buyItNowPrice: 100,
  createdAt: "2026-06-10T14:00:00+00:00",
  publishedAt: null,
};

describe("sellerListingSummarySchema", () => {
  it("parses a valid summary with all fields", () => {
    const result = sellerListingSummarySchema.parse(validSummary);
    expect(result.title).toBe("Vintage Mechanical Keyboard");
    expect(result.format).toBe("Flash");
    expect(result.status).toBe("Draft");
    expect(result.startingBid).toBe(25);
    expect(result.reservePrice).toBe(50);
    expect(result.publishedAt).toBeNull();
  });

  it("parses a summary with nullable fields as null", () => {
    const noReserve = {
      ...validSummary,
      reservePrice: null,
      buyItNowPrice: null,
    };
    const result = sellerListingSummarySchema.parse(noReserve);
    expect(result.reservePrice).toBeNull();
    expect(result.buyItNowPrice).toBeNull();
  });

  it("parses a published listing with publishedAt set", () => {
    const published = {
      ...validSummary,
      status: "Published",
      publishedAt: "2026-06-10T15:00:00+00:00",
    };
    const result = sellerListingSummarySchema.parse(published);
    expect(result.status).toBe("Published");
    expect(result.publishedAt).toBe("2026-06-10T15:00:00+00:00");
  });

  it("rejects a summary missing required fields", () => {
    const { title: _, ...missingTitle } = validSummary;
    expect(() => sellerListingSummarySchema.parse(missingTitle)).toThrow();
  });

  it("rejects an unknown listing status", () => {
    const badStatus = { ...validSummary, status: "Archived" };
    expect(() => sellerListingSummarySchema.parse(badStatus)).toThrow();
  });

  it("rejects an unknown listing format", () => {
    const badFormat = { ...validSummary, format: "Dutch" };
    expect(() => sellerListingSummarySchema.parse(badFormat)).toThrow();
  });
});

describe("sellerListingsListSchema", () => {
  it("parses an array of summaries", () => {
    const result = sellerListingsListSchema.parse([validSummary, validSummary]);
    expect(result).toHaveLength(2);
  });

  it("parses an empty array", () => {
    const result = sellerListingsListSchema.parse([]);
    expect(result).toHaveLength(0);
  });
});
