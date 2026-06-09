import { useEffect, useRef } from "react";
import type { HubConnectionState } from "@microsoft/signalr";

import { useSignalR } from "@/signalr/SignalRProvider";
import type { HubMessage } from "@/signalr/messages";

// ADR 026 consumer hooks. Components reach the live channel through these, never through a raw
// HubConnection.

/**
 * Subscribe to parsed BiddingHub messages for the component's lifetime. The handler is held in a ref
 * so the subscription is registered once and never re-subscribes when the handler identity changes
 * (a fresh closure every render), avoiding missed messages during the unsubscribe/re-subscribe gap.
 *
 * The cache bridge has already re-queried the authoritative view by the time the handler runs — use
 * `useListen` for TRANSIENT affordances (a live activity line, a toast), not to render payload
 * fields as authoritative state. The numbers come from the re-queried query, not from `message`.
 */
export function useListen(handler: (message: HubMessage) => void): void {
  const { subscribe } = useSignalR();
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(
    () => subscribe((message) => handlerRef.current(message)),
    [subscribe],
  );
}

/** The live BiddingHub connection state + last error, for connection-status UI. */
export function useHubConnectionState(): {
  status: HubConnectionState;
  lastError: string | null;
} {
  const { status, lastError } = useSignalR();
  return { status, lastError };
}

/**
 * Enrol the connection into a listing's group for the component's lifetime. Joins on mount (and on
 * reconnect, handled by the Provider) and drops local interest on unmount.
 */
export function useWatchListing(listingId: string): void {
  const { watchListing, unwatchListing } = useSignalR();
  useEffect(() => {
    watchListing(listingId);
    return () => unwatchListing(listingId);
  }, [watchListing, unwatchListing, listingId]);
}
