# M5-S3: PendingSettlement Projection + Cross-BC Consumers — Retrospective

**Date:** 2026-05-04
**Milestone:** M5 — Settlement BC
**Slice:** S3 of 6 (PendingSettlement Projection + ListingPublished Consumer)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M5-S3-pending-settlement-projection.md`
**Narrative (joint authority):** `docs/narratives/002-winner-clears-settlement.md`

---

## Baseline

- 88 tests passing (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + 1 Settlement); `dotnet build CritterBids.slnx` 0 errors, 0 warnings; M5-S2 closed at PR #26 (SHA `ccfbb0b`)
- `src/CritterBids.Settlement/` carries the empty `SettlementSaga` shell, `SettlementModule.cs` with single saga registration, no Contracts ProjectReference
- `tests/CritterBids.Settlement.Tests/` carries one boot-green smoke test; fixture excludes Selling / Auctions / Listings BC handlers
- `src/CritterBids.Contracts/Settlement/` carries the three M5-S1 stubs (`SettlementCompleted`, `PaymentFailed`, `SellerPayoutIssued`); referenced by no Settlement-side code yet
- `Program.cs` RabbitMQ block has no `settlement-*` queue routes yet
- `marten-projections.md` carries the file-top "Pending: M5-S3 amendment" callout flagging the cross-BC-event-seeded projection pattern as in-scope for retrospective amendment after S3 ships
- W003 Phase 1 Part 1 carries the `PendingSettlement` schema sketch (`(SellerId, ReservePrice, BuyItNowPrice, FeePercentage, PublishedAt, Status)`); §8 enumerates 8 scenarios across the projection's lifecycle (§8.1 / §8.2 / §8.3 / §8.4 / §8.5 / §8.6 / §8.7 / §8.8)
- ADR-019 (Settlement Workflow Hosting) accepted at S1 — Wolverine Saga; the saga's seven-phase implementation lands at S4

---

## Items completed

| Item | Description |
|------|-------------|
| S3a | `src/CritterBids.Settlement/PendingSettlement.cs` — sealed record document with init-only `Id`, `SellerId`, `ReservePrice`, `BuyItNowPrice`, `FeePercentage`, `PublishedAt`, `Status` per W003 Phase 1 Part 1's schema sketch |
| S3b | `src/CritterBids.Settlement/PendingSettlementStatus.cs` — enum `{ Pending, Consumed, Expired, Failed }` per W003 Phase 1 Part 7 (the `Failed` value added at the Phase 2 amendment per scenario §8.7) |
| S3c | `src/CritterBids.Settlement/PendingSettlementHandler.cs` — single static class with five `Handle(EventType, IDocumentSession, CancellationToken)` overloads covering §8.1 / §8.4 / §8.5 / §8.6 / §8.7 / §8.8 |
| S3d | `src/CritterBids.Settlement/SettlementModule.cs` — `opts.Schema.For<PendingSettlement>().DatabaseSchemaName("settlement")` added; comment block extended explaining why cross-BC events do not require `AddEventType<T>` registration |
| S3e | `src/CritterBids.Settlement/CritterBids.Settlement.csproj` — first `<ProjectReference>` to `CritterBids.Contracts.csproj` (per the M3-S2 → M3-S3 precedent) |
| S3f | `src/CritterBids.Api/Program.cs` — RabbitMQ block extended with `settlement-selling-events` (publishes `ListingPublished`, `ListingWithdrawn`; listens) and `settlement-auctions-events` (publishes `ListingSold`, `BuyItNowPurchased`, `ListingPassed`; listens). Note `ListingPassed` extends the M5 milestone doc §2 wiring table per the prompt's open-questions confirmation. |
| S3g | `tests/CritterBids.Settlement.Tests/PendingSettlementHandlerTests.cs` — six `[Fact]`s covering §8.1 / §8.4 / §8.5 / §8.6 / §8.7 / §8.8 via direct handler invocation |
| S3h | Foreign-BC fixture exclusions — `SettlementBcDiscoveryExclusion` added to `AuctionsTestFixture`, `ListingsTestFixture`, `SellingTestFixture` (the bidirectional N-1 exclusion pattern from M5-S2's Key Learning #2 cashes in concretely; broken Auctions saga tests forced the discovery) |
| S3i | Three contract docstring corrections — `Auctions/ListingPassed.cs`, `Selling/ListingPublished.cs`, `Selling/ListingWithdrawn.cs` updated to reference Settlement consumer + new transport route |
| S3j | `docs/skills/marten-projections.md` — file-top "Pending: M5-S3" callout replaced with a one-line pointer; new §6 "Single-Source-Seeded Caches" subsection authored with the cross-BC-event-seeded pattern documentation, idempotency discipline, and in-repo references |
| S3k | This retrospective |

The prompt structured scope as three commits:

| Commit | Items covered |
|--------|---------------|
| 1 — `feat(settlement): author PendingSettlement projection document and PendingSettlementHandler with five cross-BC consumers` | S3a, S3b, S3c, S3d, S3e (plus the M5-S3 prompt itself) |
| 2 — `feat(settlement): wire settlement-selling-events and settlement-auctions-events RabbitMQ routes; integration tests for the six §8 scenarios` | S3f, S3g, S3h, S3i |
| 3 — `docs(settlement): cash in marten-projections.md M5-S3 flag with cross-BC-event-seeded projection pattern; write M5-S3 retrospective` | S3j, S3k |

The foreign-fixture exclusion work (S3h) and the contract docstring corrections (S3i) were not anticipated in the prompt — they surfaced during integration. S3h surfaced as a test regression (two Auctions saga tests broke because `ListingPassed` flipped from `tracked.NoRoutes` to `tracked.Sent` once the new Settlement handler claimed a local in-process route). S3i surfaced as a documentation drift — the `Auctions/ListingPassed.cs` docstring claimed "Settlement BC does NOT consume ListingPassed" because that line was authored at M3-S6 before the projection's terminal-status lifecycle was workshopped; W003 §8.4 supersedes. Both folded into commit 2 since they are part of the same integration surface.

---

## S3a / S3b — PendingSettlement document + PendingSettlementStatus enum

### Shape

```csharp
public sealed record PendingSettlement
{
    public Guid Id { get; init; }                      // = ListingId; Marten document key
    public Guid SellerId { get; init; }
    public decimal? ReservePrice { get; init; }        // nullable: no reserve is valid
    public decimal? BuyItNowPrice { get; init; }       // renamed from contract's BuyItNow
    public decimal FeePercentage { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public PendingSettlementStatus Status { get; init; }
}

public enum PendingSettlementStatus { Pending, Consumed, Expired, Failed }
```

### Why `Id = ListingId` and no separate `ListingId` property

Marten resolves the document primary key from the `Id` property by convention. Setting `Id = listingId` makes `LoadAsync<PendingSettlement>(listingId)` the canonical lookup path without a schema-side `Identity(x => x.ListingId)` override. Authoring a separate `ListingId` property would force the override and complicate query paths.

The W003 Phase 1 Part 1 schema sketch nominally shows `ListingId` as the primary key. The implementation choice (Id-equals-ListingId via the property name) is a Marten-side convention call, not a contract change to the schema sketch — the *value* of the document key is the listing id, the *property name* is `Id`. This aligns with `CatalogListingView`'s shape (the M2-S7 / M3-S6 precedent uses `Id` as the property name and assigns the listing id to it).

### Why `BuyItNowPrice` not `BuyItNow`

The source `ListingPublished.BuyItNow` contract field renames to `BuyItNowPrice` on this projection per W003 Phase 1 Part 1's schema sketch and the §8 scenario vocabulary. The rename is carried in the handler's `with` expression — no contract change. The projection's name is the W003-canonical Settlement-side name; the contract's name is what Selling chose at authoring time. Mapping at the handler boundary keeps both correct.

### Why the `Failed` enum value

Per scenario §8.7's footnote: "The `Failed` status was added to `PendingSettlementStatus` in Phase 2. It distinguishes 'settlement attempted and failed' from 'no settlement will ever run' (`Expired`)." The distinction is real: `Expired` covers `ListingPassed` and `ListingWithdrawn` (no settlement was attempted because the listing didn't sell or was pulled before close), while `Failed` covers `PaymentFailed` (settlement was attempted, ran, and exited via the failure path). Two terminal states for "never going to be Consumed", differentiated by whether the saga ran.

### Structural metrics

| Metric | Value |
|--------|-------|
| Public properties on `PendingSettlement` | 7 (`Id`, `SellerId`, `ReservePrice`, `BuyItNowPrice`, `FeePercentage`, `PublishedAt`, `Status`) |
| Properties using `init` | 7 (all — record-shape immutable-post-construction discipline) |
| Enum values on `PendingSettlementStatus` | 4 (`Pending`, `Consumed`, `Expired`, `Failed`) |
| Marten `Identity` override | 0 (Id-property convention) |
| Marten `UseNumericRevisions` | 0 (tolerant-upsert document, not a saga) |

---

## S3c — PendingSettlementHandler — five cross-BC consumers

### Handler shape

```csharp
public static class PendingSettlementHandler
{
    public static async Task Handle(ListingPublished message, IDocumentSession session, CancellationToken ct)
    {
        var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, ct);
        var status = existing?.Status ?? PendingSettlementStatus.Pending;

