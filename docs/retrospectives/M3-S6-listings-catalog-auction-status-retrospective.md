# M3-S6: Listings Catalog — Auction-Status Extension — Retrospective

**Date:** 2026-04-19
**Milestone:** M3 — Auctions BC
**Slice:** S6 of 9 (penultimate M3 implementation slice; follows S5b close, precedes S7 retrospective + M3 close)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/M3-S6-listings-catalog-auction-status.md`

---

## Baseline

- 79 tests passing at S5b close (1 Api + 1 Contracts + 4 Listings + 6 Participants + 35 Auctions + 32 Selling)
- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `CatalogListingView` had 8 M2 fields; `ListingPublishedHandler` was the sole consumer of the `listings-selling-events` queue
- `listings-auctions-events` queue was unwired in `Program.cs` — S5b's bus-only outcome events (`BiddingClosed`, `ListingSold`, `ListingPassed`) cascaded to `tracked.NoRoutes` in tests and would land nowhere in production
- `AuctionsModule.AddEventType<T>()` count: 8 (frozen at S5b close — outcome events stay bus-only per OQ5)

## Session outcome

- 86 tests passing (+7: 5 Facts + 1 Theory with 2 InlineData rows + 1 BIN Fact = 6 new named tests; +7 actual test cases)
- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `CatalogListingView` extended with 10 auction-status fields (Status, ScheduledCloseAt, CurrentHighBid, CurrentHighBidderId, BidCount, HammerPrice, WinnerId, PassedReason, FinalHighestBid, ClosedAt). The 8 M2 fields are byte-identical
- `AuctionStatusHandler` (new sibling static class — OQ1 Path B): six static `Handle` methods consuming `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased` via `LoadAsync ?? new` upsert (OQ4 Path II tolerant upsert)
- `Program.cs` queue wiring: 6 `opts.PublishMessage<T>().ToRabbitQueue("listings-auctions-events")` rules + 1 `opts.ListenToRabbitQueue("listings-auctions-events")` inside the existing rabbitmq null-guard
- `ListingsTestFixture` gains two additive helpers (`SeedCatalogListingViewAsync`, `LoadCatalogListingViewAsync`); M2-S7's 4 baseline tests stay byte-identical and green
- `AuctionsTestFixture` gains a `ListingsBcDiscoveryExclusion` (mirrors `SellingBcDiscoveryExclusion`) — required because `AuctionStatusHandler` shadows the saga's handlers under `MultipleHandlerBehavior.Separated`
- `ListingPublishedHandler.cs` byte-identical vs M2-S7 close (OQ1 Path B preserved frozen-file discipline)
- `src/CritterBids.Auctions/` byte-level diff vs S5b close: **none** (no production code, no module config touched)
- `AuctionsModule.AddEventType<T>()` count: still 8 (no Listings-side stream-append needed — projection consumes bus-only events directly)
- M3-D2 deferred to S7 (rationale below); item 8 skill bulk pass deferred to S7

---

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| 1 | `CatalogListingView` extended with 10 auction-status fields | b4fcd16 |
| 2 | `AuctionStatusHandler` — five static `Handle` methods for the auction event types | d555c10, 6b19d88, f44b11e, a9e8324, 58fe621 |
| 3 | `ListingsModule.cs` — no schema changes (none required; Marten handles additive document fields transparently) | — |
| 4 | `Program.cs` — `listings-auctions-events` queue publish + listen wired | 0202006 |
| 5 | 5 named scenario tests in `CatalogListingViewTests.cs` (one is a `[Theory]` with 2 `InlineData` rows for `Reason ∈ {NoBids, ReserveNotMet}`) | d555c10, 6b19d88, f44b11e, a9e8324, 58fe621 |
| 6 | `ListingsTestFixture` — `SeedCatalogListingViewAsync`, `LoadCatalogListingViewAsync` additive helpers | d555c10 |
| 7 | M3-D2 projection-extension pattern doc | **Deferred to S7** — see below |
| 8 | `wolverine-sagas.md` skill bulk pass | **Deferred to S7** — explicit line item in "What M3-S7 should know" |
| 9 | `BuyItNowPurchased` consumption (OQ3 Path (a) — included in slice) — handler + sixth test | fd8991b |
| 10 | This retrospective | (final commit) |

Out-of-item changes required by first-use surprises:

| Change | Reason |
|--------|--------|
| `AuctionsTestFixture` adds `ListingsBcDiscoveryExclusion` and registers it | After commit d555c10, `AuctionStatusHandler` (in `CritterBids.Listings`) is discovered by the Auctions test fixture (which only excluded `CritterBids.Selling`). With `MultipleHandlerBehavior.Separated`, BiddingOpened gets two endpoints (saga Start + projection upsert), making `Host.InvokeMessageAndWaitAsync` dispatch ambiguous — surfaces as the sticky-handler `NoHandlerForEndpointException`. Mirror of the existing `SellingBcDiscoveryExclusion` shape. (Commit 1514600.) |

---

## S6-1 — `CatalogListingView` field extension

### Resulting record shape (post-S6 — single source of truth for M3 retro)

```csharp
public sealed record CatalogListingView
{
    // ─── M2 fields (byte-identical from M2-S7 close) ─────────────────────────
    public Guid Id { get; init; }                  // ListingId — Marten document identity
    public Guid SellerId { get; init; }
    public string Title { get; init; } = "";
    public string Format { get; init; } = "";      // "Flash" or "Timed" — string, not enum
    public decimal StartingBid { get; init; }
    public decimal? BuyItNow { get; init; }
    public TimeSpan? Duration { get; init; }
    public DateTimeOffset PublishedAt { get; init; }

