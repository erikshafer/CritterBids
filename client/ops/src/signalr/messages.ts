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
// completed at M8-S6b — every Operations-consumed integration event now has an ops push,
// mechanically enforced by the backend topology test, OperationsFeedTopologyTests):
//
//   with listingId: BidPlacedOperations, ListingSoldOperations, BiddingOpened, ListingPassed,
//                   ListingWithdrawn, ListingPublished, ListingRevised, ListingEndedEarly,
//                   ListingAttachedToSession, LotWatchAdded, LotWatchRemoved, DisputeOpened,
//                   DisputeResolved, DeadlineEscalated, ObligationFulfilled,
//                   SettlementCompleted, PaymentFailed
//   listingId null: SessionCreated, SessionStarted, ParticipantSessionStarted,
//                   SellerRegistrationCompleted, SellerPayoutIssued (carries no ListingId)
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
