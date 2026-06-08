import { getRouteApi, Link } from "@tanstack/react-router";

import { ListingNotFoundError, useListing } from "@/catalog/queries";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState, ErrorState } from "@/components/States";
import { formatUsd, statusVariant } from "@/lib/format";

// getRouteApi reads the typed params without importing the route object (avoids the
// router ⇄ page import cycle). The id segment is validated by the router's path pattern.
const route = getRouteApi("/listing/$id");

// Narrative 001 Moment 2 / listing detail. Read-only over `GET /api/listings/{id}`, which 404s on
// an unknown id — surfaced here as a distinct not-found state (queries.ts throws ListingNotFoundError).
// The detail binds to CatalogListingView, the SAME shape the list returns; there is no separate
// ListingDetailView in the lived backend (M8-S2 finding).
export function ListingDetailPage() {
  const { id } = route.useParams();
  const { data: listing, isPending, isError, error, refetch } = useListing(id);

  if (isPending) {
    return <DetailSkeleton />;
  }

  if (isError) {
    if (error instanceof ListingNotFoundError) {
      return (
        <div className="space-y-4">
          <BackLink />
          <EmptyState message="This listing doesn’t exist or is no longer available." />
        </div>
      );
    }
    return (
      <div className="space-y-4">
        <BackLink />
        <ErrorState message={error.message} onRetry={() => void refetch()} />
      </div>
    );
  }

  const price = listing.currentHighBid ?? listing.startingBid;
  const priceLabel = listing.currentHighBid != null ? "Current bid" : "Starting bid";

  return (
    <div className="space-y-4">
      <BackLink />
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between gap-3">
            <CardTitle className="text-2xl">{listing.title}</CardTitle>
            <Badge variant={statusVariant(listing.status)}>{listing.status}</Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-6">
          <div>
            <p className="text-muted-foreground text-xs">{priceLabel}</p>
            <p className="text-3xl font-semibold">{formatUsd(price)}</p>
          </div>
          <dl className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
            <Field label="Format" value={listing.format} />
            <Field label="Starting bid" value={formatUsd(listing.startingBid)} />
            {listing.buyItNow != null && (
              <Field label="Buy it now" value={formatUsd(listing.buyItNow)} />
            )}
            <Field label="Bids" value={String(listing.bidCount)} />
            {listing.hammerPrice != null && (
              <Field label="Hammer price" value={formatUsd(listing.hammerPrice)} />
            )}
          </dl>
          <p className="text-muted-foreground text-xs">
            Live bidding arrives in a later release — this is the read-only listing view.
          </p>
        </CardContent>
      </Card>
    </div>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-muted-foreground text-xs">{label}</dt>
      <dd className="mt-0.5 font-medium">{value}</dd>
    </div>
  );
}

function BackLink() {
  return (
    <Link to="/" className="text-muted-foreground hover:text-foreground text-sm">
      ← Back to catalog
    </Link>
  );
}

function DetailSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-5 w-32" />
      <Card>
        <CardHeader>
          <Skeleton className="h-8 w-2/3" />
        </CardHeader>
        <CardContent className="space-y-6">
          <Skeleton className="h-10 w-40" />
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
            <Skeleton className="h-10" />
            <Skeleton className="h-10" />
            <Skeleton className="h-10" />
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
