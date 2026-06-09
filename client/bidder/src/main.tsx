import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";

import "./index.css";
import { router } from "@/router";
import { SessionProvider } from "@/session/SessionContext";

// One QueryClient for the app. Defaults are conservative for a live-auction read surface:
// data is treated as authoritative-on-fetch (no hub push drives queries this slice — that
// re-query bridge is ADR 014 / M8-S3), so we keep a short staleTime and let refetch-on-focus stand.
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
        <RouterProvider router={router} />
      </SessionProvider>
    </QueryClientProvider>
  </StrictMode>,
);
