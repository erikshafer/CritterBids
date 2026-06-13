import { describe, expect, it } from "vitest";

import { createDraftSchema, editDraftSchema } from "@/listings/formSchemas";

describe("createDraftSchema", () => {
  const validFlash = {
    title: "Vintage Mechanical Keyboard",
    format: "Flash" as const,
    startingBid: "25",
    reservePrice: "50",
    buyItNowPrice: "100",
    duration: "",
    extendedBiddingEnabled: true,
    extendedBiddingTriggerWindow: "00:00:30",
    extendedBiddingExtension: "00:00:15",
  };

  const validTimed = {
    title: "Vintage Folding Camera",
    format: "Timed" as const,
    startingBid: "40",
    reservePrice: "",
    buyItNowPrice: "80",
    duration: "7.00:00:00",
    extendedBiddingEnabled: false,
    extendedBiddingTriggerWindow: "",
    extendedBiddingExtension: "",
  };

  it("accepts a valid Flash listing with extended bidding", () => {
    expect(() => createDraftSchema.parse(validFlash)).not.toThrow();
  });

  it("accepts a valid Timed listing without extended bidding", () => {
    expect(() => createDraftSchema.parse(validTimed)).not.toThrow();
  });

  it("accepts a listing with no reserve or BIN", () => {
    const minimal = {
      ...validFlash,
      reservePrice: "",
      buyItNowPrice: "",
      extendedBiddingEnabled: false,
      extendedBiddingTriggerWindow: "",
      extendedBiddingExtension: "",
    };
    expect(() => createDraftSchema.parse(minimal)).not.toThrow();
  });

  it("rejects empty title", () => {
    const result = createDraftSchema.safeParse({ ...validFlash, title: "" });
    expect(result.success).toBe(false);
  });

  it("rejects empty starting bid", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      startingBid: "",
    });
    expect(result.success).toBe(false);
  });

  it("rejects non-positive starting bid", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      startingBid: "0",
    });
    expect(result.success).toBe(false);
  });

  it("rejects Timed listing without duration", () => {
    const result = createDraftSchema.safeParse({
      ...validTimed,
      duration: "",
    });
    expect(result.success).toBe(false);
  });

  it("accepts Flash listing without duration", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      duration: "",
    });
    expect(result.success).toBe(true);
  });

  it("rejects BIN below reserve", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      reservePrice: "100",
      buyItNowPrice: "50",
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      const binError = result.error.issues.find(
        (i) => i.path[0] === "buyItNowPrice",
      );
      expect(binError).toBeDefined();
    }
  });

  it("accepts BIN equal to reserve", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      reservePrice: "50",
      buyItNowPrice: "50",
    });
    expect(result.success).toBe(true);
  });

  it("rejects missing trigger window when extended bidding enabled", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      extendedBiddingEnabled: true,
      extendedBiddingTriggerWindow: "",
      extendedBiddingExtension: "00:00:15",
    });
    expect(result.success).toBe(false);
  });

  it("rejects missing extension when extended bidding enabled", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      extendedBiddingEnabled: true,
      extendedBiddingTriggerWindow: "00:00:30",
      extendedBiddingExtension: "",
    });
    expect(result.success).toBe(false);
  });

  it("ignores extended-bidding fields when disabled", () => {
    const result = createDraftSchema.safeParse({
      ...validFlash,
      extendedBiddingEnabled: false,
      extendedBiddingTriggerWindow: "",
      extendedBiddingExtension: "",
    });
    expect(result.success).toBe(true);
  });
});

describe("editDraftSchema", () => {
  const validEdit = {
    listingId: "0192f1a0-1111-7000-8000-000000000001",
    title: "Updated Title",
    reservePrice: "60",
    buyItNowPrice: "120",
  };

  it("accepts a valid edit", () => {
    expect(() => editDraftSchema.parse(validEdit)).not.toThrow();
  });

  it("accepts empty optional fields", () => {
    const result = editDraftSchema.safeParse({
      listingId: validEdit.listingId,
      title: "",
      reservePrice: "",
      buyItNowPrice: "",
    });
    expect(result.success).toBe(true);
  });

  it("rejects BIN below reserve", () => {
    const result = editDraftSchema.safeParse({
      ...validEdit,
      reservePrice: "100",
      buyItNowPrice: "50",
    });
    expect(result.success).toBe(false);
  });

  it("rejects negative reserve price", () => {
    const result = editDraftSchema.safeParse({
      ...validEdit,
      reservePrice: "-10",
    });
    expect(result.success).toBe(false);
  });
});
