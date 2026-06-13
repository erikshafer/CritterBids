import { useCallback, useRef, type ReactNode } from "react";
import type { HubConnection } from "@microsoft/signalr";
import type { QueryClient } from "@tanstack/react-query";
import {
  createSignalRProvider,
  createTokenConnection,
} from "@critterbids/shared/signalr";

import { useStaffAuth } from "@/auth/StaffAuthContext";
import { operationsKeys } from "@/operations/keys";
import { applyOperationsFeedMessage } from "@/signalr/cacheBridge";
import { OPERATIONS_HUB_URL } from "@/signalr/hub";
import {
  parseOperationsFeedMessage,
  type OperationsFeedMessage,
} from "@/signalr/messages";

const {
  Provider: CoreProvider,
  useHub: useOperationsSignalR,
  useListen,
  useConnectionState,
} = createSignalRProvider<OperationsFeedMessage>("OperationsHubProvider");

export { useOperationsSignalR, useListen, useConnectionState };

export interface OperationsSignalRProviderProps {
  children: ReactNode;
  createConnection?: (getToken: () => string | null) => HubConnection;
}

export function OperationsSignalRProvider({
  children,
  createConnection,
}: OperationsSignalRProviderProps) {
  const { token, clearToken } = useStaffAuth();
  const tokenRef = useRef(token);
  tokenRef.current = token;
  const clearTokenRef = useRef(clearToken);
  clearTokenRef.current = clearToken;

  const factory = useCallback(() => {
    if (createConnection) {
      return createConnection(() => tokenRef.current);
    }
    return createTokenConnection(
      OPERATIONS_HUB_URL,
      () => tokenRef.current,
    );
  }, [createConnection]);

  const handleReconnected = useCallback(
    (_connection: HubConnection, queryClient: QueryClient) => {
      void queryClient.invalidateQueries({
        queryKey: operationsKeys.all,
      });
    },
    [],
  );

  const handleConnectionError = useCallback((error: unknown) => {
    const message = error instanceof Error ? error.message : String(error);
    if (/401|unauthorized/i.test(message)) {
      clearTokenRef.current(
        "The OperationsHub rejected the staff token (401). Enter a valid token.",
      );
    }
  }, []);

  return (
    <CoreProvider
      createConnection={factory}
      parseMessage={parseOperationsFeedMessage}
      applyMessage={applyOperationsFeedMessage}
      onReconnected={handleReconnected}
      onConnectionError={handleConnectionError}
    >
      {children}
    </CoreProvider>
  );
}
