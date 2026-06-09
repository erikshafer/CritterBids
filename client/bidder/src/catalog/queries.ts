import { queryOptions, useQuery } from "@tanstack/react-query";
import type { ZodType } from "zod";

import {
  catalogListSchema,
  catalogListingSchema,
  type CatalogListing,
} from "@/catalog/schema";

// Thrown on a 404 from the detail endpoint so the route can render a distinct not-found state
// rather than a generic error. `GET /api/listings/{id}` 404s on an unknown id (CatalogEndpoints.cs).
export class ListingNotFoundError extends Error {
  constructor(id: string) {
    super(`Listing ${id} was not found.`);
    this.name = "ListingNotFoundError";
  }
}

// Fetch + parse at the wire boundary (ADR 013). The Zod schema is the single parse point;
// callers consume the inferred type, never raw JSON. Same-origin paths — the Vite dev proxy
// (ADR 025) forwards /api to the host in dev; production serves the SPA from that same host.
async function fetchParsed<T>(url: string, schema: ZodType<T>): Promise<T> {
  const response = await fetch(url, {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) {
    throw new Error(`Request to ${url} failed with ${response.status}.`);
  }
  return schema.parse(await response.json());
}

export function catalogQueryOptions() {
  return queryOptions({
    queryKey: ["catalog"],
    queryFn: () => fetchParsed("/api/listings", catalogListSchema),
  });
}

export function listingQueryOptions(id: string) {
  return queryOptions({
    queryKey: ["listing", id],
    queryFn: async (): Promise<CatalogListing> => {
      const response = await fetch(`/api/listings/${id}`, {
        headers: { Accept: "application/json" },
      });
      if (response.status === 404) {
        throw new ListingNotFoundError(id);
      }
      if (!response.ok) {
        throw new Error(`Request for listing ${id} failed with ${response.status}.`);
      }
      return catalogListingSchema.parse(await response.json());
    },
    // A missing listing is a stable answer, not a transient failure — don't retry the 404.
    retry: (failureCount, error) =>
      error instanceof ListingNotFoundError ? false : failureCount < 2,
  });
}

export function useCatalog() {
  return useQuery(catalogQueryOptions());
}

export function useListing(id: string) {
  return useQuery(listingQueryOptions(id));
}
