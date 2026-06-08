import {
  createRootRoute,
  createRoute,
  createRouter,
} from "@tanstack/react-router";

import { AppShell } from "@/components/AppShell";
import { RouteNotFound } from "@/components/RouteNotFound";
import { CatalogPage } from "@/catalog/CatalogPage";
import { ListingDetailPage } from "@/catalog/ListingDetailPage";

// Code-based route tree (M8-S2). TanStack Router was chosen as ADR 013's resolved routing
// Deferred Question — fully type-safe routes + search-params-as-state, same lineage as TanStack
// Query. With a handful of routes, code-based composition needs no router plugin and emits no
// generated route-tree file, keeping the slice reviewable; a later slice can migrate to
// file-based routing if the route count grows.
const rootRoute = createRootRoute({
  component: AppShell,
  notFoundComponent: RouteNotFound,
});

const catalogRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: CatalogPage,
});

const listingRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/listing/$id",
  component: ListingDetailPage,
});

const routeTree = rootRoute.addChildren([catalogRoute, listingRoute]);

export const router = createRouter({
  routeTree,
  defaultNotFoundComponent: RouteNotFound,
});

// Register the router instance for project-wide type inference (typed Link `to`, params, etc.).
declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
