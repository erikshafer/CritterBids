import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";

import "./index.css";
import { router } from "@/router";
import { SessionProvider } from "@/session/SessionContext";
import { SignalRProvider } from "@/signalr/SignalRProvider";

// One QueryClient for the app. The BiddingHub cache bridge (ADR 026) now drives re-queries on push,
// so the cache is the live mirror of the read model — a short staleTime keeps reads fresh between
// pushes, and refetch-on-focus stands.
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
    },
  },
});

// Provider order: QueryClientProvider (the cache the bridge writes to) → SessionProvider (the held
// ParticipantId the SignalRProvider enrols into its bidder group) → SignalRProvider (the single
// BiddingHub connection) → RouterProvider (pages consume the live channel via the ADR 026 hooks).
createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <SignalRProvider>
          <RouterProvider router={router} />
        </SignalRProvider>
      </SessionProvider>
    </QueryClientProvider>
  </StrictMode>,
);
