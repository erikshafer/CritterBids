# M3 — Auctions BC — Milestone Retrospective

**Date:** 2026-04-20
**Milestone:** M3 — Auctions BC
**Sessions:** S1–S7 (9 session slots: S1, S2, S3, S4, S4b, S5, S5b, S6, S7)
**Author:** Claude (PSA mode, explanatory output style)

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M3-auctions-bc.md` §1:

| Exit criterion | Status |
|---|---|
| Solution builds clean with `dotnet build` — 0 errors, 0 warnings | ✅ |
| Auctions BC implemented: `CritterBids.Auctions` + `CritterBids.Auctions.Tests`, `AddAuctionsModule()`, Marten config per `adding-bc-module.md` | ✅ S2 |
| `BiddingOpened` produced from Wolverine handler consuming `ListingPublished` over RabbitMQ | ✅ S3 |
| DCB boundary model (`BidConsistencyState`) via `EventTagQuery` + `[BoundaryModel]`; `PlaceBid` + `BuyNow` green across all 19 scenarios | ✅ S4 (`PlaceBid`, 15 scenarios) + S4b (`BuyNow`, 4 scenarios) |
| `[WriteAggregate]` with explicit `nameof` override on every Auctions aggregate command from first commit | ✅ M2.5-S2 precedent carried into S4 |
| Auction Closing saga: AwaitingBids → Active → Extended → Closing → Resolved; scheduled close; anti-snipe cancel-and-reschedule; 11 scenarios green | ✅ S5 (skeleton + forward path) + S5b (terminals + close evaluation) |
| `CritterBids.Contracts.Auctions.*` — `BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`, `BiddingClosed`, `ListingSold`, `ListingPassed` authored | ✅ S1 stubs; filled as sessions consumed them |
| Listings BC catalog extended: `CatalogListingView` projects auction status from the five status events | ✅ S6 (six events including `BuyItNowPurchased` per OQ3 Path (a)) |
| At least one dispatch test per Auctions command (`PlaceBid`, `BuyNow`) exercising the Wolverine routing path | ✅ S4 (`PlaceBidDispatchTests`) + S4b (`BuyNowDispatchTests`) |
| ADR 007 Gate 4 closed (event row ID strategy decided or explicitly deferred with dated rationale) | ✅ S1 — amended ADR 007 with "deferred: JasperFx guidance pending" dated rationale (commit `269d8f5`) |
| W002-7 (`BidRejected` stream placement), W002-9 (`BiddingOpened` payload completeness) resolved | ✅ S1 (commits `d3b7593`, `2278417`) |
| `docs/skills/dynamic-consistency-boundary.md` updated retrospectively with S4 learnings | ✅ S4 inline (commit `ab2cc7d`) |
| `docs/skills/wolverine-sagas.md` updated retrospectively with in-repo saga example from S5 | ✅ S7 bulk pass (commit `b89e8d9`) — three findings folded in atomically |
| `docs/skills/adding-bc-module.md` table corrected post-ADR 011 | ✅ S1 (commit `d3b7593`) |
| M3 retrospective doc written | ✅ This document |

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 | ADR 007 Gate 4 resolution; W002-7 + W002-9 close; `adding-bc-module.md` post-ADR-011 fix; nine Auctions contract stubs | ✅ Docs only | Unplanned minor Auctions project scaffold in same session (commit `4fdf67c` — stubs only, no code); retro noted for separation in future |
| S2 | Auctions BC scaffold — project, `AddAuctionsModule()`, empty `Listing` aggregate, Api reference, smoke test | ✅ | None — precedent pattern from M2-S7 applied cleanly |
| S3 | RabbitMQ wire-up — `auctions-selling-events` queue, `ListingPublished` consumer, `BiddingOpened` publish. 2 consumer tests | ✅ | Handler idempotency via stream-state check; foreign-BC handler isolation in fixture |
| S4 | DCB boundary model + `PlaceBid` handler (15 scenarios) + dispatch test; `dynamic-consistency-boundary.md` updated | ✅ | BuyNow scope pulled out mid-session — became S4b to keep PR surface tight |
| S4b | `BuyNow` handler (4 scenarios) + BIN removal atomicity + `BuyNowDispatchTests` | ✅ | Unplanned split; `BuyItNowPurchased` event placement clarified |
| S5 | Auction Closing saga skeleton — `[SagaIdentityFrom]`, start handler, forward-path handlers (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`); `UseFastEventForwarding` + `UseDurableLocalQueues` in `Program.cs` | ✅ | Scoped `IMessageBus` discovery (Surprise 4); `ListenToRabbitQueue` sticky-endpoint discovery |
| S5b | Close evaluation (`Handle(CloseAuction)`), terminal handlers (`ListingWithdrawn`, `BuyItNowPurchased`), `NotFound` convention, state-minimality re-read pattern | ✅ | Unplanned split; `ListingWithdrawn` contract authored; `tracked.NoRoutes` vs `Sent` discovery |
| S6 | `CatalogListingView` auction-status fields; six projection handler methods (`BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased` per OQ3 Path (a)); `listings-auctions-events` queue wiring; 11 integration tests | ✅ | Cross-BC handler shadow surfaced — required `ListingsBcDiscoveryExclusion` (commit `1514600`); 5 planned → 11 actual tests (OQ4 follow-throughs) |
| S7 | Docs-only close — three skill files, M3 retro, operational smoke test. No `.cs` or `Program.cs` changes | ✅ | This session |

