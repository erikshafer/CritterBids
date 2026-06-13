import { Link, Outlet } from "@tanstack/react-router";

import { ConnectionIndicator } from "@/components/ConnectionIndicator";
import { useSession } from "@/session/SessionContext";
import { useActionableObligationCount } from "@/obligations/ObligationsPage";

export function AppShell() {
  const { isRegisteredSeller } = useSession();

  return (
    <div className="flex min-h-screen flex-col bg-background text-foreground">
      <header className="flex items-center justify-between border-b border-border px-6 py-3">
        <div className="flex items-center gap-6">
          <Link to="/">
            <h1 className="text-lg font-semibold tracking-tight">
              CritterBids Seller Console
            </h1>
          </Link>
          {isRegisteredSeller && (
            <nav className="flex items-center gap-4">
              <Link
                to="/listings"
                className="text-sm text-muted-foreground transition-colors hover:text-foreground [&.active]:text-foreground [&.active]:font-medium"
              >
                My Listings
              </Link>
              <ObligationsNavLink />
            </nav>
          )}
        </div>
        <ConnectionIndicator />
      </header>
      <main className="flex-1 p-6">
        <Outlet />
      </main>
    </div>
  );
}

function ObligationsNavLink() {
  const actionableCount = useActionableObligationCount();

  return (
    <Link
      to="/obligations"
      className="text-sm text-muted-foreground transition-colors hover:text-foreground [&.active]:text-foreground [&.active]:font-medium"
    >
      Obligations
      {actionableCount > 0 && (
        <span className="ml-1 inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-destructive px-1.5 text-xs font-medium text-destructive-foreground">
          {actionableCount}
        </span>
      )}
    </Link>
  );
}
