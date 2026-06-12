import { describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  HttpTransportType,
  HubConnectionState,
  type HubConnection,
  type IHttpConnectionOptions,
} from "@microsoft/signalr";
import type { ReactNode } from "react";

import { StaffAuthProvider } from "@/auth/StaffAuthContext";
import { AuthGate } from "@/auth/AuthGate";
import { OperationsSignalRProvider } from "@/signalr/SignalRProvider";
import { ConnectionIndicator } from "@/components/ConnectionIndicator";
import { RECEIVE_MESSAGE } from "@/signalr/hub";
import { useListen } from "@/signalr/hooks";
import type { OperationsFeedMessage } from "@/signalr/messages";

const STORAGE_KEY = "critterbids.staffToken";

// The provider runs the ADR 026 cache bridge (M8-S6), so it lives under a QueryClientProvider.
function Providers({
  children,
  queryClient = new QueryClient(),
}: {
  children: ReactNode;
  queryClient?: QueryClient;
}) {
  return (
    <QueryClientProvider client={queryClient}>
      <StaffAuthProvider>{children}</StaffAuthProvider>
    </QueryClientProvider>
  );
}

function Listener({
  onMessage,
}: {
  onMessage: (message: OperationsFeedMessage) => void;
}) {
  useListen(onMessage);
  return null;
}

// Captures what the PRODUCTION connection factory hands to HubConnectionBuilder.withUrl — the
// accessTokenFactory/skipNegotiation/transport assertions run against the real code path, not a
// test seam. vi.hoisted because vi.mock factories are hoisted above imports.
const hoisted = vi.hoisted(() => {
  const captured: {
    url?: string;
    options?: {
      accessTokenFactory?: () => string | Promise<string>;
      skipNegotiation?: boolean;
      transport?: number;
    };
  } = {};

  class FakeHubConnection {
    state = "Disconnected";
    handlers = new Map<string, (payload: unknown) => void>();
    reconnectedCallbacks: Array<(connectionId?: string) => void> = [];
    on(method: string, handler: (payload: unknown) => void) {
      this.handlers.set(method, handler);
    }
    onreconnecting() {}
    onreconnected(callback: (connectionId?: string) => void) {
      this.reconnectedCallbacks.push(callback);
    }
    onclose() {}
    async start() {
      this.state = "Connected";
    }
    async stop() {
      this.state = "Disconnected";
    }
  }

  const built: FakeHubConnection[] = [];

  class CapturingHubConnectionBuilder {
    withUrl(url: string, options: unknown) {
      captured.url = url;
      captured.options = options as typeof captured.options;
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      const connection = new FakeHubConnection();
      built.push(connection);
      return connection;
    }
  }

  return { captured, built, CapturingHubConnectionBuilder };
});

vi.mock("@microsoft/signalr", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@microsoft/signalr")>();
  return {
    ...actual,
    HubConnectionBuilder: hoisted.CapturingHubConnectionBuilder,
  };
});

