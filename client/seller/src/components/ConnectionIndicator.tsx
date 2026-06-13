import { HubConnectionState } from "@microsoft/signalr";

import { useConnectionState } from "@/signalr/SignalRProvider";
import { cn } from "@/lib/utils";

const DOT: Record<HubConnectionState, string> = {
  [HubConnectionState.Connected]: "bg-green-500",
  [HubConnectionState.Connecting]: "bg-amber-500",
  [HubConnectionState.Reconnecting]: "bg-amber-500",
  [HubConnectionState.Disconnected]: "bg-red-500",
  [HubConnectionState.Disconnecting]: "bg-red-500",
};

export function ConnectionIndicator() {
  const { status, lastError } = useConnectionState();

  return (
    <span
      className="text-muted-foreground inline-flex items-center gap-2 text-xs"
      title={lastError ?? "BiddingHub connection"}
    >
      <span
        className={cn("inline-block h-2 w-2 rounded-full", DOT[status])}
        aria-hidden
      />
      <span className="font-mono">{status}</span>
    </span>
  );
}
