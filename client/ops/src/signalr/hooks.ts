import { useEffect, useRef } from "react";

import { useOperationsSignalR } from "@/signalr/SignalRProvider";
import type { OperationsFeedMessage } from "@/signalr/messages";

// ADR 026 consumer hook, the ops-app counterpart of the bidder's hooks.ts. Components reach the
// live channel through this, never through a raw HubConnection.

/**
 * Subscribe to parsed OperationsHub messages for the component's lifetime. The handler is held
 * in a ref so the subscription registers once and never re-subscribes when the handler identity
 * changes (a fresh closure every render), avoiding missed messages during the
 * unsubscribe/re-subscribe gap.
 *
 * The cache bridge has already re-queried the affected boards by the time the handler runs —
 * use this for TRANSIENT affordances (an activity line, a toast), not to render payload fields
 * as authoritative state. The rows come from the re-queried boards, not from `message`.
 */
export function useListen(
  handler: (message: OperationsFeedMessage) => void,
): void {
  const { subscribe } = useOperationsSignalR();
  const handlerRef = useRef(handler);
  handlerRef.current = handler;

  useEffect(
    () => subscribe((message) => handlerRef.current(message)),
    [subscribe],
  );
}
