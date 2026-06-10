// TanStack Query key vocabulary for the ops dashboard. The boards live under the
// ["operations", …] family so the cache bridge can blanket-invalidate every board with the
// family prefix; the title join shares the bidder app's ["listing", id] key shape so one
// invalidation convention covers both apps when client/shared/ extraction happens (ADR 025).

export const operationsKeys = {
  /** Prefix matching every operations board — the bridge's forward-compatible blanket target. */
  all: ["operations"] as const,
  lotBoard: ["operations", "lot-board"] as const,
  bidActivity: ["operations", "bid-activity"] as const,
  settlementQueue: ["operations", "settlement-queue"] as const,
  escalations: ["operations", "escalations"] as const,
  disputes: ["operations", "disputes"] as const,
  sessions: ["operations", "sessions"] as const,
  participants: ["operations", "participants"] as const,
};

export function listingKey(listingId: string) {
  return ["listing", listingId] as const;
}