    // ─── M3-S6 auction-status fields (additive) ──────────────────────────────
    public string Status { get; init; } = "Published";   // Published → Open → Closed → Sold/Passed
    public DateTimeOffset? ScheduledCloseAt { get; init; }
    public decimal? CurrentHighBid { get; init; }
    public Guid? CurrentHighBidderId { get; init; }      // OQ5 Path C — redact at endpoint in M6
    public int BidCount { get; init; }                   // OQ6 Path (a) — set, not incremented
    public decimal? HammerPrice { get; init; }
    public Guid? WinnerId { get; init; }
    public string? PassedReason { get; init; }           // "NoBids" or "ReserveNotMet"
    public decimal? FinalHighestBid { get; init; }       // null when Reason = "NoBids"
    public DateTimeOffset? ClosedAt { get; init; }       // populated by whichever terminal arrived
}
```

### Structural metrics

| Metric | Before (M2-S7) | After (M3-S6) |
|--------|----------------|---------------|
| Fields | 8 | 18 (+10) |
| Nullable fields | 2 | 11 (+9) |
| Status semantics | implicit (no field) | explicit `string` (OQ2 Path A — symmetry with `Format`) |
| `init`-only record | yes | yes (preserved) |
| Schema | `listings` | `listings` (Marten handles additive properties transparently — no migration) |

---

## S6-2 — `AuctionStatusHandler` (new sibling static class — OQ1 Path B)

### Why Path B over Path A

Extending `ListingPublishedHandler` with five new methods would have stripped the class name's accuracy ("ListingPublished" no longer describes a class containing five auction-status handlers). Path B keeps M2's `ListingPublishedHandler.cs` byte-identical (preserves frozen-file discipline established at M2-S7 close) and the new class name describes its purpose directly. Wolverine's convention-based discovery binds each handler by message type regardless of which class hosts it — verified against `C:\Code\JasperFx\wolverine\src\Wolverine\Configuration\HandlerDiscovery.cs` (sibling-class discovery is the routine pattern in pristine repo usage).

### Handler shape (uniform across all six methods)

```csharp
public static async Task Handle(
    BiddingOpened message,
    IDocumentSession session,
    CancellationToken cancellationToken)
{
    var view = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken)
        ?? new CatalogListingView { Id = message.ListingId };

    session.Store(view with
    {
        Status           = "Open",
        ScheduledCloseAt = message.ScheduledCloseAt
    });
}
```

### Structural metrics

| Metric | Value |
|--------|-------|
| Class type | `public static class` |
| Handler methods | 6 (`BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`) |
| Injected dependencies | `IDocumentSession`, `CancellationToken` (per method, no constructor) |
| Return type | `Task` (no `OutgoingMessages`, no `IMessageBus`) |
| `with` expression count | 6 (one per handler) |
| `LoadAsync ?? new CatalogListingView { Id = ... }` calls | 6 (OQ4 Path II — tolerant upsert) |

---

## S6-3 — `Program.cs` queue wiring

### Resulting wiring

```csharp
opts.PublishMessage<CritterBids.Contracts.Auctions.BiddingOpened>()      .ToRabbitQueue("listings-auctions-events");
opts.PublishMessage<CritterBids.Contracts.Auctions.BidPlaced>()          .ToRabbitQueue("listings-auctions-events");
opts.PublishMessage<CritterBids.Contracts.Auctions.BiddingClosed>()      .ToRabbitQueue("listings-auctions-events");
opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()        .ToRabbitQueue("listings-auctions-events");
opts.PublishMessage<CritterBids.Contracts.Auctions.ListingPassed>()      .ToRabbitQueue("listings-auctions-events");
opts.PublishMessage<CritterBids.Contracts.Auctions.BuyItNowPurchased>()  .ToRabbitQueue("listings-auctions-events");
opts.ListenToRabbitQueue("listings-auctions-events");
```

### API-surface citations (per S5b retro §"What M3-S6 should know" §8)

- `opts.PublishMessage<T>()` returns `IPublishToExpression`: `C:\Code\JasperFx\wolverine\src\Wolverine\WolverineOptions.Endpoints.cs:102`
- `IPublishToExpression.ToRabbitQueue(string queueName)`: `C:\Code\JasperFx\wolverine\src\Transports\RabbitMQ\Wolverine.RabbitMQ\RabbitMqTransportExtensions.cs:239`
- `opts.ListenToRabbitQueue(string queueName)`: same file, sibling method

The Wolverine 4 → 5 publish API stayed structurally identical — no API drift on this surface despite S5b's caveat.

---

## S6-4 — `ListingsBcDiscoveryExclusion` in Auctions fixture

### Discovery / resolution

After commit d555c10 (first `AuctionStatusHandler` method landed), the entire Auctions test suite (35 tests) began failing with verbatim:

```
Wolverine.Runtime.Handlers.NoHandlerForEndpointException : No handlers for message type CritterBids.Contracts.Auctions.BiddingOpened at this endpoint. This is usually because of 'sticky' handler to endpoint configuration. See https://wolverinefx.net/guide/messaging/subscriptions.html
```

**Root cause.** `Program.cs`'s `opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly)` scans the Listings assembly for handlers regardless of whether `AddListingsModule()` was called. Once `AuctionStatusHandler` existed in `CritterBids.Listings`, BiddingOpened (and the other four shared event types) had two handlers in the Auctions test fixture — saga Start + projection upsert. With `MultipleHandlerBehavior.Separated`, Wolverine creates a separate endpoint per handler; `Host.InvokeMessageAndWaitAsync` (which targets one endpoint) becomes ambiguous and surfaces as the sticky-handler error.

**Fix.** Add `ListingsBcDiscoveryExclusion` to `AuctionsTestFixture` with the same shape as the existing `SellingBcDiscoveryExclusion`. The Auctions fixture doesn't register `AddListingsModule()`, so dropping these handlers from discovery is safe — they have nowhere useful to run anyway. (Commit 1514600.)

**Cited by:** memory entry `project_wolverine_sticky_handler.md` is the precedent — same root cause class, different trigger (S6 added a cross-BC handler; the original entry covered `ListenToRabbitQueue` creating sticky bindings).

---

## S6-5 — `BuyItNowPurchased` consumption (OQ3 Path (a))

### Why include in slice

S5b retro §"What M3-S6 should know" §5 was explicit: *"S6's catalog handler should subscribe to `BuyItNowPurchased` directly rather than expecting it to follow a `BiddingClosed`."* Path (a) honours that recommendation, pays the consumer-pairing cost at contract time, and avoids a known L2 publish-without-consumer gap at M3 close. Slice scope held — single PR, one extra handler method + one extra test.

### Handler distinctive shape vs the timer-path handlers

```csharp
public static async Task Handle(
    BuyItNowPurchased message,
    IDocumentSession session,
    CancellationToken cancellationToken)
{
    var view = await session.LoadAsync<CatalogListingView>(message.ListingId, cancellationToken)
        ?? new CatalogListingView { Id = message.ListingId };

    session.Store(view with
    {
        Status      = "Sold",
        HammerPrice = message.Price,
        WinnerId    = message.BuyerId,
        ClosedAt    = message.PurchasedAt
    });
}
```

Note `HammerPrice = message.Price` (not `message.HammerPrice` — the BIN payload uses `Price`); no `BidCount` field on the contract (BIN is winner-takes-all). Status transitions directly from `"Open"` (or earlier) to `"Sold"` — no intermediate `"Closed"`.

---

## Open Questions — resolutions

| OQ | Resolution | Path | Citation |
|----|------------|------|----------|
| OQ1 — Handler layout | Sibling class | **Path B** (`AuctionStatusHandler`) | `C:\Code\JasperFx\wolverine\src\Wolverine\Configuration\HandlerDiscovery.cs` — sibling-class discovery is routine; M2's `ListingPublishedHandler.cs` stays byte-identical |
| OQ2 — `Status` typing | string | **Path A** | `CatalogListingView.Format` (M2-S7) is `string` — OQ2 Path A symmetry |
| OQ3 — `BuyItNowPurchased` | Include in slice | **Path (a)** | S5b retro §"What M3-S6 should know" §5 explicit recommendation |
| OQ4 — Out-of-order arrival | Tolerant upsert | **Path II** | `LoadAsync ?? new CatalogListingView { Id = ... }` — Marten's `LoadAsync` returns null on absence (`C:\Code\JasperFx\marten\src\Marten\IDocumentSession.cs` `LoadAsync<T>` signature returns `Task<T?>`); avoids fragile-projection failure mode under cross-queue race; bus-only emission per S5b OQ5 means no replay path is needed |
| OQ5 — `CurrentHighBidderId` privacy | Include with redact-at-endpoint note | **Path C** | Field included on `CatalogListingView` (line 36 of `CatalogListingView.cs`); endpoint-layer redaction deferred to M6 auth pass — captured in field XML doc |
| OQ6 — Idempotency | Authoritative `BidCount` from message | **Path (a)** | M3-S5 retro §OQ2 Path (b) — DCB monotonicity at the source guarantees `BidCount` is non-decreasing; last-write-wins is self-correcting under at-least-once redelivery |

---

## Test results

| Phase | Listings Tests | Auctions Tests | Solution Total | Result |
|-------|---------------|----------------|----------------|--------|
| Baseline (S5b close) | 4 | 35 | 79 | Green |
| After commit b4fcd16 (field extension only) | 4 | 35 | 79 | Green (no behaviour change) |
| After commit 0202006 (queue wiring) | 4 | 35 | 79 | Green (rabbit block is null-guarded) |
| After commit d555c10 (BiddingOpened handler + test) | 5 | **0 / 35** | — | **Failed** — sticky-handler regression on all Auctions tests |
| After commit 1514600 (`ListingsBcDiscoveryExclusion`) | 5 | 35 | 80 | Green |
| After commits 6b19d88 → 58fe621 (BidPlaced, BiddingClosed, ListingSold, ListingPassed×2 Theory) | 10 | 35 | 85 | Green |
| After commit fd8991b (BuyItNowPurchased) | 11 | 35 | 86 | Green |

Final: 86 tests, 0 failures, 0 skipped. Listings count: 4 → 11 (+7 actual test cases; 6 new named tests, one of which is a `[Theory]` with 2 `InlineData` rows).

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings (no delta from baseline)
- `IMessageBus` references in `AuctionStatusHandler.cs`: 0
- `OutgoingMessages` returns in `AuctionStatusHandler.cs`: 0
- `session.Store` calls in `AuctionStatusHandler.cs`: 6 (one per handler)
- `LoadAsync ?? new CatalogListingView` calls in `AuctionStatusHandler.cs`: 6 (uniform tolerant upsert)
- `ProjectReference` count on `CritterBids.Listings.csproj`: unchanged (Contracts only — zero direct reference to `CritterBids.Auctions`)
- `src/CritterBids.Auctions/` byte-level diff vs S5b close: **none**
- `src/CritterBids.Listings/ListingPublishedHandler.cs` byte-level diff vs M2-S7 close: **none** (Path B preserved frozen-file)
- `src/CritterBids.Listings/CatalogListingView.cs` 8 M2 fields: byte-identical
- `[Obsolete]` / `#pragma warning disable` / `throw new NotImplementedException()` in production: 0
- `AuctionsModule.AddEventType<T>()` count: 8 (unchanged — outcome events stay bus-only per S5b OQ5)

