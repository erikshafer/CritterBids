import {
  createContext,
  use,
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

import { useStaffAuth } from "@/auth/StaffAuthContext";
import { OPERATIONS_HUB_URL, RECEIVE_MESSAGE } from "@/signalr/hub";

// ADR 026 replicated for the OperationsHub (M8-S5): one Provider Context owns the single staff
// HubConnection for the whole ops app, with the ADR 024 credential dance added. The provider
// mounts INSIDE the AuthGate, so it exists only while a staff token is held — clearing the token
// unmounts it and stops the connection.
//
// S5 is plumbing only: ReceiveMessage is registered and logged, per the slice scope. The
// OperationsFeedNotification parse surface, the useListen fan-out, and the TanStack Query cache
// bridge land in M8-S6 alongside the views they invalidate.
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

  const [status, setStatus] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  );
  const [lastError, setLastError] = useState<string | null>(null);

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

    // S5 scope: register + log. The S6 cache bridge replaces this body.
    connection.on(RECEIVE_MESSAGE, (payload: unknown) => {
      console.debug("[OperationsHub] ReceiveMessage", payload);
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
    () => ({ status, lastError }),
    [status, lastError],
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
