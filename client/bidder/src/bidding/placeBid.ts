import {
  placeBidResponseSchema,
  problemDetailsSchema,
  type PlaceBidRequest,
  type PlaceBidResponse,
} from "@/bidding/schema";

/**
 * A rejected (or failed) bid. Carries the machine-readable `reason` from the endpoint's
 * ProblemDetails (null on a 5xx with no reason), the HTTP `status`, and the server's authoritative
 * `currentHighBid` for reconciling the rolled-back optimistic update. `message` is the human-facing
 * copy surfaced to the bidder.
 */
export class BidRejectedError extends Error {
  constructor(
    readonly reason: string | null,
    readonly status: number,
    readonly currentHighBid: number | null,
    message: string,
  ) {
    super(message);
    this.name = "BidRejectedError";
  }
}

// Human-facing copy per machine-readable reason code (the codes PlaceBidHandler emits + the
// endpoint's UnknownBidder precondition). Any non-2xx without a recognized reason — including a 5xx
// from a DcbConcurrencyException — maps to the generic "something changed, try again" path
// (In-scope #5b): the bidder always gets a rollback and a retry prompt, never a silent failure.
function friendlyMessage(reason: string | null, status: number): string {
  switch (reason) {
    case "BelowMinimumBid":
      return "Your bid is below the current minimum. Try a higher amount.";
    case "ExceedsCreditCeiling":
      return "That bid exceeds your available credit.";
    case "ListingClosed":
    case "ListingNotOpen":
      return "This listing is no longer open for bidding.";
    case "SellerCannotBid":
      return "You can’t bid on your own listing.";
    case "UnknownBidder":
      return "Start a session before bidding.";
    default:
      return status >= 500
        ? "Something changed while placing your bid. Please try again."
        : "Your bid was not accepted. Please try again.";
  }
}

/**
 * Place a bid against `POST /api/auctions/bids` (M8-S3a). Sends a JSON body with
 * `Content-Type: application/json` even though every field is present — the binder 400s a bodyless
 * POST (LESSONS §A #1). On 200 returns the parsed {@link PlaceBidResponse}; on any non-2xx throws a
 * {@link BidRejectedError} carrying the parsed `reason`/`currentHighBid` so the caller can roll back.
 *
 * Idempotency (In-scope #5a): this is a single fire-and-await. It does NOT auto-retry a dropped
 * response — the server generates the BidId, so a blind retry could double-bid. The mutation that
 * wraps this disables retries; a lost response surfaces as a rollback and the bidder re-submits.
 */
export async function placeBid(
  request: PlaceBidRequest,
): Promise<PlaceBidResponse> {
  const response = await fetch("/api/auctions/bids", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "application/json",
    },
    body: JSON.stringify(request),
  });

  if (response.ok) {
    return placeBidResponseSchema.parse(await response.json());
  }

  const body: unknown = await response.json().catch(() => null);
  const problem = problemDetailsSchema.safeParse(body);
  const reason = problem.success ? (problem.data.reason ?? null) : null;
  const currentHighBid = problem.success
    ? (problem.data.currentHighBid ?? null)
    : null;

  throw new BidRejectedError(
    reason,
    response.status,
    currentHighBid,
    friendlyMessage(reason, response.status),
  );
}
