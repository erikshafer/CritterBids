import { Link } from "@tanstack/react-router";

// Root-level notFoundComponent — any unmatched path lands here (TanStack Router).
export function RouteNotFound() {
  return (
    <div className="py-16 text-center">
      <p className="text-muted-foreground text-sm">That page doesn’t exist.</p>
      <Link
        to="/"
        className="text-foreground mt-4 inline-block text-sm font-medium underline-offset-4 hover:underline"
      >
        Go to the catalog
      </Link>
    </div>
  );
}
