# M3 — Auctions BC

**Status:** Planning
**Scope:** Auctions BC core — DCB boundary model, `Listing` aggregate in Auctions, and the Auction Closing saga with extended bidding. Proxy Bid Manager and Session aggregate are deferred to M4.
**Companion docs:** [`../workshops/002-auctions-bc-deep-dive.md`](../workshops/002-auctions-bc-deep-dive.md) · [`../workshops/002-scenarios.md`](../workshops/002-scenarios.md) · [`../workshops/PARKED-QUESTIONS.md`](../workshops/PARKED-QUESTIONS.md) · [`../skills/README.md`](../skills/README.md) · [`../decisions/007-uuid-strategy.md`](../decisions/007-uuid-strategy.md)

---

## 1. Goal & Exit Criteria

### Goal

Deliver the core Auctions BC: the bidding mechanics via a Dynamic Consistency Boundary, the `Listing` aggregate state in Auctions, and the Auction Closing saga with extended bidding. At M3 close, a timed listing published in Selling (M2) opens for bids in Auctions, takes competing manual bids, fires reserve and extended-bidding signals, closes on its scheduled timer, and produces `ListingSold` or `ListingPassed`. The full listing lifecycle from `ListingPublished` (M2) through `ListingSold` / `ListingPassed` (M3) runs end-to-end through RabbitMQ, exercised by integration tests against real Postgres + RabbitMQ via Testcontainers. No frontend, no Proxy Bid Manager, no flash Session format in this milestone.

This milestone lands two firsts for the CritterBids codebase: the first Dynamic Consistency Boundary and the first Wolverine saga. Both patterns are documented in skill files (`dynamic-consistency-boundary.md`, `wolverine-sagas.md`) extracted from CritterSupply, but neither has been exercised in CritterBids until now.

### Exit criteria

- [ ] Solution builds clean with `dotnet build` — 0 errors, 0 warnings
- [ ] Auctions BC implemented: `CritterBids.Auctions` and `CritterBids.Auctions.Tests` projects, `AddAuctionsModule()`, Marten config per `adding-bc-module.md`
- [ ] `BiddingOpened` produced from a Wolverine handler consuming `CritterBids.Contracts.Selling.ListingPublished` over RabbitMQ
- [ ] DCB boundary model (`BidConsistencyState`) implemented via `EventTagQuery` + `[BoundaryModel]`; `PlaceBid` and `BuyNow` handlers green against all 19 DCB scenarios
- [ ] `[WriteAggregate]` pattern applied with explicit `nameof` override from first commit on every Auctions aggregate command (per M2.5-S1 / M2.5-S2 precedent)
- [ ] Auction Closing saga implemented: AwaitingBids → Active → Extended → Closing → Resolved state machine; scheduled close message with anti-snipe cancel-and-reschedule; all 11 closing-saga scenarios green
- [ ] `CritterBids.Contracts.Auctions.*` integration events authored — `BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`, `BiddingClosed`, `ListingSold`, `ListingPassed`
- [ ] Listings BC catalog extended: `CatalogListingView` projects auction status from `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`
- [ ] At least one dispatch test per Auctions command (`PlaceBid`, `BuyNow`) exercising the Wolverine routing path, not just direct handler invocation
- [ ] ADR 007 Gate 4 closed (event row ID strategy decided or explicitly deferred with dated rationale)
- [ ] W002-7 (`BidRejected` stream placement), W002-9 (`BiddingOpened` payload completeness) resolved in S1 ADR/docs session
- [ ] `docs/skills/dynamic-consistency-boundary.md` updated retrospectively with any CritterBids-specific learnings from S4
- [ ] `docs/skills/wolverine-sagas.md` updated retrospectively with the first in-repo saga example from S5
- [ ] `docs/skills/adding-bc-module.md` table corrected (line item in S1) — ADR 011 moved all BCs to Marten; the two-flavor Marten/Polecat table is stale
- [ ] M3 retrospective doc written

---

## 2. In Scope

### Auctions BC — core components

