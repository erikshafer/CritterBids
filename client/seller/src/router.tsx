import {
  createRootRoute,
  createRoute,
  createRouter,
} from "@tanstack/react-router";

import { AppShell } from "@/components/AppShell";
import { RouteNotFound } from "@/components/RouteNotFound";
import { HomePage } from "@/pages/HomePage";

const rootRoute = createRootRoute({
  component: AppShell,
  notFoundComponent: RouteNotFound,
});

const homeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: HomePage,
});

const routeTree = rootRoute.addChildren([homeRoute]);

export const router = createRouter({
  routeTree,
  basepath: "/seller",
  defaultNotFoundComponent: RouteNotFound,
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
