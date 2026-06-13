import { queryOptions, useQuery } from "@tanstack/react-query";
import type { ZodType } from "zod";
import {
  catalogListingSchema,
  type CatalogListing,
} from "@critterbids/shared/schemas";

import {
  sellerListingsListSchema,
  type SellerListingSummary,
} from "@/listings/schema";

async function fetchParsed<T>(url: string, schema: ZodType<T>): Promise<T> {
  const response = await fetch(url, {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    if (response.status === 404) {
      throw new ListingNotFoundError(url);
    }
    throw new Error(`Request to ${url} failed with ${response.status}.`);
  }
  return schema.parse(await response.json());
}

export class ListingNotFoundError extends Error {
  constructor(url: string) {
    super(`Listing not found: ${url}`);
    this.name = "ListingNotFoundError";
  }
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

export function listingDetailQueryOptions(listingId: string) {
  return queryOptions({
    queryKey: ["listing", listingId],
    queryFn: (): Promise<CatalogListing> =>
      fetchParsed(`/api/listings/${listingId}`, catalogListingSchema),
    enabled: listingId.length > 0,
  });
}

export function useListing(listingId: string) {
  return useQuery(listingDetailQueryOptions(listingId));
}