| Component | What it owns | Scenario source |
|---|---|---|
| `Listing` aggregate (Auctions-side) | Bidding state: current high bid / bidder, bid count, reserve status, buy-it-now availability, scheduled close time | `002-scenarios.md` §1, §2 |
| DCB: `BidConsistencyState` + `PlaceBid` handler | Bid acceptance rules, reserve threshold signalling, extended bidding trigger logic, Buy It Now removal on first bid | `002-scenarios.md` §1 (15 scenarios) |
| DCB: `BuyNow` handler | Buy It Now short-circuit path with credit-ceiling and availability checks | `002-scenarios.md` §2 (4 scenarios) |
| Auction Closing saga | AwaitingBids → Active → Extended → Closing → Resolved; scheduled close message; anti-snipe cancel-and-reschedule on `ExtendedBiddingTriggered`; outcome events | `002-scenarios.md` §3 (11 scenarios) |

Total DCB + saga scenarios in scope: **30** (15 + 4 + 11).

### Cross-BC wiring

| From | Event | To | Purpose |
|---|---|---|---|
| Selling (M2) | `ListingPublished` | Auctions (M3) | Start bidding — consumer produces `BiddingOpened` per Phase 1 Option B |
| Auctions (M3) | `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed` | Listings (M3) | Extend `CatalogListingView` with auction status and final outcome |

Two new RabbitMQ queue routes:

- `auctions-selling-events` — Selling publishes `ListingPublished`; Auctions listens (new subscription)
- `listings-auctions-events` — Auctions publishes the five status events; Listings listens

The existing `listings-selling-events` queue (M2) stays unchanged — Listings continues to build `CatalogListingView` from `ListingPublished` directly. The M3 work extends that same view with the auction-status fields, populated by a second handler subscribed to `listings-auctions-events`.

### Integration contracts authored in M3

All go in `src/CritterBids.Contracts/Auctions/`:

- `BiddingOpened` — carries scheduled close time, reserve threshold, BIN price, extended-bidding config
- `BidPlaced` — carries listing, bidder, amount, bid count, proxy flag (flag always `false` in M3 since proxy is deferred; field kept to avoid contract churn in M4)
- `BuyItNowOptionRemoved` — signals Listings and Relay that BIN is no longer available
- `ReserveMet` — real-time UX signal; Settlement will carry the authoritative check later (W001-5)
- `ExtendedBiddingTriggered` — carries previous and new close time; consumed by saga and Relay
- `BuyItNowPurchased` — terminal event for BIN path
- `BiddingClosed` — mechanical close signal
- `ListingSold` — outcome; feeds Settlement (M5) and Listings
- `ListingPassed` — outcome with reason (`NoBids` | `ReserveNotMet`); feeds Listings

Contracts carry complete payload for all future consumers at first commit, per `integration-messaging.md` L2 and the discipline re-confirmed in M2-S6 (`ListingPublished` precedent).

### ADR 007 Gate 4 — event row ID strategy

S1 closes Gate 4. Auctions is the highest-write BC in the system and the BC whose write profile originally motivated the UUID v7 insert-locality rationale in ADR 007. Decision options:

- **Accept v7 for Auctions event row IDs** if Marten 8 exposes the generation seam (Gate 1) and JasperFx guidance supports it for this workload profile.
- **Defer with dated rationale** if the seam is not exposed or JasperFx input is still pending. Deferral is acceptable but must name the specific blocker and when it would be re-evaluated.

The decision lives in S1. No code session after S1 should encounter Gate 4 as an open question.

### Workshop 002 implementation-level questions resolved in S1

| ID | Question | Resolution expected in S1 |
|---|---|---|
| W002-7 | `BidRejected` stream: dedicated Marten stream per listing, or general audit stream? | Pick one, write it in the ADR / decision doc, point `dynamic-consistency-boundary.md` at the choice before S4 starts |
| W002-9 | `BiddingOpened` payload: carry full extended-bidding config or saga loads from stream? | Current workshop design carries full config; S1 confirms or trims |

### Retrospective skills work

- `dynamic-consistency-boundary.md` updated retrospectively with CritterBids-specific learnings from S4 (extracted from CritterSupply, but first in-repo application surfaces its own patterns)
- `wolverine-sagas.md` updated retrospectively with the first in-repo saga example — the Auction Closing saga is the canonical reference implementation
- `adding-bc-module.md` corrected to reflect ADR 011 (the two-flavor Marten/Polecat table is stale post-All-Marten pivot)

