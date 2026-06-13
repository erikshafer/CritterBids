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
  createSignalRProvider<HubMessage>("SellerBiddingHubProvider");

export interface SellerSignalRContextValue {
  status: HubConnectionState;
  lastError: string | null;
  watchListing: (listingId: string) => void;
  unwatchListing: (listingId: string) => void;
  subscribe: (listener: (message: HubMessage) => void) => () => void;
}

const SellerSignalRContext = createContext<SellerSignalRContextValue | null>(
  null,
);

function defaultCreateConnection(): HubConnection {
  return createAnonymousConnection(BIDDING_HUB_URL);
}

export interface SellerSignalRProviderProps {
  children: ReactNode;
  createConnection?: () => HubConnection;
}

export function SellerSignalRProvider({
  children,
  createConnection = defaultCreateConnection,
}: SellerSignalRProviderProps) {
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
      <SellerGroupManager
        connectionRef={connectionRef}
        watchedListingsRef={watchedListingsRef}
        participantIdRef={participantIdRef}
      >
        {children}
      </SellerGroupManager>
    </CoreProvider>
  );
}

function SellerGroupManager({
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

  const value = useMemo<SellerSignalRContextValue>(
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

  return (
    <SellerSignalRContext value={value}>{children}</SellerSignalRContext>
  );
}

export function useSellerSignalR(): SellerSignalRContextValue {
  const context = use(SellerSignalRContext);
  if (context === null) {
    throw new Error(
      "useSellerSignalR must be used within a SellerSignalRProvider.",
    );
  }
  return context;
}

export { useHub as useConnectionState };
