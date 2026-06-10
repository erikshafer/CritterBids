import { Link, Outlet } from "@tanstack/react-router";

import { ConnectionIndicator } from "@/components/ConnectionIndicator";

// The persistent ops shell (M8-S5): a header carrying the app identity + the live OperationsHub
// connection signal, a sidebar nav for the six M8-S6 dashboard views, and the routed content
// area. High-contrast and projector-legible by design: always-dark theme (index.html pins
// class="dark"), an 18px rem root (index.css), and generous type throughout — the dashboard is
// projected on a conference screen beside the bidder app.
const NAV_ITEMS = [
  { to: "/", label: "Lot board" },
  { to: "/bid-activity", label: "Bid activity" },
  { to: "/settlement-queue", label: "Settlement queue" },
  { to: "/escalations", label: "Escalations" },
  { to: "/disputes", label: "Disputes" },
  { to: "/sessions", label: "Sessions & participants" },
] as const;

export function AppShell() {
  return (
    <div className="bg-background text-foreground flex min-h-screen flex-col">
      <header className="border-border/60 border-b">
        <div className="flex items-center justify-between gap-4 px-6 py-4">
          <Link to="/" className="text-xl font-semibold tracking-tight">
            CritterBids{" "}
            <span className="text-muted-foreground font-normal">
              Operations
            </span>
          </Link>
          <ConnectionIndicator />
        </div>
      </header>
      <div className="flex flex-1">
        <nav
          aria-label="Dashboard views"
          className="border-border/60 w-64 shrink-0 border-r px-3 py-6"
        >
          <ul className="space-y-1">
            {NAV_ITEMS.map((item) => (
              <li key={item.to}>
                <Link
                  to={item.to}
                  className="text-muted-foreground hover:bg-accent hover:text-accent-foreground block rounded-md px-3 py-2 text-base font-medium"
                  activeProps={{
                    className: "bg-accent text-accent-foreground",
                  }}
                  activeOptions={{ exact: item.to === "/" }}
                >
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        <main className="flex-1 px-8 py-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
