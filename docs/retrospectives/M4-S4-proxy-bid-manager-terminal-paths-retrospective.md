# M4-S4: Proxy Bid Manager Saga — Terminal Paths + Exhaustion + Bidding War — Retrospective

**Date:** 2026-05-19
**Milestone:** M4 — Auctions BC Completion
**Slice:** S4 of 7 (S4b pre-drafted slot unused — base S4 absorbed all seven scenarios)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M4-S4-proxy-bid-manager-terminal-paths.md`
**Baseline:** 125 tests passing · `dotnet build` 0 errors, 24 NU1904 NuGet warnings · M4-S3 closed at the squash-merge of PR #33 (`affadff`)

---

## Baseline

- 125 tests passing at session open (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 42 Auctions)
- `dotnet build` — 0 errors, 24 pre-existing NU1904 Marten vulnerability warnings (unchanged across M3 / M4 / M5)
- `ProxyBidManagerSaga` exists with `Handle(ProxyBidObserved)` covering own-bid (monotone `LastBidAmount`) and competing-bid (`PlaceBid` emission ≤ `MaxAmount`) branches; exhaustion is a `TODO(M4-S4)` comment
- `BidderCreditCeiling` defaults to `0m` on saga construction (M4-S3 OQ4 Path c deferral)
- `ProxyBidDispatchHandler.Handle(BidPlaced)` fans one inbound to N `ProxyBidObserved` per active saga
- `[SagaIdentityFrom(nameof(ProxyBidObserved.SagaId))]` is the lone reactive handler on the proxy saga
- Four S3 scenario tests green: 4.1 / 4.2 / 4.4 / 4.5

---

## Phase table

| Phase | After commit | New tests | Total tests | Build | Note |
|-------|--------------|-----------|-------------|-------|------|
| Baseline | `9df4cd4` | — | 125 | Green | Session open |
| ParticipantCreditCeiling projection + queue | `5f9e2dd` | 0 | 125 | Green | Source-only addition |
| Saga-start loads BidderCreditCeiling + retry policy | `6e56da0` | 0 | 125 | Green | Two S3 tests updated (seed added); 1 assertion adjusted |
| Pre-emptive Send-vs-Invoke on ListingWithdrawn test | `8174b4a` | 0 | 125 | Green | Single-line dispatch shape change |
| Dispatcher terminal methods + 4 cascade-bucket flips | `ce8355c` | 0 | 125 | Green | NoRoutes → Sent for cascade outcome events |
| Saga terminal handlers + scenarios 4.6 / 4.7 / 4.8 | `8f45f78` | +3 | 128 | Green | Three terminal Handle + NotFound absorbers |
| Exhaustion branch + scenarios 4.3 / 4.9 | `1339b11` | +2 | 130 | Green | Workshop-corrected three-way min |
| Scenario 4.10 — two-proxy bidding war | `10139d4` | +1 | 131 | Green | Cascade required Listing stream seed (first-run blocker) |
| Scenario 4.11 — register-while-outbid | `8ff26e9` | +1 | 132 | Green | Single-handler invoke retained |
| Projection idempotency tests | `3a045ce` | +2 | 134 | Green | First-delivery + redelivery-preserves |
| Skill append (cascade timing) | `01d5c12` | 0 | 134 | Green | wolverine-sagas.md §"Saga-to-Saga Cascades" |
| Retrospective | this commit | 0 | 134 | Green | Slice close |

Test count by project at close: 1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + **51 Auctions** = **134** (matches the prompt's 133–134 range).

---

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| S4a | `src/CritterBids.Auctions/ParticipantCreditCeiling.cs` — sealed record `(BidderId, CreditCeiling, RegisteredAt)` with `Id => BidderId` alias; M4-D4 second lived application | `5f9e2dd` |
| S4b | `src/CritterBids.Auctions/ParticipantCreditCeilingHandler.cs` — tolerant-upsert `Handle(ParticipantSessionStarted, IDocumentSession, CancellationToken)`; existing-row preservation | `5f9e2dd` |
| S4c | `src/CritterBids.Auctions/AuctionsModule.cs` — schema registration `For<ParticipantCreditCeiling>().DatabaseSchemaName("auctions")`; no `AddEventType<ParticipantSessionStarted>` (OQ8) | `5f9e2dd` |
| S4d | `src/CritterBids.Api/Program.cs` — `PublishMessage<ParticipantSessionStarted>().ToRabbitQueue("auctions-participants-events")` + `ListenToRabbitQueue` | `5f9e2dd` |
| S4e | `src/CritterBids.Auctions/ParticipantCreditCeilingNotFoundException.cs` — sealed Exception with `BidderId` field; OQ5 mirrors M5-S4 `PendingSettlementNotFoundException` | `6e56da0` |
| S4f | `src/CritterBids.Auctions/AuctionsModule.cs` — `OnException<ParticipantCreditCeilingNotFoundException>().RetryWithCooldown(100ms, 250ms, 500ms)` added to `AuctionsConcurrencyRetryPolicies` | `6e56da0` |
| S4g | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs` — loads `ParticipantCreditCeiling` and populates `saga.BidderCreditCeiling`; throws on miss | `6e56da0` |
| S4h | `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — new `SeedParticipantCreditCeilingAsync(bidderId, creditCeiling = 500m)` helper | `6e56da0` |
| S4i | `ProxyBidManagerSagaTests.SeedProxySagaAsync` accepts `bidderCreditCeiling` with `$500` default (workshop convention) | `6e56da0` |
| S4j | `RegisterProxyBid_StartsSaga_ProducesProxyBidRegistered` updated — seeds ceiling, asserts `BidderCreditCeiling == 500m` (was `0m`) | `6e56da0` |
| S4k | `RegisterProxyBidDispatchTests` updated — seeds ceiling | `6e56da0` |
| S4l | `AuctionClosingSagaTests.ListingWithdrawn_TerminatesWithoutEvaluation` — `InvokeMessageAndWaitAsync` → `SendMessageAndWaitAsync` (pre-emptive) | `8174b4a` |
| S4m | Three wrapped command records: `ProxyListingSoldObserved`, `ProxyListingPassedObserved`, `ProxyListingWithdrawnObserved` (OQ1 Path A) | `ce8355c` |
| S4n | `ProxyBidDispatchHandler` — three new `Handle(ListingSold/ListingPassed/ListingWithdrawn)` methods + shared `QueryActiveSagasAsync` helper | `ce8355c` |
| S4o | Four `AuctionClosingSagaTests.Close_*` cascade-assertion bucket flips (`NoRoutes` → `Sent` for `ListingSold` / `ListingPassed`) | `ce8355c` |
| S4p | `ProxyBidManagerSaga` — three terminal `Handle` methods + three static `NotFound` absorbers | `8f45f78` |
| S4q | Three scenario tests: `ListingSold_CompletesSaga`, `ListingPassed_CompletesSaga`, `ListingWithdrawn_CompletesSaga` | `8f45f78` |
| S4r | `ProxyBidManagerSaga.Handle(ProxyBidObserved)` — `TODO(M4-S4)` replaced with three-way min exhaustion calc + `ProxyBidExhausted` emission + `MarkCompleted()` | `1339b11` |
| S4s | Two scenario tests: `CompetingBid_NextBidExceedsMax_ProducesProxyBidExhausted` (§4.3), `CompetingBidAtCeiling_ProducesProxyBidExhausted` (§4.9 corrected example) | `1339b11` |
| S4t | `TwoProxies_WeakerExhausts_StrongerWins` (§4.10) — saga-to-saga cascade end-to-end with Listing stream seed | `10139d4` |
| S4u | `RegisterProxy_WhileOutbid_WaitsForNextCompetingBid` (§4.11) — verifies reactive-only posture | `8ff26e9` |
| S4v | `ParticipantCreditCeilingProjectionTests` — two `[Fact]` methods (first-delivery + redelivery-preserves) | `3a045ce` |
| S4w | `docs/skills/wolverine-sagas.md` — §"Saga-to-Saga Cascades — Eager / Single-Cycle Under SendMessageAndWaitAsync" subsection appended | `01d5c12` |
| S4x | This retrospective | this commit |

---

## Open Questions — resolutions

### OQ1 — Terminal wrapped-command shape — **Path A (three separate commands)**

**Resolution.** Authored `ProxyListingSoldObserved`, `ProxyListingPassedObserved`, `ProxyListingWithdrawnObserved` as three separate sealed records, each carrying `(Guid SagaId, Guid ListingId)`. Three matching saga handlers and three dispatcher methods. Symmetric with the M4-S3 `ProxyBidObserved` precedent — each handler stays a one-line state transition + `MarkCompleted()`, and the dispatcher's three `Handle(X)` methods each emit their own wrapped type.

**Why not Path B.** Single polymorphic `ProxyListingClosedObserved` with a `ListingClosedReason` enum discriminator would have collapsed three files into one but would have required either an enum-based switch inside the saga handler or three near-identical handlers anyway. The token savings was negative once docstrings on the enum + the saga's branching were counted. Path A holds.

**Where it lives.** `src/CritterBids.Auctions/ProxyListingSoldObserved.cs` and siblings.

### OQ2 — `ProxyBidExhausted` emission shape — **Path (a) bus-only emission**

**Resolution.** `ProxyBidExhausted` emitted via `OutgoingMessages` from the saga's exhaustion branch. No `AddEventType` registration (event is not appended to any Marten stream). Cross-BC consumer is post-M5 Relay per `src/CritterBids.Contracts/Auctions/ProxyBidExhausted.cs`, so the event lands in `tracked.NoRoutes` in tests — same shape as M4-S3 `ProxyBidRegistered`. The §4.3 and §4.9 tests assert via `tracked.NoRoutes.MessagesOf<ProxyBidExhausted>().ShouldHaveSingleItem()`.

**Audit-stream consideration deferred.** M4-S6 Operations BC will decide whether the saga's terminal state needs historical event-stream backing. The current bus-only shape can be promoted to event-stream-backed later without breaking consumers (the contract docstring's "Promoted from W001 Parked #3" wording covers this).

### OQ3 — Exhaustion calc formula — **Workshop-corrected three-way min, inline**

**Resolution.** Implemented verbatim per Workshop 002 §4.9 corrected formula:

```csharp
var capped = Math.Min(Math.Min(message.Amount + increment, MaxAmount), BidderCreditCeiling);
if (capped <= message.Amount) { /* exhaust */ }
else                          { /* emit PlaceBid at capped amount */ }
```

Inline expression in `ProxyBidManagerSaga.Handle(ProxyBidObserved)` competing-bid branch. The workshop's first-pass §4.9 example using competing $195 is internally inconsistent (it does NOT exhaust — `min(196, 300, 200) = 196 > 195`); the test uses the workshop's self-corrected example with competing $200, where `min(201, 300, 200) = 200` does NOT strictly exceed competing → exhausts.

**Edge case verified.** Scenario 4.2 still passes after the formula change because `SeedProxySagaAsync` now defaults `BidderCreditCeiling = 500m` (workshop convention) rather than the S3 sentinel `0m` — `min(46, 75, 500) = 46 > 45` → still bids. The default change was a forced consequence of the formula expansion (a zero ceiling would instantaneously exhaust the saga on the first competing bid). All four S3 scenarios (4.1 / 4.2 / 4.4 / 4.5) remain green.

### OQ4 — `BidderId` ↔ `ParticipantId` identity mapping — **`Guid` Both, mapping confirmed**

**Resolution.** Read `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` at session open: `ParticipantId: Guid` is the aggregate id; `BidderId: string` is the separate display correlation ("Bidder 4217"). `ProxyBidManagerSaga.BidderId: Guid`, `RegisterProxyBid.BidderId: Guid`, and `ParticipantCreditCeiling.BidderId: Guid` all refer to the same value — the Participants-side `ParticipantId`. The projection's Marten key is the `ParticipantId`; the start handler loads via `session.LoadAsync<ParticipantCreditCeiling>(message.BidderId)` which is equivalent.

The display-string `BidderId` from `ParticipantSessionStarted` is intentionally not stored on the Auctions projection — the saga and downstream consumers correlate by Guid.

### OQ5 — Retry policy shape — **Mirrored M5-S4 `PendingSettlementNotFoundException`**

**Resolution.** Authored `ParticipantCreditCeilingNotFoundException` as a sealed Exception sibling to M5-S4's `PendingSettlementNotFoundException`, carrying `BidderId` for diagnostic context. Added a third entry to the existing `AuctionsConcurrencyRetryPolicies` (alongside `ConcurrencyException` and `DcbConcurrencyException`):

```csharp
options.OnException<ParticipantCreditCeilingNotFoundException>()
    .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds(), 500.Milliseconds());
