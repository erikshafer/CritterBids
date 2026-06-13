import { z } from "zod";

// Zod wire-boundary schemas for the BiddingHub push surface the seller consumes. The seller
// joins listing:{listingId} groups and receives the same auction-lifecycle notifications as the
// bidder — BidPlacedNotification, ListingSoldNotification, and the eventType-tagged
// ListingGroupNotification family. The seller does NOT receive bidder-targeted pushes
// (BidderGroupNotification, SettlementCompletedNotification).
//
// Structural discrimination: no uniform wire discriminator (M8-S3b finding). Parse
// most-specific-first and assign a client-side `kind` discriminator.

const bidPlacedSchema = z.object({
  listingId: z.string(),
  bidId: z.string(),
  bidderId: z.string(),
  amount: z.number(),
  bidCount: z.number().int(),
  occurredAt: z.string(),
});

const listingSoldSchema = z.object({
  listingId: z.string(),
  winnerId: z.string(),
  hammerPrice: z.number(),
  bidCount: z.number().int(),
  soldAt: z.string(),
});

const listingGroupSchema = z.object({
  listingId: z.string(),
  eventType: z.string(),
  payload: z.string(),
  occurredAt: z.string(),
});

export type HubMessage =
  | {
      kind: "bidPlaced";
      listingId: string;
      bidId: string;
      bidderId: string;
      amount: number;
      bidCount: number;
      occurredAt: string;
    }
  | {
      kind: "listingSold";
      listingId: string;
      winnerId: string;
      hammerPrice: number;
      bidCount: number;
      soldAt: string;
    }
  | {
      kind: "listingEvent";
      listingId: string;
      eventType: string;
      payload: string;
      occurredAt: string;
    };

export function parseHubMessage(payload: unknown): HubMessage | null {
  const bidPlaced = bidPlacedSchema.safeParse(payload);
  if (bidPlaced.success) {
    return { kind: "bidPlaced", ...bidPlaced.data };
  }

  const listingSold = listingSoldSchema.safeParse(payload);
  if (listingSold.success) {
    return { kind: "listingSold", ...listingSold.data };
  }

  const listingEvent = listingGroupSchema.safeParse(payload);
  if (listingEvent.success) {
    return { kind: "listingEvent", ...listingEvent.data };
  }

  return null;
}

export function listingIdOf(message: HubMessage): string {
  return message.listingId;
}
