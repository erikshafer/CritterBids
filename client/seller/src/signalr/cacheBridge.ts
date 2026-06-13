import type { QueryClient } from "@tanstack/react-query";

import { listingIdOf, type HubMessage } from "@/signalr/messages";

// ADR 026 cache bridge for the seller app. A hub push is a "something changed, refetch" signal,
// never authoritative data. Invalidate the listing detail query so the CatalogListingView
// re-fetches, and invalidate the seller listings so the dashboard reflects status changes.

export function applyHubMessage(
  queryClient: QueryClient,
  message: HubMessage,
): void {
  const listingId = listingIdOf(message);

  void queryClient.invalidateQueries({ queryKey: ["listing", listingId] });
  void queryClient.invalidateQueries({ queryKey: ["sellerListings"] });
}