```

Three retries with progressive backoff (cumulative ~850ms wait) gives the projection plenty of time to catch up from the `auctions-participants-events` queue. The race essentially never fires in practice — participants establish their credit ceiling well before they register a proxy bid.

**Race not explicitly tested.** The projection idempotency tests verify the upsert shape; the retry path itself would require a contrived race window (dispatch `RegisterProxyBid` before the projection has caught up — possible only with deliberate delay-injection between session-started forwarding and saga-start). The retry rule's correctness is inherited from the M5-S4 pattern's lived precedent.

### OQ6 — Pre-emptive AuctionClosingSagaTests fix — **Path B on 6a, single-test switch on 6b**

**Sub-question 6a (BIN as fourth proxy terminal): Path B (defer to post-MVP).** Tension with the OQ6 body's "Path A recommended" hint resolved by following the prompt's explicit out-of-scope directive: "BuyItNowPurchased proxy-termination handler ... explicitly flag for post-MVP decision per Open Question 6, do not implement in S4." Concrete consequence: `BuyItNowPurchased_CompletesSaga` keeps `InvokeMessageAndWaitAsync` (still single-handler — no dispatcher Handle method added). An exhausted/active proxy survives a BIN closure as an orphan; the saga document leaks. Not observably harmful in the M6 demo (no live proxy paths exercised), but documented here for the post-MVP decision. Recommend filing as a tracked issue against M7+ or the proxy-cancellation slice when scope reopens.

**Sub-question 6b (`RealSellingProducerSagaTerminationTests`): no fix needed.** The cross-BC integration test dispatches `WithdrawListing` (Selling-side, single handler) via `InvokeAsync`; the downstream `ListingWithdrawn` event reaches Auctions via `UseFastEventForwarding` which uses `PublishAsync` (fan-out path). Even with two `ListingWithdrawn` handlers in Auctions after S4, the fan-out is correct. Verified at the final full-suite run — the test passes unchanged.

### OQ7 — Two-proxy bidding war cascade timing — **Eager / single-cycle**

**Resolution.** `SendMessageAndWaitAsync`'s tracked session waits for the **full recursive cascade**, not just the first dispatch. The §4.10 test dispatches one `BidPlaced` trigger and asserts end-state (proxy-003 deleted, proxy-002 Active, one `ProxyBidExhausted` for participant-003) in a single tracked invocation that completes in ~1 second. The cascade alternates between sagas via the in-process bus (Wolverine routes the cascaded `PlaceBid` → `BidPlaced` → `ProxyBidObserved` envelopes through the dispatcher and sticky local queues; `TrackedSession` waits for the queue to drain).

This matches the workshop's "completes in milliseconds" claim verbatim. No polling, no multi-step dispatch, no custom waiters needed. Folded into `wolverine-sagas.md` §"Saga-to-Saga Cascades — Eager / Single-Cycle" (commit `01d5c12`).

### OQ8 — Marten projection register order — **No `AddEventType` needed**

**Resolution.** Confirmed by reading `src/CritterBids.Settlement/SettlementModule.cs:59–66` — the M5-S5 parallel pattern registers `BidderCreditView` via `Schema.For<>()` but does NOT call `AddEventType<ParticipantSessionStarted>()`. Wolverine routes handler-consumed integration events independently of Marten event-type registration; the latter is only required for stream replay / forwarding to resolve typed payloads, and the projection's `Handle` method does neither.

S4 follows the same shape: `opts.Schema.For<ParticipantCreditCeiling>().DatabaseSchemaName("auctions")` in `AuctionsModule.ConfigureMarten`; no `AddEventType` change. No registration order concerns.

---

## Blockers encountered

### Blocker 1 — Scenario §4.10 cascade halted at step one

**Symptom.** First run of `TwoProxies_WeakerExhausts_StrongerWins` failed with `saga-003 should be null but was {SagaDocument}` — proxy-003 survived. The cascade only executed two steps (initial dispatch + one saga reaction), not the full ~10-hop bidding war.

**Root cause.** PlaceBidHandler validates each `PlaceBid` command against the listing's Marten stream via DCB (`FetchForWritingByTags<BidConsistencyState>`). Without a seeded `BiddingOpened` event in the stream, the DCB state is the default-empty `BidConsistencyState`, and every cascaded `PlaceBid` is rejected → no `BidPlaced` appended → no forwarded event → no next saga reaction. Cascade dies at step one.

**Fix.** Added `await _fixture.SeedListingStreamAsync(listingId, sellerId, closeAt, startingBid: 25m)` to the test before the trigger dispatch. The cascade then runs through all ~10 hops cleanly.

**Source citation.** `src/CritterBids.Auctions/PlaceBidHandler.cs:32-55` — `HandleAsync` calls `FetchForWritingByTags<BidConsistencyState>` and routes to `EvaluateRejection` / `AcceptanceEvents` based on the recovered state.

**Folded into skill file.** `wolverine-sagas.md` §"Saga-to-Saga Cascades" §"What the cascade requires" calls out the DCB-seed requirement as a generic cascade-test prerequisite.

### Blocker 2 (anticipated, not an in-the-loop failure) — Cross-cut: cascade outcome events flip `tracked.*` bucket assignments

**Symptom (anticipated).** The four `AuctionClosingSagaTests.Close_*` tests previously asserted `tracked.NoRoutes.MessagesOf<ListingSold/ListingPassed>().ShouldHaveSingleItem()`. After `ProxyBidDispatchHandler.Handle(ListingSold)` / `Handle(ListingPassed)` land in Commit 4, those messages become routed (the dispatcher always runs, even with empty fan-out), so they flip from `NoRoutes` to `Sent`.

**Spotted at context-load time** (not at test-run time) by tracing what handlers the new dispatcher methods would surface. Updated the four affected assertions as part of Commit 4 alongside the dispatcher itself, so the bucket flip and the cause-effect dispatcher addition land in the same commit for reviewer clarity.

**Prompt scope deviation.** The acceptance criterion "existing assertions otherwise unchanged" for `AuctionClosingSagaTests.cs` underspecified this cross-cut. The prompt anticipated only the `ListingWithdrawn` Send-vs-Invoke switch but not the assertion-bucket flip on `ListingSold/ListingPassed`. Recorded here for the next slice; the fix is mechanical once spotted.

**Folded into skill file.** `wolverine-sagas.md` §"Saga-to-Saga Cascades" §"Assertion-bucket cross-cut when adding a handler" documents the pattern.

---

## Decisions inheriting forward

### Bid-increment helper — **Not extracted; three inline copies**

Per CLAUDE.md's "three similar lines is better than a premature abstraction" rule. Inline copies at session close:

1. `src/CritterBids.Auctions/PlaceBidHandler.cs:174-175` — the canonical implementation (M3-S4)
2. `src/CritterBids.Auctions/ProxyBidManagerSaga.cs` competing-bid branch (M4-S3 + M4-S4 expansion) — the increment is now part of the three-way `Math.Min(...)` expression rather than a separate `nextBid` variable, but still uses the `message.Amount >= 100m ? 5m : 1m` pattern
3. No third copy emerged in S4. The §4.10 bidding-war scenario was expected to introduce one, but in practice the cascade re-uses copy #2 in each saga reaction.

The threshold-of-three is not yet crossed because copies #1 and #2 are the only true co-locations. Defer extraction to whichever slice introduces a genuine third copy (likely M5-S5 / M6 if a UI hint surface needs to render "next minimum bid" client-side).

### `BuyItNowPurchased` as proxy terminal — **Deferred; orphan saga risk acknowledged**

Per OQ6a Path B. The proxy treats BIN as a non-terminal in production reasoning — an active proxy that hasn't reached exhaustion survives a BIN closure as an orphan saga document with no future trigger. The orphan is not observably harmful in the current M6 demo path (BIN is M3-S4b scope; no live proxy registrations are wired into the demo flow), but should be addressed when proxy cancellation/modification scope opens. Recommend a tracked issue against M7+ or any future "active proxies count" Operations BC surface.

### Cross-cut testing pattern for new BC-local handlers

When adding a new BC-local handler for a cascade-produced event in CritterBids, grep test fixtures for `tracked.NoRoutes.MessagesOf<X>()` BEFORE adding the handler. Any matches will flip to `tracked.Sent` after the handler lands. The mechanical fix (NoRoutes → Sent) is trivial; the discovery-pass cost is the cheap part. This is the second time this happens in M4 (M4-S3 introduced `ProxyBidDispatchHandler.Handle(BidPlaced)` — the cascading `BidPlaced` from `PlaceBidHandler` had no prior assertion in that bucket because `PlaceBidHandler.HandleAsync` writes directly to the stream without going through `OutgoingMessages`; M4-S4 introduces it for `ListingSold` / `ListingPassed` / `ListingWithdrawn`).

---

## What M4-S5 should know

### Auctions BC test count at S4 close

**51 Auctions tests** (up from 42 at S3 close). Composition: 11 AuctionClosingSagaTests + 9 ProxyBidManagerSagaTests + 2 ParticipantCreditCeilingProjectionTests + 1 RegisterProxyBidDispatchTests + 1 RealSellingProducerSagaTerminationTests + 27 pre-existing M2/M3-shipped tests across DCB, PlaceBid dispatch, listings, and fixtures. Total solution count: **134**.

### Duplicate-projection pattern now lived twice in M4

`ParticipantCreditCeiling` (M4-S4) is the second lived application of the M4-D4 duplicate-projection pattern. The first was Settlement's `BidderCreditView` at M5-S5; same source contract (`ParticipantSessionStarted`), same tolerant-upsert shape, same idempotent on re-delivery posture. The third application is M4-S5's `PublishedListings` projection (Auctions-side cache of Selling's `ListingPublished` for the `AttachListingToSession` published-status check). M4-S5 inherits a fully-shipped reference implementation in the same BC — copy the `ParticipantCreditCeiling` + `ParticipantCreditCeilingHandler` shape verbatim, swap the source event and projection fields. The `auctions-selling-events` queue is already wired in `Program.cs:49-50` from M3-S3; S5 adds a handler to the existing queue rather than a new queue.

### ADR 014 (Cross-BC Read-Model Extension Shape) — stays at M4-S6

M4-S5's `PublishedListings` projection is the duplicate-projection pattern (M4-D4), not the read-model extension pattern (M3-D2 / ADR-014). The two are structurally distinct: duplicate-projection is "each BC keeps its own local copy of upstream seed data"; read-model extension is "Listings BC's `CatalogListingView` is extended with status fields driven by downstream consumers." ADR 014 still belongs with M4-S6 per milestone doc §8 M4-D3. No early-draft author hint needed at S5 open.

### `auctions-participants-events` queue surprises that might affect S5's `auctions-selling-events`

None surfaced. The queue wired cleanly alongside the existing `settlement-participants-events` route in `Program.cs:111-117` — the pattern is "publish to BC-specific queue, listen on same queue." Handler discovery picked up `ParticipantCreditCeilingHandler.Handle(ParticipantSessionStarted)` automatically via the existing `IncludeAssembly(typeof(Listing).Assembly)` in `Program.cs:30`. The Auctions test fixture's three existing BC-discovery exclusions (Selling, Listings, Settlement) were sufficient — no `ParticipantsBcDiscoveryExclusion` needed because Participants has no handler for `ParticipantSessionStarted` (it's the producer).

S5's `ListingPublished` consumer follows the same shape: handler-only (no aggregate state to manage; the M4-S5 `PublishedListings` is the projection document), drop into `CritterBids.Auctions` namespace, register schema in `AuctionsModule.ConfigureMarten`, no Program.cs queue addition (the queue already exists).

### Bidding-war timing convention — eager / single-cycle confirmed

OQ7 resolved to eager / single-cycle behaviour under `SendMessageAndWaitAsync`. S5's `SessionStarted → BiddingOpened` fan-out handler can rely on the same cascade-completion semantics: a single `SendMessageAndWaitAsync(SessionStarted)` will wait for every cascaded `BiddingOpened` emission to complete. No cascade-wait infrastructure needed. The skill file's new §"Saga-to-Saga Cascades" subsection is the canonical reference.

### Test-fixture seed helpers worth copying

S5 will likely need a Session aggregate seed and a way to dispatch `SessionStarted` while ensuring the cascaded `BiddingOpened` events reach the Auctions saga's start handler. The M4-S4 fixture pattern (seed Listing stream + AuctionClosingSaga + ParticipantCreditCeiling + ProxyBidManagerSaga) demonstrates the layered-seed shape. Helpers added in S4: `SeedParticipantCreditCeilingAsync(bidderId, creditCeiling = 500m)`. Helpers worth preserving: keep the workshop-default values explicit in helper parameter defaults so test bodies show their data choices.

### `BuyItNowPurchased` proxy-termination gap

Per OQ6a Path B, an exhausted/active proxy survives a BIN closure as an orphan saga document. Not currently observable, but M5 / M6 introduces session-time surfaces (Operations BC's "active proxies count" live board) that would surface the orphan. Decide before that surface ships. Implementation path: add `Handle(BuyItNowPurchased)` to `ProxyBidDispatchHandler` + a fourth wrapped command `ProxyListingBuyItNowPurchasedObserved` + a fourth terminal handler on the saga (symmetric with the three already in place). Pre-emptive Send-vs-Invoke fix on `BuyItNowPurchased_CompletesSaga` and any cascade-bucket flip on related tests.

### Pre-existing increment helper extraction threshold

Bid-increment math has two co-located inline copies at S4 close (PlaceBidHandler + ProxyBidManagerSaga). The "three similar lines is better than a premature abstraction" threshold remains uncrossed. M5's `SessionStarted → BiddingOpened` fan-out doesn't introduce a new increment user. The threshold likely crosses when a UI hint surface needs the same math.

---

## Test count summary

| Project | M4-S3 close | M4-S4 delta | M4-S4 close |
|---------|-------------|-------------|-------------|
| `CritterBids.Api.Tests` | 1 | 0 | 1 |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 |
| `CritterBids.Listings.Tests` | 14 | 0 | 14 |
| `CritterBids.Selling.Tests` | 36 | 0 | 36 |
| `CritterBids.Settlement.Tests` | 25 | 0 | 25 |
| `CritterBids.Auctions.Tests` | 42 | **+9** | **51** |
| **Total** | **125** | **+9** | **134** |

`dotnet build`: 0 errors · 24 NU1904 NuGet warnings (unchanged from baseline).
