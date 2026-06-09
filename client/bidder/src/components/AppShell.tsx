import { Link, Outlet } from "@tanstack/react-router";

import { ConnectionIndicator } from "@/components/ConnectionIndicator";
import { SessionIndicator } from "@/components/SessionIndicator";

// The persistent bidder shell (M8-S2). Header carries the app identity, the anonymous-session
// indicator, and the live BiddingHub connection signal; the routed page renders into <Outlet />.
// The shell never unmounts across navigation, so the hub connection and the session persist.
export function AppShell() {
  return (
    <div className="bg-background text-foreground min-h-screen">
      <header className="border-border/60 border-b">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3">
          <Link to="/" className="text-lg font-semibold tracking-tight">
            CritterBids
          </Link>
          <div className="flex items-center gap-4">
            <SessionIndicator />
            <ConnectionIndicator />
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-5xl px-4 py-6">
        <Outlet />
      </main>
    </div>
  );
}