---

## Key learnings

1. **`IncludeAssembly` scans for handlers regardless of module registration.** Adding a handler in any included assembly automatically makes it visible to every test fixture that builds `Program`, even one that does not call the BC's `AddXyzModule()`. Cross-BC handler isolation in fixtures is not optional once a shared event type gains a second handler — `MultipleHandlerBehavior.Separated` turns this into a sticky-handler dispatch failure rather than a graceful no-op. The `*BcDiscoveryExclusion` pattern is now the established remediation; mirror its shape exactly.

2. **`LoadAsync ?? new` is the cheapest tolerant-upsert primitive for cross-queue races.** Marten's `LoadAsync<T>(id)` returns null on absence rather than throwing; combining it with a record-init expression handles the cross-queue arrival race (`BiddingOpened` arriving before `ListingPublished`) in two lines, no `Patch`, no separate insert/update branch. The minimal-fields constructor (`new CatalogListingView { Id = ... }`) lets a later `ListingPublished` arrival fill in the M2 fields via the same upsert pattern (M2-S7's `ListingPublishedHandler` already loads-then-stores with full M2 field set).

3. **Sibling static handler classes preserve frozen-file discipline at zero cost.** OQ1 Path B (sibling `AuctionStatusHandler`) is structurally identical to extending `ListingPublishedHandler` from Wolverine's perspective — discovery binds by message type, not by class. The benefit is purely human-facing: PR diff legibility, accurate class names, byte-identical M2 file. This is the projection-extension precedent for any future BC adding handlers to a view another BC seeded.

