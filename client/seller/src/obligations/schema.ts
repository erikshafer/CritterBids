import { z } from "zod";

export const obligationStatusEnum = z.enum([
  "AwaitingShipment",
  "Shipped",
  "Escalated",
  "Fulfilled",
  "Disputed",
]);
export type ObligationStatus = z.infer<typeof obligationStatusEnum>;

export const obligationStatusViewSchema = z.object({
  id: z.string(),
  listingId: z.string(),
  winnerId: z.string(),
  sellerId: z.string(),
  hammerPrice: z.number(),
  status: obligationStatusEnum,
  shipByDeadline: z.string(),
  trackingNumber: z.string().nullable(),
  reminderSentAt: z.string().nullable(),
  trackingProvidedAt: z.string().nullable(),
  fulfilledAt: z.string().nullable(),
  escalatedAt: z.string().nullable(),
  disputeId: z.string().nullable(),
  disputeReason: z.string().nullable(),
  disputeOpenedAt: z.string().nullable(),
  disputeResolution: z.string().nullable(),
  disputeResolvedAt: z.string().nullable(),
});

export type ObligationStatusView = z.infer<typeof obligationStatusViewSchema>;

export const obligationStatusListSchema = z.array(obligationStatusViewSchema);
