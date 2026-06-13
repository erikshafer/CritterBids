import { useEffect, useRef, useState } from "react";
import type { CatalogListing } from "@critterbids/shared/schemas";

import { LiveActivity } from "@/listings/LiveActivity";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { formatUsd } from "@/lib/format";

const TERMINAL_STATUSES = ["Sold", "Passed", "Closed", "Settled", "Withdrawn"];

function formatCloseTime(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? null
    : date.toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      });
}

interface LiveAuctionProps {
  listing: CatalogListing;
  reservePrice: number | null;
}

export function LiveAuction({ listing, reservePrice }: LiveAuctionProps) {
  const isOpen = listing.status === "Open" || listing.status === "Extended";
  const isTerminal = TERMINAL_STATUSES.includes(listing.status);

  const reserveMet =
    reservePrice !== null &&
    listing.currentHighBid !== null &&
    listing.currentHighBid !== undefined &&
    listing.currentHighBid >= reservePrice;

  // --- Derived: extended bidding --------------------------------------------
  const prevCloseRef = useRef<string | null>(null);
  const [extended, setExtended] = useState(false);
  useEffect(() => {
    const close = listing.scheduledCloseAt ?? null;
    if (
      prevCloseRef.current !== null &&
      close !== null &&
      new Date(close).getTime() > new Date(prevCloseRef.current).getTime()
    ) {
      setExtended(true);
    }
    prevCloseRef.current = close;
  }, [listing.scheduledCloseAt]);

  const price = listing.currentHighBid ?? listing.startingBid;
  const priceLabel =
    listing.currentHighBid != null ? "Current bid" : "Starting bid";
  const closeTime = formatCloseTime(listing.scheduledCloseAt);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Live auction</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Headline numbers */}
        <div className="flex items-end justify-between gap-4">
          <div>
            <p className="text-muted-foreground text-xs">{priceLabel}</p>
            <p className="text-3xl font-semibold">{formatUsd(price)}</p>
            <p className="text-muted-foreground text-xs">
              {listing.bidCount} {listing.bidCount === 1 ? "bid" : "bids"}
            </p>
          </div>
          {isOpen && closeTime && (
            <div className="text-right">
              <p className="text-muted-foreground text-xs">Closes</p>
              <p className="font-mono text-sm">{closeTime}</p>
            </div>
          )}
        </div>

        {/* Reserve indicator — confidential to the seller */}
        {reservePrice !== null && isOpen && (
          <ReserveIndicator
            reservePrice={reservePrice}
            reserveMet={reserveMet}
          />
        )}

        {extended && isOpen && (
          <div
            className="rounded-md bg-amber-100 px-3 py-2 text-xs font-medium text-amber-900"
            role="status"
          >
            Extended bidding — the close moved to {closeTime ?? "a later time"}.
          </div>
        )}

        {isOpen ? (
          <p className="text-muted-foreground text-xs">
            You are observing this auction. Bids are placed by participants on
            the bidder app.
          </p>
        ) : isTerminal ? (
          <TerminalOutcome listing={listing} />
        ) : (
          <p className="text-muted-foreground text-xs">
            Bidding hasn't opened on this listing yet.
          </p>
        )}

        {/* Transient live feed */}
        <div className="border-border/60 border-t pt-3">
          <LiveActivity listingId={listing.id} />
        </div>
      </CardContent>
    </Card>
  );
}

function ReserveIndicator({
  reservePrice,
  reserveMet,
}: {
  reservePrice: number;
  reserveMet: boolean;
}) {
  if (reserveMet) {
    return (
      <div
        className="rounded-md bg-green-100 px-3 py-2 text-xs font-medium text-green-900"
        role="status"
      >
        Reserve met — your reserve of {formatUsd(reservePrice)} has been
        reached.
      </div>
    );
  }

  return (
    <div
      className="rounded-md bg-orange-100 px-3 py-2 text-xs font-medium text-orange-900"
      role="status"
    >
      Reserve not met — current bidding is below your reserve of{" "}
      {formatUsd(reservePrice)}.
    </div>
  );
}

function TerminalOutcome({ listing }: { listing: CatalogListing }) {
  if (listing.status === "Sold" || listing.status === "Settled") {
    const hammer = listing.hammerPrice ?? listing.currentHighBid;
    return (
      <div
        className="rounded-md bg-green-100 px-3 py-3 text-center text-green-900"
        role="status"
      >
        <p className="text-sm font-semibold">
          Sold{hammer != null && <> for {formatUsd(hammer)}</>}
        </p>
        <p className="mt-1 text-xs">
          {listing.bidCount} {listing.bidCount === 1 ? "bid" : "bids"}
          {listing.winnerId && (
            <> — winner: {listing.winnerId.slice(0, 8)}…</>
          )}
        </p>
      </div>
    );
  }

  if (listing.status === "Passed") {
    return (
      <div
        className="bg-muted text-muted-foreground rounded-md px-3 py-3 text-center text-sm"
        role="status"
      >
        <p className="font-medium">Listing passed</p>
        {listing.passedReason && (
          <p className="mt-1 text-xs">{listing.passedReason}</p>
        )}
      </div>
    );
  }

  return (
    <div
      className="bg-muted text-muted-foreground rounded-md px-3 py-3 text-center text-sm"
      role="status"
    >
      Bidding has closed on this listing.
    </div>
  );
}