describe("OperationsSignalRProvider", () => {
  it("opens /hub/operations with accessTokenFactory supplying the stored token, skipNegotiation + WebSockets", async () => {
    sessionStorage.setItem(STORAGE_KEY, "s3cret-staff-token");

    render(
      <Providers>
        <OperationsSignalRProvider>
          <ConnectionIndicator />
        </OperationsSignalRProvider>
      </Providers>,
    );

    await waitFor(() => expect(hoisted.captured.url).toBe("/hub/operations"));

    const options = hoisted.captured.options as IHttpConnectionOptions;
    // The browser WS transport appends accessTokenFactory's value as ?access_token=… on the
    // upgrade — the query credential the StaffToken scheme reads for this path (ADR 024).
    expect(await options.accessTokenFactory!()).toBe("s3cret-staff-token");
    // Negotiate must be skipped: the v7+ client sends the factory token to HTTP requests
    // (incl. negotiate) as an Authorization header, which the backend scheme does not read.
    expect(options.skipNegotiation).toBe(true);
    expect(options.transport).toBe(HttpTransportType.WebSockets);

    // Connection state is rendered once start() resolves.
    expect(await screen.findByText("Connected")).toBeInTheDocument();
  });

  it("registers the ReceiveMessage client method (ADR 023 single method)", async () => {
    sessionStorage.setItem(STORAGE_KEY, "s3cret-staff-token");

    render(
      <Providers>
        <OperationsSignalRProvider>
          <ConnectionIndicator />
        </OperationsSignalRProvider>
      </Providers>,
    );

    await screen.findByText("Connected");
    const connection = hoisted.built.at(-1)!;
    expect(connection.handlers.has(RECEIVE_MESSAGE)).toBe(true);
  });

  it("parses a push, runs the cache bridge BEFORE the useListen fan-out, and ignores junk (ADR 026)", async () => {
    sessionStorage.setItem(STORAGE_KEY, "s3cret-staff-token");

    const queryClient = new QueryClient();
    const invalidateSpy = vi
      .spyOn(queryClient, "invalidateQueries")
      .mockResolvedValue(undefined);

    const received: OperationsFeedMessage[] = [];
    let invalidationsWhenListenerRan = -1;

    render(
      <Providers queryClient={queryClient}>
        <OperationsSignalRProvider>
          <ConnectionIndicator />
          <Listener
            onMessage={(message) => {
              received.push(message);
              invalidationsWhenListenerRan = invalidateSpy.mock.calls.length;
            }}
          />
        </OperationsSignalRProvider>
      </Providers>,
    );

    await screen.findByText("Connected");
    const connection = hoisted.built.at(-1)!;
    const handler = connection.handlers.get(RECEIVE_MESSAGE)!;

    act(() => {
      handler({
        listingId: "11111111-0000-0000-0000-000000000001",
        eventType: "BidPlacedOperations",
        payload: "Bid placed: $42.00",
        occurredAt: "2026-06-10T15:04:05Z",
      });
    });

    expect(received).toHaveLength(1);
    expect(received[0]!.eventType).toBe("BidPlacedOperations");
    // The bridge ran first: the re-query was in flight before the listener saw the message.
    expect(invalidationsWhenListenerRan).toBeGreaterThan(0);
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ["operations", "lot-board"],
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: ["operations", "bid-activity"],
    });

    // An unrecognized wire shape is logged-and-ignored: no listener call, no invalidation.
    const invalidationsBeforeJunk = invalidateSpy.mock.calls.length;
    act(() => {
      handler({ totally: "unrelated" });
    });
    expect(received).toHaveLength(1);
    expect(invalidateSpy.mock.calls.length).toBe(invalidationsBeforeJunk);
  });

  it("reconciles a reconnect with exactly one ['operations']-family invalidation (M8-S6b)", async () => {
    sessionStorage.setItem(STORAGE_KEY, "s3cret-staff-token");

    const queryClient = new QueryClient();
    const invalidateSpy = vi
      .spyOn(queryClient, "invalidateQueries")
      .mockResolvedValue(undefined);

    render(
      <Providers queryClient={queryClient}>
        <OperationsSignalRProvider>
          <ConnectionIndicator />
        </OperationsSignalRProvider>
      </Providers>,
    );

    await screen.findByText("Connected");
    const connection = hoisted.built.at(-1)!;
    expect(connection.reconnectedCallbacks.length).toBeGreaterThan(0);

    // Events missed while disconnected are reconciled by re-query, the same authority rule as
    // a push (ADR 026) — one blanket family invalidation per reconnect, no payload writes.
    act(() => {
      for (const callback of connection.reconnectedCallbacks) callback();
    });

    const familyInvalidations = invalidateSpy.mock.calls.filter((call) => {
      const { queryKey } = call[0] as { queryKey: readonly string[] };
      return queryKey.length === 1 && queryKey[0] === "operations";
    });
    expect(familyInvalidations).toHaveLength(1);
  });

  it("clears the token and re-shows the gate when the hub start fails with a visible 401", async () => {
    sessionStorage.setItem(STORAGE_KEY, "stale-token");

    const rejecting = {
      state: HubConnectionState.Disconnected,
      on: vi.fn(),
      onreconnecting: vi.fn(),
      onreconnected: vi.fn(),
      onclose: vi.fn(),
      start: vi.fn(async () => {
        throw new Error("Unable to connect: Status code '401' (Unauthorized).");
      }),
      stop: vi.fn(async () => {}),
    };

    render(
      <Providers>
        <AuthGate>
          <OperationsSignalRProvider
            createConnection={() => rejecting as unknown as HubConnection}
          >
            <div data-testid="dashboard" />
          </OperationsSignalRProvider>
        </AuthGate>
      </Providers>,
    );

    await waitFor(() => expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull());
    expect(
      screen.getByPlaceholderText("Enter the staff token"),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "The OperationsHub rejected the staff token (401). Enter a valid token.",
      ),
    ).toBeInTheDocument();
  });
});
