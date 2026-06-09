import { useState, type FormEvent } from "react";

import type { CatalogListing } from "@/catalog/schema";
import { usePlaceBid } from "@/bidding/usePlaceBid";
import { BidRejectedError } from "@/bidding/placeBid";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { formatUsd } from "@/lib/format";

// The MVP bid-increment scale ($1 below $100, $5 at $100+) — mirrors the Auctions policy so the
// prefilled amount is a plausible next bid. The server still enforces the real minimum; this is a
// suggestion, not a gate.
function nextMinimumBid(listing: CatalogListing): number {
  const base = listing.currentHighBid ?? listing.startingBid;
  if (listing.currentHighBid == null) return base; // first bid may equal the starting bid
  return base >= 100 ? base + 5 : base + 1;
}

// Narrative 001 Moment 4 — the PlaceBidSheet, deferred from the narrative to this slice. An amount
// field + submit over `POST /api/auctions/bids` with optimistic update / rollback (usePlaceBid).
export function BidPanel({
  listing,
  participantId,
}: {
  listing: CatalogListing;
  participantId: string | null;
}) {
  const mutation = usePlaceBid(listing.id, participantId);
  const [amount, setAmount] = useState<string>(() =>
    String(nextMinimumBid(listing)),
  );

  const sessionReady = participantId !== null;
  const numericAmount = Number(amount);
  const amountValid = Number.isFinite(numericAmount) && numericAmount > 0;
  const canSubmit =
    sessionReady && amountValid && !mutation.isPending;

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSubmit) return;
    mutation.mutate(numericAmount, {
      // On acceptance, advance the field to the next plausible bid so a re-bid is one tap.
      onSuccess: (response) =>
        setAmount(
          String(response.currentHighBid >= 100 ? response.currentHighBid + 5 : response.currentHighBid + 1),
        ),
    });
  }

  const rejection =
    mutation.error instanceof BidRejectedError ? mutation.error : null;

  return (
    <form onSubmit={handleSubmit} className="space-y-2" aria-label="Place a bid">
      <div className="flex items-end gap-2">
        <div className="flex-1">
          <label htmlFor="bid-amount" className="text-muted-foreground text-xs">
            Your bid (USD)
          </label>
          <Input
            id="bid-amount"
            type="number"
            inputMode="decimal"
            min="0"
            step="1"
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            disabled={!sessionReady || mutation.isPending}
          />
        </div>
        <Button type="submit" disabled={!canSubmit}>
          {mutation.isPending ? "Placing…" : "Place bid"}
        </Button>
      </div>

      {!sessionReady && (
        <p className="text-muted-foreground text-xs" role="status">
          Starting your session… bidding will be available shortly.
        </p>
      )}

      {mutation.isError && (
        <p className="text-destructive text-xs" role="alert">
          {mutation.error.message}
          {rejection?.currentHighBid != null && (
            <> The current high bid is {formatUsd(rejection.currentHighBid)}.</>
          )}
        </p>
      )}

      {mutation.isSuccess && !mutation.isPending && (
        <p className="text-xs text-green-600" role="status">
          Bid accepted at {formatUsd(mutation.data.amount)}.
          {mutation.data.reserveMet && <> Reserve met.</>}
        </p>
      )}
    </form>
  );
}