        session.Store(new PendingSettlement
        {
            Id = message.ListingId,
            // ... fields from message ...
            Status = status,
        });
    }

    // Four terminal-status handlers, each with the same shape:
    public static async Task Handle(ListingPassed message, IDocumentSession session, CancellationToken ct)
    {
        var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, ct)
            ?? new PendingSettlement { Id = message.ListingId };

        if (existing.Status != PendingSettlementStatus.Pending) return;

        session.Store(existing with { Status = PendingSettlementStatus.Expired });
    }

    // Same shape for Handle(ListingWithdrawn), Handle(SettlementCompleted), Handle(PaymentFailed)
    // — each transitions to its own terminal status.
}
```

### Why the status-preservation guard on terminal handlers

Without the guard, an out-of-order delivery would regress the row's terminal status. Concrete example: `ListingPassed` arrives on a row already `Consumed` (because the saga completed first under at-least-once redelivery). Without the guard, the handler would set the row to `Expired`, which would be semantically wrong — the listing did sell, the saga did complete, calling the row "Expired" would lie. The guard makes terminal statuses absorbing.

The guard pattern collapses to one early-return per handler:

```csharp
if (existing.Status != PendingSettlementStatus.Pending) return;
```

Four handlers × one line each = four lines of defensive code that cover every possible cross-terminal collision without case-by-case reasoning.

### Why the seed handler reads existing.Status into a local before constructing

`Handle(ListingPublished)` is the seed handler. A re-delivered `ListingPublished` arriving on an already-terminal row should not regress the row to `Pending`. Two paths:
- (a) Read `existing.Status` and use it as the new row's status (current behavior — `existing?.Status ?? Pending`)
- (b) Short-circuit if `existing != null` (skip writing)

Path (a) is more defensive: if `ListingPublished`'s payload itself changes between deliveries (e.g., a corrected reserve price after a publish-time edit, hypothetically), the upsert preserves the new fields while preserving the existing status. Path (b) would skip the field updates. Path (a) is the W003 Phase 1 Part 1 schema's intent — fields are mutable at the projection layer (until the row terminates), but the lifecycle status is owned by the lifecycle handlers.

### Handler test pattern — direct invocation

Each test opens an `IDocumentSession`, invokes the static `Handle` method, calls `SaveChangesAsync`, then opens a fresh session for assertions. Per `BiddingOpenedConsumerTests` (Auctions). Avoids the `MultipleHandlerBehavior.Separated`-induced `NoHandlerForEndpointException` that `Host.InvokeMessageAndWaitAsync` would surface per memory `project_wolverine_sticky_handler.md`.

### Structural metrics

| Metric | Value |
|--------|-------|
| Handle methods | 5 (one per source event type) |
| Static class | 1 (single `PendingSettlementHandler`) |
| `OutgoingMessages` returns | 0 |
| `IMessageBus` injections | 0 |
| `await session.SaveChangesAsync()` calls inside handlers | 0 (`AutoApplyTransactions()` commits) |
| Guard early-returns | 4 (one per terminal-status handler) |

---

## S3f — Program.cs RabbitMQ wiring

### Diff

```diff
  opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowPurchased>()
      .ToRabbitQueue("listings-auctions-events");
  opts.ListenToRabbitQueue("listings-auctions-events");

