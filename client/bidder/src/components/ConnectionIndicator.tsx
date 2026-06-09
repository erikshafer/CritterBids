import { HubConnectionState } from "@microsoft/signalr";

import { useHubConnectionState } from "@/signalr/hooks";
import { cn } from "@/lib/utils";

// Dot colour + label per SignalR connection state. The BiddingHub connection is owned by the
// app-wide SignalRProvider (ADR 026); this indicator just reads its live state. As of M8-S3b the
// live bid feed, placement, and outbid/extended/gavel affordances ride that same connection.
const DOT: Record<HubConnectionState, string> = {
  [HubConnectionState.Connected]: "bg-green-500",
  [HubConnectionState.Connecting]: "bg-amber-500",
  [HubConnectionState.Reconnecting]: "bg-amber-500",
  [HubConnectionState.Disconnected]: "bg-red-500",
  [HubConnectionState.Disconnecting]: "bg-red-500",
};

export function ConnectionIndicator() {
  const { status } = useHubConnectionState();

  return (
    <span
      className="text-muted-foreground inline-flex items-center gap-2 text-xs"
      title="BiddingHub connection"
    >
      <span
        className={cn("inline-block h-2 w-2 rounded-full", DOT[status])}
        aria-hidden
      />
      <span className="font-mono">{status}</span>
    </span>
  );
}