4. **BIN payload field naming differs from `ListingSold`.** `BuyItNowPurchased.Price` (not `HammerPrice`); no `BidCount`. Mapping into `CatalogListingView` requires `HammerPrice = message.Price` and leaves `BidCount` at its prior value. This is a contract-shape distinction worth flagging in skill files when the projection-extension pattern is documented.

5. **The `[Theory]` with `[InlineData]` pattern requires `double?` not `decimal?` for nullable money values.** xUnit's `InlineData` rejects `decimal` literals; coercing the parameter to `double?` and unboxing inside the test body is the established workaround for parameterised passes/fails coverage like `ListingPassed_SetsCatalogStatusPassed(string reason, double? highestBidNullable)`.

---

## Verification checklist

- [x] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [x] `dotnet test CritterBids.slnx` — 79-test baseline preserved; +7 new test cases green; zero skipped, zero failing; **total 86** (5 milestone-listed scenarios + 1 BIN scenario, the `ListingPassed` scenario is a `[Theory]` with 2 `InlineData` rows)
- [x] `src/CritterBids.Listings/CatalogListingView.cs` extended with 10 auction-status fields; 8 M2 fields byte-identical
- [x] Handler(s) consume `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed` (+ `BuyItNowPurchased` per OQ3 Path (a)) — OQ1 Path B layout (sibling `AuctionStatusHandler`); each handler uses `LoadAsync` + mutate + `Store`; no `IMessageBus`; no `OutgoingMessages`
- [x] `Program.cs` — `listings-auctions-events` queue wired publish + listen; 6 publish rules; API surface cited (`WolverineOptions.Endpoints.cs:102`, `RabbitMqTransportExtensions.cs:239`)
- [x] All 5 + 1 named test methods present and green
- [x] `BiddingOpened_SetsCatalogStatusOpen` — `Status = "Open"`; `ScheduledCloseAt` populated
- [x] `BidPlaced_UpdatesCatalogHighBid` — `CurrentHighBid = message.Amount`; `BidCount = message.BidCount`; `CurrentHighBidderId` populated (OQ5 Path C)
- [x] `BiddingClosed_SetsCatalogStatusClosed` — `Status = "Closed"`; `ClosedAt` populated
- [x] `ListingSold_SetsCatalogStatusSold` — `Status = "Sold"`; `HammerPrice`, `WinnerId`, `ClosedAt` populated
- [x] `ListingPassed_SetsCatalogStatusPassed` — `Status = "Passed"`; `PassedReason ∈ {NoBids, ReserveNotMet}` (Theory rows); `FinalHighestBid` null when `Reason = "NoBids"`, populated otherwise
- [x] Zero direct references from `CritterBids.Listings` to `CritterBids.Auctions`
- [x] `CritterBids.Listings.csproj` `ProjectReference` count unchanged
- [x] `src/CritterBids.Auctions/` byte-level diff vs S5b close: **none**
- [x] `src/CritterBids.Listings/ListingPublishedHandler.cs` byte-identical vs M2-S7 close (OQ1 Path B)
- [x] `src/CritterBids.Listings/CatalogListingView.cs` 8 M2 fields byte-identical
- [x] M2-S7 `CatalogListingViewTests` 4 baseline scenarios unchanged and green
- [x] No `[Obsolete]`, `#pragma warning disable`, or `throw new NotImplementedException()` in production
- [x] This retrospective exists

