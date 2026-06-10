import { HubConnectionState } from "@microsoft/signalr";

import { useOperationsSignalR } from "@/signalr/SignalRProvider";
import { cn } from "@/lib/utils";

// Dot colour + label per SignalR connection state (same shape as the bidder's indicator). The
// OperationsHub connection is owned by the app-wide OperationsSignalRProvider; this just reads
// its live state. A failure reason (e.g. a refused WebSocket upgrade) renders beside the state.
const DOT: Record<HubConnectionState, string> = {
  [HubConnectionState.Connected]: "bg-green-500",
  [HubConnectionState.Connecting]: "bg-amber-500",
  [HubConnectionState.Reconnecting]: "bg-amber-500",
  [HubConnectionState.Disconnected]: "bg-red-500",
  [HubConnectionState.Disconnecting]: "bg-red-500",
};

export function ConnectionIndicator() {
  const { status, lastError } = useOperationsSignalR();

  return (
    <span
      className="text-muted-foreground inline-flex items-center gap-2 text-sm"
      title={lastError ?? "OperationsHub connection"}
    >
      <span
        className={cn("inline-block h-2.5 w-2.5 rounded-full", DOT[status])}
        aria-hidden
      />
      <span className="font-mono">{status}</span>
      {lastError !== null && status === HubConnectionState.Disconnected && (
        <span className="text-destructive max-w-64 truncate text-xs">
          {lastError}
        </span>
      )}
    </span>
  );
}