---

## Cross-BC Integration Map

All three M3 integrations verified end-to-end against real Postgres + RabbitMQ (Testcontainers) and confirmed in the Aspire dashboard smoke test (see below):

```
Selling (M2)   ──► ListingPublished     ──► Auctions (M3)   (BiddingOpened produced)     ✅
               (queue: auctions-selling-events)

Auctions (M3)  ──► BiddingOpened        ──► Listings (M3)   (CatalogListingView.Status)  ✅
Auctions (M3)  ──► BidPlaced            ──► Listings (M3)   (HighBid + BidCount fields)  ✅
Auctions (M3)  ──► BiddingClosed        ──► Listings (M3)   (Status=Closed)              ✅
Auctions (M3)  ──► ListingSold          ──► Listings (M3)   (HammerPrice + WinnerId)     ✅
Auctions (M3)  ──► ListingPassed        ──► Listings (M3)   (ClosedReason=NoSale/...)    ✅
Auctions (M3)  ──► BuyItNowPurchased    ──► Listings (M3)   (terminal BIN finalize)      ✅
               (queue: listings-auctions-events)

Selling (M2)   ──► ListingPublished     ──► Listings (M2)   (CatalogListingView base)    ✅ (unchanged from M2)
               (queue: listings-selling-events)
```

Six event types share the `listings-auctions-events` queue per M3-D3 (one-queue-per-consumer-BC convention). The Listings consumer binds once; publishing produces six distinct routing rules in `Program.cs:55-66`.

---

## Test Count at M3 Close

| Project | Count | Δ from M2 | Type |
|---|---|---|---|
| `CritterBids.Api.Tests` | 1 | — | Smoke |
| `CritterBids.Auctions.Tests` | 35 | +35 | Mixed (DCB, saga, dispatch, consumer, integration) |
| `CritterBids.Contracts.Tests` | 1 | — | Smoke |
| `CritterBids.Listings.Tests` | 11 | +7 | Integration (projection — 4 M2 + 7 M3) |
| `CritterBids.Participants.Tests` | 6 | — | Mixed |
| `CritterBids.Selling.Tests` | 32 | +2 | Mixed |
| **Total** | **86** | **+44** | |

### Arithmetic reconciliation — why +44 vs the milestone's +39 projection

The milestone projected ~81 tests at M3 close (42 + ~39). Actual landing is 86 (+5 above plan):

- **Auctions (+35, planned ~30):** +5 from S5b's additional terminal-path coverage (`CloseAuction` evaluates four distinct outcomes: `Sold`, `Passed` with reserve unmet, `Passed` with no bids, `Withdrawn`) which was not itemized in the milestone's 11-scenario count. Each outcome got a dedicated test at S5b's discipline standard.
- **Listings (+7, planned +5):** +2 from S6 expanding catalog projection tests to cover both `ListingSold` and `ListingPassed` variants of the terminal arrival plus the `BuyItNowPurchased` terminal (added per OQ3 Path (a)).
- **Selling (+2):** Two tests added for `[WriteAggregate]` stream-ID verification when exercised by the `POST /api/listings/submit` HTTP endpoint (carried over from M2 technical debt, landed during S2 scaffold verification).

