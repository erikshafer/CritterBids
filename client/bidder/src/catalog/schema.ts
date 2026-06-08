import { z } from "zod";

// Zod wire-boundary schema (ADR 013 — Zod at the boundary). Mirrors the backend read model
// `CritterBids.Listings.CatalogListingView` (src/CritterBids.Listings/CatalogListingView.cs),
// the SINGLE shape both `GET /api/listings` and `GET /api/listings/{id}` return — there is no
// separate ListingDetailView in the lived backend (M8-S2 finding; the milestone surface table
// named one optimistically). JSON keys are camelCase (ASP.NET / System.Text.Json web defaults).
//
// Nullable-or-absent fields use `.nullish()`: the backend writes them as `null` under STJ web
// defaults, but tolerating `undefined` keeps the boundary robust if null-ignoring is ever enabled.
// Decimals arrive as JSON numbers; Guids and DateTimeOffsets as strings.
export const catalogListingSchema = z.object({
  id: z.string(),
  sellerId: z.string(),
  title: z.string(),
  format: z.string(), // "Flash" | "Timed" — string on the backend, not an enum
  startingBid: z.number(),
  buyItNow: z.number().nullish(),
  duration: z.string().nullish(), // TimeSpan, serialized "hh:mm:ss" or null
  publishedAt: z.string(), // ISO 8601 DateTimeOffset
  status: z.string(), // "Published" | "Open" | "Closed" | "Sold" | "Passed" | "Settled" | "Withdrawn"
  scheduledCloseAt: z.string().nullish(),
  currentHighBid: z.number().nullish(),
  currentHighBidderId: z.string().nullish(),
  bidCount: z.number().int(),
  hammerPrice: z.number().nullish(),
  winnerId: z.string().nullish(),
  passedReason: z.string().nullish(),
  finalHighestBid: z.number().nullish(),
  closedAt: z.string().nullish(),
  settledAt: z.string().nullish(),
  sessionId: z.string().nullish(),
  sessionStartedAt: z.string().nullish(),
});

export type CatalogListing = z.infer<typeof catalogListingSchema>;

// GET /api/listings returns an array (empty, never 404, when no listings exist).
export const catalogListSchema = z.array(catalogListingSchema);