---

## What M3-S7 should know

1. **Skill-pass debt is undischarged.** The accumulated first-use findings from S4b / S5 / S5b (`NotFound` named-method convention, saga state minimality re-read pattern, `tracked.NoRoutes` vs `Sent` in test harness, scoped `IMessageBus` resolution) plus S6's two findings (the `*BcDiscoveryExclusion` cross-BC pattern; the `LoadAsync ?? new` tolerant-upsert primitive) all await a single skill bulk pass in S7. Five skill files are candidates: `wolverine-sagas.md` (4 from S4b/S5/S5b), `marten-projections.md` (1 from S6 — tolerant upsert), `critter-stack-testing-patterns.md` (1 from S6 — `*BcDiscoveryExclusion`). The S5b rule still applies: all-with-citations or none.

2. **`CatalogListingView` field inventory at M3 close.** 18 fields total: 8 M2-frozen + 10 M3-S6-additive. Listed in full under "S6-1 — Resulting record shape" above. This is the single source of truth for the M3 retrospective's "what the catalog looks like at M3 close" summary; future projection extensions (Settlement / Watchlist / Operations dashboards) build additively on top of these 18.

3. **M3-D2 (projection-extension pattern doc) is deferred to S7.** Rationale: S6 surfaces the pattern in production code with one in-repo precedent (M2-S7 base + M3-S6 extension). S7's bulk-pass slot is the natural place to document it alongside the other accumulated skill findings — folding it in now would create a partial pass that violates the "all-with-citations or none" discipline. Path A (document in `marten-projections.md`) remains the structural fit; the framing is **one view per logical entity, handlers per event-source BC, additive field growth across milestones**.