+ // M5-S3: Settlement BC subscribes to Selling-source events.
+ opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
+     .ToRabbitQueue("settlement-selling-events");
+ opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
+     .ToRabbitQueue("settlement-selling-events");
+ opts.ListenToRabbitQueue("settlement-selling-events");
+
+ // M5-S3: Settlement BC subscribes to Auctions-source events.
+ opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
+     .ToRabbitQueue("settlement-auctions-events");
+ opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowPurchased>()
+     .ToRabbitQueue("settlement-auctions-events");
+ opts.PublishMessage<CritterBids.Contracts.Auctions.ListingPassed>()
+     .ToRabbitQueue("settlement-auctions-events");
+ opts.ListenToRabbitQueue("settlement-auctions-events");
```

### Negative assertion — no self-loop queue

| Metric | Value |
|--------|-------|
| `settlement-self-events` queue | **absent** |
| `opts.PublishMessage<SettlementCompleted>().ToRabbitQueue(...)` lines | 0 in S3 (S6 wires when the saga's first publish happens) |
| `opts.PublishMessage<PaymentFailed>().ToRabbitQueue(...)` lines | 0 in S3 (S5 wires) |

Per the prompt's "no new queue for self-published events" decision: `SettlementCompleted` and `PaymentFailed` consumed by Settlement's own `PendingSettlementHandler` fire from local in-process bus dispatch when the saga's `OutgoingMessages` emits them. `MultipleHandlerBehavior.Separated` lets the local handler co-exist with the cross-BC outbound publish via the same emission. No round-trip through RabbitMQ.

### Note on the saga's S4 consumption of ListingSold / BuyItNowPurchased

`settlement-auctions-events` is wired to publish `ListingSold` and `BuyItNowPurchased` in this slice. The saga that consumes them does not exist yet — only the `Handle(ListingPassed)` handler in `PendingSettlementHandler` fires on this queue at S3 close. S4 adds the saga's `Handle(ListingSold)` and `Handle(BuyItNowPurchased)` consumers; the queue topology is already in place. This is structurally the right shape: wire the queue once with all its payload types, and let consumer handlers come online slice by slice.

### Open-question fold — ListingPassed on settlement-auctions-events

The prompt's Open Question #1 noted that `ListingPassed` is not in the M5 milestone doc §2's wiring table for `settlement-auctions-events` (which lists `ListingSold` and `BuyItNowPurchased` only). Confirmed at session-start scoping that this was a milestone-doc completeness gap, not an intentional exclusion — the projection's terminal-status handlers were not enumerated at milestone-doc authoring time. Recorded for a future milestone-doc cleanup pass.

---

## S3h — Foreign-BC fixture exclusions

### What broke and what fixed it

After commit 2's first `dotnet test` run, two Auctions saga tests failed:

```
Failed CritterBids.Auctions.Tests.AuctionClosingSagaTests.Close_NoBids_ProducesListingPassed
Shouldly.ShouldAssertException : tracked.NoRoutes.MessagesOf<ListingPassed>()
    should have single item but had 0 items
