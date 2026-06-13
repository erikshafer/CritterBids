import { describe, expect, it } from "vitest";

import type { CreateDraftFormValues, EditDraftFormValues } from "./formSchemas";

function toNumber(value: string | undefined): number | null {
  if (!value) return null;
  const n = Number(value);
  return Number.isFinite(n) && n > 0 ? n : null;
}

function toCreateDraftPayload(values: CreateDraftFormValues, sellerId: string) {
  return {
    sellerId,
    title: values.title,
    format: values.format,
    startingBid: Number(values.startingBid),
    reservePrice: toNumber(values.reservePrice),
    buyItNowPrice: toNumber(values.buyItNowPrice),
    duration:
      values.format === "Timed" && values.duration ? values.duration : null,
    extendedBiddingEnabled: values.extendedBiddingEnabled,
    extendedBiddingTriggerWindow: values.extendedBiddingEnabled
      ? (values.extendedBiddingTriggerWindow || null)
      : null,
    extendedBiddingExtension: values.extendedBiddingEnabled
      ? (values.extendedBiddingExtension || null)
      : null,
  };
}

function toEditDraftPayload(values: EditDraftFormValues) {
  return {
    listingId: values.listingId,
    title: values.title && values.title.length > 0 ? values.title : null,
    reservePrice: toNumber(values.reservePrice),
    buyItNowPrice: toNumber(values.buyItNowPrice),
  };
}

describe("toCreateDraftPayload", () => {
  it("converts Flash form values to the backend wire shape", () => {
    const form: CreateDraftFormValues = {
      title: "Vintage Mechanical Keyboard",
      format: "Flash",
      startingBid: "25",
      reservePrice: "50",
      buyItNowPrice: "100",
      duration: "",
      extendedBiddingEnabled: true,
      extendedBiddingTriggerWindow: "00:00:30",
      extendedBiddingExtension: "00:00:15",
    };

    const payload = toCreateDraftPayload(form, "seller-123");

    expect(payload).toEqual({
      sellerId: "seller-123",
      title: "Vintage Mechanical Keyboard",
      format: "Flash",
      startingBid: 25,
      reservePrice: 50,
      buyItNowPrice: 100,
      duration: null,
      extendedBiddingEnabled: true,
      extendedBiddingTriggerWindow: "00:00:30",
      extendedBiddingExtension: "00:00:15",
    });
  });

  it("converts Timed form values with duration", () => {
    const form: CreateDraftFormValues = {
      title: "Vintage Folding Camera",
      format: "Timed",
      startingBid: "40",
      reservePrice: "",
      buyItNowPrice: "80",
      duration: "7.00:00:00",
      extendedBiddingEnabled: false,
      extendedBiddingTriggerWindow: "",
      extendedBiddingExtension: "",
    };

    const payload = toCreateDraftPayload(form, "seller-456");

    expect(payload).toEqual({
      sellerId: "seller-456",
      title: "Vintage Folding Camera",
      format: "Timed",
      startingBid: 40,
      reservePrice: null,
      buyItNowPrice: 80,
      duration: "7.00:00:00",
      extendedBiddingEnabled: false,
      extendedBiddingTriggerWindow: null,
      extendedBiddingExtension: null,
    });
  });

  it("nullifies extended-bidding fields when disabled", () => {
    const form: CreateDraftFormValues = {
      title: "Test",
      format: "Flash",
      startingBid: "10",
      reservePrice: "",
      buyItNowPrice: "",
      duration: "",
      extendedBiddingEnabled: false,
      extendedBiddingTriggerWindow: "00:00:30",
      extendedBiddingExtension: "00:00:15",
    };

    const payload = toCreateDraftPayload(form, "seller-789");

    expect(payload.extendedBiddingTriggerWindow).toBeNull();
    expect(payload.extendedBiddingExtension).toBeNull();
  });

  it("nullifies duration for Flash format even if set", () => {
    const form: CreateDraftFormValues = {
      title: "Test",
      format: "Flash",
      startingBid: "10",
      reservePrice: "",
      buyItNowPrice: "",
      duration: "01:00:00",
      extendedBiddingEnabled: false,
      extendedBiddingTriggerWindow: "",
      extendedBiddingExtension: "",
    };

    const payload = toCreateDraftPayload(form, "seller-000");

    expect(payload.duration).toBeNull();
  });
});

describe("toEditDraftPayload", () => {
  it("converts edit form values to the backend wire shape", () => {
    const form: EditDraftFormValues = {
      listingId: "listing-123",
      title: "Updated Title",
      reservePrice: "60",
      buyItNowPrice: "120",
    };

    const payload = toEditDraftPayload(form);

    expect(payload).toEqual({
      listingId: "listing-123",
      title: "Updated Title",
      reservePrice: 60,
      buyItNowPrice: 120,
    });
  });

  it("nullifies empty string fields", () => {
    const form: EditDraftFormValues = {
      listingId: "listing-456",
      title: "",
      reservePrice: "",
      buyItNowPrice: "",
    };

    const payload = toEditDraftPayload(form);

    expect(payload).toEqual({
      listingId: "listing-456",
      title: null,
      reservePrice: null,
      buyItNowPrice: null,
    });
  });
});
