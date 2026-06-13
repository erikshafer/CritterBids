import { getRouteApi, Link, useSearch } from "@tanstack/react-router";

import { ListingNotFoundError, useListing } from "@/listings/queries";
import { useWatchListing } from "@/signalr/hooks";
import { LiveAuction } from "@/listings/LiveAuction";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState, ErrorState } from "@/components/States";
import { formatUsd, auctionStatusVariant } from "@/lib/format";

const route = getRouteApi("/listings/$id");

export function ListingDetailPage() {
  const { id } = route.useParams();
  const search = useSearch({ from: "/listings/$id" });
  const reservePrice =
    "reserve" in search && typeof search.reserve === "number"
      ? search.reserve
      : null;

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
          <EmptyState message="This listing doesn't exist or is no longer available." />
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
            <Badge variant={auctionStatusVariant(listing.status)}>
              {listing.status}
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          <dl className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
            <Field label="Format" value={listing.format} />
            <Field label="Starting bid" value={formatUsd(listing.startingBid)} />
            {listing.buyItNow != null && (
              <Field label="Buy it now" value={formatUsd(listing.buyItNow)} />
            )}
            {reservePrice != null && (
              <Field
                label="Your reserve"
                value={formatUsd(reservePrice)}
              />
            )}
          </dl>
        </CardContent>
      </Card>

      <LiveAuction listing={listing} reservePrice={reservePrice} />
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
    <Link
      to="/listings"
      className="text-muted-foreground hover:text-foreground text-sm"
    >
      ← Back to my listings
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
        <CardContent>
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
            <Skeleton className="h-10" />
            <Skeleton className="h-10" />
            <Skeleton className="h-10" />
          </div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <Skeleton className="h-5 w-32" />
        </CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-12 w-40" />
          <Skeleton className="h-16" />
        </CardContent>
      </Card>
    </div>
  );
}