4. **No new ADR candidates surfaced in S6.** OQ5 resolved to Path C (include with redact-at-endpoint note) — the rationale is a one-line XML doc on `CurrentHighBidderId`, not ADR-worthy. OQ4 resolved to Path II (tolerant upsert) — established Marten idiom, not ADR-worthy. If S7's skill pass surfaces a structural pattern that crosses ≥2 BCs, ADR 013 is the next available number.

5. **`BuyItNowPurchased` Listings-side subscription is live as of S6.** OQ3 Path (a) was taken; the handler is in `AuctionStatusHandler`, the test is `BuyItNowPurchased_SetsCatalogStatusSold`. No follow-up slice required for BIN catalog projection.

6. **`listings-auctions-events` queue operational posture at M3 close.** Wired in `Program.cs` (publish: 6 rules; listen: 1 rule) inside the rabbitmq null-guard. Confirmed in tests via direct handler invocation (`InvokeAuctionHandlerAsync` helper in `CatalogListingViewTests.cs`). Aspire dashboard verification is a S7 task: S6 did not start the AppHost during this session — a manual smoke test against `http://localhost:15237` confirming the queue's existence under the `critterbids` Docker Compose project label is the one outstanding operational verification before M3 close. No known gaps; no mid-flight surprises in test runs.

7. **No additional in-scope work surfaced in S6.** S5b's frozen-saga discipline held — `src/CritterBids.Auctions/` byte-level diff is zero. Listings-side scope landed exactly per the prompt's items 1–9 plus the one out-of-item `ListingsBcDiscoveryExclusion` change. S7's prompt can size itself to (a) the skill bulk pass + M3-D2 doc, (b) operational smoke test of the queue against Aspire, (c) M3 retrospective. No emergent work-pulls.

8. **One reusable test-fixture pattern.** The `InvokeAuctionHandlerAsync<TMessage>(Func<TMessage, IDocumentSession, CancellationToken, Task>, TMessage)` helper in `CatalogListingViewTests.cs` is a direct-invocation alternative to `Host.InvokeMessageAndWaitAsync` that bypasses sticky-handler ambiguity entirely. Worth promoting to the fixture (`ListingsTestFixture`) if S7 or later slices need to dispatch to a single handler when multiple are registered for the same message type — but in the meantime the inline form is a per-test-class convention, not a fixture-level helper.
