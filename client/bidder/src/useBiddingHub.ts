import { useEffect, useState } from "react";
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";

// The record SignalR delivers on the "ReceiveMessage" client method. Per ADR 023 the payload is
// the raw notification record (no CloudEvents envelope). This proof only needs to show the channel
// is live, so the payload is captured untyped; the Zod schema that validates it at the wire boundary
// (ADR 013) lands with the live-bidding slice (M8-S3), not here.
export interface ReceivedMessage {
  receivedAt: string;
  payload: unknown;
}

export interface BiddingHubState {
  status: HubConnectionState;
  lastError: string | null;
  messages: ReceivedMessage[];
}

// ADR 025: same-origin "/hub/bidding". In dev the Vite proxy forwards it (ws:true) to the API host;
// in production the SPA is served from that same host. The anonymous BiddingHub (Program.cs:431,
// [AllowAnonymous]) needs no credential — unlike the StaffOnly OperationsHub, which is out of scope
// for this proof (it requires the access_token query-string dance, ADR 024).
const BIDDING_HUB_URL = "/hub/bidding";

/**
 * Opens a single HubConnection to the anonymous BiddingHub and surfaces its live connection state.
 * The connection's lifetime is tied to the component: it starts on mount and stops on unmount.
 */
export function useBiddingHub(): BiddingHubState {
  const [status, setStatus] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  );
  const [lastError, setLastError] = useState<string | null>(null);
  const [messages, setMessages] = useState<ReceivedMessage[]>([]);

  useEffect(() => {
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl(BIDDING_HUB_URL)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    const syncStatus = () => setStatus(connection.state);

    // ADR 023 client-method name. The proof records that a push arrived and what it carried;
    // rendering its domain meaning (a bid, an outbid) is M8-S3 work.
    connection.on("ReceiveMessage", (payload: unknown) => {
      setMessages((prev) => [
        { receivedAt: new Date().toISOString(), payload },
        ...prev,
      ]);
    });

    connection.onreconnecting((error) => {
      setLastError(error?.message ?? "reconnecting");
      syncStatus();
    });
    connection.onreconnected(() => {
      setLastError(null);
      syncStatus();
    });
    connection.onclose((error) => {
      setLastError(error?.message ?? null);
      syncStatus();
    });

    let cancelled = false;
    setStatus(HubConnectionState.Connecting);
    connection
      .start()
      .then(() => {
        if (!cancelled) {
          setLastError(null);
          syncStatus();
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setLastError(error instanceof Error ? error.message : String(error));
          syncStatus();
        }
      });

    return () => {
      cancelled = true;
      void connection.stop();
    };
  }, []);

  return { status, lastError, messages };
}
