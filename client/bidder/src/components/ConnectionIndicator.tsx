import { HubConnectionState } from "@microsoft/signalr";

import { useBiddingHub } from "@/useBiddingHub";
import { cn } from "@/lib/utils";

// Dot colour + label per SignalR connection state. The BiddingHub stays connected and observable
// across the app (the hook lives in the persistent shell header), but M8-S2 wires NO bid data —
// the live bid feed / placement / outbid handling is M8-S3 (ADR 014). This is the S1 proof's
// connection signal, retained.
const DOT: Record<HubConnectionState, string> = {
  [HubConnectionState.Connected]: "bg-green-500",
  [HubConnectionState.Connecting]: "bg-amber-500",
  [HubConnectionState.Reconnecting]: "bg-amber-500",
  [HubConnectionState.Disconnected]: "bg-red-500",
  [HubConnectionState.Disconnecting]: "bg-red-500",
};

export function ConnectionIndicator() {
  const { status } = useBiddingHub();

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
