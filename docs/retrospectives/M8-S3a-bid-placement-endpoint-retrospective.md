# M8-S3a: Bid Placement Endpoint - Retrospective

**Date:** 2026-06-08
**Milestone:** M8 - React Frontend SPAs
**Slice:** S3a - Bid Placement Endpoint (backend precursor)
**Agent:** @PSA (Auctions BC) with @QAE on the DCB / rejection-path coverage
**Prompt:** `docs/prompts/implementations/M8-S3a-bid-placement-endpoint.md`

## Baseline

- Build clean at session start: `dotnet build CritterBids.slnx` -> 0 errors / 0 warnings.
- Auctions suite: 65 tests green. Full solution: 280 backend tests green across 9 suites.
- `PlaceBid` had **no HTTP surface** - the DCB command (`PlaceBidHandler.HandleAsync`) was bus-/test-dispatch only and returned `void`, folding every outcome into either the listing's acceptance events or the `BidRejected` audit stream. The live-bidding *read* side (`BiddingHub`) was already complete.
- The Auctions-local `ParticipantCreditCeiling` projection (M4-S4) already existed, fed from `ParticipantSessionStarted` on the `auctions-participants-events` queue, and was already read at saga-start by `StartProxyBidManagerSagaHandler`.

## Items completed

| Item | Description |
|------|-------------|
| S3a.1 | `POST /api/auctions/bids` endpoint, `[AllowAnonymous]`, accept -> 200 body / reject -> ProblemDetails 4xx |
| S3a.2 | Result path over the existing DCB write - extracted `PlaceBidHandler.Execute` returning `BidOutcome`, shared by the bus handler and the endpoint |
| S3a.3 | Server-side credit-ceiling sourcing from the existing `ParticipantCreditCeiling` projection (no new read surface) |
| S3a.4 | Integration tests: acceptance, each rejection reason, inflated-ceiling bypass, unknown bidder, DCB-concurrency preservation |
| S3a.5 | `[AllowAnonymous]` posture per ADR-024 (bidder-facing stays anonymous) |

## S3a.1 / S3a.2: Endpoint + result path

**Why this approach.** The prompt offered two result-path shapes: (a) a dedicated endpoint handler that runs the decision + DCB write + returns an outcome, or (b) a refactor that has the decision return an outcome both paths consume. Chosen: **extract a shared `Execute` core**, keeping the bus handler's `void` signature.

```csharp
// bus handler - unchanged void signature, discards the outcome
public static async Task HandleAsync(PlaceBid command, IDocumentSession session, TimeProvider time)
    => await Execute(command, session, time);

// shared core - same FetchForWritingByTags + AssertDcbConsistency write + BidRejected audit
public static async Task<BidOutcome> Execute(PlaceBid command, IDocumentSession session, TimeProvider time)
```