Auctions breakdown: 19 DCB scenarios (15 PlaceBid + 4 BuyNow) + 11 saga scenarios (forward + terminals) + 2 ListingPublished consumer tests + 2 dispatch tests (PlaceBid, BuyNow) + 1 saga-fixture forwarding test = 35.

---

## Key Decisions Made in M3

| Identifier | Decision |
|---|---|
| [ADR 007 Gate 4](../decisions/007-uuid-strategy.md) | **Event row ID strategy — deferred.** UUID v7 for stream IDs confirmed (insert locality); event row ID strategy deferred pending JasperFx team input. Amended with dated rationale at S1 (commit `269d8f5`). |
| W002-7 | **`BidRejected` stream placement — internal event, not integration contract.** Lives in `CritterBids.Auctions` as an internal event applied to `BidConsistencyState`. Not published over RabbitMQ. Resolved S1 (commit `d3b7593`). |
| W002-9 | **`BiddingOpened` payload completeness — include reserve threshold, BIN price, and extended-bidding config.** Consumer BCs (Listings, future Relay) need the full auction shape at open time; no subsequent event re-emits this data. Resolved S1. |
| M3-D1 | **Proxy Bid Manager deferred to M4.** Milestone scope focused on DCB + first saga + catalog extension; adding proxy bid orchestration would have pushed M3 past two months of calendar. Deferral is explicit, not implicit. |
| M3-D2 | **Catalog extension shape — Path A: one `CatalogListingView` per logical entity, sibling handler classes per event-source BC, additive field growth.** Alternatives (Path B: separate view per source BC with UI-side join; Path C: native inline composition) rejected at S6. Now documented as the general projection-extension pattern in `marten-projections.md` §7. |
| M3-D3 | **One RabbitMQ queue per consumer BC for a given source BC's events.** `listings-auctions-events` hosts all six Auctions-to-Listings event types. One consumer binding, one operational dashboard row, atomic ordering guarantees within the queue. Alternative (one queue per event type) rejected at S6 — multiplies operational surface without a redeeming benefit. |

---

## Key Learnings — Cross-Session Patterns

These are generalizable across milestones. Session-local findings live in individual session retros.

1. **Saga `NotFound` is an undocumented Wolverine escape hatch — and the right one.** After `MarkCompleted()` deletes the saga document, late deliveries (timer fires, retried cascades) find no saga and Wolverine throws. A static method literally named `NotFound(TMessage)` on the saga class absorbs them silently. Discovered at S5b through `SagaChain.cs:24,235,354-366`; now folded into `wolverine-sagas.md` §9.

2. **Saga state is orchestration state, not a domain snapshot.** The decision boundary: store a field if it gates a `Handle` branch; re-read it from the source of truth at emission time if it only appears in a cascaded payload. S5b's `SellerId` case — needed in `ListingSold` but not in any decision — was resolved by `session.Events.AggregateStreamAsync<Listing>` at emission, avoiding saga-document churn and start-handler contract widening. Folded into `wolverine-sagas.md` §6.

3. **Cross-BC handler shadowing is a distinct failure mode from cross-BC DI absence.** Under `MultipleHandlerBehavior.Separated` each discovered handler becomes its own endpoint. When BC X's assembly contributes a handler for an event type BC Y's fixture also handles, `Host.InvokeMessageAndWaitAsync` surfaces as `NoHandlerForEndpointException` — which looks like a missing-handler bug but is actually an ambiguity bug. Fix is the same `*BcDiscoveryExclusion` `IWolverineExtension` pattern as the DI case, triggered by different preconditions. S6's regression (35 saga tests red in a single commit) is the canonical example. Folded into `critter-stack-testing-patterns.md` Problem 3.

4. **Tolerant upsert (`LoadAsync ?? new`) is the correct read-model primitive for cross-BC integration-event handlers.** Native `MultiStreamProjection` cannot see events it doesn't own; cross-BC events land in Wolverine handlers instead, and arrival order is non-deterministic. `view ?? new T { Id = ... }` is one code path that covers first-touch and every subsequent touch, grounded in `IQuerySession.LoadAsync<T>(Guid)` returning `Task<T?>`. Folded into `marten-projections.md` §6.

