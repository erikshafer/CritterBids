import { getRouteApi, Link } from "@tanstack/react-router";

import { ListingNotFoundError, useListing } from "@/catalog/queries";
import { useWatchListing } from "@/signalr/hooks";
import { LiveBidding } from "@/bidding/LiveBidding";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState, ErrorState } from "@/components/States";
import { formatUsd, statusVariant } from "@/lib/format";

// getRouteApi reads the typed params without importing the route object (avoids the
// router ⇄ page import cycle). The id segment is validated by the router's path pattern.
const route = getRouteApi("/listing/$id");

// Narrative 001 Moments 2–7 / listing detail + live bidding. The static card binds to
// CatalogListingView over `GET /api/listings/{id}` (the SAME shape the list returns — no separate
// ListingDetailView in the lived backend, M8-S2 finding); the LiveBidding surface (M8-S3b) adds
// real-time bid placement + outbid/extended/gavel affordances over the BiddingHub.
export function ListingDetailPage() {
  const { id } = route.useParams();
  // Join this listing's BiddingHub group as soon as the route is known (ADR 026); the cache bridge
  // re-queries the line below on every relevant push.
  useWatchListing(id);
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
          <dl className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
            <Field label="Format" value={listing.format} />
            <Field label="Starting bid" value={formatUsd(listing.startingBid)} />
            {listing.buyItNow != null && (
              <Field label="Buy it now" value={formatUsd(listing.buyItNow)} />
            )}
          </dl>
        </CardContent>
      </Card>

      <LiveBidding listing={listing} />
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
