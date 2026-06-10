import { z } from "zod";

// Zod wire-boundary schemas (ADR 013) mirroring the six Operations BC view records the seven
// staff endpoints return verbatim (src/CritterBids.Operations/*View.cs, OperationsQueryEndpoints.cs).
// JSON keys are camelCase; decimals arrive as JSON numbers; Guids and DateTimeOffsets as strings;
// enums as their string names (System.Text.Json web defaults — the wolverine-http-frontend-contract
// skill's verified table). Nullable fields use `.nullish()` to tolerate both null and absent.
// Unknown keys (e.g. the records' computed `id` identity property) are stripped by default.
//
// Guid fields are `z.string()` deliberately — the boundary catches shape/type drift, not
// re-validation of ids the server minted (M8-S2 finding; stable across Zod versions).

export const lotBoardRowSchema = z.object({
  listingId: z.string(),
  sellerId: z.string(),
  title: z.string().nullish(),
  format: z.string().nullish(),
  startingBid: z.number(),
  reservePrice: z.number().nullish(),
  buyItNow: z.number().nullish(),
  feePercentage: z.number(),
  scheduledCloseAt: z.string().nullish(),
  currentBid: z.number().nullish(),
  bidCount: z.number().int(),
  hammerPrice: z.number().nullish(),
  winnerId: z.string().nullish(),
  passReason: z.string().nullish(),
  withdrawnBy: z.string().nullish(),
  withdrawalReason: z.string().nullish(),
  status: z.string(), // "Draft" | "Open" | "Sold" | "Passed" | "Withdrawn"
  lastUpdatedAt: z.string(),
});
export type LotBoardRow = z.infer<typeof lotBoardRowSchema>;

export const bidActivityRowSchema = z.object({
  bidId: z.string(),
  listingId: z.string(),
  bidderId: z.string(),
  amount: z.number(),
  bidCount: z.number().int(),
  isProxy: z.boolean(),
  placedAt: z.string(),
});
export type BidActivityRow = z.infer<typeof bidActivityRowSchema>;

export const settlementQueueRowSchema = z.object({
  settlementId: z.string(),
  listingId: z.string(),
  winnerId: z.string(),
  sellerId: z.string().nullish(),
  hammerPrice: z.number().nullish(),
  feeAmount: z.number().nullish(),
  sellerPayout: z.number().nullish(),
  payoutAmount: z.number().nullish(),
  feeDeducted: z.number().nullish(),
  failureReason: z.string().nullish(),
  status: z.string(), // "Failed" | "Completed" | "PaidOut"
  lastUpdatedAt: z.string(),
});
export type SettlementQueueRow = z.infer<typeof settlementQueueRowSchema>;

export const obligationsRowSchema = z.object({
  obligationId: z.string(),
  listingId: z.string(),
  disputeId: z.string().nullish(),
  raisedBy: z.string().nullish(),
  disputeReason: z.string().nullish(),
  resolutionType: z.string().nullish(),
  resolutionParticipantId: z.string().nullish(),
  winnerId: z.string().nullish(),
  sellerId: z.string().nullish(),
  escalatedAt: z.string().nullish(),
  disputeOpenedAt: z.string().nullish(),
  disputeResolvedAt: z.string().nullish(),
  fulfilledAt: z.string().nullish(),
  queueState: z.string(), // "None" | "Escalated" | "Disputed" | "Active" | "Resolved" | "Fulfilled"
});
export type ObligationsRow = z.infer<typeof obligationsRowSchema>;

export const sessionRowSchema = z.object({
  sessionId: z.string(),
  title: z.string().nullish(),
  durationMinutes: z.number().int(),
  attachedListingIds: z.array(z.string()),
  status: z.string(), // "Created" | "Started"
  createdAt: z.string(),
  startedAt: z.string().nullish(),
});
export type SessionRow = z.infer<typeof sessionRowSchema>;

export const participantRowSchema = z.object({
  participantId: z.string(),
  displayName: z.string().nullish(),
  bidderId: z.string().nullish(),
  creditCeiling: z.number(),
  startedAt: z.string(),
});
export type ParticipantRow = z.infer<typeof participantRowSchema>;

// Every board endpoint returns IReadOnlyList<T> — [] when empty, never 404.
export const lotBoardSchema = z.array(lotBoardRowSchema);
export const bidActivitySchema = z.array(bidActivityRowSchema);
export const settlementQueueSchema = z.array(settlementQueueRowSchema);
export const obligationsSchema = z.array(obligationsRowSchema);
export const sessionsSchema = z.array(sessionRowSchema);
export const participantsSchema = z.array(participantRowSchema);

// The title join's tolerant projection of CatalogListingView (GET /api/listings/{id}) — the ops
// app needs only the display title; the full shape is the bidder app's concern.
export const listingTitleSchema = z.object({
  title: z.string(),
});
