import { useMutation, useQueryClient } from "@tanstack/react-query";

import type { CreateDraftFormValues, EditDraftFormValues } from "./formSchemas";

interface CreateDraftPayload {
  sellerId: string;
  title: string;
  format: string;
  startingBid: number;
  reservePrice: number | null;
  buyItNowPrice: number | null;
  duration: string | null;
  extendedBiddingEnabled: boolean;
  extendedBiddingTriggerWindow: string | null;
  extendedBiddingExtension: string | null;
}

function toNumber(value: string | undefined): number | null {
  if (!value) return null;
  const n = Number(value);
  return Number.isFinite(n) && n > 0 ? n : null;
}

function toCreateDraftPayload(
  values: CreateDraftFormValues,
  sellerId: string,
): CreateDraftPayload {
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

export function useCreateDraft(sellerId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (values: CreateDraftFormValues) => {
      const payload = toCreateDraftPayload(values, sellerId);
      const response = await fetch("/api/listings/draft", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
        },
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        const text = await response.text().catch(() => "");
        throw new Error(
          text || `Create draft failed with ${response.status}.`,
        );
      }
      return response.headers.get("Location");
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ["sellerListings", sellerId],
      });
    },
  });
}

interface EditDraftPayload {
  listingId: string;
  title: string | null;
  reservePrice: number | null;
  buyItNowPrice: number | null;
}

function toEditDraftPayload(values: EditDraftFormValues): EditDraftPayload {
  return {
    listingId: values.listingId,
    title: values.title && values.title.length > 0 ? values.title : null,
    reservePrice: toNumber(values.reservePrice),
    buyItNowPrice: toNumber(values.buyItNowPrice),
  };
}

export function useEditDraft(sellerId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (values: EditDraftFormValues) => {
      const payload = toEditDraftPayload(values);
      const response = await fetch("/api/selling/listings/draft", {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
        },
        body: JSON.stringify(payload),
      });
      if (!response.ok) {
        const text = await response.text().catch(() => "");
        throw new Error(
          text || `Update draft failed with ${response.status}.`,
        );
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ["sellerListings", sellerId],
      });
    },
  });
}

export function useSubmitListing(sellerId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (listingId: string) => {
      const response = await fetch("/api/selling/listings/submit", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
        },
        body: JSON.stringify({ listingId, sellerId }),
      });
      if (!response.ok) {
        const text = await response.text().catch(() => "");
        throw new Error(text || `Submit failed with ${response.status}.`);
      }
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ["sellerListings", sellerId],
      });
    },
  });
}
