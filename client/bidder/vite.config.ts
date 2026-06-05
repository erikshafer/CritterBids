import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// ADR 025 — build + dev integration.
//   base "/"            : the bidder app is served at the host root in production.
//   plugins             : React + the Tailwind v4-native Vite plugin (ADR 013).
//   server.proxy        : forward /api and /hub to the API host so the browser stays
//                         same-origin in dev — no CORS, no API-host change. ws:true carries
//                         the SignalR negotiate POST + WebSocket upgrade for /hub/bidding.
// CRITTERBIDS_API_URL overrides the target if the API host runs on a non-default port.
const API_HOST = process.env.CRITTERBIDS_API_URL ?? "http://localhost:5180";

export default defineConfig({
  base: "/",
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      "/api": { target: API_HOST, changeOrigin: true },
      "/hub": { target: API_HOST, changeOrigin: true, ws: true },
    },
  },
});
