import { useEffect, useRef, useState } from "react";

import type { CatalogListing } from "@/catalog/schema";
import { useSession } from "@/session/SessionContext";
import { BidPanel } from "@/bidding/BidPanel";
import { LiveActivity } from "@/bidding/LiveActivity";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { formatUsd } from "@/lib/format";

const TERMINAL_STATUSES = ["Sold", "Passed", "Closed", "Settled", "Withdrawn"];

function formatCloseTime(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const date = new Date(iso);
  return Number.isNaN(date.getTime())
    ? null
    : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
}

// The live-bidding surface (Narrative 001 Moments 4–7). Every number it renders — current high bid,
// bid count, high bidder, close time, terminal outcome — comes from `listing`, the re-queried
// CatalogListingView. Hub pushes never land here as truth; they arrive as cache invalidations that
// refresh `listing`, and the affordances below are derived from how `listing` CHANGES between
// re-queries:
//   • outbid  — held participant dropped from high bidder to not-high while still Open
//   • extended — scheduledCloseAt moved later than the previously-observed value
//   • gavel    — status reached a terminal value
export function LiveBidding({ listing }: { listing: CatalogListing }) {
  const { participantId } = useSession();

  const amHighBidder =
    participantId !== null && listing.currentHighBidderId === participantId;
  const isOpen = listing.status === "Open" || listing.status === "Extended";
  const isTerminal = TERMINAL_STATUSES.includes(listing.status);

  // --- Derived: outbid -------------------------------------------------------
  const wasHighRef = useRef(false);
  const [outbid, setOutbid] = useState(false);
  useEffect(() => {
    if (participantId === null) return;
    if (
      wasHighRef.current &&
      !amHighBidder &&
      listing.currentHighBidderId != null &&
      isOpen
    ) {
      setOutbid(true);
    }
    if (amHighBidder) setOutbid(false);
    wasHighRef.current = amHighBidder;
  }, [amHighBidder, listing.currentHighBidderId, isOpen, participantId]);

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
  const priceLabel = listing.currentHighBid != null ? "Current bid" : "Starting bid";
  const closeTime = formatCloseTime(listing.scheduledCloseAt);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Live bidding</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Headline numbers — authoritative, from the re-queried view */}
        <div className="flex items-end justify-between gap-4">
          <div>
            <p className="text-muted-foreground text-xs">{priceLabel}</p>
            <p className="text-3xl font-semibold">{formatUsd(price)}</p>
            <p className="text-muted-foreground text-xs">
              {listing.bidCount} {listing.bidCount === 1 ? "bid" : "bids"}
              {amHighBidder && isOpen && (
                <span className="ml-2 font-medium text-green-600">
                  You’re the high bidder
                </span>
              )}
            </p>
          </div>
          {isOpen && closeTime && (
            <div className="text-right">
              <p className="text-muted-foreground text-xs">Closes</p>
              <p className="font-mono text-sm">{closeTime}</p>
            </div>
          )}
        </div>

        {/* Derived affordances */}
        {outbid && isOpen && (
          <div
            className="bg-destructive/10 text-destructive rounded-md px-3 py-2 text-xs font-medium"
            role="alert"
          >
            You’ve been outbid — the high bid is now {formatUsd(price)}. Bid again to reclaim it.
          </div>
        )}

        {extended && isOpen && (
          <div
            className="rounded-md bg-amber-100 px-3 py-2 text-xs font-medium text-amber-900"
            role="status"
          >
            Extended bidding — the close moved to {closeTime ?? "a later time"}.
          </div>
        )}

        {/* Bid placement (only while open) */}
        {isOpen ? (
          <BidPanel listing={listing} participantId={participantId} />
        ) : isTerminal ? (
          <TerminalOutcome listing={listing} amWinner={listing.winnerId === participantId} />
        ) : (
          <p className="text-muted-foreground text-xs">
            Bidding hasn’t opened on this listing yet.
          </p>
        )}

        {/* Transient live feed (useListen) */}
        <div className="border-border/60 border-t pt-3">
          <LiveActivity listingId={listing.id} />
        </div>
      </CardContent>
    </Card>
  );
}

function TerminalOutcome({
  listing,
  amWinner,
}: {
  listing: CatalogListing;
  amWinner: boolean;
}) {
  if (listing.status === "Sold" || listing.status === "Settled") {
    const hammer = listing.hammerPrice ?? listing.currentHighBid;
    return (
      <div
        className="rounded-md bg-green-100 px-3 py-3 text-center text-green-900"
        role="status"
      >
        <p className="text-sm font-semibold">
          {amWinner ? "You won!" : "Sold"}
          {hammer != null && <> — {formatUsd(hammer)}</>}
        </p>
        {amWinner && (
          <p className="mt-1 text-xs">
            The gavel fell in your favor. Settlement confirmation arrives next.
          </p>
        )}
      </div>
    );
  }

  // Passed / Withdrawn / Closed-without-sale
  return (
    <div
      className="bg-muted text-muted-foreground rounded-md px-3 py-3 text-center text-sm"
      role="status"
    >
      Bidding has closed. This listing did not sell.
    </div>
  );
}
