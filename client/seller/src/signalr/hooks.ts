import { useEffect, useRef } from "react";
import type { HubConnectionState } from "@microsoft/signalr";

import { useSellerSignalR } from "@/signalr/SignalRProvider";
import type { HubMessage } from "@/signalr/messages";

export function useListen(handler: (message: HubMessage) => void): void {
  const { subscribe } = useSellerSignalR();
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(
    () => subscribe((message) => handlerRef.current(message)),
    [subscribe],
  );
}

export function useHubConnectionState(): {
  status: HubConnectionState;
  lastError: string | null;
} {
  const { status, lastError } = useSellerSignalR();
  return { status, lastError };
}

export function useWatchListing(listingId: string): void {
  const { watchListing, unwatchListing } = useSellerSignalR();
  useEffect(() => {
    watchListing(listingId);
    return () => unwatchListing(listingId);
  }, [watchListing, unwatchListing, listingId]);
}
