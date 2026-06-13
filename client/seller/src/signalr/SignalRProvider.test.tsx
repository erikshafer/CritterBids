import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { HubConnectionState, type HubConnection } from "@microsoft/signalr";

import { SessionProvider } from "@/session/SessionContext";
import { SellerSignalRProvider } from "@/signalr/SignalRProvider";
import { ConnectionIndicator } from "@/components/ConnectionIndicator";

class FakeHubConnection {
  state: HubConnectionState = HubConnectionState.Disconnected;
  on() {}
  onreconnecting() {}
  onreconnected() {}
  onclose() {}
  invoke = vi.fn(async () => {});
  async start() {
    this.state = HubConnectionState.Connected;
  }
  async stop() {
    this.state = HubConnectionState.Disconnected;
  }
}

function Providers({ children }: { children: React.ReactNode }) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>{children}</SessionProvider>
    </QueryClientProvider>
  );
}

describe("SellerSignalRProvider", () => {
  it("connects to the BiddingHub and renders connection state", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        status: 200,
        headers: new Headers({ Location: "/api/participants/p-seller" }),
        json: async () => ({ value: "p-seller" }),
      })),
    );

    const connection = new FakeHubConnection();

    render(
      <Providers>
        <SellerSignalRProvider
          createConnection={() => connection as unknown as HubConnection}
        >
          <ConnectionIndicator />
        </SellerSignalRProvider>
      </Providers>,
    );

    await waitFor(() =>
      expect(screen.getByText("Connected")).toBeInTheDocument(),
    );

    vi.unstubAllGlobals();
  });

  it("joins the bidder group with the participant id on connect", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        status: 200,
        headers: new Headers({ Location: "/api/participants/seller-123" }),
        json: async () => ({ value: "seller-123" }),
      })),
    );

    const connection = new FakeHubConnection();

    render(
      <Providers>
        <SellerSignalRProvider
          createConnection={() => connection as unknown as HubConnection}
        >
          <ConnectionIndicator />
        </SellerSignalRProvider>
      </Providers>,
    );

    await waitFor(() =>
      expect(screen.getByText("Connected")).toBeInTheDocument(),
    );

    await waitFor(() =>
      expect(connection.invoke).toHaveBeenCalledWith(
        "JoinBidderGroup",
        "seller-123",
      ),
    );

    vi.unstubAllGlobals();
  });
});
