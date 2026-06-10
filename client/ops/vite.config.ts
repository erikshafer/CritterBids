/// <reference types="vitest/config" />
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";

// ADR 025 — build + dev integration for the ops dashboard.
//   base "/ops/"        : the ops app is served at the host's /ops/ base path in production.
//                         The router's basepath ("/ops", src/router.tsx) must agree — a mismatch
//                         surfaces as broken asset URLs (ADR 025 "base-path discipline").
//   server.port 5174    : a port separate from the bidder's :5173 so both SPAs dev simultaneously.
//   plugins             : React + the Tailwind v4-native Vite plugin (ADR 013) + the PWA plugin.
//   server.proxy        : forward /api and /hub to the API host so the browser stays
//                         same-origin in dev — no CORS, no API-host change. ws:true carries the
//                         /hub/operations WebSocket upgrade (the connection's only request — the
//                         client skips negotiate; see src/signalr/SignalRProvider.tsx).
// CRITTERBIDS_API_URL overrides the target if the API host runs on a non-default port.
const API_HOST = process.env.CRITTERBIDS_API_URL ?? "http://localhost:5180";

export default defineConfig({
  base: "/ops/",
  plugins: [
    react(),
    tailwindcss(),
    // PWA "from day one" (ADR 013), mirroring the bidder app's M8-S2 wiring: app-shell precache
    // only — offline *data* scope stays an ADR-013-deferred question.
    VitePWA({
      registerType: "autoUpdate",
      injectRegister: "auto",
      manifest: {
        name: "CritterBids Operations",
        short_name: "CB Ops",
        description:
          "CritterBids operations dashboard — staff lot board, settlement queue, and dispute pipeline.",
        theme_color: "#0f172a",
        background_color: "#0f172a",
        display: "standalone",
        start_url: "/ops/",
        icons: [
          {
            src: "/ops/critterbids.svg",
            sizes: "any",
            type: "image/svg+xml",
            purpose: "any maskable",
          },
        ],
      },
      workbox: {
        globPatterns: ["**/*.{js,css,html,svg,woff2}"],
      },
    }),
  ],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  server: {
    port: 5174,
    proxy: {
      "/api": { target: API_HOST, changeOrigin: true },
      "/hub": { target: API_HOST, changeOrigin: true, ws: true },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
  },
});
