# Integrated-Host Flash Bidding — Manual-Test Findings (2026-06-09)

Findings from the first end-to-end **manual** run of the Flash bidding journey against the live
Aspire AppHost (Postgres + RabbitMQ + the API host), driving the bidder SPA. These bugs are invisible
to the existing unit/integration suites because those exercise each BC against an **isolated** store
with hand-seeded, pre-tagged events; the defects only manifest where the BCs meet in one running
process. Surfaced while validating M8-S3b (bidder live bidding).

---

## How to reproduce (the seed procedure)

A dev-only seed endpoint drives a single Flash listing all the way to `Open` over the in-process
Wolverine bus, because the seller-submit and operator-attach commands are bus-only (no HTTP) and the
staff session endpoints 401 in dev (no token configured). It lives at
`src/CritterBids.Api/Dev/DemoSeedEndpoint.cs`, gated to `IsDevelopment()`.

```bash
# 1. Run the stack
dotnet run --project src/CritterBids.AppHost --launch-profile http   # API on :5180, dashboard :15237
cd client/bidder && npm run dev                                       # bidder SPA on :5173

# 2. Seed an Open Flash listing (publish -> session -> attach -> start, with cross-BC polling)
curl -s -X POST http://localhost:5180/api/dev/seed-flash -H "Content-Type: application/json" -d '{}'
#   -> { "listingId": "...", "sessionId": "...", "detailPath": "/listing/...", ... }

# 3. (optional) place a bid as a fresh participant
bidder=$(curl -s -D - -X POST http://localhost:5180/api/participants/session \
  -H "Content-Type: application/json" -d '{}' | grep -i '^location:' | awk -F/ '{print $NF}' | tr -d '\r')
curl -s -X POST http://localhost:5180/api/auctions/bids -H "Content-Type: application/json" \
  -d "{\"listingId\":\"<lid>\",\"bidderId\":\"$bidder\",\"amount\":30}"
```

DB inspection (Aspire generates a random pw; one DB `postgres`, all schemas inside; events in the
shared `public.mt_events`):

```bash
pg=$(docker ps --format '{{.Names}}' | grep -i postgres | head -1)
pw=$(docker exec "$pg" printenv POSTGRES_PASSWORD)
docker exec -e PGPASSWORD="$pw" "$pg" psql -U postgres -d postgres \
  -c "select version,type from public.mt_events where stream_id='<lid>' order by version;"
```

---

## Bug #1 — Flash listings never reach `Open` (FIXED)

**Symptom.** A published Flash listing, attached to a started session, stays `Status: "Published"`;
`bidding_opened` count in the whole store is **0**. Bidding is impossible.

**Root cause.** Selling's `SellerListing` aggregate and Auctions' `Listing` aggregate **share the same
`listingId` stream** in the shared Marten event store (ADR 009). By open time the stream already holds
Selling's `draft/submitted/approved/published` events, so:

1. `SessionStartedHandler` (Flash) and `ListingPublishedHandler` (Timed) guarded idempotency with
   `FetchStreamStateAsync(listingId)` → *skip if the stream exists*. It **always** exists → the
   `BiddingOpened` append was skipped forever.
2. They wrote `BiddingOpened` via an **untagged** `StartStream<Listing>`. The bid DCB
   (`PlaceBidHandler`) reads **by tag** (`EventTagQuery.For(new ListingStreamId(id))` over
   `BidConsistencyState`), so an untagged `BiddingOpened` is invisible to the boundary — every bid
   would reject `ListingNotOpen` even if the event were written.

Tests missed it because Auctions fixtures seed `BiddingOpened` on an isolated stream and tag it by hand
(`BuyNowDispatchTests.SeedOpenListing`: `StartStream<Listing>` + `.AddTag(new ListingStreamId(id))`).

**Fix.** New `OpenListingForBidding.AppendIfNotOpenAsync` (mirrors `PlaceBidHandler`): reads the DCB
boundary (`FetchForWritingByTags<BidConsistencyState>`), skips if already open (tag-based idempotency
+ `AssertDcbConsistency` guards a concurrent open), else appends a **tagged** `BiddingOpened` via
`BuildEvent` + `AddTag(ListingStreamId)` + `Append`. Both open-handlers call it.

