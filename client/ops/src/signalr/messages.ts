import { z } from "zod";

// Zod wire-boundary schema for the OperationsHub push surface (ADR 013 — Zod at the boundary;
// ADR 026 — the SignalR integration pattern). Every payload SignalR delivers on the single
// `ReceiveMessage` client method (ADR 023) is parsed here before the app trusts it.
//
// Unlike the bidder's heterogeneous five-record wire (M8-S3b), the ops feed is HOMOGENEOUS:
// one record, `OperationsFeedNotification { listingId?, eventType, payload, occurredAt }`
// (src/CritterBids.Relay/Notifications/OperationsFeedNotification.cs), broadcast Clients.All.
// `eventType` is a wire-carried discriminator — no client-assigned `kind` is needed; the cache
// bridge switches on the server-named string directly.
//
// The lived eventType vocabulary (the Relay handlers targeting IHubContext<OperationsHub>,
// verified at M8-S6 prompt authoring):
//
//   with listingId: BidPlacedOperations, ListingSoldOperations, ListingPublished,
//                   ListingRevised, ListingEndedEarly, ListingAttachedToSession,
//                   LotWatchAdded, LotWatchRemoved, DisputeOpened, DisputeResolved
//   listingId null: SessionCreated, SessionStarted, ParticipantSessionStarted,
//                   SellerRegistrationCompleted
//
// Guids and DateTimeOffsets arrive as strings (System.Text.Json web defaults, camelCase keys).
const operationsFeedSchema = z.object({
  listingId: z.string().nullish(),
  eventType: z.string(),
  payload: z.string(),
  occurredAt: z.string(),
});

/** A parsed OperationsHub push. `listingId` is normalized to `string | null`. */
export interface OperationsFeedMessage {
  listingId: string | null;
  eventType: string;
  payload: string;
  occurredAt: string;
}

/**
 * Parse a raw `ReceiveMessage` payload into an {@link OperationsFeedMessage}, or `null` if it
 * does not match the wire shape. Returning `null` rather than throwing keeps an unrecognized
 * push from tearing down the connection: a future notification type is logged-and-ignored by
 * the caller, never fatal (the same forward-compatibility convention as the bidder app).
 *
 * Note the asymmetry with eventType: an UNKNOWN eventType value still parses (the shape is the
 * contract, not the vocabulary) and flows to the cache bridge, which handles it with a blanket
 * invalidation. Only a shape mismatch returns `null`.
 */
export function parseOperationsFeedMessage(
  payload: unknown,
): OperationsFeedMessage | null {
  const result = operationsFeedSchema.safeParse(payload);
  if (!result.success) {
    return null;
  }
  return {
    listingId: result.data.listingId ?? null,
    eventType: result.data.eventType,
    payload: result.data.payload,
    occurredAt: result.data.occurredAt,
  };
}
