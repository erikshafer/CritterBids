import { describe, expect, it } from "vitest";

import { catalogListingSchema, catalogListSchema } from "@/catalog/schema";

// A representative `CatalogListingView` payload (camelCase, as the API serializes it).
const validListing = {
  id: "0192f1a0-1111-7000-8000-000000000001",
  sellerId: "0192f1a0-2222-7000-8000-000000000002",
  title: "Bold Ferret",
  format: "Flash",
  startingBid: 50,
  buyItNow: null,
  duration: "00:05:00",
  publishedAt: "2026-06-04T12:00:00+00:00",
  status: "Open",
  scheduledCloseAt: null,
  currentHighBid: 75,
  currentHighBidderId: "0192f1a0-3333-7000-8000-000000000003",
  bidCount: 3,
  hammerPrice: null,
  winnerId: null,
  passedReason: null,
  finalHighestBid: null,
  closedAt: null,
  settledAt: null,
  sessionId: null,
  sessionStartedAt: null,
};

describe("catalogListingSchema", () => {
  it("accepts a representative CatalogListingView payload", () => {
    const result = catalogListingSchema.safeParse(validListing);
    expect(result.success).toBe(true);
  });

  it("tolerates omitted nullable fields (STJ may write null or, if configured, omit)", () => {
    const { currentHighBid, buyItNow, ...withoutNullables } = validListing;
    void currentHighBid;
    void buyItNow;
    const result = catalogListingSchema.safeParse(withoutNullables);
    expect(result.success).toBe(true);
  });

  it("rejects a payload with a wrong-typed required field", () => {
    const malformed = { ...validListing, startingBid: "fifty" };
    const result = catalogListingSchema.safeParse(malformed);
    expect(result.success).toBe(false);
  });

  it("rejects a payload missing a required field", () => {
    const { title, ...withoutTitle } = validListing;
    void title;
    const result = catalogListingSchema.safeParse(withoutTitle);
    expect(result.success).toBe(false);
  });

  it("parses an array of listings and an empty array", () => {
    expect(catalogListSchema.safeParse([validListing]).success).toBe(true);
    expect(catalogListSchema.safeParse([]).success).toBe(true);
  });
});