**Verified end-to-end:** listing reaches `Open`; the event store shows `bidding_opened` then an
accepted `bid_placed` + `buy_it_now_option_removed`; the bid returns 200 through the live DCB. The
cross-aggregate tagged append works under `UseMandatoryStreamTypeDeclaration = true` (the feared risk
did not bite).

---

## Bug #2 — HTTP-placed bids don't forward to consumers (OPEN — needs dedicated work)

**Symptom.** An accepted `bid_placed` persists, but the Listings read model
(`CatalogListingView.CurrentHighBid`) and the Relay `BiddingHub` never update — there are **zero**
`BidPlaced` envelopes (outgoing, incoming, or dead-letter). So the bidder app shows a bid optimistically
then **reverts** it on the `onSettled` re-query (the read model is stale). This is exactly the path the
M8-S3a retro flagged as unverified ("BiddingHub observability of an HTTP-placed bid... not tested").

**Root cause (diagnosed).** `UseFastEventForwarding` pushes Marten-appended events to the Wolverine
outbox **on `SaveChangesAsync()` of an outbox-enrolled session**. Events appended in a **routed /
cascaded** message handler forward correctly — that is how `SessionStartedHandler`'s `BiddingOpened`
reaches Listings. Events appended in the **synchronous, HTTP-origin** `PlaceBid` DCB write do **not**
forward, regardless of the commit/publish mechanism.

**Approaches tried (all produced 0 `BidPlaced` envelopes):**

| # | Approach | Result |
|---|---|---|
| 1 | Endpoint calls `PlaceBidHandler.Execute` directly (shipped M8-S3a) | no forward |
| 2 | Endpoint returns `(IResult, OutgoingMessages)` with the accepted events | no forward |
| 3 | Endpoint injects `IMartenOutbox`, `PublishAsync` each event | no forward |
| 4 | Endpoint dispatches `bus.InvokeAsync<BidOutcome>(command)` (HandleAsync returns the outcome) | no forward |
| 5 | Explicit `await session.SaveChangesAsync()` in the endpoint | no forward |

Even #4 — a genuine message-handler context — did not forward, which points at synchronous/`InvokeAsync`
delivery not enrolling the DCB session in the forwarding outbox, vs. routed delivery which does.

**The tension.** A *synchronous* accept/reject response is required by both the M8-S3a HTTP contract and
the M8-S3b optimistic-reconcile-against-200 UX. Routed (fire-and-forget) delivery is what forwards. The
two are in conflict in this DCB + HTTP setup.

**Recommended directions (a dedicated slice, possibly with JasperFx input — cf. the ADR-010 dual-store
consult):**

- **(a) Async bid:** endpoint returns `202 Accepted`, the bid is processed via routed delivery
  (forwarding works), and the outcome is pushed over `BiddingHub`. Changes the M8-S3a contract and the
  M8-S3b reconcile model (optimistic-until-push instead of optimistic-until-200).
- **(b) Enroll the synchronous session in forwarding:** find the Wolverine configuration / session
  wiring that makes a synchronous DCB write's `SaveChangesAsync` fire `UseFastEventForwarding` (the
  crux unknown — worth a JasperFx question).
- **(c) Explicit cross-BC publish that actually flushes** from the endpoint (approaches #2/#3 *should*
  have worked per the docs but did not — understand why before relying on it).

All current experiments were reverted; the bid endpoint + `PlaceBidHandler` are back to shipped M8-S3a.

---

## Bug #3 — `AuctionClosingSaga` start not idempotent under redelivery (MINOR — noted)

`BiddingOpened` dead-letters twice with `JasperFx.DocumentAlreadyExistsException: AuctionClosingSaga
<listingId>` — the saga-start handler creates the saga on first delivery, then a redelivery re-attempts
creation. The close is still scheduled (functionally OK), but the dead-letters are noise. Likely a
`StartAuctionClosingSagaHandler` at-least-once-idempotency gap; separate from the bidding path. Not
investigated further.

---

## What works vs. what's blocked (for the M8-S3b demo)

- **Works:** anonymous session, catalog, listing detail, the seeded listing reaching `Open`, the live
  `BiddingHub` connection, bid **acceptance** (HTTP 200 through the real DCB), and the M8-S3b optimistic
  update reflecting the bidder's own bid from the 200 response.
- **Blocked by Bug #2:** cross-client live propagation — others' bids in the feed, the read-model
  refresh on `onSettled` re-query (which currently reverts the optimistic value), outbid/extended/gavel
  driven by push → re-query.
