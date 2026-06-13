import { Outlet } from "@tanstack/react-router";

import { ConnectionIndicator } from "@/components/ConnectionIndicator";

export function AppShell() {
  return (
    <div className="flex min-h-screen flex-col bg-background text-foreground">
      <header className="flex items-center justify-between border-b border-border px-6 py-3">
        <h1 className="text-lg font-semibold tracking-tight">
          CritterBids Seller Console
        </h1>
        <ConnectionIndicator />
      </header>
      <main className="flex-1 p-6">
        <Outlet />
      </main>
    </div>
  );
}
