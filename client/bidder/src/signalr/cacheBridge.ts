import type { QueryClient } from "@tanstack/react-query";

import { listingIdOf, type HubMessage } from "@/signalr/messages";

// The cache bridge — the load-bearing rule of ADR 026 (and milestone §6 / M7 §5): a hub push is a
// "something changed, refetch" SIGNAL, never an authoritative payload. We translate every parsed
// {@link HubMessage} into TanStack Query cache invalidations and let the existing query functions
// re-fetch the authoritative read model (`GET /api/listings/{id}` → CatalogListingView). No push
// field is ever written into the cache as truth here; the numbers the UI renders always come from
// the re-queried view.
//
// Pure function over (queryClient, message) so it is directly unit-testable without a live hub.

export function applyHubMessage(
  queryClient: QueryClient,
  message: HubMessage,
): void {
  const listingId = listingIdOf(message);

  if (listingId !== null) {
    // The detail surface watching this listing re-fetches its high bid / count / status / close time.
    void queryClient.invalidateQueries({ queryKey: ["listing", listingId] });
  }

  // The catalog incidentally shows current bid + status per tile; keep it live too. Cheap, and the
  // global connection means a push for any listing can refresh the list the user may be viewing.
  void queryClient.invalidateQueries({ queryKey: ["catalog"] });
}
