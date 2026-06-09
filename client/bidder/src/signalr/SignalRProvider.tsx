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
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";

import { useSession } from "@/session/SessionContext";
import { applyHubMessage } from "@/signalr/cacheBridge";
import { parseHubMessage, type HubMessage } from "@/signalr/messages";
import { RECEIVE_MESSAGE } from "@/signalr/hub";

// ADR 026 — the SignalR integration pattern. One `SignalRProvider` Context owns the single
// BiddingHub `HubConnection` for the whole app (it lives above the router, so the connection
// survives navigation), bridges every push into the TanStack Query cache, and fans parsed messages
// out to component subscribers via `useListen`. This replaces M8-S2's `useBiddingHub`, which opened
// its own connection inside a single indicator component.
//
// ADR 023 client conventions: anonymous `/hub/bidding`, `.withAutomaticReconnect()`, the single
// `ReceiveMessage` client method. ADR 025: same-origin URL (the Vite proxy forwards it in dev).

export interface SignalRContextValue {
  status: HubConnectionState;
  lastError: string | null;
  /** Enrol the connection into a listing's `listing:{id}` group (idempotent; re-applied on reconnect). */
  watchListing: (listingId: string) => void;
  /** Drop local interest in a listing group. (The hub exposes no server-side leave; this only stops re-join.) */
  unwatchListing: (listingId: string) => void;
  /** Subscribe to parsed hub messages. Returns an unsubscribe function. Prefer the `useListen` hook. */
  subscribe: (listener: (message: HubMessage) => void) => () => void;
}

const SignalRContext = createContext<SignalRContextValue | null>(null);

const BIDDING_HUB_URL = "/hub/bidding";

/** Default connection factory. Overridable via the Provider's `createConnection` prop for tests. */
function defaultCreateConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(BIDDING_HUB_URL)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}

export interface SignalRProviderProps {
  children: ReactNode;
  /** Test seam: inject a fake HubConnection. Production uses {@link defaultCreateConnection}. */
  createConnection?: () => HubConnection;
}

export function SignalRProvider({
  children,
  createConnection = defaultCreateConnection,
}: SignalRProviderProps) {
  const queryClient = useQueryClient();
  const { participantId } = useSession();

  const [status, setStatus] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  );
  const [lastError, setLastError] = useState<string | null>(null);

  const connectionRef = useRef<HubConnection | null>(null);
  const listenersRef = useRef(new Set<(message: HubMessage) => void>());
  const watchedListingsRef = useRef(new Set<string>());

  // `createConnection` is captured once on mount; a test that swaps it mid-life is out of scope.
  const createConnectionRef = useRef(createConnection);

  useEffect(() => {
    const connection = createConnectionRef.current();
    connectionRef.current = connection;

    const syncStatus = () => setStatus(connection.state);

    connection.on(RECEIVE_MESSAGE, (payload: unknown) => {
      const message = parseHubMessage(payload);
      if (message === null) return; // forward-compatible: ignore unknown notification shapes
      applyHubMessage(queryClient, message);
      for (const listener of listenersRef.current) {
        listener(message);
      }
    });

    const rejoinWatchedGroups = () => {
      for (const listingId of watchedListingsRef.current) {
        void connection.invoke("JoinListingGroup", listingId).catch(() => {});
      }
    };

    connection.onreconnecting((error) => {
      setLastError(error?.message ?? "reconnecting");
      syncStatus();
    });
    connection.onreconnected(() => {
      setLastError(null);
      rejoinWatchedGroups();
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
        rejoinWatchedGroups();
        syncStatus();
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setLastError(error instanceof Error ? error.message : String(error));
        syncStatus();
      });

    return () => {
      cancelled = true;
      connectionRef.current = null;
      void connection.stop();
    };
    // One connection for the app's lifetime. `queryClient` is a stable singleton, so this effect
    // runs once; the connection factory is read from a ref so swapping it never re-runs the effect.
  }, [queryClient]);

  // Enrol the held participant's `bidder:{id}` group once the session id is known and we are
  // connected — the channel for bidder-targeted pushes (e.g. ProxyBidExhausted). Re-runs on
  // reconnect because `status` flips back to Connected.
  useEffect(() => {
    const connection = connectionRef.current;
    if (
      connection === null ||
      status !== HubConnectionState.Connected ||
      participantId === null
    ) {
      return;
    }
    void connection.invoke("JoinBidderGroup", participantId).catch(() => {});
  }, [status, participantId]);

  const value = useMemo<SignalRContextValue>(
    () => ({
      status,
      lastError,
      watchListing: (listingId) => {
        watchedListingsRef.current.add(listingId);
        const connection = connectionRef.current;
        if (connection?.state === HubConnectionState.Connected) {
          void connection.invoke("JoinListingGroup", listingId).catch(() => {});
        }
      },
      unwatchListing: (listingId) => {
        watchedListingsRef.current.delete(listingId);
      },
      subscribe: (listener) => {
        listenersRef.current.add(listener);
        return () => {
          listenersRef.current.delete(listener);
        };
      },
    }),
    [status, lastError],
  );

  return <SignalRContext value={value}>{children}</SignalRContext>;
}

export function useSignalR(): SignalRContextValue {
  const context = use(SignalRContext);
  if (context === null) {
    throw new Error("useSignalR must be used within a SignalRProvider.");
  }
  return context;
}