Why not return `BidOutcome` directly from `HandleAsync` (option b's literal form): a non-`void` return from a Wolverine message handler is treated as a cascading message. The proxy-saga auto-bid path and the 16 existing `PlaceBid` bus/dispatch tests dispatch `PlaceBid` through the bus; changing the handler's return type risks an unrouted-cascade surprise. Keeping `HandleAsync` returning `Task` and delegating to `Execute` left every existing caller byte-for-byte unchanged (proven by the 65 prior tests staying green) while giving the HTTP path the outcome it needs.

| Metric | Before | After |
|--------|--------|-------|
| `PlaceBidHandler.HandleAsync` return | `Task` (void) | `Task` (void), delegates |
| Decision+write core | inline in `HandleAsync` | `Execute` -> `Task<BidOutcome>` |
| DCB write call sites | 1 (bus) | 1 (shared by bus + HTTP) |
| Endpoint return | n/a | `Task<IResult>` (200 / ProblemDetails) |

**Status mapping** (signed off pre-wiring): 400 for input-relative rejections (`BelowMinimumBid`, `ExceedsCreditCeiling`, `SellerCannotBid`); 409 Conflict for listing-state rejections (`ListingClosed`, `ListingNotOpen`); the machine-readable code rides `ProblemDetails.Extensions["reason"]`, with `currentHighBid` carried for the S3b rollback reconciliation. Acceptance returns 200 + the "Full" body shape (`bidId, listingId, bidderId, amount, bidCount, currentHighBid, reserveMet, extendedBidding{previousCloseAt,newCloseAt}?`).

## S3a.3: Server-side credit-ceiling sourcing

**The prompt's central Open Question / anticipated escalation dissolved against lived code.** The browser must not supply `CreditCeiling`; the endpoint reads the Auctions-local `ParticipantCreditCeiling` row via `session.LoadAsync<ParticipantCreditCeiling>(request.BidderId)` - the **same row** `StartProxyBidManagerSagaHandler` already reads. This is the thinnest possible source: a BC-local Marten document read, no cross-BC call, no new read API, no BC-boundary violation. The candidate "(c) it already lands via an integration event" from the prompt's Open Questions was lived: the M4-D4 duplicate-projection pattern had already put the ceiling in Auctions.

The `PlaceBidRequest` record has **no `CreditCeiling` field**, so a client cannot supply it at all; the inflated-ceiling test additionally proves an extra `creditCeiling` JSON property in the body is ignored and the server-sourced ceiling still rejects the bid.

**Unknown-bidder decision** (the one genuinely-new mapping, signed off): a missing `ParticipantCreditCeiling` row is an HTTP precondition failure **outside** the five domain rejection reasons - 404 `UnknownBidder`, **no DCB decision and no `BidRejected` audit** (there is no domain decision to audit when the bidder cannot be sourced). This keeps "no new domain capability" intact - it is not a sixth rejection reason.

## S3a.4: Tests

`UseFastEventForwarding` shaped the test design. Raw fixture-session writes (`GetDocumentSession()`) do not forward (the M3-S5 finding baked into the existing seed helpers), but the **endpoint's** Wolverine-managed session does - an accepted `BidPlaced` is forwarded in-process to `AuctionClosingSaga` + `ProxyBidDispatchHandler`. Consequences:

- The acceptance test starts the saga through the bus (mirroring `PlaceBidDispatchTests.SeedOpenListing`) and drains via a new `TrackedHttpCall` fixture helper (`Host.ExecuteAndWaitAsync(() => Host.Scenario(...))`, same shape as `SellingTestFixture.TrackedHttpCall`), so the forwarded fan-out cannot race the next test's cleanup.
- The DCB-concurrency test stays at the `Execute`/raw-session layer (two sessions fetch the same boundary version, both append, first commit wins, second throws `DcbConcurrencyException`) - proving the optimistic-concurrency guarantee survives the new path without orchestrating true HTTP concurrency.
- Reserve + extended-bidding outcome fields are asserted at the `Execute` layer with a pinned `FixedTimeProvider`, because the HTTP pipeline resolves `TimeProvider.System` and cannot be pinned per-test into the trigger window.

## Test results

| Phase | Auctions Tests | Result |
|-------|---------------|--------|
| Baseline | 65 | green |
| After S3a endpoint tests added (filter) | 9 | green |
| Full Auctions suite after refactor | 74 | green (65 prior + 9 new, no regressions) |
| Full solution | 289 | green (0 failures) |

Auctions test count: 65 -> 74 (+9). No prior Auctions test changed.

## Build state at session close

- `dotnet build CritterBids.slnx` -> 0 errors / 0 warnings (delta from baseline: 0).
- New HTTP endpoints in the Auctions BC: 1 (`POST /api/auctions/bids`).
- `PlaceBidHandler` DCB write call sites: 1 (`Execute`, shared by bus + HTTP).
- `CreditCeiling` fields on the HTTP request contract: 0 (sourced server-side).
- New domain events / rejection rules / increment-policy changes: 0.
- `client/` changes: 0. ADR-014: not authored (M8-S3b). BuyNow HTTP endpoint: 0. `BiddingHub` / `StaffToken` changes: 0.

## Key learnings

1. **Check the BC for an existing local projection before treating a cross-BC value as an escalation.** The prompt framed credit-ceiling sourcing as the likely first escalation; the M4-D4 duplicate-projection pattern had already solved it in-BC. The escalation budget was spent on reading lived code, not on a new slice.
2. **Keep a message handler's return type stable when adding a synchronous caller.** Delegating `HandleAsync` to a non-handler-named `Execute` (same trick as the existing `Decide`) lets an HTTP endpoint reuse the exact DCB write path without exposing the handler to cascading-return semantics.
3. **`UseFastEventForwarding` makes HTTP-acceptance tests forward in-process.** Any test whose endpoint commits acceptance events must either start the downstream saga through the bus and drain with a tracked HTTP call, or be pushed to the `Execute`/raw-session layer where forwarding does not fire.
4. **`AutoApplyTransactions` commits a `Task<IResult>` Wolverine.HTTP endpoint that injects `IDocumentSession`** - the acceptance test's persisted `BidPlaced` confirms it; no explicit `SaveChangesAsync` was needed (same mechanism the bus handler relies on).

## Findings against narrative

Narrative 001 Moment 4 (`docs/narratives/001-bidder-wins-flash-auction.md`), anchored per the prompt's `Narrative:` line.

- **`narrative-update` (resolved in this PR).** The narrative's `## Deferred` backlog and Moment 4's "Things deliberately not included" both deferred the `/api/auctions/bids` endpoint with the trigger "M6 frontend MVP, **when the `[AllowAnonymous]` posture lifts**." That trigger assumption was wrong: per ADR-024 bidder-facing endpoints stay anonymous, so the endpoint shipped at M8-S3a **as** `[AllowAnonymous]` without the posture changing. Both notes were amended to record the endpoint landed at M8-S3a (sourcing the ceiling server-side) and to scope the remaining **PlaceBidSheet UI** to M8-S3b.
- **`document-as-intentional`.** Moment 4's interaction text describes the internal `PlaceBid` command carrying `CreditCeiling` directly (the M3 transitional shape). That remains accurate for the internal command; the M8-S3a HTTP layer wraps it by sourcing the ceiling server-side before constructing the command. No code-update finding - the two are the same intent at different layers.

## Spec delta - landed?

Landed with the narrative divergence noted above. Per ADR 020, narrative 001 Moment 4 gained its **HTTP surface**: `POST /api/auctions/bids` now exposes the previously-internal `PlaceBid` DCB command to bidders, returning accept-vs-reject correctly (200 body / 400 / 409 / 404), sourcing the credit ceiling server-side from the `ParticipantCreditCeiling` projection, and preserving both the DCB `AssertDcbConsistency` guarantee and the `BidRejected` audit stream. The workshop-002 §1 `PlaceBid` scenarios gained **HTTP-level test coverage** (acceptance + each rejection reason) without any scenario amendment - the decision rules, rejection reasons, and `$1`/`$5` increment policy are unchanged, so `docs/workshops/002-scenarios.md` required no edit. No ADR was authored (ADR-014 is M8-S3b). The bid-placement journey's backend half landed; the frontend half (optimistic UI, live feed) is M8-S3b. The amendments are in `docs/narratives/001-bidder-wins-flash-auction.md` (Moment 4 deferral note + `## Deferred` backlog entry).

## Verification checklist

- [x] `POST /api/auctions/bids` exists, `[AllowAnonymous]`, binds `{ listingId, bidderId, amount }` with a JSON body
- [x] Acceptance returns 200 with the optimistic-update/rollback body (new high bid + bid count + extended-bidding outcome); `BidPlaced` is appended (observable downstream on `BiddingHub` via the existing Relay route)
- [x] Each rejection reason returns the correct 4xx ProblemDetails with a machine-readable `reason` (400: BelowMinimumBid, ExceedsCreditCeiling, SellerCannotBid; 409: ListingClosed, ListingNotOpen)
- [x] DCB `AssertDcbConsistency` guarantee and the `BidRejected` audit write are preserved (test-covered, no regression)
- [x] Credit ceiling sourced server-side; a client cannot supply an inflated ceiling to bypass `ExceedsCreditCeiling`
- [x] `BidId` is server-generated UUID v7 (client idempotency key deferred to S3b)
- [x] `dotnet build CritterBids.slnx` -> 0 errors / 0 warnings; `dotnet test` green; Auctions suite extended with the endpoint tests
- [x] No `client/` change, no ADR-014, no BuyNow endpoint, no auth/`StaffToken` change, no DCB-rule or increment-policy change, no `BiddingHub` change
- [x] This retrospective written with the `**Prompt:**` header and `## Spec delta - landed?` paragraph
- [x] No commit to `main`; work on branch `M8-S3a-bid-placement-endpoint`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M8-S3b (frontend live bidding + ADR-014)** binds to this slice's response contract: 200 `PlaceBidResponse` (`bidId, listingId, bidderId, amount, bidCount, currentHighBid, reserveMet, extendedBidding{previousCloseAt,newCloseAt}?`) and ProblemDetails 4xx with `extensions.reason` (+ `currentHighBid` on rejections). The request is `{ listingId, bidderId, amount }` where `bidderId` is the `ParticipantId` Guid from session start.
- **Idempotency** is deferred to S3b: `BidId` is server-generated per request, so a client that auto-retries a dropped request could double-bid. S3b owns the retry/rollback story and decides whether to add a client idempotency key.
- **Concurrent-conflict UX**: a genuine `DcbConcurrencyException` on simultaneous bids is not mapped to a graceful status on the HTTP path (it surfaces as a 5xx); the optimistic-concurrency guarantee (no lost update) holds regardless. A graceful 409 mapping was left out to avoid an untested branch; revisit if the demo's two-bidder bid war surfaces it.
- **`BiddingHub` observability** of an HTTP-placed bid is end-to-end (Relay consumes `BidPlaced` off `relay-auctions-events`); this slice tested the `BidPlaced` append (the prerequisite), not the live push - that is M8-S3b / M8-S7 e2e territory.
