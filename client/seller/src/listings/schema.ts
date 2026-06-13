import { z } from "zod";

export const listingFormatSchema = z.enum(["Flash", "Timed"]);
export type ListingFormat = z.infer<typeof listingFormatSchema>;

export const listingStatusSchema = z.enum([
  "Draft",
  "Submitted",
  "Published",
  "Rejected",
  "Withdrawn",
]);
export type ListingStatus = z.infer<typeof listingStatusSchema>;

export const sellerListingSummarySchema = z.object({
  id: z.string(),
  sellerId: z.string(),
  title: z.string(),
  format: listingFormatSchema,
  status: listingStatusSchema,
  startingBid: z.number(),
  reservePrice: z.number().nullable(),
  buyItNowPrice: z.number().nullable(),
  createdAt: z.string(),
  publishedAt: z.string().nullable(),
});

export type SellerListingSummary = z.infer<typeof sellerListingSummarySchema>;

export const sellerListingsListSchema = z.array(sellerListingSummarySchema);
