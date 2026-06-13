import { queryOptions, useQuery } from "@tanstack/react-query";
import type { ZodType } from "zod";

import {
  sellerListingsListSchema,
  type SellerListingSummary,
} from "@/listings/schema";

async function fetchParsed<T>(url: string, schema: ZodType<T>): Promise<T> {
  const response = await fetch(url, {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    throw new Error(`Request to ${url} failed with ${response.status}.`);
  }
  return schema.parse(await response.json());
}

export function sellerListingsQueryOptions(sellerId: string) {
  return queryOptions({
    queryKey: ["sellerListings", sellerId],
    queryFn: (): Promise<SellerListingSummary[]> =>
      fetchParsed(
        `/api/selling/listings?sellerId=${encodeURIComponent(sellerId)}`,
        sellerListingsListSchema,
      ),
    enabled: sellerId.length > 0,
  });
}

export function useSellerListings(sellerId: string) {
  return useQuery(sellerListingsQueryOptions(sellerId));
}
