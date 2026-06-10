import type { QueryClient } from "@tanstack/react-query";

import { listingKey, operationsKeys } from "@/operations/keys";
import type { OperationsFeedMessage } from "@/signalr/messages";

// The cache bridge — the load-bearing rule of ADR 026 (and milestone §6 / M7 §5): a hub push is
// a "something changed, refetch" SIGNAL, never an authoritative payload. Every parsed
// OperationsFeedMessage becomes TanStack Query invalidations; the board query functions
// re-fetch the authoritative /api/operations/* read models. No push field is ever written into
// the cache as truth.
//
// The eventType → board mapping below covers the lived 14-value ops-feed vocabulary (see
// messages.ts). An eventType OUTSIDE that vocabulary still parsed (the shape is the contract),
// so it invalidates the whole ["operations"] family — the forward-compatible safe default: a
// new server eventType refreshes every board rather than silently refreshing none.
//
// Pure function over (queryClient, message) so it is directly unit-testable without a live hub.

const EVENT_TARGETS: Record<string, ReadonlyArray<readonly string[]>> = {
  // A bid advances the lot board row and prepends a bid-activity entry.
  BidPlacedOperations: [operationsKeys.lotBoard, operationsKeys.bidActivity],
  // A sale closes the lot board row and (via the Settlement saga it starts) seeds a settlement
  // queue row; invalidating the queue here is a cheap proxy signal for that eventually-consistent
  // row — the ops feed carries no settlement events of its own (M8-S6 prompt finding 2).
  ListingSoldOperations: [
    operationsKeys.lotBoard,
    operationsKeys.settlementQueue,
  ],
  ListingPublished: [operationsKeys.lotBoard],
  // The one title-bearing change: refresh the title-join entry alongside the board (handled
  // below, where the listingId is in hand).
  ListingRevised: [operationsKeys.lotBoard],
  ListingEndedEarly: [operationsKeys.lotBoard],
  ListingAttachedToSession: [operationsKeys.lotBoard, operationsKeys.sessions],
  // No board renders watch data yet (M8-S6 open question 2); the lot board is the row the
  // watches concern, so keep it fresh and move on.
  LotWatchAdded: [operationsKeys.lotBoard],
  LotWatchRemoved: [operationsKeys.lotBoard],
  // A dispute moves a card BETWEEN the two obligations queues (Escalated → Disputed), and a
  // resolution moves it out of Disputed (Extension → back to active, outside both queues) —
  // both queues re-query on either event.
  DisputeOpened: [operationsKeys.escalations, operationsKeys.disputes],
  DisputeResolved: [operationsKeys.escalations, operationsKeys.disputes],
  SessionCreated: [operationsKeys.sessions],
  SessionStarted: [operationsKeys.sessions],
  ParticipantSessionStarted: [operationsKeys.participants],
  SellerRegistrationCompleted: [operationsKeys.participants],
};

export function applyOperationsFeedMessage(
  queryClient: QueryClient,
  message: OperationsFeedMessage,
): void {
  const targets = EVENT_TARGETS[message.eventType];

  if (targets === undefined) {
    void queryClient.invalidateQueries({ queryKey: operationsKeys.all });
    return;
  }

  for (const queryKey of targets) {
    void queryClient.invalidateQueries({ queryKey });
  }

  // ListingRevised is the vocabulary's title-bearing change; the title join caches per
  // ["listing", id], so refresh that entry too.
  if (message.eventType === "ListingRevised" && message.listingId !== null) {
    void queryClient.invalidateQueries({
      queryKey: listingKey(message.listingId),
    });
  }
}
