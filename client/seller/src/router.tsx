import {
  createRootRoute,
  createRoute,
  createRouter,
} from "@tanstack/react-router";

import { AppShell } from "@/components/AppShell";
import { RouteNotFound } from "@/components/RouteNotFound";
import { HomePage } from "@/pages/HomePage";
import { ListingsPage } from "@/listings/ListingsPage";
import { CreateListingPage } from "@/listings/CreateListingPage";

const rootRoute = createRootRoute({
  component: AppShell,
  notFoundComponent: RouteNotFound,
});

const homeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: HomePage,
});

const listingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/listings",
  component: ListingsPage,
});

const createListingRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/listings/new",
  component: CreateListingPage,
});

const routeTree = rootRoute.addChildren([
  homeRoute,
  listingsRoute,
  createListingRoute,
]);

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
