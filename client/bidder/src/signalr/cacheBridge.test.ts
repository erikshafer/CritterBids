import { describe, expect, it, vi } from "vitest";
import { QueryClient } from "@tanstack/react-query";

import { applyHubMessage } from "@/signalr/cacheBridge";
import type { HubMessage } from "@/signalr/messages";

const listingId = "0192f1a0-1111-7000-8000-000000000001";

describe("applyHubMessage", () => {
  it("invalidates the listing and catalog queries on settlementCompleted", () => {
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const message: HubMessage = {
      kind: "settlementCompleted",
      settlementId: "0192f1a0-4444-7000-8000-000000000004",
      listingId,
      winnerId: "0192f1a0-2222-7000-8000-000000000002",
      hammerPrice: 55,
      completedAt: "2026-06-08T12:06:00+00:00",
    };

    applyHubMessage(queryClient, message);

    expect(invalidate).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: ["listing", listingId] }),
    );
    expect(invalidate).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: ["catalog"] }),
    );
  });

  it("invalidates the listing and catalog queries on bidPlaced", () => {
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");

    const message: HubMessage = {
      kind: "bidPlaced",
      listingId,
      bidId: "0192f1a0-3333-7000-8000-000000000003",
      bidderId: "0192f1a0-2222-7000-8000-000000000002",
      amount: 35,
      bidCount: 2,
      occurredAt: "2026-06-08T12:00:00+00:00",
    };

    applyHubMessage(queryClient, message);

    expect(invalidate).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: ["listing", listingId] }),
    );
    expect(invalidate).toHaveBeenCalledWith(
      expect.objectContaining({ queryKey: ["catalog"] }),
    );
  });
});
