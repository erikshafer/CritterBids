import { useListingTitle } from "@/operations/queries";
import { shortId } from "@/operations/format";

// The render-time Title join cell. The operations records carry ListingId only (the lot board's
// own Title is nullable) — display titles resolve from GET /api/listings/{id}, deduplicated
// across rows by the shared ["listing", id] cache entry. While resolving, and for a 404'd
// listing (a stable answer, never retried), the shortened id keeps the row legible.

export function ListingTitle({ listingId }: { listingId: string }) {
  const { data: title, isPending } = useListingTitle(listingId);

  if (isPending || title == null) {
    return (
      <span className="text-muted-foreground font-mono" title={listingId}>
        {shortId(listingId)}
      </span>
    );
  }

  return <span title={listingId}>{title}</span>;
}