---

## 3. Explicit Non-Goals

Hard line — if you catch yourself building any of these in M3, stop and flag it:

- **Proxy Bid Manager saga** — deferred to M4. All 11 §4 scenarios skip to M4.
- **`RegisterProxyBid` command and `ProxyBidRegistered` / `ProxyBidExhausted` events** — authored in M4 alongside the saga.
- **Session aggregate** — deferred to M4. All 7 §5 scenarios skip to M4. Timed listings only in M3; flash Session format is an M4 deliverable.
- **`SessionCreated`, `ListingAttachedToSession`, `SessionStarted` integration events** — authored in M4.
- **Selling-side `WithdrawListing` / `ListingWithdrawn` command** — remains deferred per M2's non-goals ("end early / relist — deferred"). The Auctions saga's termination-on-withdrawn path (scenario 3.10) is still in M3 scope, tested with a hand-crafted `ListingWithdrawn` event in the Auctions test fixture. The Selling-side command is a future Selling BC session, unscheduled in M3.
- **Selling BC `ReviseListing`, `EndListingEarly`, `Relist`** — same deferral as M2.
- **Settlement BC work** — M5. `ListingSold` and `BuyItNowPurchased` are published in M3 but have no consumer until M5.
- **Obligations, Relay, Operations BC work** — post-M5.
- **Frontend (`critterbids-web`, `critterbids-ops`)** — M6 per MVP doc.
- **Real authentication scheme** — M6. `[AllowAnonymous]` through M5 remains the intentional project stance.
- **SignalR wiring** — the Relay BC has not yet been scaffolded; there is no hub to push from in M3.
- **Manual proxy flag on `BidPlaced`** — field is present on the contract (`IsProxy`), but is hard-coded to `false` by the `PlaceBid` handler in M3 since no proxy exists to set it to `true`. M4 wires the proxy path through the same contract shape with zero contract change.
- **Listings BC watchlist, LotWatchAdded / LotWatchRemoved** — post-M3.
- **`ParticipantBidHistoryView`** — W001-9 still targets Listings or Auctions; not in M3 scope.

---

## 4. Solution Layout

### New projects added in M3

```
src/
  CritterBids.Auctions/           # Auctions BC class library
tests/
  CritterBids.Auctions.Tests/     # sibling of CritterBids.Auctions
```

### Full solution layout at M3 close

```
CritterBids/
├── CritterBids.sln
├── Directory.Packages.props
├── src/
│   ├── CritterBids.AppHost/
│   ├── CritterBids.Api/                  # gains AddAuctionsModule()
│   ├── CritterBids.Contracts/            # gains Auctions/ folder with 9 contract events
│   ├── CritterBids.Participants/
│   ├── CritterBids.Selling/
│   ├── CritterBids.Listings/             # extended — CatalogListingView gains auction-status fields
│   └── CritterBids.Auctions/             # NEW
└── tests/
    ├── CritterBids.Api.Tests/
    ├── CritterBids.Contracts.Tests/
    ├── CritterBids.Participants.Tests/
    ├── CritterBids.Selling.Tests/
    ├── CritterBids.Listings.Tests/
    └── CritterBids.Auctions.Tests/       # NEW
```

The Layout 2 rule (one test project per production project) pinned in M1-S1 continues: adding `CritterBids.Auctions` requires adding `CritterBids.Auctions.Tests` in the same PR.

`CritterBids.Api.csproj` gains a `<ProjectReference>` to `CritterBids.Auctions.csproj` — the `Program.cs` `typeof(...)` pattern requires it per the M2-S7 discovery documented in `adding-bc-module.md`.

---

## 5. Infrastructure

### Marten configuration

Auctions is the sixth BC registered on the shared primary Marten store (per ADR 009). Schema isolation per ADR 008: Auctions owns the `auctions` Postgres schema, no cross-schema references.

