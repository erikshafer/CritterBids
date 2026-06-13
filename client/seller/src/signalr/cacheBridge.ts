import type { QueryClient } from "@tanstack/react-query";

import { listingIdOf, type HubMessage } from "@/signalr/messages";

const OBLIGATION_EVENT_TYPES = new Set([
  "TrackingInfoProvided",
  "ObligationFulfilled",
]);

export function applyHubMessage(
  queryClient: QueryClient,
  message: HubMessage,
): void {
  const listingId = listingIdOf(message);

  if (listingId) {
    void queryClient.invalidateQueries({ queryKey: ["listing", listingId] });
  }
  void queryClient.invalidateQueries({ queryKey: ["sellerListings"] });

  if (
    message.kind === "bidderEvent" &&
    OBLIGATION_EVENT_TYPES.has(message.eventType)
  ) {
    void queryClient.invalidateQueries({ queryKey: ["sellerObligations"] });
  }
}
