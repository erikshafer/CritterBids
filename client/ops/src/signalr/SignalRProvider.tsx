import {
  createContext,
  use,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  HttpTransportType,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";

import { useStaffAuth } from "@/auth/StaffAuthContext";
import { operationsKeys } from "@/operations/keys";
import { applyOperationsFeedMessage } from "@/signalr/cacheBridge";
import { OPERATIONS_HUB_URL, RECEIVE_MESSAGE } from "@/signalr/hub";
import {
  parseOperationsFeedMessage,
  type OperationsFeedMessage,
} from "@/signalr/messages";

// ADR 026 replicated for the OperationsHub (M8-S5): one Provider Context owns the single staff
// HubConnection for the whole ops app, with the ADR 024 credential dance added. The provider
// mounts INSIDE the AuthGate, so it exists only while a staff token is held — clearing the token
// unmounts it and stops the connection.
//
// M8-S6 completes the pattern: every ReceiveMessage payload is parsed at the wire boundary
// (messages.ts), runs the TanStack Query cache bridge FIRST (the re-query is in flight before
// any listener sees the message), then fans out to useListen subscribers. An unparseable payload
// is logged-and-ignored, never thrown — a future notification shape cannot tear down the
// connection.
//
// ── The credential transport (ADR 024), and why the connection skips negotiate ──
// The StaffToken scheme reads the hub credential ONLY from the `access_token` query string on the
// /hub/operations path (the X-Staff-Token header everywhere else; never a query string on HTTP).
// Since v7, @microsoft/signalr delivers `accessTokenFactory` tokens to HTTP requests — including
// the negotiate POST — as an `Authorization: Bearer` header (AccessTokenHttpClient), which the
// backend scheme deliberately does not read; the `access_token` query parameter is appended only
// to the browser WebSocket upgrade, where headers are impossible. A default negotiate-first start
// therefore 401s before any WebSocket opens. Skipping negotiation (supported only with the
// WebSockets transport) makes the upgrade request — the one that carries `access_token` — the
// connection's only request. That is exactly the credential path ADR 024 designed and M7-S6
// integration-tested. Cost: no SSE/long-polling fallback, acceptable for the staff dashboard.
export interface OperationsSignalRContextValue {
  status: HubConnectionState;
  lastError: string | null;
  /**
   * Subscribe to parsed OperationsHub messages; returns the unsubscribe. By the time a
   * subscriber runs, the cache bridge has already invalidated the affected boards — listeners
   * are for TRANSIENT affordances (a feed line, a toast), never for rendering payload fields
   * as authoritative state (ADR 026).
   */
  subscribe: (
    listener: (message: OperationsFeedMessage) => void,
  ) => () => void;
}

const OperationsSignalRContext =
  createContext<OperationsSignalRContextValue | null>(null);

/** Default connection factory. Overridable via the Provider's `createConnection` prop for tests. */
function defaultCreateConnection(getToken: () => string | null): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(OPERATIONS_HUB_URL, {
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
      // Read per (re)connect attempt; the browser WS transport sends it as ?access_token=…
      accessTokenFactory: () => getToken() ?? "",
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}

export interface OperationsSignalRProviderProps {
  children: ReactNode;
  /** Test seam: inject a fake HubConnection. Production uses {@link defaultCreateConnection}. */
  createConnection?: (getToken: () => string | null) => HubConnection;
}

export function OperationsSignalRProvider({
  children,
  createConnection = defaultCreateConnection,
}: OperationsSignalRProviderProps) {
  const { token, clearToken } = useStaffAuth();
  const queryClient = useQueryClient();

  const [status, setStatus] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  );
  const [lastError, setLastError] = useState<string | null>(null);

  // Listener fan-out for useListen. A ref'd Set keeps subscribe's identity stable and lets the
  // connection effect read the current subscribers without re-running.
  const listenersRef = useRef(
    new Set<(message: OperationsFeedMessage) => void>(),
  );
  const subscribe = useCallback(
    (listener: (message: OperationsFeedMessage) => void) => {
      listenersRef.current.add(listener);
      return () => {
        listenersRef.current.delete(listener);
      };
    },
    [],
  );

  const queryClientRef = useRef(queryClient);
  queryClientRef.current = queryClient;

  // The factory + token are read through refs so the connection effect runs exactly once per
  // provider mount (the provider lives inside the AuthGate, so a token change = a remount).
  const createConnectionRef = useRef(createConnection);
  const tokenRef = useRef(token);
  tokenRef.current = token;
  const clearTokenRef = useRef(clearToken);
  clearTokenRef.current = clearToken;

  useEffect(() => {
    const connection = createConnectionRef.current(() => tokenRef.current);

    const syncStatus = () => setStatus(connection.state);

    // ADR 026: parse → cache bridge → fan out, in that order.
    connection.on(RECEIVE_MESSAGE, (payload: unknown) => {
      const message = parseOperationsFeedMessage(payload);
      if (message === null) {
        // Forward-compatible: an unknown wire shape is ignored, not fatal.
        console.debug("[OperationsHub] unrecognized payload ignored", payload);
        return;
      }
      applyOperationsFeedMessage(queryClientRef.current, message);
      for (const listener of listenersRef.current) {
        listener(message);
      }
    });

    connection.onreconnecting((error) => {
      setLastError(error?.message ?? "reconnecting");
      syncStatus();
    });
    connection.onreconnected(() => {
      setLastError(null);
      syncStatus();
      // M8-S6b reconnection reconciliation: pushes missed while disconnected are recovered by
      // one one-shot re-query of the whole board family — the same authority rule as a push
      // (push = "something changed, refetch"; ADR 026). This replaces the accidental recovery
      // the retired polling stopgap used to provide. No payload is written; the queries re-fetch.
      void queryClientRef.current.invalidateQueries({
        queryKey: operationsKeys.all,
      });
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
        if (cancelled) return;
        setLastError(null);
        syncStatus();
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        const message = error instanceof Error ? error.message : String(error);
        setLastError(message);
        syncStatus();
        // Browsers hide the WS upgrade status code, so most auth failures surface as a generic
        // transport error (the gate's HTTP probe is the reliable 401 detector). When a 401 IS
        // visible in the failure (non-browser transports, future server changes), honor the
        // ADR 024 contract: clear the token and re-show the gate.
        if (/401|unauthorized/i.test(message)) {
          clearTokenRef.current(
            "The OperationsHub rejected the staff token (401). Enter a valid token.",
          );
        }
      });

    return () => {
      cancelled = true;
      void connection.stop();
    };
  }, []);

  const value = useMemo<OperationsSignalRContextValue>(
    () => ({ status, lastError, subscribe }),
    [status, lastError, subscribe],
  );

  return (
    <OperationsSignalRContext value={value}>
      {children}
    </OperationsSignalRContext>
  );
}

export function useOperationsSignalR(): OperationsSignalRContextValue {
  const context = use(OperationsSignalRContext);
  if (context === null) {
    throw new Error(
      "useOperationsSignalR must be used within an OperationsSignalRProvider.",
    );
  }
  return context;
}