```
services.ConfigureMarten(opts =>
{
    opts.DatabaseSchemaName = "auctions";
    opts.Events.AddEventType<BiddingOpened>();
    opts.Events.AddEventType<BidPlaced>();
    // ...
    opts.Projections.LiveStreamAggregation<Listing>();
    // ...
});
```

Event type registration (`AddEventType<T>()`) happens at the same commit as the event type itself — the M2 key learning about silent `AggregateStreamAsync<T>` null returns applies here too.

### RabbitMQ routing

M3 adds two queues:

| Queue | Publisher | Consumer | Added in |
|---|---|---|---|
| `auctions-selling-events` | Selling BC | Auctions BC | S3 |
| `listings-auctions-events` | Auctions BC | Listings BC | S5 or S6 |

Queue names follow `<consumer>-<publisher>-<category>` per `integration-messaging.md` — same convention as M2's `selling-participants-events` and `listings-selling-events`.

Routing lives in `Program.cs`, not in BC modules, for the same reason as M2: BC module extension methods do not have access to `WolverineOptions`. Threading `WolverineOptions` into module methods is tracked as technical debt in the M2 retro and remains deferred in M3.

### Scheduled messages

The Auction Closing saga uses Wolverine scheduled messages with cancel-and-reschedule for anti-snipe. This is the first use of scheduled messages in CritterBids. Pattern per `wolverine-sagas.md`:

- Saga starts on `BiddingOpened` and schedules a `CloseAuction` command for `ScheduledCloseAt`
- On `ExtendedBiddingTriggered`, saga cancels the pending `CloseAuction` and schedules a new one at the updated time
- On `ListingWithdrawn` or `BuyItNowPurchased`, saga cancels the pending `CloseAuction` and terminates

---

## 6. Conventions Pinned

Conventions inherit from `CLAUDE.md` and all prior milestones unless overridden below.

### `[WriteAggregate]` explicit override from first commit

Every Auctions aggregate command handler uses `[WriteAggregate(nameof(Command.ListingId))]` from the first commit. No implicit-convention usages. The M2.5-S1 / M2.5-S2 retrospectives established this as the canonical pattern for commands whose property name diverges from Wolverine's `{AggregateTypeName}Id` convention, and in Auctions the aggregate is named `Listing` while the command property is `ListingId` — naming actually aligns, but the explicit override is still applied for:

1. Consistency with `SubmitListing` and `UpdateDraftListing`
2. Refactor safety — if the command is ever renamed, `nameof` produces a compile error at the attribute site
3. Zero ambiguity in first-dispatch code-gen, which runs lazily and detonates only at runtime

Every Auctions command gets a dispatch test (not just a direct-call test) per the M2.5 pattern. The first-dispatch failure mode (`InvalidOperationException: Unable to determine an aggregate id…`) is caught at PR time, not in production.

### UUID v7 for Auctions stream IDs

Stream IDs use `Guid.CreateVersion7()` — consistent with all Marten BCs per ADR 007 stream-ID section. Gate 4 (event row IDs) is closed in S1; its outcome may change the event row ID strategy but does not change stream IDs.

### `BidRejected` stream placement — decided in S1

Per W002-7, the `BidRejected` stream placement decision lives in S1. The workshop-level guidance (separate stream, excluded from DCB tag query) stands; S1 confirms the specific Marten shape (dedicated stream per listing tagged with `ListingId`, vs a single general audit stream with listing filtering). Whichever shape S1 picks is applied uniformly from S4 forward.

### `BiddingOpened` payload — decided in S1

Per W002-9, the workshop design carries full extended-bidding config on `BiddingOpened`. S1 either confirms this or trims the payload. The contract shape in `CritterBids.Contracts.Auctions.BiddingOpened` is finalized in S1 before S3 authors the consumer.

### Saga state type and storage

The Auction Closing saga uses Wolverine's `Saga` base class with Marten as the saga storage, per `wolverine-sagas.md`. Saga ID is the `ListingId` — one saga instance per listing. No compound keys needed in M3; compound keys (`ListingId + BidderId`) arrive with the Proxy Bid Manager in M4.

### `IsProxy` flag on `BidPlaced`

