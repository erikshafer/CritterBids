import {
  createRootRoute,
  createRoute,
  createRouter,
} from "@tanstack/react-router";

import { AppShell } from "@/components/AppShell";
import { PlaceholderView } from "@/components/PlaceholderView";
import { RouteNotFound } from "@/components/RouteNotFound";

// Code-based route tree, same rationale as the bidder app (M8-S2): a handful of routes needs no
// router plugin and no generated route-tree file. One route per M8-S6 dashboard view; each
// renders an S5 placeholder naming the staff endpoint that will back it.
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
  component: () => (
    <PlaceholderView
      title="Lot board"
      description="Current high bid + status per listing, live. Titles resolve at render time from /api/listings/{id} (the view carries ListingId only)."
      endpoint="GET /api/operations/lot-board"
    />
  ),
});

const bidActivityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/bid-activity",
  component: () => (
    <PlaceholderView
      title="Bid activity"
      description="Append-style live feed of every accepted bid, newest first."
      endpoint="GET /api/operations/bid-activity"
    />
  ),
});

const settlementQueueRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/settlement-queue",
  component: () => (
    <PlaceholderView
      title="Settlement queue"
      description="In-flight and failed settlements, with PaymentFailed rows flagged."
      endpoint="GET /api/operations/settlement-queue"
    />
  ),
});

const escalationsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/escalations",
  component: () => (
    <PlaceholderView
      title="Escalations"
      description="Obligations whose ship-by deadline lapsed (QueueState == Escalated), newest first."
      endpoint="GET /api/operations/obligations/escalations"
    />
  ),
});

const disputesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/disputes",
  component: () => (
    <PlaceholderView
      title="Disputes"
      description="Obligations with an open dispute (QueueState == Disputed) — narrative 008's operator queue."
      endpoint="GET /api/operations/obligations/disputes"
    />
  ),
});

const sessionsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/sessions",
  component: () => (
    <PlaceholderView
      title="Sessions & participants"
      description="The Flash-session lineup and participant-session activity boards."
      endpoint="GET /api/operations/sessions · GET /api/operations/participants"
    />
  ),
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
