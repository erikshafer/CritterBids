import {
  createRootRoute,
  createRoute,
  createRouter,
} from "@tanstack/react-router";

import { AppShell } from "@/components/AppShell";
import { RouteNotFound } from "@/components/RouteNotFound";
import { BidActivity } from "@/operations/BidActivity";
import { LotBoard } from "@/operations/LotBoard";
import { Disputes, Escalations } from "@/operations/ObligationsQueues";
import { SessionsBoard } from "@/operations/SessionsBoard";
import { SettlementQueue } from "@/operations/SettlementQueue";

// Code-based route tree, same rationale as the bidder app (M8-S2): a handful of routes needs no
// router plugin and no generated route-tree file. One route per dashboard view — the six M8-S6
// data boards over /api/operations/* (the S5 placeholders are gone).
//
// basepath "/ops": must agree with the Vite `base: "/ops/"` (ADR 025 base-path discipline —
// re-stated in this slice exactly as the ADR's mitigation prescribes).
const rootRoute = createRootRoute({
  component: AppShell,
  notFoundComponent: RouteNotFound,
});

const lotBoardRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: LotBoard,
});

const bidActivityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/bid-activity",
  component: BidActivity,
});

const settlementQueueRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/settlement-queue",
  component: SettlementQueue,
});

const escalationsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/escalations",
  component: Escalations,
});

const disputesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/disputes",
  component: Disputes,
});

const sessionsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/sessions",
  component: SessionsBoard,
});

const routeTree = rootRoute.addChildren([
  lotBoardRoute,
  bidActivityRoute,
  settlementQueueRoute,
  escalationsRoute,
  disputesRoute,
  sessionsRoute,
]);

export const router = createRouter({
  routeTree,
  basepath: "/ops",
  defaultNotFoundComponent: RouteNotFound,
});

// Register the router instance for project-wide type inference (typed Link `to`, params, etc.).
declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
