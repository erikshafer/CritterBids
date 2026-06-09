import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { HubConnectionState, type HubConnection } from "@microsoft/signalr";

import { SessionProvider } from "@/session/SessionContext";
import { SignalRProvider } from "@/signalr/SignalRProvider";
import { useWatchListing } from "@/signalr/hooks";
import { useListing } from "@/catalog/queries";

const listingId = "0192f1a0-1111-7000-8000-000000000001";

// A minimal stand-in for @microsoft/signalr's HubConnection: records the ReceiveMessage handler so
// the test can simulate a server push via `emit`, and no-ops the lifecycle methods.
class FakeHubConnection {
  state: HubConnectionState = HubConnectionState.Disconnected;
  private receive: ((payload: unknown) => void) | null = null;
  invoke = vi.fn(async () => {});

  on(_method: string, handler: (payload: unknown) => void) {
    this.receive = handler;
  }
  onreconnecting() {}
  onreconnected() {}
  onclose() {}
  async start() {
    this.state = HubConnectionState.Connected;
  }
  async stop() {
    this.state = HubConnectionState.Disconnected;
  }
  emit(payload: unknown) {
    this.receive?.(payload);
  }
}

function listingBody(currentHighBid: number) {
  return {
    id: listingId,
    sellerId: "0192f1a0-2222-7000-8000-000000000002",
    title: "Vintage Keyboard",
    format: "Flash",
    startingBid: 25,
    buyItNow: 100,
    duration: "00:05:00",
    publishedAt: "2026-06-08T12:00:00+00:00",
    status: "Open",
    scheduledCloseAt: "2026-06-08T12:05:00+00:00",
    currentHighBid,
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

function ListingProbe() {
  useWatchListing(listingId);
  const { data } = useListing(listingId);
  return <span data-testid="high-bid">{data?.currentHighBid ?? "—"}</span>;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("SignalRProvider cache bridge", () => {
  it("re-queries the listing read model when a hub push arrives", async () => {
    let listingFetches = 0;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string | URL) => {
        const u = String(url);
        if (u.includes("/api/participants/session")) {
          return {
            ok: true,
            status: 200,
            headers: new Headers({ Location: "/api/participants/p-1" }),
            json: async () => ({ value: "p-1" }),
          } as unknown as Response;
        }
        if (u.includes(`/api/listings/${listingId}`)) {
          listingFetches += 1;
          // Each fetch returns a higher bid so a re-query is observable in the DOM.
          return {
            ok: true,
            status: 200,
            json: async () => listingBody(30 + listingFetches),
          } as unknown as Response;
        }
        return { ok: true, status: 200, json: async () => [] } as unknown as Response;
      }),
    );

    const connection = new FakeHubConnection();
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: 0 } },
    });

    render(
      <QueryClientProvider client={queryClient}>
        <SessionProvider>
          <SignalRProvider
            createConnection={() => connection as unknown as HubConnection}
          >
            <ListingProbe />
          </SignalRProvider>
        </SessionProvider>
      </QueryClientProvider>,
    );

    // First load — the optimistic value 31 from the initial query.
    expect(await screen.findByText("31")).toBeInTheDocument();
    await waitFor(() => expect(listingFetches).toBe(1));

    // Simulate a BidPlaced push on the listing's group. The bridge must invalidate ["listing", id]
    // and the query must re-fetch — NOT render the push payload (amount 999 is never shown).
    act(() => {
      connection.emit({
        listingId,
        bidId: "0192f1a0-3333-7000-8000-000000000003",
        bidderId: "0192f1a0-9999-7000-8000-000000000009",
        amount: 999,
        bidCount: 2,
        occurredAt: "2026-06-08T12:01:00+00:00",
      });
    });

    await waitFor(() => expect(listingFetches).toBe(2));
    expect(await screen.findByText("32")).toBeInTheDocument();
    expect(screen.queryByText("999")).not.toBeInTheDocument();
  });
});
