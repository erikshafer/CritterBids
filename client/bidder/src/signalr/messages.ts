import { z } from "zod";

// Zod wire-boundary schemas for the BiddingHub push surface (ADR 013 — Zod at the boundary;
// ADR 026 — the SignalR integration pattern). Every payload SignalR delivers on the single
// `ReceiveMessage` client method (ADR 023) is parsed here before the app trusts it.
//
// The lived Relay contract is HETEROGENEOUS — there is no uniform `type` discriminator on the wire
// (a finding of this slice). Four notification records reach a bidder client
// (src/CritterBids.Relay/Notifications/):
//
//   • BidPlacedNotification    { listingId, bidId, bidderId, amount, bidCount, occurredAt }
//   • ListingSoldNotification  { listingId, winnerId, hammerPrice, bidCount, soldAt }
//   • ListingGroupNotification { listingId, eventType, payload, occurredAt }   ← eventType-tagged
//   • BidderGroupNotification  { bidderId, listingId?, eventType, payload, occurredAt }
//
// The first two carry NO `eventType`; they are discriminated structurally (presence of `bidId` vs
// `winnerId`). The latter two carry an `eventType` string (BiddingOpened, BidRejected, ReserveMet,
// ExtendedBiddingTriggered, ListingPassed, ListingWithdrawn, BuyItNowPurchased,
// BuyItNowOptionRemoved, ProxyBidExhausted) plus a human-readable `payload` string. We normalize all
// four into one discriminated `HubMessage` union so the rest of the app switches on `kind`.
//
// Decimals arrive as JSON numbers; Guids and DateTimeOffsets as strings (System.Text.Json web
// defaults, camelCase keys).

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

// The normalized internal shape. `kind` is the discriminator the app reasons over — it does NOT
// exist on the wire; `parseHubMessage` assigns it.
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

/**
 * Parse a raw `ReceiveMessage` payload into a normalized {@link HubMessage}, or `null` if it matches
 * no known shape. Tried most-specific-first: `bidId` ⇒ bidPlaced, `winnerId`+`hammerPrice` ⇒
 * listingSold, `bidderId`+`eventType` ⇒ bidderEvent, `eventType` ⇒ listingEvent. The required-field
 * sets are disjoint, so order only matters for the two eventType-tagged shapes (bidder before
 * listing, because a bidder notification also satisfies the listing schema's looser requirements).
 *
 * Returning `null` rather than throwing keeps an unrecognized push from tearing down the connection:
 * a forward-compatible new notification type is logged-and-ignored, not fatal.
 */
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

/**
 * The listing this message concerns, or `null` (a bidder-group event may carry no listing). Used by
 * the cache bridge to decide which `["listing", id]` query to invalidate.
 */
export function listingIdOf(message: HubMessage): string | null {
  return message.listingId;
}