The contract carries `IsProxy: bool`. In M3, `PlaceBid` handler always sets it to `false`. M4 adds the proxy path, which sets it to `true`. The contract shape is stable across M3 and M4.

### Catalog projection extension pattern

`CatalogListingView` is extended with new fields (auction status, current high bid, bid count, scheduled close time, etc.) in Listings BC. The projection handler set grows to consume `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed` in addition to the existing `ListingPublished`. This is a projection-extension pattern — new fields and handlers added to an existing view, not a new view. The pattern should be documented in `domain-event-conventions.md` or `marten-projections.md` retrospectively if a pattern-level learning emerges.

### No new auth or storage conventions

M3 introduces no new auth or storage conventions. `[AllowAnonymous]` everywhere through M5 is unchanged. All-Marten is unchanged. Shared primary Marten store (ADR 009) is unchanged.

---

## 7. Acceptance Tests

Tests organized by project. All integration tests use xUnit + Shouldly + Testcontainers + Alba per `critter-stack-testing-patterns.md`.

### `CritterBids.Auctions.Tests`

#### `BiddingOpenedConsumerTests.cs` (S3)

| Scenario | Test method |
|---|---|
| `ListingPublished` consumed → `BiddingOpened` produced | `ListingPublished_FromSelling_ProducesBiddingOpened` |
| Consumer is idempotent on duplicate `ListingPublished` | `ListingPublished_Duplicate_IsIdempotent` |

#### `PlaceBidHandlerTests.cs` (S4)

Mapping from `002-scenarios.md` §1. Integration tests — DCB requires Marten to exercise `EventTagQuery`.

| Scenario | Test method |
|---|---|
| 1.1 — First bid on listing | `FirstBid_ProducesBidPlaced_AndBuyItNowOptionRemoved` |
| 1.2 — Outbid | `Outbid_ProducesBidPlaced_NoBuyItNowOptionRemoved` |
| 1.3 — Reject below starting bid | `BelowStartingBid_ProducesBidRejected` |
| 1.4 — Reject below increment | `BelowIncrement_ProducesBidRejected` |
| 1.5 — Reject exceeds credit ceiling | `ExceedsCreditCeiling_ProducesBidRejected` |
| 1.6 — Reject listing not open | `NoBiddingOpened_ProducesBidRejected` |
| 1.7 — Reject listing closed | `ListingClosed_ProducesBidRejected` |
| 1.8 — Reject seller bidding on own listing | `SellerCannotBidOnOwnListing_ProducesBidRejected` |
| 1.9 — Reserve met crossing threshold | `ReserveCrossed_ProducesReserveMet` |
| 1.10 — No duplicate ReserveMet | `ReserveAlreadyMet_NoDuplicateSignal` |
| 1.11 — Extended bidding triggered in window | `BidInTriggerWindow_ProducesExtendedBiddingTriggered` |
| 1.12 — Extended bidding not triggered outside window | `BidOutsideTriggerWindow_NoExtendedBiddingTriggered` |
| 1.13 — Extended bidding disabled | `ExtendedBiddingDisabled_NoExtension` |
| 1.14 — Extended bidding within MaxDuration | `ExtensionWithinMaxDuration_Fires` |
| 1.15 — Extended bidding blocked by MaxDuration | `ExtensionExceedsMaxDuration_Blocked` |

**Plus: `PlaceBidDispatchTests.cs`** — one integration test dispatching `PlaceBid` via `IMessageBus` per the M2.5 pattern.

#### `BuyNowHandlerTests.cs` (S4)

Mapping from `002-scenarios.md` §2.

| Scenario | Test method |
|---|---|
| 2.1 — BIN happy path (no prior bids) | `BuyNow_NoPriorBids_ProducesBuyItNowPurchased` |
| 2.2 — BIN rejected, option removed | `BuyNow_OptionRemoved_Rejected` |
| 2.3 — BIN rejected, exceeds credit ceiling | `BuyNow_ExceedsCreditCeiling_Rejected` |
| 2.4 — BIN rejected, listing closed | `BuyNow_ListingClosed_Rejected` |

**Plus: `BuyNowDispatchTests.cs`** — one integration test dispatching `BuyNow` via `IMessageBus`.

