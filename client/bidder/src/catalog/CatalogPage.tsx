import { Link } from "@tanstack/react-router";

import { useCatalog } from "@/catalog/queries";
import type { CatalogListing } from "@/catalog/schema";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState, ErrorState } from "@/components/States";
import { formatUsd, statusVariant } from "@/lib/format";

// Narrative 001 Moment 2 / catalog. Read-only over the [AllowAnonymous] `GET /api/listings`
// (CatalogEndpoints.cs) via TanStack Query; the response is Zod-validated at the boundary
// (queries.ts → schema.ts). Empty catalog renders an empty state, never an error (the endpoint
// returns [] on empty). Data is authoritative-on-fetch — no hub push drives this query (that
// re-query bridge is ADR 014 / M8-S3).
export function CatalogPage() {
  const { data, isPending, isError, error, refetch } = useCatalog();

  if (isPending) {
    return <CatalogSkeleton />;
  }

  if (isError) {
    return <ErrorState message={error.message} onRetry={() => void refetch()} />;
  }

  if (data.length === 0) {
    return <EmptyState message="No listings are published yet. Check back soon." />;
  }

  return (
    <section>
      <h1 className="mb-4 text-xl font-semibold tracking-tight">Catalog</h1>
      <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {data.map((listing) => (
          <li key={listing.id}>
            <ListingCard listing={listing} />
          </li>
        ))}
      </ul>
    </section>
  );
}

function ListingCard({ listing }: { listing: CatalogListing }) {
  const price = listing.currentHighBid ?? listing.startingBid;
  const priceLabel = listing.currentHighBid != null ? "Current bid" : "Starting bid";

  return (
    <Link
      to="/listing/$id"
      params={{ id: listing.id }}
      className="focus-visible:ring-ring block rounded-xl focus-visible:ring-2 focus-visible:outline-none"
    >
      <Card className="hover:border-border h-full transition-colors">
        <CardHeader>
          <div className="flex items-start justify-between gap-2">
            <CardTitle className="text-base">{listing.title}</CardTitle>
            <Badge variant={statusVariant(listing.status)}>{listing.status}</Badge>
          </div>
        </CardHeader>
        <CardContent className="flex items-end justify-between">
          <div>
            <p className="text-muted-foreground text-xs">{priceLabel}</p>
            <p className="text-lg font-semibold">{formatUsd(price)}</p>
          </div>
          <p className="text-muted-foreground text-xs">
            {listing.bidCount} {listing.bidCount === 1 ? "bid" : "bids"} · {listing.format}
          </p>
        </CardContent>
      </Card>
    </Link>
  );
}

function CatalogSkeleton() {
  return (
    <section>
      <Skeleton className="mb-4 h-7 w-32" />
      <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 6 }).map((_, index) => (
          <li key={index}>
            <Card className="h-full">
              <CardHeader>
                <Skeleton className="h-5 w-3/4" />
              </CardHeader>
              <CardContent className="space-y-2">
                <Skeleton className="h-4 w-20" />
                <Skeleton className="h-6 w-24" />
              </CardContent>
            </Card>
          </li>
        ))}
      </ul>
    </section>
  );
}