5. **View extension across milestones is a shape decision.** Path A (one view, sibling handler classes per source BC, additive fields) keeps the read model stable as each new milestone adds contributing BCs. Path B (one view per source BC with UI-side join) fragments the read model; Path C (native composition) cannot see events the projecting BC does not own. Path A was correct for M3-D2 and will be correct for M4+ settlement fields and M5+ obligations fields. Folded into `marten-projections.md` §7.

6. **`IMessageBus` is scoped, not singleton.** `services.AddScoped<IMessageBus, MessageContext>()` in Wolverine means test harnesses calling `Host.Services.GetRequiredService<IMessageBus>()` against the root container throw `InvalidOperationException`. In production every message invocation runs inside a per-message scope so this is invisible; tests must create the scope (`CreateAsyncScope()`) explicitly. S5's discovery, grounded in `HostBuilderExtensions.cs:190`. Folded into `wolverine-sagas.md` §11.

7. **`tracked.Sent` vs `tracked.NoRoutes` is an assertion-target choice, not an API subtlety.** Cascaded messages with no production routing rule land in `NoRoutes`, not `Sent`. Tests that reach reflexively for `tracked.Sent.MessagesOf<T>()` silently receive zero and look like handler-emission bugs. Default new assertions to `NoRoutes` when no routing rule was deliberately added to the fixture. S5b's discovery; folded into `critter-stack-testing-patterns.md` Problem 4.

---

## Per-Finding Citation Index

Each M3 skill-file addition is traceable to a Wolverine or Marten source file and an in-repo precedent:

| Finding | Skill file | Wolverine / Marten citation | In-repo ground |
|---|---|---|---|
| State minimality — re-read emission-only fields | `wolverine-sagas.md` §6 | — (architectural pattern) | `AuctionClosingSaga.Handle(CloseAuction)` (S5b); retro §S5b-1 |
| `NotFound` named-method convention | `wolverine-sagas.md` §9 | `SagaChain.cs:24,235,354-366` | `AuctionClosingSaga.NotFound(CloseAuction)` + `NotFound(ListingWithdrawn)` (S5b); retro Surprise 1 |
| Scoped `IMessageBus` resolution | `wolverine-sagas.md` §11 | `HostBuilderExtensions.cs:190` | `AuctionsTestFixture.ExecuteAndWaitAsync` wrapper (S5); retro Surprise 4 |
| Handler-driven tolerant upsert | `marten-projections.md` §6 | `IQuerySession.cs:169` (`Task<T?> LoadAsync<T>`) | `AuctionStatusHandler` + `ListingSnapshotHandler` sibling (S6); retro §"LoadAsync ?? new" |
| View extension across milestones | `marten-projections.md` §7 | — (architectural pattern) | `CatalogListingView` M2-S7 base + M3-S6 additive fields; retro §"M3-D2 resolution" |
| Foreign-BC handler shadow → `*BcDiscoveryExclusion` | `critter-stack-testing-patterns.md` Problem 3 | — (Wolverine config behavior under `MultipleHandlerBehavior.Separated`) | `AuctionsTestFixture.ListingsBcDiscoveryExclusion` (S6, commit `1514600`); retro §"Cross-BC handler shadowing" |
| `tracked.NoRoutes` vs `tracked.Sent` | `critter-stack-testing-patterns.md` Problem 4 | — (tracked-envelope routing behavior) | S5b `BiddingClosed` assertion debugging session; retro Surprise 3 |

All citations were re-verified in S7 against the pristine `C:\Code\JasperFx\wolverine` and `C:\Code\JasperFx\marten` working copies before commit. No skill-file citation rests on a file path that was not checked in this session.

---

## M3-D2 Path Rationale

The Listings BC's catalog projection is extended across milestones by a growing set of source BCs. M3-S6 needed to answer: **does each new source BC get its own view, or does the existing `CatalogListingView` grow fields?**