```

(Plus `Close_BidsButReserveNotMet_ProducesListingPassed` with the same shape.)

### Root cause

The Auctions test fixture excluded Selling and Listings BC handlers via two `IWolverineExtension` exclusions (per the M5-S2 cross-BC handler isolation pattern). The Settlement BC was not excluded — at M5-S2 close it had no handlers (only the empty `SettlementSaga` shell), so foreign-BC fixtures could safely discover its assembly without consequence.

M5-S3 added `PendingSettlementHandler.Handle(ListingPassed, ...)`. Wolverine's discovery in the Auctions fixture found the new handler. With `MultipleHandlerBehavior.Separated`, each handler claims its own endpoint. When the saga emitted `ListingPassed` via `OutgoingMessages`, the message was routed to Settlement's local in-process handler endpoint — landing in `tracked.Sent` rather than `tracked.NoRoutes`. The saga's pre-existing assertion on `NoRoutes` then failed.

### Resolution

Added `SettlementBcDiscoveryExclusion : IWolverineExtension` to the Auctions / Listings / Selling test fixtures. Each exclusion drops handlers under `CritterBids.Settlement.*` namespace from Wolverine's discovery in the foreign fixture. The Settlement module isn't registered in those fixtures, so the `PendingSettlement` schema isn't configured; the handler couldn't run anyway. The exclusion makes the failure mode explicit rather than silent.

After exclusion: 36/36 Auctions, 11/11 Listings, 32/32 Selling, 7/7 Settlement — all green.

### Why this is the correct fix, not assertion-level

The alternative would be to update the Auctions saga's assertions from `NoRoutes` to `Sent`. That would mask a real test contract change (the saga's tests assert *what the saga emits*, not *what arrives at downstream consumers*) and would couple the saga's tests to whichever foreign-BC happens to be co-discovered at any given time. The exclusion-based fix keeps the saga's tests asserting their own contract — handler outputs end up unrouted because no production routing rule resolves under `DisableAllExternalWolverineTransports()` and no foreign-BC handler is in scope.

### Bidirectional N-1 exclusion observation

M5-S2's retrospective Key Learning #2 named the N-1 exclusion pattern (a BC fixture excludes every other BC's handlers when running with only its own module). The lived discovery at M5-S3: the pattern is bidirectional and lazy — you only realize foreign fixtures need to exclude *your* BC when *your* BC actually grows handlers. M5-S2 added Settlement's exclusions for foreign handlers in the Settlement fixture; M5-S3 reverses the polarity by adding Settlement to foreign fixtures. The exclusion pair is symmetric, but the moments at which each direction becomes load-bearing are slice-aligned with handler additions.

This generalizes to every BC: when adding handlers to BC X for the first time, audit all foreign-BC fixtures for an `XBcDiscoveryExclusion`. If any are missing, add them. The audit is mechanical (one grep + N edits) but easy to forget mid-implementation when the BC's own tests pass.

### Structural metrics

| Metric | Value |
|--------|-------|
| Foreign fixtures gaining `SettlementBcDiscoveryExclusion` | 3 (Auctions, Listings, Selling) |
| Test failures pre-fix | 2 (`Close_NoBids_ProducesListingPassed`, `Close_BidsButReserveNotMet_ProducesListingPassed`) |
| Test failures post-fix | 0 |
| Total fixture exclusions in CritterBids post-S3 | 3 + 3 + 1 = 7 (Settlement's three + Auctions's three + Listings's two + Selling's one — symmetric matrix is filling in) |

---

## S3i — Contract docstring corrections

### What changed and why

| Contract | Drift | Correction |
|---|---|---|
| `Auctions/ListingPassed.cs` | Docstring claimed "Settlement BC does NOT consume ListingPassed — no money moves on a passed listing"; W003 §8.4 has the projection's terminal-status handler consuming it | Removed the disclaimer; added Settlement consumer entry referencing W003 §8.4 with the lifecycle-vs-money-movement clarification |
| `Selling/ListingPublished.cs` | Transport list named only `listings-selling-events`; Settlement consumer description said "Initiate fee calculation" (vague) | Extended transport list to all three queues (`listings-selling-events`, `auctions-selling-events`, `settlement-selling-events`); Settlement entry now describes the W003 §8.1 PendingSettlement seeding semantics |
| `Selling/ListingWithdrawn.cs` | Transport list named only two queues; Settlement consumer entry absent | Extended transport list to three queues; added Settlement consumer entry referencing W003 §8.5 |

### Lane

The drift is `code-update` per the four-lane discipline in `docs/narratives/README.md` (the contract's documentation said one thing; the workshop scenarios said another; the workshop is authoritative; the contract is the asset that diverged). Resolved in this PR.

The narrative 002 findings ledger is unchanged — these are contract docstrings, not narrative drift. No `narrative-update` lane fires.

### Why this folded into S3 rather than a separate doc-cleanup PR

The drift surfaced precisely *because* of S3's wiring — S3 is the slice where Settlement actually does consume those events. Leaving `Auctions/ListingPassed.cs:15`'s "does NOT consume" claim in place after the consumer ships would be misinformation. The fix is one line per contract; folding into S3 keeps the contract's docstring history aligned with the slice that introduced the new consumption pattern.

---

## S3j — marten-projections.md cash-in

### What landed

- The file-top "Pending: M5-S3 amendment" callout was replaced with a one-line forward pointer to §6's new "Single-Source-Seeded Caches" subsection
- New subsection inside §6 (Handler-Driven Projections — Tolerant Upsert) titled "Single-Source-Seeded Caches — A Sub-Shape of Handler-Driven Projections" — about 80 lines of pattern documentation
- Cross-references to `PendingSettlement.cs`, `PendingSettlementHandler.cs`, `PendingSettlementHandlerTests.cs`, W003 §8 scenarios, and this retrospective

### Pattern content covered

- The structural distinction from canonical multi-source views (`CatalogListingView`'s shape) — single-source seed, status-dominant lifecycle, immutable-post-seed informational fields, cross-BC-boundary-cache role rather than read-model role
- The "why it exists" framing — modular monolith BC isolation made operational; the projection is the alternative to a cross-BC query at workflow-start time
- Idempotency under at-least-once redelivery — both the seed handler's status-preservation pattern (`existing?.Status ?? Pending`) and the terminal handlers' guard (`if (existing.Status != Pending) return;`)
- The lifecycle-correlation framing — the consumer (saga) loads the row at start time; if missing, the consumer enters retry-on-not-found per W003 Phase 1 Part 1 Option A; the projection's only job is "seed on first source event; transition status absorbingly"
- The Marten conventions — `Id = ListingId`, no `Identity` override, no `UseNumericRevisions`

### Why this placement (sub-section of §6) rather than a new top-level section

The cross-BC-event-seeded pattern is a *sub-shape* of the handler-driven tolerant-upsert pattern, not a structurally distinct class. The mechanics are identical (`LoadAsync ?? new`; record `with`; `session.Store`); what differs is the *role* the projection plays (cache vs view) and the *cardinality* of its update vocabulary (status-dominant vs field-accumulating). Authoring as a sub-section preserves the relationship; a new top-level §6.5 would have implied independence the patterns do not have.

The Open Question #4 in the prompt leaned this way; the placement decision held without revision.

---

## Test results

| Phase | Settlement.Tests | Auctions.Tests | All Tests | Result |
|-------|------------------|----------------|-----------|--------|
| Baseline (M5-S2 close) | 1 | 36 | 88 | Green |
| After commit 1 (Settlement-only build verify) | 1 (no integration test added yet) | 36 | 88 | Green (no test changes; only `src/CritterBids.Settlement/` edited) |
| After commit 2 first run | 7 (1 boot + 6 §8 scenarios) | 34 | 92 | **Red** — two Auctions saga `tracked.NoRoutes.MessagesOf<ListingPassed>()` assertions failed |
| After foreign-fixture exclusions added | 7 | 36 | 94 | Green |
| Session close | 7 | 36 | 94 | Green |

Test count delta across the session: **+6** (the six new §8 scenario tests; the M5-S2 boot smoke test still passes).

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` — 94 passing (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + **7 Settlement**)
- `.cs` files added in `src/CritterBids.Settlement/`: 3 (`PendingSettlement.cs`, `PendingSettlementStatus.cs`, `PendingSettlementHandler.cs`)
- `.cs` files added in `tests/CritterBids.Settlement.Tests/`: 1 (`PendingSettlementHandlerTests.cs`)
- Production handlers authored: 5 (`Handle(ListingPublished)`, `Handle(ListingPassed)`, `Handle(ListingWithdrawn)`, `Handle(SettlementCompleted)`, `Handle(PaymentFailed)` — all on `PendingSettlementHandler`)
- HTTP endpoints authored: 0 (Settlement remains backend-only through M5)
- Saga `Handle` methods authored: 0 (saga shell still empty)
- `MarkCompleted()` calls: 0
- `opts.Events.AddEventType<T>()` calls in SettlementModule: 0 (cross-BC events are bus-routed, not Marten-stored)
- `opts.Projections.*` calls in SettlementModule: 0 (`PendingSettlement` is a tolerant-upsert document via `Schema.For<T>`, not a Marten projection in the daemon sense)
- `IWolverineExtension` registrations on the BC side: 0 (no concurrency-retry or scheduled-message policies needed in S3)
- New RabbitMQ queue routes added to Program.cs: 2 (`settlement-selling-events`, `settlement-auctions-events`)
- Publish-route lines added to Program.cs: 5 (2 to selling-events, 3 to auctions-events)
- Listen lines added to Program.cs: 2 (one per new queue)
- Foreign-BC fixture exclusions added: 3 (`SettlementBcDiscoveryExclusion` in Auctions / Listings / Selling fixtures)
- Contract docstring corrections: 3 (`ListingPassed.cs`, `ListingPublished.cs`, `ListingWithdrawn.cs`)
- `marten-projections.md` net additions: 1 file-top callout replacement + 1 new subsection (~80 lines)
- `ProjectReference` from Settlement to Contracts: **present** (added at S3e — first Settlement-side reference per the prompt's expectation)
- `SettlementSaga` shell edits: 0 (untouched per the prompt's out-of-scope list)
- §8.2 / §8.3 implementation: deferred (no `ListingRevised` contract producer)

---

## Key learnings

1. **The N-1 exclusion pattern is bidirectional and slice-aligned with handler additions.** M5-S2's retro Key Learning #2 named the N-1 exclusion pattern but observed it from one direction only (Settlement's fixture excluding foreign-BC handlers when only Settlement is registered). M5-S3 surfaced the reverse direction: foreign-BC fixtures need to exclude Settlement's handlers when Settlement grows real handlers. The exclusion pair is symmetric, but the moments at which each direction becomes load-bearing are slice-aligned with handler additions — you only feel the need for the reverse exclusion when the BC actually starts handling messages foreign fixtures dispatch. The mechanical generalization for future slices: when adding handlers to BC X for the first time, audit all foreign-BC fixtures for an `XBcDiscoveryExclusion`. If any are missing, add them. The audit is one grep plus N edits and is easy to forget mid-implementation because the BC's own tests pass.

2. **`MultipleHandlerBehavior.Separated` plus `tracked.NoRoutes` assertions are coupled to handler discovery.** The Auctions saga's tests asserted on `tracked.NoRoutes.MessagesOf<ListingPassed>()` and were green at M5-S2 close because no routed handler existed for the message in the Auctions fixture (Listings was excluded; the RabbitMQ route to `listings-auctions-events` was disabled by `DisableAllExternalWolverineTransports()`). M5-S3's new `PendingSettlementHandler.Handle(ListingPassed)`, discovered in-process, claimed a local routing endpoint — flipping the message into `tracked.Sent`. This is the same problem class as `critter-stack-testing-patterns.md` §"Problem 4 — Tracked Bucket Mismatch" but observed from the *new-handler-on-old-message* angle rather than the more-common *new-message-no-route* angle. Future slices should expect the pattern: any new handler on a message type that an existing test asserts via `tracked.NoRoutes` will potentially flip the assertion bucket.

3. **Foreign-BC handlers running silently against an unregistered Marten schema is a quieter failure mode than expected.** The intuition was that `PendingSettlementHandler.Handle(ListingPublished)` would crash with a Marten schema-resolution exception when discovered in the Listings test fixture (which doesn't call `AddSettlementModule()`, so the `settlement` schema isn't configured). The actual failure was subtler: the message simply landed in the wrong tracked-bucket, with no Marten exception surfacing. The Listings tests passed. The Auctions tests's tracked-bucket assertion was the only test that *visibly* surfaced the silent contamination. Without that assertion shape, the failure could have lurked. This is an argument for *defensive* foreign-BC exclusions in every fixture, not "exclusions added when something visibly breaks." Future BC slices should add the exclusion at fixture-authoring time, not retroactively after a regression.

4. **The cross-BC-event-seeded projection pattern earns the "single-source-seeded cache" framing.** `marten-projections.md` already had the handler-driven tolerant-upsert pattern documented (with `CatalogListingView` as the in-repo ground). The temptation was to treat `PendingSettlement` as just another instance of the same pattern. The lived ground refines that: the *role* the projection plays (a cross-BC-boundary cache that lets a downstream consumer load the data at workflow-start time without crossing the boundary) is structurally distinct from the *role* `CatalogListingView` plays (a denormalized read model for query performance). Both use the same mechanics; they exist for different reasons. The skill amendment names this distinction; future projections in CritterBids will be one or the other shape, and naming the choice up front clarifies what the projection's *job* is.

5. **Contract docstring drift is a side effect of slice-by-slice consumer addition.** `Auctions/ListingPassed.cs:15`'s "Settlement BC does NOT consume ListingPassed" disclaimer was authored at M3-S6 in good faith — at that time, the Settlement BC's projection lifecycle had not been workshopped yet. The W003 workshop (which assigns ListingPassed to the projection's terminal-status handler) post-dated the docstring. M5-S3 is the slice where the docstring becomes wrong. The lesson: contract docstrings name *current* consumers and *current* transports; they are time-sensitive assets that drift as new BCs come online. Future slices that add consumer entries to existing contracts should audit the contract's full docstring (transport list + consumer list + any "does NOT consume" disclaimers) for drift, not just append a new bullet to the consumer list.

6. **Settlement self-publish via local dispatch under `MultipleHandlerBehavior.Separated` worked exactly as designed without ceremony.** `Handle(SettlementCompleted)` and `Handle(PaymentFailed)` will fire from the saga's `OutgoingMessages` emission via in-process bus dispatch when S5 / S6 lands the saga's terminal handlers. The decision to NOT wire a `settlement-self-events` RabbitMQ queue (the prompt's explicit out-of-scope item) saves an unnecessary cross-process round-trip. The S3 tests verified the handlers' behavior via direct invocation; the S5 / S6 saga-driven path will exercise the local-dispatch routing as a side effect. This is one of the situations where "don't pre-engineer" saved real plumbing — the temptation to be defensive and wire a self-loop queue would have added two Program.cs lines and a queue topology that no consumer actually needs.

7. **The cutover gate's joint-authority discipline holds across S1 / S2 / S3.** Three consecutive slices have inherited the `Narrative:` metadata line and operated against the joint-authority scope without ceremony. Narrative 002 did not dramatise the projection's lifecycle at S3 (the projection is below the saga-Moment grain narrative 002 dramatises), but the narrative's Cast, Setting, and Moment-level framing remained the design ground — the `PendingSettlement` row was named in narrative 002 Moment 1's `Context`, the `BidderCreditView` separation was named in Moment 3's `Things deliberately not included`. The narrative is doing its job as design witness; the slices implement against it without adding methodology overhead.

---

## Skill gaps surfaced

- **`adding-bc-module.md` — nothing to add from this session.** The skill's BC module pattern, the Marten BC schema registration shape, and the cross-BC handler isolation guidance all applied verbatim. The discoveries at S3 (foreign-BC fixture exclusion needed in the reverse direction) reinforce the existing guidance rather than expand it.
- **`critter-stack-testing-patterns.md` §Cross-BC Handler Isolation — one observation to surface in a future amendment.** Key Learning #1 (the bidirectional N-1 exclusion pattern is slice-aligned with handler additions) and Key Learning #3 (foreign-BC handlers running silently against unregistered schemas) are real distinctions worth a callout in §Cross-BC Handler Isolation. Not fixed in this session per the prompt's "do not edit skills in-session" rule beyond the planned `marten-projections.md` cash-in; flagged for the next skills-maintenance pass.
- **M5 milestone doc §2 — `ListingPassed` extension to `settlement-auctions-events` is undocumented.** The wiring table lists `ListingSold` and `BuyItNowPurchased` only. Recorded in the prompt's Open Questions and confirmed at session-start scoping; the milestone doc itself can be amended in a future doc-cleanup pass. Not fixed here because milestone-doc edits are higher-orbit than slice-level work and the project convention is that milestone docs change via dedicated PRs.
- **Skill-amendment placement — Single-Source-Seeded Caches placed inside §6.** The Open Question #4 leaned this way and the placement decision held. No skill-file refactor needed; the new subsection is the right neighbor of the canonical handler-driven pattern.

---

## Findings against narrative

The slice operated against narrative 002 as a design witness rather than as a Moment-grain implementation reference. Narrative 002 names `PendingSettlement` in Moment 1's `Context.` ("The PendingSettlement row for the keyboard sits in `Status: Pending`, cached since `ListingPublished` arrived days before") and Moment 1's `Response.` ("The `PendingSettlement` row for the keyboard transitions from `Status: Pending` to `Status: Consumed`"). The S3 implementation matches both renderings without modification.

| Lane | Action |
|---|---|
| `narrative-update` | None. The narrative 002 Cast / Setting / Moment 1 framing for `PendingSettlement` matches the implementation as authored. |
| `workshop-update` | None. W003 §8 specifies the lifecycle exactly as implemented; the §8.2 / §8.3 deferral (no `ListingRevised`) is a producer-side gap, not a workshop drift. |
| `code-update` | Three contract docstring corrections (`Auctions/ListingPassed.cs`, `Selling/ListingPublished.cs`, `Selling/ListingWithdrawn.cs`). Resolved in this PR. |
| `document-as-intentional` | None. |

The cumulative narrative 002 findings ledger is unchanged: F001 ✓ (PR #20), F002 ✓ (PR #25), F003 ✓ minimum-scope (PR #20), F004 ✓ (PR #25), F005 ✓ (PR #25). All five pre-existing findings remain closed; no new findings against narrative 002 in S3.

---

## Verification checklist

- [x] `src/CritterBids.Settlement/PendingSettlement.cs` defines `public sealed record PendingSettlement` with `init`-only properties `Id`, `SellerId`, `ReservePrice`, `BuyItNowPrice`, `FeePercentage`, `PublishedAt`, `Status`.
- [x] `src/CritterBids.Settlement/PendingSettlementStatus.cs` defines `public enum PendingSettlementStatus { Pending, Consumed, Expired, Failed }`.
- [x] `src/CritterBids.Settlement/PendingSettlementHandler.cs` defines `public static class PendingSettlementHandler` with five `Handle(EventType, IDocumentSession, CancellationToken)` overloads — one each for `ListingPublished`, `ListingPassed`, `ListingWithdrawn`, `SettlementCompleted`, `PaymentFailed`.
- [x] `src/CritterBids.Settlement/CritterBids.Settlement.csproj` has a `<ProjectReference>` to `CritterBids.Contracts.csproj`.
- [x] `src/CritterBids.Settlement/SettlementModule.cs` `ConfigureMarten` block adds `opts.Schema.For<PendingSettlement>().DatabaseSchemaName("settlement")`. Zero `opts.Events.AddEventType<T>()` calls; zero `opts.Projections.*` calls.
- [x] `src/CritterBids.Api/Program.cs` RabbitMQ block adds two `opts.PublishMessage<T>().ToRabbitQueue("settlement-selling-events")` lines plus `opts.ListenToRabbitQueue("settlement-selling-events")`; three `opts.PublishMessage<T>().ToRabbitQueue("settlement-auctions-events")` lines plus `opts.ListenToRabbitQueue("settlement-auctions-events")`.
- [x] `Program.cs` does not gain a `settlement-self-events` queue.
- [x] `tests/CritterBids.Settlement.Tests/PendingSettlementHandlerTests.cs` exists; six `[Fact]` methods cover §8.1 / §8.4 / §8.5 / §8.6 / §8.7 / §8.8.
- [x] `docs/skills/marten-projections.md` replaces the "Pending: M5-S3 amendment" callout and authors the cross-BC-event-seeded projection pattern in §6.
- [x] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [x] `dotnet test CritterBids.slnx` — all green; baseline 88 tests still pass; six new Settlement projection tests pass; total 94.
- [x] This retrospective exists; mirrors the M5-S2 retro shape; records the handler-shape choice, the queue-payload-extension decision, the §8.2 / §8.3 deferral rationale, the foreign-fixture exclusion discovery, and a "what M5-S4 should know" note (below).

---

## What M5-S4 should know

**M5-S4 lands the Settlement saga's seven-phase happy-path implementation** — the saga consumes `ListingSold` (and S5 adds `BuyItNowPurchased`) and runs the workflow Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed. Concrete items S4 should walk in with:

1. **The `SettlementSaga` shell stays as-is until S4 starts editing it.** S3 left it untouched per the prompt's out-of-scope discipline. S4's first edit is to add `SettlementStatus` enum, the per-phase state fields (`HammerPrice`, `FeePercentage`, `FeeAmount`, `SellerPayout`, participant identifiers), and the per-phase `Handle` methods.

2. **`PendingSettlement` is the saga's load-at-start data source.** The saga's `Handle(ListingSold)` (and `Handle(BuyItNowPurchased)`) loads the `PendingSettlement` row by `ListingId` to retrieve the reserve, BIN price, fee percentage, and seller identity that the `ListingSold` payload deliberately does not carry. This is the W003 Phase 1 Part 1 framing operationalized — the projection exists for exactly this lookup. If the row is missing (the W003 Phase 1 Part 1 race-condition Option A scenario), the saga's Wolverine retry policy retries the inbound message with backoff until the projection has caught up. The retry policy is a Wolverine convention; S4 wires it via `OnException<PendingSettlementNotFoundException>().RetryWithCooldown(...)` analogous to `AuctionsConcurrencyRetryPolicies` in `AuctionsModule.cs`.

3. **The deterministic `SettlementId` derivation lands in S4 with the saga's `Handle(ListingSold)` first emit.** Per W003 Phase 1 Part 6: `SettlementId = UuidV5(AuctionsNamespace, $"settlement:{ListingId}")`. Idempotent by construction; a duplicate `ListingSold` consumption derives the same `SettlementId` and the saga's state guard rejects re-initiation. The namespace constant lives in `SettlementsIdentityNamespaces.cs` (S4 authors the file analogous to `AuctionsIdentityNamespaces.cs`).

4. **The five Settlement-internal events land in `src/CritterBids.Settlement/`** (not in `Contracts/`) — `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`, `SellerPayoutIssued` per W003 §"Integration in/out". The integration-out trio (`SettlementCompleted`, `PaymentFailed`, the third `SellerPayoutIssued` — wait, `SellerPayoutIssued` is both Settlement-internal AND integration-out per the M5 milestone doc §2; verify the W003 vocabulary at S4 start). Each event type registered via `opts.Events.AddEventType<T>()` in `SettlementModule.ConfigureMarten` at first emit per the M2 silent-`AggregateStreamAsync<T>`-null lesson.

5. **`SettlementCompleted` self-consumption is wired and ready at S3.** When the saga's terminal handler emits `SettlementCompleted` via `OutgoingMessages` in S5 (or wherever the workflow's terminal Completed phase is implemented), Wolverine's `MultipleHandlerBehavior.Separated` will dispatch to both the cross-BC publish route (`listings-settlement-events`, wired in S6 — not yet) and the local `PendingSettlementHandler.Handle(SettlementCompleted)` consumer (wired at S3). The S3 wiring already covers the projection's status update on saga completion.

6. **`SettlementCompleted` and `PaymentFailed` integration-event publish routes are NOT wired yet.** S6 wires `listings-settlement-events` for `SettlementCompleted` (Listings consumes); S5 wires the `PaymentFailed` route to wherever Operations consumes (post-M5 likely deferred — Operations doesn't ship at M5 close). S4's saga emits both via `OutgoingMessages`; the messages will land in `tracked.NoRoutes` in saga-handler tests until the publish routes wire in S5 / S6. Mirror the Auctions saga's `tracked.NoRoutes.MessagesOf<ListingSold>()` assertion shape for outgoing-event tests.

7. **`wolverine-sagas.md` skill-file amendment is queued for M5-S4's retrospective.** ADR-019 §Consequences flags the Settlement-side example as in-scope for the M5-S4 retro. The seven-phase saga structurally distinct from the M3-S5 Auction Closing saga's two-phase shape — worth documenting as a saga-pattern variant.

8. **The saga's queue subscription is already in place.** S3 wired `opts.ListenToRabbitQueue("settlement-auctions-events")` covering `ListingSold`, `BuyItNowPurchased`, and `ListingPassed`. S4's saga handlers fire from this subscription. No Program.cs edit needed for queue topology in S4.

9. **`marten-projections.md` is not S4 territory.** The skill cash-in for M5-S3 closed the cross-BC-event-seeded projection pattern documentation. S4 lands `wolverine-sagas.md` amendments, not `marten-projections.md` edits.

10. **§8.2 and §8.3 (the `ListingRevised` projection scenarios) remain deferred.** S4 is saga territory; it does not introduce `ListingRevised` consumers. When a future Selling-side slice (post-M5) authors the `ListingRevised` contract and producer, a follow-up Settlement slice (post-M5) extends `PendingSettlementHandler` with a `Handle(ListingRevised)` overload and authors the §8.2 / §8.3 tests.

---

## What remains / deferred into later M5 sessions

**In scope for M5, deferred to later slices:**

- Settlement saga's seven-phase happy path; `ListingSold` consumer; deterministic `SettlementId` derivation (UUID v5); `SettlementsIdentityNamespaces.cs`; the five Settlement-internal events; `wolverine-sagas.md` skill amendment (S4)
- Failure-path scenarios (`PaymentFailed`); BIN source path (`BuyItNowPurchased` consumer); `BidderCreditView` projection (S5)
- `SettlementCompleted` integration-event publish route (`listings-settlement-events`); Listings-side `CatalogListingView.Status = "Settled"` extension (S6)
- M5 milestone retrospective (after S6 ships)

**In scope for M5, deferred to a doc-cleanup pass (any milestone):**

- M5 milestone doc §2 wiring table — extend the `settlement-auctions-events` row to include `ListingPassed`. Recorded in this retro and the M5-S3 prompt's Open Questions for visibility.
- `critter-stack-testing-patterns.md` §Cross-BC Handler Isolation — surface the "bidirectional N-1 exclusion is slice-aligned with handler additions" observation and the "foreign-BC handlers running silently against unregistered schema" failure-mode note. Both are covered by Key Learnings #1 and #3 from this retro.

**Out of scope for M5, tracked elsewhere:**

- `ListingRevised` contract authoring (Selling-side, post-M5); §8.2 / §8.3 implementation (post-M5 follow-up Settlement slice once `ListingRevised` ships)
- Real payment-processor integration — post-MVP per W003 §"Winner Charge"
- Compensation paths beyond MVP — post-MVP per W003 Phase 1 Part 3
- W003 broader storage-staleness sweep (narrative 002 F003's references at L29 / L649 / L663) — future workshop-cleanup session
- `ProcessManager<TState>` framework primitive — out of scope per CritterBids' shipped-Wolverine stance (ADR-019)
- M6 frontend MVP design — `[AllowAnonymous]` posture unchanged through M5

**Cumulative cross-BC handler isolation matrix at M5-S3 close:**

| Fixture | Excludes |
|---|---|
| Auctions.Tests | Selling, Listings, Settlement (3) |
| Listings.Tests | Selling, Settlement (2) |
| Selling.Tests | Settlement (1) |
| Settlement.Tests | Selling, Auctions, Listings (3) |
| Participants.Tests | (none — Participants tests work standalone) |

The matrix is symmetric for any pair of BCs whose handlers consume each other's events. The Listings → Auctions exclusion has not been added yet; Listings tests pass without it because Listings's `AuctionStatusHandler` is the discovered handler and its module IS registered. As more BCs ship (Obligations, Relay, Operations remain), this matrix continues filling in. Future slices should treat fixture-exclusion auditing as a mechanical step at handler-addition time.
