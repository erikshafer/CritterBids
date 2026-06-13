import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";

import "./index.css";
import { router } from "@/router";
import { SessionProvider } from "@/session/SessionContext";
import { SellerSignalRProvider } from "@/signalr/SignalRProvider";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <SellerSignalRProvider>
          <RouterProvider router={router} />
        </SellerSignalRProvider>
      </SessionProvider>
    </QueryClientProvider>
  </StrictMode>,
);