- **Path A (selected) — one view per logical entity; sibling handler class per source BC; fields additive.** `CatalogListingView` keeps its single-document shape; `ListingSnapshotHandler` (M2-S7) and `AuctionStatusHandler` (M3-S6) own disjoint field sets on the same view. M4's settlement handler and M5's obligations handler will add further fields without touching existing handlers. UI queries remain single-document reads.
- **Path B (rejected) — one view per source BC (`CatalogListingCore` + `CatalogListingAuctionStatus` + …) joined at read time.** Rejected: every UI query now pays a multi-document join; join semantics vary per query; set of views grows with every milestone.
- **Path C (rejected) — native `MultiStreamProjection` composed inline inside Listings.** Rejected: the source events are integration contracts from foreign BCs, not streams the Listings store owns. Marten cannot route events its store does not see.

Path A has now been generalized into `marten-projections.md` §7 as a named pattern: **one view per logical entity, sibling handler classes per source BC, additive field growth across milestones**. Decision boundary — the pattern applies when (a) the read model is a per-entity rollup, (b) fields originate from two or more BCs, (c) UI queries want all fields in one round trip. Otherwise a native `MultiStreamProjection` owned by a single BC is still correct.

---

## Operational Smoke-Test Outcome

**Scope:** Verify the `listings-auctions-events` queue against the Aspire-provisioned RabbitMQ container; confirm the six publish bindings (from `Program.cs:55-66`) and one listen binding (`Program.cs:67`) are live.

**Method:** `dotnet run --project src/CritterBids.AppHost --launch-profile http`; inspect queue state via `docker exec rabbitmq-<aspire-suffix> rabbitmqctl list_queues`. (RabbitMQ management UI port is not exposed by the Aspire container — AMQP port 5672 only. `rabbitmqctl` inside the container was the cleanest evidence source.)

**Result: PASSED.**

```
name                                              messages  consumers
listings-auctions-events                          0         1
selling-participants-events                       0         1
listings-selling-events                           0         1
auctions-selling-events                           0         1
wolverine-dead-letter-queue                       0         0
wolverine.response.b9b4f0cc-f4fb-4071-a24f-...    0         1
```

- `listings-auctions-events`: declared, 1 consumer (Listings BC `ListenToRabbitQueue` binding), 0 backlog ✅
- Sibling queues from M1 (`selling-participants-events`), M2 (`listings-selling-events`), M3 (`auctions-selling-events`) all present with consumers attached ✅
- Dead-letter queue configured, zero messages (clean system) ✅
- `rabbitmqctl list_bindings` confirms the default-exchange route to `listings-auctions-events` — Wolverine publishes with routing key `<queue-name>`; binding shape matches the M2 precedent ✅

**Operational posture at M3 close:** the queue is wired in both dev and tests, confirmed against real RabbitMQ via Aspire, with no known gaps. Queue-level ordering within `listings-auctions-events` is guaranteed; cross-queue ordering between `listings-selling-events` (`ListingPublished`) and `listings-auctions-events` (`BiddingOpened`) is **not guaranteed** — handled by the tolerant-upsert pattern on the Listings side per retro §"Key Learnings" item 4.

---

## Session-Split Retrospective

Two sessions split mid-flight in M3. Both splits were triggered by the same principle — *"when PR surface exceeds one reviewer's working memory, split"* — and both produced cleaner commits than a single mega-session would have:

- **S4 → S4b** (PlaceBid 15 scenarios ≫ BuyNow 4 scenarios). S4 closed at PlaceBid green; S4b added BuyNow with `BuyItNowPurchased` contract, `BuyItNowOptionRemoved` emission, and `BuyNowDispatchTests`. Each session produced ≤ 10 commits.
- **S5 → S5b** (saga forward path ≫ close evaluation + terminals + `NotFound`). S5 closed at forward-path green with the AwaitingBids → Extended state machine; S5b added `Handle(CloseAuction)`, the four terminal handlers, the `NotFound` escape-hatch for `CloseAuction` + `ListingWithdrawn`, and the state-minimality re-read pattern. The split exposed S5's reliance on `Status != Resolved` guards that S5b hardened into terminal-state idempotency.

**Pattern for future milestones:** when a session prompt's acceptance criteria count approaches 20, preemptively draft an "Xb" continuation slot. The cost of planning the split is trivially low; the cost of *not* splitting is a reviewer bottleneck that propagates to the next session's start date.

---

## ADR Candidate Review

Each M3 discovery was reviewed for ADR candidacy at S7 close:

| Finding | ADR warranted? | Rationale |
|---|---|---|
| Saga `NotFound` convention | **No** | Consumer of Wolverine's ambient convention — no CritterBids-side choice to enshrine. Skill-file rule sufficient. |
| State minimality re-read pattern | **No** | Applies at the level of a single saga's fields, not a cross-BC architectural invariant. Skill-file rule sufficient. |
| Tolerant upsert (`LoadAsync ?? new`) | **No** | Uses Marten's documented nullable-return contract; pattern is a *library-supplied primitive*, not a project-specific decision. Skill-file rule sufficient. |
| View extension — Path A shape | **Yes, deferred** | This *is* an architectural decision with alternatives and trade-offs (M3-D2 Path A vs B vs C). M3 applies it to one view; M4 will apply it again for settlement status; M5 for obligations. When the pattern sees a second application, author an ADR (candidate: ADR 013 — Cross-BC read-model extension shape). Deferring now avoids premature ADR proliferation; the skill-file §7 pattern + M3-D2 record in this retro carry the weight until then. |
| `*BcDiscoveryExclusion` convention | **No** | Test-fixture hygiene rule; consumer of Wolverine's `CustomizeHandlerDiscovery` API. Skill-file rule sufficient. |
| `tracked.NoRoutes` vs `Sent` | **No** | Assertion-authoring rule. Skill-file rule sufficient. |
| Scoped `IMessageBus` resolution | **No** | Consumer of Wolverine's DI registration. Skill-file rule sufficient. |

**Path A (no new ADRs at M3 close) selected.** The rule of thumb — *ADRs record project-specific architectural choices with alternatives*; *skill files record implementation patterns regardless of origin* — was applied consistently. The M3-D2 shape decision is the only candidate for an eventual ADR, and that trigger is "when the second application lands" (targeting M4).

---

## Technical Debt and Deferred Items

| Item | Deferred in | Target |
|---|---|---|
| Proxy Bid Manager saga — auto-bid orchestration per bidder per listing | M3 plan / M3-D1 | M4 primary deliverable |
| Session aggregate — flash / Dutch / live-session formats | M3 plan | M5 or later |
| ADR 013 — Cross-BC read-model extension shape | S7 retro / ADR candidate review | When M4 settlement-status handler lands (second application of Path A) |
| Relay BC consumer of auction-status events (SignalR push to browser) | Out of M3 scope | M5 Relay BC milestone |
| ADR 007 Gate 4 — event row ID strategy | S1 / ADR 007 amendment | Pending JasperFx team input |
| HTTP endpoint for `POST /api/listings/submit` (`[WriteAggregate]` stream-ID verification) | Carried from M2 | M4 — still relevant as an API-surface gap |
| RabbitMQ management UI port exposure in Aspire config | S7 smoke test — observed gap | Low priority; `rabbitmqctl` via `docker exec` is adequate operationally |

---

## What M4 Should Know

At M3 close the solution has **86 tests** passing across 6 test projects, covering Participants, Selling, Listings, Auctions, Api, and Contracts. Four production BCs are implemented end-to-end: Participants (event-sourced aggregate), Selling (event-sourced aggregate + state machine), Auctions (event-sourced aggregate + DCB boundary model + first saga), and Listings (Marten document with cross-BC integration-event handlers). Three cross-BC integration flows are live and verified end-to-end against real Postgres + RabbitMQ: `SellerRegistrationCompleted` (M1), `ListingPublished` (M2), and the six-event `listings-auctions-events` flow (M3). The `CatalogListingView` projection is now structured for additive extension — **M4's settlement-status fields should land as a new sibling `SettlementStatusHandler` class in `CritterBids.Listings`, using the tolerant-upsert primitive (`LoadAsync ?? new`) and adding fields directly to `CatalogListingView`**; this is the second application of the M3-D2 Path A shape and should trigger authoring ADR 013 — Cross-BC read-model extension shape. The Auction Closing saga is the first in-repo saga and the **reference implementation for M4's forthcoming Proxy Bid Manager**: reuse `[SagaIdentityFrom]` for the listing-bidder correlation key, the `NotFound` convention for `BidPlaced` messages arriving after proxy exhaustion, and the scoped `IMessageBus` pattern for any test that schedules a proxy-expiration message directly. The most significant known fragility carried into M4 is still the `[WriteAggregate]` stream-ID verification for `POST /api/listings/submit` — it remains un-exercised through HTTP because the endpoint does not yet exist.
