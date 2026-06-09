import { z } from "zod";

// Zod wire-boundary schemas for the bid-placement endpoint (M8-S3a, `POST /api/auctions/bids`).
// The single parse point for the bid request/response surface (ADR 013). Shapes mirror
// src/CritterBids.Auctions/PlaceBidEndpoint.cs + BidOutcome.cs.

// The extended-bidding shoulders on an accepted bid that pushed the close out (null otherwise).
export const extendedBiddingSchema = z.object({
  previousCloseAt: z.string(),
  newCloseAt: z.string(),
});

// 200 body for an accepted bid — the contract the optimistic-update reconciliation binds to.
export const placeBidResponseSchema = z.object({
  bidId: z.string(),
  listingId: z.string(),
  bidderId: z.string(),
  amount: z.number(),
  bidCount: z.number().int(),
  currentHighBid: z.number(),
  reserveMet: z.boolean(),
  extendedBidding: extendedBiddingSchema.nullish(),
});

export type PlaceBidResponse = z.infer<typeof placeBidResponseSchema>;

// ProblemDetails on a 4xx/5xx rejection. The endpoint flattens `reason` + `currentHighBid` into the
// JSON via ProblemDetails.Extensions (System.Text.Json serializes extensions as top-level members),
// so they parse as ordinary fields. Everything is nullish — a 5xx (e.g. a DcbConcurrencyException)
// carries no machine-readable reason, and we still need to roll back gracefully.
export const problemDetailsSchema = z.object({
  reason: z.string().nullish(),
  currentHighBid: z.number().nullish(),
  title: z.string().nullish(),
  detail: z.string().nullish(),
  status: z.number().nullish(),
});

export type ProblemDetails = z.infer<typeof problemDetailsSchema>;

// The request body — exactly what the browser legitimately knows. There is NO creditCeiling field:
// the endpoint sources the ceiling server-side (M8-S3a), so the client cannot supply one.
export interface PlaceBidRequest {
  listingId: string;
  bidderId: string;
  amount: number;
}
