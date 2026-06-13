import { useCallback, useEffect, useRef, type ReactNode } from "react";
import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";
import {
  createSignalRProvider,
  createAnonymousConnection,
} from "@critterbids/shared/signalr";

import { useSession } from "@/session/SessionContext";

const BIDDING_HUB_URL = "/hub/bidding";

// The seller app connects to the same BiddingHub as the bidder (anonymous). Seller-specific
// notifications (SellerPayoutIssued, ObligationFulfilled, TrackingInfoProvided) arrive on the
// bidder:{participantId} group — the seller's ParticipantId IS their SellerId (OQ-1 resolution).
// No cache bridge yet — later M9 slices wire seller-specific queries and their invalidations.

function parseMessage(_payload: unknown): null {
  return null;
}

function applyMessage(_queryClient: QueryClient, _message: never): void {}

const {
  Provider: CoreProvider,
  useHub: useSellerSignalR,
  useConnectionState,
} = createSignalRProvider<never>("SellerBiddingHubProvider");

export { useSellerSignalR, useConnectionState };

export interface SellerSignalRProviderProps {
  children: ReactNode;
  createConnection?: () => HubConnection;
}

export function SellerSignalRProvider({
  children,
  createConnection,
}: SellerSignalRProviderProps) {
  const connectionRef = useRef<HubConnection | null>(null);
  const participantIdRef = useRef<string | null>(null);

  const factory = useCallback(() => {
    if (createConnection) return createConnection();
    return createAnonymousConnection(BIDDING_HUB_URL);
  }, [createConnection]);

  const handleConnected = useCallback((connection: HubConnection) => {
    connectionRef.current = connection;
    const pid = participantIdRef.current;
    if (pid) {
      void connection.invoke("JoinBidderGroup", pid).catch(() => {});
    }
  }, []);

  const handleReconnected = useCallback((connection: HubConnection) => {
    const pid = participantIdRef.current;
    if (pid) {
      void connection.invoke("JoinBidderGroup", pid).catch(() => {});
    }
  }, []);

  return (
    <CoreProvider
      createConnection={factory}
      parseMessage={parseMessage}
      applyMessage={applyMessage}
      onConnected={handleConnected}
      onReconnected={handleReconnected}
    >
      <SellerGroupManager
        connectionRef={connectionRef}
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
  participantIdRef,
}: {
  children: ReactNode;
  connectionRef: React.RefObject<HubConnection | null>;
  participantIdRef: React.MutableRefObject<string | null>;
}) {
  const { status } = useSellerSignalR();
  const { participantId } = useSession();
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

  return <>{children}</>;
}
