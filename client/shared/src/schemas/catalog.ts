import { z } from "zod";

// Mirrors the backend read model CritterBids.Listings.CatalogListingView — the SINGLE shape both
// GET /api/listings and GET /api/listings/{id} return. Shared between the bidder (catalog) and
// seller (my listings) apps. JSON keys are camelCase (ASP.NET / System.Text.Json web defaults).
export const catalogListingSchema = z.object({
  id: z.string(),
  sellerId: z.string(),
  title: z.string(),
  format: z.string(),
  startingBid: z.number(),
  buyItNow: z.number().nullish(),
  duration: z.string().nullish(),
  publishedAt: z.string(),
  status: z.string(),
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

export const catalogListSchema = z.array(catalogListingSchema);
