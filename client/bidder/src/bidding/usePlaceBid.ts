import { useMutation, useQueryClient } from "@tanstack/react-query";

import type { CatalogListing } from "@/catalog/schema";
import { placeBid } from "@/bidding/placeBid";
import type { PlaceBidResponse } from "@/bidding/schema";

interface OptimisticContext {
  previous: CatalogListing | undefined;
}

/**
 * The bid-placement mutation with optimistic update, reconciliation, and rollback (ADR 026; In-scope
 * #3 + #5). The cache key is the same `["listing", id]` the detail view reads, so the optimistic
 * write updates the rendered view immediately.
 *
 * Lifecycle:
 *  • onMutate   — snapshot the cached listing, then optimistically write the bid as the new high bid
 *                 (amount, held bidder, count+1) so the UI reflects the bid the instant it is sent.
 *  • onSuccess  — reconcile against the authoritative 200 PlaceBidResponse (the server's high bid,
 *                 count, and any extended-bidding new close time win over the optimistic guess).
 *  • onError    — roll the snapshot back; the component surfaces `error.message` (the reason).
 *  • onSettled  — invalidate so the subsequent re-query confirms against the read model. The
 *                 BiddingHub push for this same bid also lands a cache invalidation; both converge on
 *                 the one authoritative view, so the bidder sees no flicker or double-count.
 *
 * `retry: false` is the idempotency guarantee (In-scope #5a): a dropped response is never blindly
 * retried (the server-generated BidId means a retry could double-bid) — it rolls back and the bidder
 * re-submits deliberately.
 */
export function usePlaceBid(listingId: string, participantId: string | null) {
  const queryClient = useQueryClient();
  const queryKey = ["listing", listingId] as const;

  return useMutation<PlaceBidResponse, Error, number, OptimisticContext>({
    retry: false,
    mutationFn: (amount) => {
      if (participantId === null) {
        throw new Error("No active session — cannot place a bid.");
      }
      return placeBid({ listingId, bidderId: participantId, amount });
    },
    onMutate: async (amount) => {
      await queryClient.cancelQueries({ queryKey });
      const previous = queryClient.getQueryData<CatalogListing>(queryKey);
      if (previous && participantId !== null) {
        queryClient.setQueryData<CatalogListing>(queryKey, {
          ...previous,
          currentHighBid: amount,
          currentHighBidderId: participantId,
          bidCount: previous.bidCount + 1,
        });
      }
      return { previous };
    },
    onSuccess: (response) => {
      const previous = queryClient.getQueryData<CatalogListing>(queryKey);
      if (!previous) return;
      queryClient.setQueryData<CatalogListing>(queryKey, {
        ...previous,
        currentHighBid: response.currentHighBid,
        currentHighBidderId: response.bidderId,
        bidCount: response.bidCount,
        // An accepted bid that triggered extended bidding moves the close out; reflect it now so the
        // banner can read the new time without waiting for the push-driven re-query.
        scheduledCloseAt:
          response.extendedBidding?.newCloseAt ?? previous.scheduledCloseAt,
      });
    },
    onError: (_error, _amount, context) => {
      if (context?.previous) {
        queryClient.setQueryData(queryKey, context.previous);
      }
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey });
    },
  });
}
