import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";

import "./index.css";
import { router } from "@/router";
import { AuthGate } from "@/auth/AuthGate";
import { StaffAuthProvider } from "@/auth/StaffAuthContext";
import { OperationsSignalRProvider } from "@/signalr/SignalRProvider";

// One QueryClient for the app. S5 wires no queries — M8-S6's view queries (and the OperationsHub
// cache bridge that invalidates them) land in this client. staleTime matches the bidder app.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
    },
  },
});

// Provider order: QueryClientProvider → StaffAuthProvider (the held token) → AuthGate (nothing
// below mounts without a token) → OperationsSignalRProvider (the single staff-credentialed
// OperationsHub connection — clearing the token unmounts it, stopping the connection) →
// RouterProvider (the shell + views).
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <StaffAuthProvider>
        <AuthGate>
          <OperationsSignalRProvider>
            <RouterProvider router={router} />
          </OperationsSignalRProvider>
        </AuthGate>
      </StaffAuthProvider>
    </QueryClientProvider>
  </StrictMode>,
);
