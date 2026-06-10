# M8-S3c: ADR 027 Per-BC Sticky Queue Bindings - Retrospective

**Date:** 2026-06-09
**Milestone:** M8 - React Frontend SPAs (backend-housekeeping slice)
**Slice:** S3c - ADR 027 sticky queue bindings
**Agent:** Claude Code
**Prompt:** `docs/prompts/implementations/M8-S3c-adr027-sticky-queue-bindings.md`

## Baseline

- `main` at PR #92; build clean; full suite 298 tests green (Contracts 1, Selling 36, Participants 6, Listings 20, Settlement 25, Api 46, Operations 38, Obligations 13, Relay 36, Auctions 77).
- Live baseline on fresh containers (debug-logged seed + one bid): every broker-published contract event executes once **per consuming queue** via the Separated fan-out — `BidPlaced` ×3 per consumer, `ParticipantSessionStarted` ×4, `SessionStarted` ×3. Zero `local://`-free deliveries.
- `wolverine_dead_letters` after one seed: **2** rows, both `BiddingOpened × JasperFx.DocumentAlreadyExistsException` (Bug #3 class — saga start racing its own fan-out duplicates).
- Routing probe (`GET /api/dev/routing-probe`): `BidPlaced`/`Event<BidPlaced>` resolve to exactly the 3 explicit rabbit routes, **zero local routes** — all in-process consumers were fan-out-fed.
- BIN and withdrawal journeys had never been observed live.

## Items completed

| Item | Description |
|------|-------------|
| S3c-1 | Consumer audit: every contract event × consumer classified broker-fed / forwarding-fed local / internal-command, from probe + debug-logged seed run + Wolverine 6.5.1 source (table below) |
| S3c-2 | Sticky bindings across six BCs — `[StickyHandler]` attribute, class-level default, METHOD-level for the three multi-queue classes |
| S3c-3 | `auctions-auctions-events` queue + routes + dispatcher/saga-start/session-fan-out bindings; **plus** `settlement-settlement-events` (audit discovery) |
| S3c-4 | Test-suite reconciliation — 6 compile-fix call sites, **zero assertion changes**; full suite green |
| S3c-5 | Live verification on fresh containers: seed → bid → outbid → close → settle, BIN, withdrawal, browser smoke; two masked races found and fixed |
| S3c-6 | Docs: milestone §7 S3c row + v0.4 history, findings-note Bug #3 → resolved, wolverine-sagas skill correction, STATUS refresh, this retro |

## S3c-1: Consumer audit

Evidence: live routing probe, one debug-logged seed+bid run on fresh containers (pre-change), and
Wolverine source at tag `V6.5.1`. Mechanism facts that drove every decision:

1. `[StickyHandler]` is valid at **class and method** level (`AttributeUsage(Class | Method)`;
   `HandlerChain.findStickyEndpoints` checks both). The fluent `AddStickyHandler(Type)` is
   type-level only.
2. Sticky endpoints resolve **by endpoint name** (`WolverineOptions.FindOrCreateEndpointByName`);
   a name with no matching endpoint becomes `local://<name>` — this is what test fixtures
   without the rabbit block get, and why they keep working.
3. Sticky separation only runs when a message type has **more than one handler call**
   (`grouping.Count() > 1` gate in the `HandlerChain` ctor). Single-handler chains ignore the
   attribute and execute as defaults at whatever endpoint delivers them — already exactly-once
   for single-queue routes.
4. **Dispatch executes at most ONE sticky handler class per (message type, endpoint)**:
   `HandlerGraph.HandlerFor(Type, Endpoint)` resolves `chain.ByEndpoint.FirstOrDefault(...)`.
   A second class sticky at the same endpoint for the same type silently starves — no startup
   diagnostic. This forced the four method consolidations in S3c-2 and is an upstream finding.
5. Once any handler of a type is sticky at a broker endpoint, a delivery at a queue with **no
   sticky match throws `NoHandlerForEndpointException`** (no defaults, not all-local) — so
   consumer-less routes become poison and had to be removed, not tolerated.
6. Local conventional routing (`LocalTransport.DiscoverSenders`) yields all sticky endpoints —
   fixture publishes route to the named local queues and each consumer executes exactly once.

### Audit table

Classification: **B** broker-fed (sticky binding added), **L** forwarding-fed local (untouched),
**I** internal-command / single-handler default (unaffected), **P** poison/vestigial route (removed).

| Contract event | Consumer | Queue | Class | Action |
|---|---|---|---|---|
| SellerRegistrationCompleted | Selling.SellerRegistrationCompletedHandler | selling-participants-events | B | sticky (class) |
| SellerRegistrationCompleted | Relay.ParticipantsOperationsHandler | relay-participants-events | B | sticky (class) |
| ParticipantSessionStarted | Settlement.BidderCreditViewHandler | settlement-participants-events | B | sticky (METHOD — class also handles forwarding-fed WinnerCharged) |
| ParticipantSessionStarted | Auctions.ParticipantCreditCeilingHandler | auctions-participants-events | B | sticky (class) |
| ParticipantSessionStarted | Operations.ParticipantActivityHandler | operations-participants-events | B | sticky (class) |
| ParticipantSessionStarted | Relay.ParticipantsOperationsHandler | relay-participants-events | B | sticky (class) |
| WinnerCharged (Settlement-internal) | Settlement.BidderCreditViewHandler | — (fast forwarding, local) | L | no attribute on that method |
| ListingPublished | Listings.ListingPublishedHandler | listings-selling-events | B | sticky (class) |
| ListingPublished | Auctions.ListingPublishedHandler **+ Auctions.PublishedListingsHandler** | auctions-selling-events | B | **consolidated** — one discovered handler (timed-open + cache upsert) |
| ListingPublished | Settlement.PendingSettlementHandler | settlement-selling-events | B | sticky (METHOD) |
| ListingPublished | Operations.LotBoardSellingHandler | operations-selling-events | B | sticky (METHOD) |
| ListingPublished | Relay.SellingOperationsHandler | relay-selling-events | B | sticky (class) |
| ListingRevised / ListingEndedEarly | Relay.SellingOperationsHandler | relay-selling-events | I | none needed |
| ListingWithdrawn | Listings.SellingListingWithdrawnHandler | listings-selling-events | B | sticky (class) |
| ListingWithdrawn | Auctions.PublishedListingsHandler | auctions-selling-events | B | sticky (class) |
| ListingWithdrawn | Auctions dispatchers | auctions-auctions-events (NEW) | B | **consolidated** into ProxyBidDispatchHandler (emits ClosingListingWithdrawnObserved via Translate) |
| ListingWithdrawn | Settlement.PendingSettlementHandler | settlement-selling-events | B | sticky (METHOD) |
| ListingWithdrawn | Operations.LotBoardSellingHandler | operations-auctions-events (M7 §2 literal) | B | sticky (METHOD) |
| ListingWithdrawn | Relay.AuctionsBiddingHandler | relay-auctions-events | B | sticky (class) |
| BiddingOpened | Auctions.StartAuctionClosingSagaHandler | auctions-auctions-events (NEW — route the prompt's list omitted) | B | sticky (class) — kills Bug #3 |
| BiddingOpened | Listings.AuctionStatusHandler / Operations.LotBoardAuctionsHandler / Relay.AuctionsBiddingHandler | listings- / operations- / relay-auctions-events | B | sticky (class) ×3 |
| BidPlaced | Auctions dispatchers | auctions-auctions-events (NEW) | B | **consolidated** into ProxyBidDispatchHandler (+ ClosingBidObserved via Translate) |
| BidPlaced | Listings.AuctionStatusHandler | listings-auctions-events | B | sticky (class) |
| BidPlaced | Operations.LotBoardAuctionsHandler **+ BidActivityHandler** | operations-auctions-events | B | **consolidated** — activity append via `BidActivityHandler.AppendActivityAsync` |
| BidPlaced | Relay.BidPlacedHandler **+ AuctionsOperationsHandler** | relay-auctions-events | B | **consolidated** — OperationsHub push moved into BidPlacedHandler |
| BiddingClosed | Listings.AuctionStatusHandler | listings-auctions-events | I (single) | none |
| ReserveMet / ExtendedBiddingTriggered / BuyItNowPurchased | Auctions.AuctionClosingDispatchHandler | auctions-auctions-events (NEW) | B | sticky (class) |
| ReserveMet / ExtendedBiddingTriggered | Relay.AuctionsBiddingHandler | relay-auctions-events | B | sticky (class) |
| BuyItNowPurchased | Listings / Settlement.StartSettlementSagaHandler / Relay | listings- / settlement- / relay-auctions-events | B | sticky (class) ×3 |
| ListingSold | Auctions.ProxyBidDispatchHandler | auctions-auctions-events (NEW) | B | sticky (class) |
| ListingSold | Listings / Settlement.StartSettlementSagaHandler / Operations | listings- / settlement- / operations-auctions-events | B | sticky (class) ×3 |
| ListingSold | Relay.ListingSoldHandler **+ AuctionsOperationsHandler** | relay-auctions-events | B | **consolidated** — OperationsHub push moved into ListingSoldHandler |
| ListingPassed | Auctions.ProxyBidDispatchHandler / Listings / Settlement.PendingSettlementHandler (METHOD) / Operations / Relay | five queues | B | sticky |
| BidRejected / ProxyBidExhausted / BuyItNowOptionRemoved | Relay.AuctionsBiddingHandler | relay-auctions-events | I (single) | none |
| SessionCreated | (no Listings consumer) | listings-auctions-events | **P** | **route removed** (would throw NoHandlerForEndpointException post-sticky) |
| SessionCreated / ListingAttachedToSession / SessionStarted | Operations.SessionActivityHandler + Relay.AuctionsOperationsHandler (+ Listings.AuctionsSessionHandler for the latter two) | operations- / relay- / listings-auctions-events | B | sticky (class) |
| SessionStarted | Auctions.SessionStartedHandler | auctions-auctions-events (NEW — OQ2 resolution) | B | sticky (class) + route |
| SettlementCompleted | Listings / Obligations.SettlementCompletedHandler / Operations / Relay | listings- / obligations- / operations- / relay-settlement-events | B | sticky (class) ×4 |
| SettlementCompleted / PaymentFailed | Settlement.PendingSettlementHandler | settlement-settlement-events (NEW — audit discovery) | B | sticky (METHOD) + new queue |
| PaymentFailed | Operations.SettlementQueueHandler | operations-settlement-events | B | sticky (class) |
| SellerPayoutIssued | Relay.SellerPayoutIssuedHandler + Operations.SettlementQueueHandler | relay- / operations-settlement-events | B | sticky (class) |
| TrackingInfoProvided | Relay.ObligationsRelayHandler | relay-obligations-events | I (single) | none |
| ObligationFulfilled / DisputeOpened / DisputeResolved | Relay.ObligationsRelayHandler + Operations.OperationsObligationsHandler | relay- / operations-obligations-events | B | sticky (class) |
| DeadlineEscalated | (no Relay consumer ever shipped) | relay-obligations-events | **P** | **route removed** (was double-executing the Operations handler via the default chain) |
| DeadlineEscalated | Operations.OperationsObligationsHandler | operations-obligations-events | I (single after removal) | none |
| LotWatchAdded / LotWatchRemoved | Relay.ListingsOperationsHandler | relay-listings-events | I (single) | class attribute added anyway — inert at 6.5.1, self-heals if a second consumer appears |
| All `*Observed` internals, `CloseAuction`, settlement pipeline commands, post-sale commands, `RegisterProxyBid`, `BuyNow` | sagas / single handlers | — (local) | I | untouched |

### Open questions from the prompt — resolved

1. **Attribute vs fluent → attribute.** Method-level granularity is *required* for
   `PendingSettlementHandler` (three queues across five methods), `LotBoardSellingHandler`
   (two queues), and `BidderCreditViewHandler` (one broker-fed + one forwarding-fed method);
   the fluent `AddStickyHandler(Type)` is type-level and cannot express the split. The attribute
   also keeps the binding visible at the handler that owns the queue name.
2. **`SessionStarted` dual life → there is no dual life.** The baseline debug log shows NO direct
   local forwarding to `SessionStartedHandler` — it was fan-out-fed from the three queues other
   BCs own (3 relays to `local://critterbids.auctions.sessionstartedhandler/` per session start).
   That is the "needs a binding + route" branch the question anticipated, so no halt-and-consult:
   `SessionStarted` now routes to `auctions-auctions-events` and the handler is sticky there.
3. **Fan-out-multiplicity assertions → none existed.** Zero test assertions changed; the suite
   needed only the six compile fixes from the consolidation signature changes.

## S3c-2/S3c-3: Bindings, consolidations, topology changes

**Why consolidation, not just annotation.** Wolverine 6.5.1 resolves the sticky handler for an
endpoint with `ByEndpoint.FirstOrDefault(x => x.Endpoints.Contains(endpoint))` — one chain wins,
siblings starve silently. ADR 027 predicted "a wide but mechanical diff"; the mechanical part held
for 24 of 28 handler classes, but four same-BC same-event pairs had to merge into one discovered
handler each:

| BC | Event(s) | Keeper | Displaced logic |
|---|---|---|---|
| Auctions | BidPlaced, ListingWithdrawn | ProxyBidDispatchHandler (needs the saga query anyway) | AuctionClosingDispatchHandler's translations became non-discovered `Translate(...)` pure functions the keeper emits |
| Auctions | ListingPublished | ListingPublishedHandler (timed-open) | PublishedListingsHandler's upsert became `UpsertPublishedListingAsync`, called FIRST (Flash listings need the cache row; only the stream-open is Duration-gated) |
| Operations | BidPlaced | LotBoardAuctionsHandler | BidActivityHandler's append became `AppendActivityAsync` (same file, same behavior, one session/commit) |
| Relay | BidPlaced; ListingSold | BidPlacedHandler; ListingSoldHandler | AuctionsOperationsHandler's OperationsHub pushes moved in; that class keeps the session trio |

**Topology changes in `Program.cs`:**

- NEW `auctions-auctions-events` (broker self-consumption per ADR 027): BiddingOpened, BidPlaced,
  ReserveMet, ExtendedBiddingTriggered, BuyItNowPurchased, ListingSold, ListingPassed,
  SessionStarted, Selling.ListingWithdrawn. The prompt's event list omitted **BiddingOpened**
  (the saga-start binding needs it — it is what eliminates Bug #3) and **SessionStarted** (OQ2);
  the audit added both.
- NEW `settlement-settlement-events`: SettlementCompleted, PaymentFailed. **Audit discovery the
  ADR missed** — Settlement's `PendingSettlementHandler` consumes its own BC's events (workshop
  003 §8.6/§8.7) and was fan-out-fed from queues other BCs own; sticky bindings would have starved
  it. Same self-consumption logic the ADR applies to Auctions.
- REMOVED `SessionCreated → listings-auctions-events` (no Listings consumer; poison post-sticky).
- REMOVED `DeadlineEscalated → relay-obligations-events` (the M6-S4 publish-only route's Relay
  consumer never shipped; the consumer-less copy made the Operations handler execute twice per
  escalation via the Separated default chain).

## S3c-4: Test-suite reconciliation

Six compile-fix call sites (signature changes from the consolidations), zero assertion changes:

| File | Fix |
|---|---|
| `BiddingOpenedConsumerTests.cs` ×3, `OpenListingForBiddingTests.cs` ×2 | `ListingPublishedHandler.Handle` gained a `CancellationToken` |
| `PublishedListingsProjectionTests.cs` ×1 | `Handle(ListingPublished…)` → `UpsertPublishedListingAsync(…)` |

Why the suite was untouched otherwise: isolated single-BC fixtures collapse contract events to
single-handler chains (mechanism fact 3 — sticky ignored, classic dispatch); the all-module
Api fixture has no rabbit block, so sticky names materialize as `local://<queue-name>` queues that
local routing feeds exactly once (facts 2 + 6).

## S3c-5: Live verification — and the two races the fan-out had been masking

Three full fresh-container runs (baseline → first verification → final), all debug-logged
(`Logging__LogLevel__Wolverine=Debug`), driven via the seed endpoint, the bid endpoint, the new
dev-only `POST /api/dev/buy-now` (BuyNow is bus-only by design; the trigger follows the
`DemoSeedEndpoint` precedent), and the staff-gated `POST /api/selling/listings/withdraw`.

**Exactly-once signatures (final run).** Per contract event, one `Received … at
rabbitmq://queue/<q>` + one `Successfully processed … from rabbitmq://queue/<q>` per consuming
queue, and **zero** post-receipt `local://` relays of contract events across the entire run
(`grep "sending <ContractEvent>#… to local://"` count: **0**). `BiddingOpened` arrives at 4 queues,
one saga start. `BidPlaced` at 4, `ListingWithdrawn` at 6, `BuyItNowPurchased` at 4 — one
processing each.

**Dead letters (AC2).** `wolverine_dead_letters` count after seed ×4 → bid ×2 → BIN → withdraw →
close → settle: **0** (baseline: +2 per seeded listing).

**First-ever live BIN + withdrawal journeys (AC3).** BIN: `CatalogListingView` → `Settled`,
settlement saga MarkCompleted-deleted, PostSaleCoordination saga opened. Withdrawal: → `Withdrawn`,
closing + proxy sagas deleted, `PendingSettlement` → `Expired`.

**Race #1 — saga lost update (`IRevisioned` was missing).** The first verification run closed the
two-bid auction with the WRONG winner: `ListingSold {HammerPrice=30, WinnerId=<bid1>, BidCount=1}`
while the catalog's live fields showed the correct `CurrentHighBid=55`. The two
`ClosingBidObserved` commands processed concurrently on the local queue (the log shows bid2's
`Successfully processed` BEFORE bid1's), both loaded the saga at `BidCount=0`, and the stale write
committed last. `UseNumericRevisions(true)` had been configured since M3-S5 — but Wolverine's
Marten saga persistence (`MartenPersistenceFrameProvider.DetermineUpdateFrame`, 6.5.1) emits the
revision-checked `UpdateSagaRevisionFrame` **only when the saga implements `JasperFx.IRevisioned`**;
without it, plain `IDocumentSession.Update`, last-writer-wins. The fan-out era's duplicate copies
had been silently repairing the race (a trailing higher-BidCount copy re-applied the lost update).
Fix: `AuctionClosingSaga` and `ProxyBidManagerSaga` now implement `IRevisioned` (both have
concurrent observations by design and the `OnException<ConcurrencyException>` retry policy already
registered). Final run: the race re-occurred, was caught —
`JasperFx.ConcurrencyException: Optimistic concurrency check failed for
CritterBids.Auctions.AuctionClosingSaga #<listingId>` — retried through the BidCount-monotonic
guard, and the auction closed correctly (`hammer=55, winner=bid2, bidCount=2`). The
wolverine-sagas skill's saga-registration section was corrected (it showed the schema half only).
`SettlementSaga` / `PostSaleCoordinationSaga` share the schema-half-only gap but have sequential
input patterns and **no** ConcurrencyException retry policy — adding enforcement blind would
convert silent races into dead letters; flagged below as follow-up.

**Race #2 — order-fragile catalog guard.** Same run: listing A stuck at `"Sold"` with `settledAt`
null although its settlement saga had completed. The log shows both `SettlementCompleted` copies
processed at `listings-settlement-events` (log lines 3595/3603) BEFORE `ListingSold` at
`listings-auctions-events` (line 3689) — the settlement pipeline completes in milliseconds and the
two events ride different queues. `SettlementStatusHandler`'s `Status != "Sold" → return` guard
no-opped; pre-S3c the trailing fan-out duplicates retried the transition after `ListingSold`
landed. Fix: the Settled transition is taken from any pre-terminal status
(`Passed`/`Withdrawn`/`Settled` stay absorbing), and `AuctionStatusHandler`'s
BiddingClosed/ListingSold/BuyItNowPurchased preserve an already-Settled terminal while still
landing the sale payload. Final run: A reached `Settled` with the full correct payload.

**Discovered, recorded, NOT fixed (pre-existing):** `SettlementCompleted` and `PaymentFailed` are
published **twice** per settlement — the saga both appends them to the financial event stream
(fast event forwarding publishes the appended copy) and returns them via `OutgoingMessages`
(`SettlementSaga.Handle(CompleteSettlement)`, lines 228/232). Two distinct envelopes per queue,
absorbed by idempotent consumers — at-least-once hygiene, but doubled traffic and a wider race
window. Follow-up below.

**Browser smoke (AC5, testing-patterns §6).** Two fresh contexts (bidder X, bidder Y) against the
live Vite dev server: X bids 30, Y bids 35; **both feeds show exactly one entry per bid**
(`["New bid $35.00","Buy It Now option removed.","New bid $30.00"]`), no duplicate-key warnings,
X's page reflects the new high bid. One pre-existing dev-only console artifact per context —
`Failed to start the connection: Error: The connection was stopped during negotiation.` — is the
React StrictMode double-mount stopping the first SignalR connection mid-negotiate (the
`@microsoft/signalr` logger reports it; the provider's `cancelled` flag suppresses the app-level
error and the second connection succeeds). The S3c diff contains no client changes; recorded, not
fixed (frontend out of scope).

### Post-change topology (derived from `describe-routing --all` + the live probe; both captured)

| Event family | Queues |
|---|---|
| BidPlaced / BiddingOpened | listings-, operations-, relay-, **auctions-**auctions-events |
| ListingSold / ListingPassed | + settlement-auctions-events (5 total) |
| BuyItNowPurchased | listings-, settlement-, relay-, **auctions-**auctions-events |
| ReserveMet / ExtendedBiddingTriggered | relay-, **auctions-**auctions-events |
| SessionStarted | listings-, operations-, relay-, **auctions-**auctions-events |
| SessionCreated | operations-, relay-auctions-events (listings route REMOVED) |
| Selling.ListingWithdrawn | listings-selling, auctions-selling, settlement-selling, operations-auctions, relay-auctions, **auctions-auctions** (6) |
| SettlementCompleted | listings-, obligations-, operations-, relay-, **settlement-**settlement-events (5) |
| PaymentFailed | operations-, **settlement-**settlement-events |
| DeadlineEscalated | operations-obligations-events only (relay route REMOVED) |

`describe-routing --all` excerpt (BidPlaced; the CLI wraps the table — fragments joined):

```
CritterBids.Contracts.Auctions.BidPlaced
  rabbitmq://queue/listings-auctions-events    Inline  application/json
  rabbitmq://queue/operations-auctions-events  Inline  application/json
  rabbitmq://queue/relay-auctions-events       Inline  application/json
  rabbitmq://queue/auctions-auctions-events    Inline  application/json
```

## Test results

| Phase | Tests | Result |
|---|---|---|
| Baseline (main) | 298 | green |
| After bindings + consolidations + topology | 298 | green (6 compile fixes, 0 assertion changes) |
| After `IRevisioned` fix | 298 | green |
| After order-tolerant catalog fix (final) | 298 | green |

Test count unchanged: 298. No new tests — the slice's behavior claims are live-verified per the
prompt's acceptance criteria; the two race fixes are guarded by existing suites plus the live
journey (a regression test for the saga race would need deterministic concurrent local-queue
interleaving — noted as follow-up).

## Build state at session close

- Build: 0 errors, 0 warnings (unchanged from baseline).
- `[StickyHandler(` occurrences in `src/`: **38** (30 class-level, 8 method-level) across 6 BCs.
- Contract-event `local://` fan-out relays in the final live run's debug log: **0**.
- `wolverine_dead_letters` after the full final journey: **0**.
- Sagas implementing `JasperFx.IRevisioned`: 2 of 4 (`AuctionClosingSaga`, `ProxyBidManagerSaga`);
  the other two flagged below.
- `.ToRabbitQueue(` publish routes in `Program.cs`: 77 (was 68 on `main`: +11 new, −2 removed); `ListenToRabbitQueue(` listeners: 23 (+2).

## Key learnings

1. **Sticky dispatch is one-handler-class-per-(message type, endpoint).** `ByEndpoint.FirstOrDefault`
   silently drops sibling sticky chains at the same endpoint, and nothing warns at startup. Any BC
   with two handler classes for the same event must consolidate to one discovered handler before
   binding. (Upstream candidate alongside the existing fan-out work order.)
2. **At-least-once duplication can mask correctness bugs; removing it is a behavioral change even
   when "behavior-preserving."** Two latent races (saga lost-update, order-fragile status guard)
   had been silently repaired by trailing duplicate deliveries for multiple milestones. Exactly-once
   delivery is a *correctness audit* of every consumer that was relying on retries-by-duplication.
3. **`UseNumericRevisions(true)` on the schema does NOT enforce saga concurrency** — the saga must
   implement `JasperFx.IRevisioned` or Wolverine generates a plain `Update` (6.5.1
   `MartenPersistenceFrameProvider.DetermineUpdateFrame`). The schema half alone is decorative.
   Pair the interface with a `ConcurrencyException` retry policy or conflicts dead-letter.
4. **Sticky bindings turn consumer-less routes into poison.** A queue receiving an event with no
   sticky match throws `NoHandlerForEndpointException` once any handler of that type is sticky at
   a broker endpoint. The audit must cover every (event × queue) pair, not just every consumer.
5. **The `grouping.Count() > 1` gate makes single-consumer events self-managing** — attributes on
   them are inert at 6.5.1 but self-heal future drift (the binding activates if a second consumer
   appears), so annotating them is forward-proofing, not noise.
6. **Cross-queue ordering is undefined and now unmasked.** Handlers whose guards encode "event A
   processes before event B" across different queues (`SettlementStatusHandler`) are bugs under
   exactly-once. Status guards must be transition-tolerant (terminal-absorbing, not
   sequence-assuming) — the tolerant-upsert discipline, applied to ordering as well as redelivery.
7. **Test fixtures degrade gracefully under sticky bindings** because absent endpoint names become
   `local://<name>` queues and local routing feeds all sticky endpoints — zero fixture rewires.
   The corollary: fixtures CANNOT catch any of this; only the integrated host can (the
   message-flow-diagnosis discipline, third confirmation).

## Findings against narrative

The prompt declares `Narrative: none` (infrastructure truth-restoration; no journey change). No
narrative or workshop drift surfaced: every journey-observable behavior is identical except the
duplicate-delivery side effects disappearing — and the two masked-race fixes restore what
narratives 001/002 already claimed (the highest bidder wins; the catalog reaches `Settled`).
No follow-up narrative warranted; the operator-facing queue topology is reference-architecture
documentation, owned by `bounded-contexts.md` and ADR 027.

## Spec delta - landed?

The prompt declared the milestone §7 amendment as the spec delta, and it landed as written:
`docs/milestones/M8-frontend-spas.md` gained the M8-S3c slice-ladder row and the v0.4 Document
History entry; ADR 027 already records the decision rationale and needed no amendment. Two
documents beyond the declared delta were corrected as session findings:
`docs/skills/wolverine-sagas/SKILL.md` (the saga-registration snippet now shows the load-bearing
`IRevisioned` half) and `docs/notes/integrated-host-flash-bidding-findings.md` (Bug #3 → resolved,
per the prompt's docs item). No workshop or narrative was amended.

## Verification checklist

- [x] AC1 — Debug-logged live run shows each broker-fed consumer processing each event exactly
  once at its own BC's queue; zero post-receipt `local://` fan-out relays for contract events.
- [x] AC2 — `wolverine_dead_letters` unchanged (0) across the full
  seed→bid→BIN→withdraw→close→settle journey on fresh containers.
- [x] AC3 — BIN and withdrawal live-verified: `CatalogListingView` reaches `Settled` / `Withdrawn`,
  Settlement + Obligations react, settlement/closing/proxy saga documents MarkCompleted-deleted
  (post-sale sagas stay open by design, awaiting fulfilment).
- [x] AC4 — Full solution suite green (298); no auth-posture changes; `describe-routing --all`
  captured, topology excerpted above.
- [x] AC5 — Bidder SPA fresh-state browser smoke passes (two contexts; exactly one feed entry per
  bid, naturally — the client dedupe is now redelivery hygiene only).

## What remains / next session should verify

- **Follow-up (new): `IRevisioned` + ConcurrencyException retry policies for `SettlementSaga` and
  `PostSaleCoordinationSaga`.** Same schema-half-only gap; their inputs are sequential by
  construction today, but PostSaleCoordination can race a timer against an HTTP command. Pair the
  interface with retry policies in the same change — enforcement without retry converts silent
  races into dead letters.
- **Follow-up (new): `SettlementCompleted`/`PaymentFailed` double-publish.** The saga appends to
  the financial stream AND returns via `OutgoingMessages`; fast forwarding publishes the appended
  copy too — two envelopes per queue per settlement. Decide one canonical publish path.
- **Follow-up (new): deterministic regression test for the saga lost-update race** (concurrent
  same-saga commands through the local queue). The live journey covers it today.
- **Upstream (existing work order extended):** alongside the single-saga fan-out defect
  (`wolverine-upstream-saga-sticky-separation-handoff.md`), the `FirstOrDefault` sticky resolution
  silently starving same-endpoint sibling handlers deserves a diagnostic or multi-chain execution
  upstream.
- **Pre-existing, recorded:** dev-only StrictMode SignalR console artifact ("connection stopped
  during negotiation") on every page load; cosmetic, frontend-owned, untouched by this slice.
- **Revisit triggers from ADR 027 stand:** upstream fix landing; a BC extracting to its own
  process; sticky-binding friction in tests exceeding fan-out-era friction (observed so far: zero).
