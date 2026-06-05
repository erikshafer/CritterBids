import { HubConnectionState } from "@microsoft/signalr";
import { useBiddingHub } from "./useBiddingHub";

// Connection-state colour per SignalR state. Tailwind v4 utilities (ADR 013).
const STATUS_STYLES: Record<HubConnectionState, string> = {
  [HubConnectionState.Connected]: "bg-green-500",
  [HubConnectionState.Connecting]: "bg-amber-500",
  [HubConnectionState.Reconnecting]: "bg-amber-500",
  [HubConnectionState.Disconnected]: "bg-red-500",
  [HubConnectionState.Disconnecting]: "bg-red-500",
};

export function App() {
  const { status, lastError, messages } = useBiddingHub();

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 bg-slate-950 p-6 text-slate-100">
      <div className="w-full max-w-md rounded-2xl border border-slate-800 bg-slate-900 p-6 shadow-xl">
        <h1 className="text-xl font-semibold">CritterBids — BiddingHub connection proof</h1>
        <p className="mt-1 text-sm text-slate-400">
          M8-S1 — proves the <code className="text-slate-200">/hub/bidding</code> negotiate +
          WebSocket path end to end. This is a connection harness, not a bidding UI.
        </p>

        <div className="mt-6 flex items-center gap-3">
          <span
            className={`inline-block h-3 w-3 rounded-full ${STATUS_STYLES[status]}`}
            aria-hidden
          />
          <span className="font-mono text-lg">{status}</span>
        </div>

        {lastError && (
          <p className="mt-3 rounded-lg bg-red-950/60 px-3 py-2 text-sm text-red-300">
            {lastError}
          </p>
        )}

        <div className="mt-6">
          <h2 className="text-sm font-medium text-slate-300">
            ReceiveMessage pushes ({messages.length})
          </h2>
          {messages.length === 0 ? (
            <p className="mt-1 text-sm text-slate-500">
              None yet — the connection state above is the proof.
            </p>
          ) : (
            <ul className="mt-2 space-y-1 font-mono text-xs text-slate-400">
              {messages.slice(0, 10).map((message, index) => (
                <li key={index} className="truncate">
                  {message.receivedAt} — {JSON.stringify(message.payload)}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </main>
  );
}
