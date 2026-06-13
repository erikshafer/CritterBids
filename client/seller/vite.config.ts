/// <reference types="vitest/config" />
import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";

const API_HOST = process.env.CRITTERBIDS_API_URL ?? "http://localhost:5180";

export default defineConfig({
  base: "/seller/",
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: "autoUpdate",
      injectRegister: "auto",
      manifest: {
        name: "CritterBids Seller Console",
        short_name: "CB Seller",
        description:
          "CritterBids seller console — manage listings, observe auctions, and track obligations.",
        theme_color: "#0f172a",
        background_color: "#0f172a",
        display: "standalone",
        start_url: "/seller/",
        icons: [
          {
            src: "/seller/critterbids.svg",
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
    port: 5175,
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
