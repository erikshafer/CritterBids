import { afterEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import { usePlaceBid } from "@/bidding/usePlaceBid";
import { BidRejectedError } from "@/bidding/placeBid";
import type { CatalogListing } from "@/catalog/schema";

const listingId = "0192f1a0-1111-7000-8000-000000000001";
const participantId = "0192f1a0-2222-7000-8000-000000000002";

function seedListing(): CatalogListing {
  return {
    id: listingId,
    sellerId: "0192f1a0-7777-7000-8000-000000000007",
    title: "Vintage Keyboard",
    format: "Flash",
    startingBid: 25,
    buyItNow: 100,
    duration: "00:05:00",
    publishedAt: "2026-06-08T12:00:00+00:00",
    status: "Open",
    scheduledCloseAt: "2026-06-08T12:05:00+00:00",
    currentHighBid: 30,
    currentHighBidderId: "0192f1a0-9999-7000-8000-000000000009",
    bidCount: 1,
    hammerPrice: null,
    winnerId: null,
    passedReason: null,
    finalHighestBid: null,
    closedAt: null,
    settledAt: null,
    sessionId: null,
    sessionStartedAt: null,
  };
}

function renderPlaceBid() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  queryClient.setQueryData(["listing", listingId], seedListing());
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  const view = renderHook(() => usePlaceBid(listingId, participantId), {
    wrapper,
  });
  return { queryClient, ...view };
}

function cached(queryClient: QueryClient): CatalogListing | undefined {
  return queryClient.getQueryData<CatalogListing>(["listing", listingId]);
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("usePlaceBid", () => {
  it("reconciles the cache against the 200 PlaceBidResponse on acceptance", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        status: 200,
        json: async () => ({
          bidId: "0192f1a0-3333-7000-8000-000000000003",
          listingId,
          bidderId: participantId,
          amount: 35,
          bidCount: 2,
          currentHighBid: 35,
          reserveMet: false,
          extendedBidding: null,
        }),
      })) as unknown as typeof fetch,
    );

    const { queryClient, result } = renderPlaceBid();
    result.current.mutate(35);

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const listing = cached(queryClient);
    expect(listing?.currentHighBid).toBe(35);
    expect(listing?.currentHighBidderId).toBe(participantId);
    expect(listing?.bidCount).toBe(2);
  });

  it("rolls the optimistic update back and surfaces the reason on a 4xx rejection", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: false,
        status: 400,
        json: async () => ({
          title: "Bid rejected",
          detail: "The bid was rejected: BelowMinimumBid.",
          status: 400,
          reason: "BelowMinimumBid",
          currentHighBid: 30,
        }),
      })) as unknown as typeof fetch,
    );

    const { queryClient, result } = renderPlaceBid();
    result.current.mutate(26);

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Rolled back to the seeded snapshot — the optimistic 26/+1 is gone.
    const listing = cached(queryClient);
    expect(listing?.currentHighBid).toBe(30);
    expect(listing?.currentHighBidderId).toBe(
      "0192f1a0-9999-7000-8000-000000000009",
    );
    expect(listing?.bidCount).toBe(1);

    const error = result.current.error;
    expect(error).toBeInstanceOf(BidRejectedError);
    expect((error as BidRejectedError).reason).toBe("BelowMinimumBid");
    expect((error as BidRejectedError).currentHighBid).toBe(30);
  });
});