#### `AuctionClosingSagaTests.cs` (S5)

Mapping from `002-scenarios.md` §3. Saga tests use the standard Wolverine saga test harness per `wolverine-sagas.md`.

| Scenario | Test method |
|---|---|
| 3.1 — Saga starts on BiddingOpened | `BiddingOpened_StartsSaga_SchedulesClose` |
| 3.2 — AwaitingBids → Active on first bid | `FirstBid_TransitionsToActive` |
| 3.3 — Saga tracks ReserveMet | `ReserveMet_UpdatesSagaState` |
| 3.4 — Reschedule on ExtendedBiddingTriggered | `ExtendedBidding_CancelsAndReschedules` |
| 3.5 — Close → ListingSold (reserve met, bids exist) | `Close_ReserveMet_ProducesListingSold` |
| 3.6 — Close → ListingPassed (reserve not met) | `Close_ReserveNotMet_ProducesListingPassed` |
| 3.7 — Close → ListingPassed (no bids) | `Close_NoBids_ProducesListingPassed` |
| 3.8 — BuyItNowPurchased completes saga | `BuyItNowPurchased_CompletesSaga` |
| 3.9 — CloseAuction after BIN is no-op | `CloseAuction_AfterBuyItNow_NoOp` |
| 3.10 — ListingWithdrawn terminates saga | `ListingWithdrawn_TerminatesWithoutEvaluation` |
| 3.11 — Close uses rescheduled time | `Close_AfterExtension_UsesRescheduledTime` |

### `CritterBids.Listings.Tests` (S6)

Additions to `CatalogListingViewTests.cs` — extending the view with auction-status fields.

| Scenario | Test method |
|---|---|
| `BiddingOpened` sets status to `Open` on catalog entry | `BiddingOpened_SetsCatalogStatusOpen` |
| `BidPlaced` updates current high bid and bid count | `BidPlaced_UpdatesCatalogHighBid` |
| `BiddingClosed` sets status to `Closed` | `BiddingClosed_SetsCatalogStatusClosed` |
| `ListingSold` sets status to `Sold` with hammer price | `ListingSold_SetsCatalogStatusSold` |
| `ListingPassed` sets status to `Passed` with reason | `ListingPassed_SetsCatalogStatusPassed` |

### Test count summary at M3 close

| Project | M2.5 Close | M3 Delta | M3 Close | Type |
|---|---|---|---|---|
| `CritterBids.Auctions.Tests` | 0 | **+32** | **32** | Integration (DCB + saga) |
| `CritterBids.Listings.Tests` | 4 | **+5** | **9** | Integration |
| `CritterBids.Selling.Tests` | 32 | 0 | 32 | Unchanged |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 | Unchanged |
| `CritterBids.Api.Tests` | 1 | 0 | 1 | Unchanged |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 | Unchanged |
| **Total** | **44** | **+37** | **81** | |

Auctions test breakdown: 2 consumer + 15 PlaceBid + 1 PlaceBid dispatch + 4 BuyNow + 1 BuyNow dispatch + 11 saga = 34. Accounting for a couple of scenarios collapsing or a smoke test added at S2, call it **~32 at M3 close**.

---

## 8. Open Questions / Decisions

| ID | Question | Disposition |
|---|---|---|
| ADR 007 Gate 4 | Event row ID strategy for Auctions at scale — UUID v7 vs engine default | **Resolve in S1.** Auctions is the BC whose write profile motivated the UUID v7 rationale; this is the moment to close the gate or produce a dated, concrete deferral. |
| W002-7 | `BidRejected` stream: dedicated type or general audit pattern? | **Resolve in S1.** Shape called out in `dynamic-consistency-boundary.md` update before S4 starts. |
| W002-9 | `BiddingOpened` payload completeness — full config or saga loads from stream? | **Resolve in S1.** Contract shape locked before S3 authors the consumer. |
| W002-8 | Two-proxy bidding war — integration test shape | **Defer to M4.** Proxy saga is M4 scope; this question moves with it. |
| M3-D1 | DCB under concurrent load — does M3 include a concurrency soak test, or trust `EventTagQuery` correctness and defer load testing? | **Call in S4.** Likely defer load testing; DCB correctness is tested by the 19 DCB scenarios, not concurrent load. |
| M3-D2 | Listings catalog extension pattern — document as a new pattern in `marten-projections.md` or `domain-event-conventions.md`? | **Call in S6 or S7.** Depends on whether a pattern-level learning emerges during the work. |
| M2-deferred | RabbitMQ routing in BC modules vs `Program.cs` — threading `WolverineOptions` into module methods | **Stays deferred.** No M3 session has scope to rework this; continues in `Program.cs`. |
| M2-deferred | `adding-bc-module.md` table post-ADR 011 | **Resolve in S1.** Low-cost doc fix; bundled with the ADR 007 / W002-7 / W002-9 docs-only session. |

