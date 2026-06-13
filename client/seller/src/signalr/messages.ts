import { z } from "zod";

// Zod wire-boundary schemas for the BiddingHub push surface the seller consumes. The seller
// joins listing:{listingId} groups for auction-lifecycle notifications and bidder:{participantId}
// for obligation-lifecycle pushes. Parse most-specific-first; assign a client-side `kind`.

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

const bidderGroupSchema = z.object({
  bidderId: z.string(),
  listingId: z.string().nullish(),
  eventType: z.string(),
  payload: z.string(),
  occurredAt: z.string(),
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
      kind: "bidderEvent";
      bidderId: string;
      listingId: string | null;
      eventType: string;
      payload: string;
      occurredAt: string;
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

  const bidderEvent = bidderGroupSchema.safeParse(payload);
  if (bidderEvent.success) {
    return {
      kind: "bidderEvent",
      bidderId: bidderEvent.data.bidderId,
      listingId: bidderEvent.data.listingId ?? null,
      eventType: bidderEvent.data.eventType,
      payload: bidderEvent.data.payload,
      occurredAt: bidderEvent.data.occurredAt,
    };
  }

  const listingEvent = listingGroupSchema.safeParse(payload);
  if (listingEvent.success) {
    return { kind: "listingEvent", ...listingEvent.data };
  }

  return null;
}

export function listingIdOf(message: HubMessage): string | null {
  return message.listingId;
}
