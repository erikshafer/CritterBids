import { useSession } from "@/session/SessionContext";
import { useSellerListings } from "@/listings/queries";
import type { SellerListingSummary } from "@/listings/schema";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState, ErrorState } from "@/components/States";
import { formatUsd, sellerStatusVariant } from "@/lib/format";

export function ListingsPage() {
  const { participantId } = useSession();
  const { data, isPending, isError, error, refetch } = useSellerListings(
    participantId ?? "",
  );

  if (isPending) {
    return <ListingsSkeleton />;
  }

  if (isError) {
    return (
      <section>
        <h1 className="mb-4 text-xl font-semibold tracking-tight">
          My Listings
        </h1>
        <ErrorState
          message={error.message}
          onRetry={() => void refetch()}
        />
      </section>
    );
  }

  return (
    <section>
      <h1 className="mb-4 text-xl font-semibold tracking-tight">
        My Listings
      </h1>
      {data.length === 0 ? (
        <EmptyState message="You haven't created any listings yet." />
      ) : (
        <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {data.map((listing) => (
            <li key={listing.id}>
              <ListingCard listing={listing} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function ListingCard({ listing }: { listing: SellerListingSummary }) {
  const created = new Date(listing.createdAt);

  return (
    <Card className="h-full">
      <CardHeader>
        <div className="flex items-start justify-between gap-2">
          <CardTitle className="text-base">{listing.title}</CardTitle>
          <Badge variant={sellerStatusVariant(listing.status)}>
            {listing.status}
          </Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-2">
        <div className="flex items-end justify-between">
          <div>
            <p className="text-muted-foreground text-xs">Starting bid</p>
            <p className="text-lg font-semibold">
              {formatUsd(listing.startingBid)}
            </p>
          </div>
          <p className="text-muted-foreground text-xs">{listing.format}</p>
        </div>
        {listing.reservePrice != null && (
          <p className="text-muted-foreground text-xs">
            Reserve: {formatUsd(listing.reservePrice)}
          </p>
        )}
        {listing.buyItNowPrice != null && (
          <p className="text-muted-foreground text-xs">
            BIN: {formatUsd(listing.buyItNowPrice)}
          </p>
        )}
        <p className="text-muted-foreground text-xs">
          Created{" "}
          {created.toLocaleDateString("en-US", {
            month: "short",
            day: "numeric",
            year: "numeric",
          })}
        </p>
      </CardContent>
    </Card>
  );
}

function ListingsSkeleton() {
  return (
    <section>
      <Skeleton className="mb-4 h-7 w-36" />
      <ul className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: 3 }).map((_, index) => (
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