---

## 9. Session Breakdown

Seven sessions, matching M2's shape. S1 is a docs-only decisions session; S7 is retrospective + skills + M3 close. Every implementation session corresponds to a PR and a retrospective.

| # | Prompt file | Scope summary |
|---|---|---|
| 1 | `docs/prompts/implementations/M3-S1-auctions-foundation-decisions.md` | Docs only. Close ADR 007 Gate 4; resolve W002-7 and W002-9; update `dynamic-consistency-boundary.md` and `wolverine-sagas.md` with the decisions; fix `adding-bc-module.md` table post-ADR 011; author all nine `CritterBids.Contracts.Auctions.*` event shapes as record stubs (no implementation, just the payload contracts). |
| 2 | `docs/prompts/implementations/M3-S2-auctions-bc-scaffold.md` | Auctions BC scaffold — `CritterBids.Auctions` and `CritterBids.Auctions.Tests` projects, `AddAuctionsModule()` with Marten config + Wolverine integration, `Listing` aggregate empty shell, `Api.csproj` project reference, smoke test. No handlers, no DCB, no saga. |
| 3 | `docs/prompts/implementations/M3-S3-bidding-opened-consumer.md` | Cross-BC wire-up — RabbitMQ queue `auctions-selling-events` subscription in Auctions, `Program.cs` routing rule for Selling to publish `ListingPublished` to it, Wolverine handler consuming `ListingPublished` and producing `BiddingOpened`. 2 consumer tests. Handler is idempotent on replay. |
| 4 | `docs/prompts/implementations/M3-S4-dcb-place-bid-buy-now.md` | **Largest session.** DCB boundary model (`BidConsistencyState`, `EventTagQuery`, `[BoundaryModel]`), `PlaceBid` handler (15 scenarios), `BuyNow` handler (4 scenarios), dispatch tests for both. Includes reserve-crossing logic, extended-bidding trigger math, Buy It Now removal atomicity, MaxDuration cap. Skill `dynamic-consistency-boundary.md` updated retrospectively. |
| 5 | `docs/prompts/implementations/M3-S5-auction-closing-saga.md` | **Second-largest session.** Auction Closing saga with AwaitingBids → Active → Extended → Closing → Resolved state machine, scheduled `CloseAuction` message, cancel-and-reschedule on `ExtendedBiddingTriggered`, 11 saga scenarios including synthetic-`ListingWithdrawn` scenario (3.10). First in-repo saga. Skill `wolverine-sagas.md` updated retrospectively. |
| 6 | `docs/prompts/implementations/M3-S6-listings-catalog-auction-status.md` | Listings BC catalog extension — `CatalogListingView` gains auction-status fields, projection handlers for `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`. New RabbitMQ queue `listings-auctions-events`. 5 projection integration tests. |
| 7 | `docs/prompts/implementations/M3-S7-retrospective-skills-m3-close.md` | Skills + retro + M3 close. Consolidate S4 and S5 skill updates if not fully captured inline. Author M3 retrospective. If ADR 007 Gate 4 resolution produced any code-level implications not captured in earlier sessions, fold them in here. |

### Session dependency graph

```
S1 (docs — ADR 007 Gate 4, W002-7, W002-9, contract stubs)
 └── S2 (Auctions scaffold)
      └── S3 (BiddingOpened consumer — cross-BC wire-up)
           └── S4 (DCB — PlaceBid + BuyNow, 19 scenarios)
                └── S5 (Auction Closing saga, 11 scenarios)
                     └── S6 (Listings catalog extension)
                          └── S7 (skills + retro + M3 close)
```

