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
import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import {
  createSignalRProvider,
  createAnonymousConnection,
} from "@critterbids/shared/signalr";

import { useSession } from "@/session/SessionContext";
import { applyHubMessage } from "@/signalr/cacheBridge";
import { parseHubMessage, type HubMessage } from "@/signalr/messages";

const BIDDING_HUB_URL = "/hub/bidding";

const { Provider: CoreProvider, useHub } =
  createSignalRProvider<HubMessage>("BiddingHubProvider");

export interface SignalRContextValue {
  status: HubConnectionState;
  lastError: string | null;
  watchListing: (listingId: string) => void;
  unwatchListing: (listingId: string) => void;
  subscribe: (listener: (message: HubMessage) => void) => () => void;
}

const SignalRContext = createContext<SignalRContextValue | null>(null);

function defaultCreateConnection(): HubConnection {
  return createAnonymousConnection(BIDDING_HUB_URL);
}

export interface SignalRProviderProps {
  children: ReactNode;
  createConnection?: () => HubConnection;
}

export function SignalRProvider({
  children,
  createConnection = defaultCreateConnection,
}: SignalRProviderProps) {
  const watchedListingsRef = useRef(new Set<string>());
  const connectionRef = useRef<HubConnection | null>(null);
  const participantIdRef = useRef<string | null>(null);

  const handleConnected = useCallback((connection: HubConnection) => {
    connectionRef.current = connection;
    const pid = participantIdRef.current;
    if (pid) {
      void connection.invoke("JoinBidderGroup", pid).catch(() => {});
    }
    for (const listingId of watchedListingsRef.current) {
      void connection.invoke("JoinListingGroup", listingId).catch(() => {});
    }
  }, []);

  const handleReconnected = useCallback((connection: HubConnection) => {
    const pid = participantIdRef.current;
    if (pid) {
      void connection.invoke("JoinBidderGroup", pid).catch(() => {});
    }
    for (const listingId of watchedListingsRef.current) {
      void connection.invoke("JoinListingGroup", listingId).catch(() => {});
    }
  }, []);

  return (
    <CoreProvider
      createConnection={createConnection}
      parseMessage={parseHubMessage}
      applyMessage={applyHubMessage}
      onConnected={handleConnected}
      onReconnected={handleReconnected}
    >
      <BidderGroupManager
        connectionRef={connectionRef}
        watchedListingsRef={watchedListingsRef}
        participantIdRef={participantIdRef}
      >
        {children}
      </BidderGroupManager>
    </CoreProvider>
  );
}

function BidderGroupManager({
  children,
  connectionRef,
  watchedListingsRef,
  participantIdRef,
}: {
  children: ReactNode;
  connectionRef: React.RefObject<HubConnection | null>;
  watchedListingsRef: React.RefObject<Set<string>>;
  participantIdRef: React.MutableRefObject<string | null>;
}) {
  const { status, lastError, subscribe } = useHub();
  const { participantId } = useSession();
  const [, forceUpdate] = useState(0);

  participantIdRef.current = participantId;

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
  }, [status, participantId, connectionRef]);

  // Re-join watched groups on reconnect (status transitions back to Connected)
  const prevStatusRef = useRef(status);
  useEffect(() => {
    const prev = prevStatusRef.current;
    prevStatusRef.current = status;
    if (
      status === HubConnectionState.Connected &&
      prev === HubConnectionState.Reconnecting
    ) {
      forceUpdate((n) => n + 1);
    }
  }, [status]);

  const value = useMemo<SignalRContextValue>(
    () => ({
      status,
      lastError,
      watchListing: (listingId: string) => {
        watchedListingsRef.current.add(listingId);
        const connection = connectionRef.current;
        if (connection?.state === HubConnectionState.Connected) {
          void connection
            .invoke("JoinListingGroup", listingId)
            .catch(() => {});
        }
      },
      unwatchListing: (listingId: string) => {
        watchedListingsRef.current.delete(listingId);
      },
      subscribe,
    }),
    [status, lastError, subscribe, connectionRef, watchedListingsRef],
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
