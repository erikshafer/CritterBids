import { describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import {
  HttpTransportType,
  HubConnectionState,
  type HubConnection,
  type IHttpConnectionOptions,
} from "@microsoft/signalr";

import { StaffAuthProvider } from "@/auth/StaffAuthContext";
import { AuthGate } from "@/auth/AuthGate";
import { OperationsSignalRProvider } from "@/signalr/SignalRProvider";
import { ConnectionIndicator } from "@/components/ConnectionIndicator";
import { RECEIVE_MESSAGE } from "@/signalr/hub";

const STORAGE_KEY = "critterbids.staffToken";

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
    on(method: string, handler: (payload: unknown) => void) {
      this.handlers.set(method, handler);
    }
    onreconnecting() {}
    onreconnected() {}
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
      <StaffAuthProvider>
        <OperationsSignalRProvider>
          <ConnectionIndicator />
        </OperationsSignalRProvider>
      </StaffAuthProvider>,
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
      <StaffAuthProvider>
        <OperationsSignalRProvider>
          <ConnectionIndicator />
        </OperationsSignalRProvider>
      </StaffAuthProvider>,
    );

    await screen.findByText("Connected");
    const connection = hoisted.built.at(-1)!;
    expect(connection.handlers.has(RECEIVE_MESSAGE)).toBe(true);
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
      <StaffAuthProvider>
        <AuthGate>
          <OperationsSignalRProvider
            createConnection={() => rejecting as unknown as HubConnection}
          >
            <div data-testid="dashboard" />
          </OperationsSignalRProvider>
        </AuthGate>
      </StaffAuthProvider>,
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
