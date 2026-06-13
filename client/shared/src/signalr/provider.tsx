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
import { useQueryClient, type QueryClient } from "@tanstack/react-query";

import { RECEIVE_MESSAGE } from "./hub";

export interface SignalRContextValue<TMessage> {
  status: HubConnectionState;
  lastError: string | null;
  subscribe: (listener: (message: TMessage) => void) => () => void;
}

export interface SignalRProviderProps<TMessage> {
  children: ReactNode;
  createConnection: () => HubConnection;
  parseMessage: (payload: unknown) => TMessage | null;
  applyMessage: (queryClient: QueryClient, message: TMessage) => void;
  onConnected?: (connection: HubConnection) => void;
  onReconnected?: (
    connection: HubConnection,
    queryClient: QueryClient,
  ) => void;
  onConnectionError?: (error: unknown) => void;
}

export function createSignalRProvider<TMessage>(displayName: string) {
  const Context = createContext<SignalRContextValue<TMessage> | null>(null);

  function Provider({
    children,
    createConnection,
    parseMessage,
    applyMessage,
    onConnected,
    onReconnected,
    onConnectionError,
  }: SignalRProviderProps<TMessage>) {
    const queryClient = useQueryClient();

    const [status, setStatus] = useState<HubConnectionState>(
      HubConnectionState.Disconnected,
    );
    const [lastError, setLastError] = useState<string | null>(null);

    const listenersRef = useRef(new Set<(message: TMessage) => void>());
    const subscribe = useCallback(
      (listener: (message: TMessage) => void) => {
        listenersRef.current.add(listener);
        return () => {
          listenersRef.current.delete(listener);
        };
      },
      [],
    );

    const createConnectionRef = useRef(createConnection);
    const queryClientRef = useRef(queryClient);
    queryClientRef.current = queryClient;
    const parseMessageRef = useRef(parseMessage);
    parseMessageRef.current = parseMessage;
    const applyMessageRef = useRef(applyMessage);
    applyMessageRef.current = applyMessage;
    const onConnectedRef = useRef(onConnected);
    onConnectedRef.current = onConnected;
    const onReconnectedRef = useRef(onReconnected);
    onReconnectedRef.current = onReconnected;
    const onConnectionErrorRef = useRef(onConnectionError);
    onConnectionErrorRef.current = onConnectionError;

    useEffect(() => {
      const connection = createConnectionRef.current();
      const syncStatus = () => setStatus(connection.state);

      connection.on(RECEIVE_MESSAGE, (payload: unknown) => {
        const message = parseMessageRef.current(payload);
        if (message === null) return;
        applyMessageRef.current(queryClientRef.current, message);
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
        onReconnectedRef.current?.(connection, queryClientRef.current);
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
          onConnectedRef.current?.(connection);
        })
        .catch((error: unknown) => {
          if (cancelled) return;
          const message =
            error instanceof Error ? error.message : String(error);
          setLastError(message);
          syncStatus();
          onConnectionErrorRef.current?.(error);
        });

      return () => {
        cancelled = true;
        void connection.stop();
      };
    }, []);

    const value = useMemo<SignalRContextValue<TMessage>>(
      () => ({ status, lastError, subscribe }),
      [status, lastError, subscribe],
    );

    return <Context value={value}>{children}</Context>;
  }

  Provider.displayName = displayName;

  function useHub(): SignalRContextValue<TMessage> {
    const context = use(Context);
    if (context === null) {
      throw new Error(
        `useHub must be used within a <${displayName}> provider.`,
      );
    }
    return context;
  }

  function useListen(handler: (message: TMessage) => void): void {
    const { subscribe } = useHub();
    const handlerRef = useRef(handler);
    handlerRef.current = handler;

    useEffect(
      () => subscribe((message) => handlerRef.current(message)),
      [subscribe],
    );
  }

  function useConnectionState(): {
    status: HubConnectionState;
    lastError: string | null;
  } {
    const { status, lastError } = useHub();
    return { status, lastError };
  }

  return { Provider, useHub, useListen, useConnectionState };
}