Sessions are strictly sequential — each depends on the prior.

### Session sizing notes

- **S4 is the single largest session in M3, by a margin.** 19 DCB scenarios plus the boundary model itself plus two dispatch tests. If it runs long, the BuyNow path (4 scenarios) is the cleanest split point — defer it to an S4b, with the scaffolded BuyNow handler arriving empty from S4. This is a known split plan, not a surprise.
- **S5 is the second-largest.** The first Wolverine saga in the codebase, with scheduled message cancel-and-reschedule. Risk node. If the scheduled-message cancel pattern surfaces an unexpected API shape, S5 may produce a small docs follow-up ADR or skill update that lands alongside the code.
- **S3 is smaller than it looks.** Two tests, one handler, one `Program.cs` change. The cross-BC coordination is what makes it worth its own session — the Selling→Auctions wire-up is the first time a contract authored in one milestone is consumed in another.
- **S6 is projection work, which is pattern-stable by M2-S7 precedent.** Lowest-risk implementation session in M3.
- **S1 and S7 are docs-only.** Their risk is scope creep, not technical difficulty. Both should close cleanly in a single session each.

### Risk watch-items

Lifted from the M2 retro's "three rapid ADR pivots" warning. Things M3 is structurally at risk of:

1. **DCB + saga interaction has never been exercised in CritterBids.** S4 lands the DCB, S5 lands the saga, and the saga consumes `BidPlaced` / `ReserveMet` / `ExtendedBiddingTriggered` that the DCB produces. If the two patterns interact in a way the skills files didn't anticipate, S5 may surface it. The skills were extracted from CritterSupply where both patterns exist, so the risk is "CritterBids-specific config wrinkle" rather than "unknown pattern."
2. **Wolverine scheduled message cancel-and-reschedule under anti-snipe load.** The pattern is documented in `wolverine-sagas.md`, but extended bidding chains (scenarios 1.14, 1.15, 3.4, 3.11) are the hardest saga test cases in the milestone. If the cancel API has edge cases not covered by the skill file, S5 produces a skill update.
3. **Marten `EventTagQuery` first-use.** The skill is extracted from CritterSupply. The first in-repo use is S4. First-use always surfaces something; the question is whether it surfaces a config wrinkle or a pattern-level issue. A config wrinkle stays in S4. A pattern-level issue triggers an S4 → S4b split with a short docs follow-up.

Any of these blowing up justifies one unplanned docs session (M2-S3/S4 precedent). If that happens, it gets a number in the M3 session log and the M3 count moves from 7 to 8.

---

## Appendix: Cross-BC Integration Map at M3 Close

Four integration connections live at M3 close — two from M2, two new:

```
Participants ─── SellerRegistrationCompleted ────────────► Selling
              (queue: selling-participants-events — M2)    (RegisteredSellers projection)

Selling ─────── ListingPublished ────────────────────────► Listings
              (queue: listings-selling-events — M2)        (CatalogListingView — base projection)

Selling ─────── ListingPublished ────────────────────────► Auctions        [NEW M3]
              (queue: auctions-selling-events — M3)        (BiddingOpened produced)

Auctions ────── BiddingOpened, BidPlaced,                ─► Listings        [NEW M3]
                BiddingClosed, ListingSold, ListingPassed
              (queue: listings-auctions-events — M3)       (CatalogListingView — auction-status fields)
```

Settlement remains a future consumer of `ListingPublished` (for reserve value) and `ListingSold` / `BuyItNowPurchased` (for settlement). Neither subscription exists at M3 close — they are authored in M5. The contract payloads are complete for those future consumers per the `integration-messaging.md` L2 discipline applied throughout M2 and maintained in M3.

At M3 close, the end-to-end demo-path from `ParticipantSessionStarted` (M1) through `ListingSold` / `ListingPassed` (M3) runs over RabbitMQ with five integration event hops. No frontend, no Settlement, no Obligations — but the core "one timed listing, start to finish" story works end-to-end and is exercised by integration tests.
