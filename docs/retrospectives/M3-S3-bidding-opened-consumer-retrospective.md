# M3-S3: BiddingOpened Consumer (Selling → Auctions) — Retrospective

**Date:** 2026-04-17
**Milestone:** M3 — Auctions BC
**Session:** S3 of 7
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M3-S3-bidding-opened-consumer.md`
**Baseline:** 45 tests passing · `dotnet build` 0 errors, 0 warnings · M3-S2 complete

---

## Baseline

- 45 tests passing (1 Api + 1 Contracts + 1 Auctions + 4 Listings + 6 Participants + 32 Selling)
- `dotnet build` — 0 errors, 0 warnings
- `src/CritterBids.Auctions/CritterBids.Auctions.csproj` — zero `ProjectReference` nodes (no Contracts reference)
- `src/CritterBids.Auctions/AuctionsModule.cs` — `services.ConfigureMarten` with `auctions` schema registration and `LiveStreamAggregation<Listing>()`; zero `AddEventType<T>()` calls
- `src/CritterBids.Api/Program.cs` — two RabbitMQ queues declared: `selling-participants-events`, `listings-selling-events`
- `src/CritterBids.Auctions/Listing.cs` — empty aggregate shell plus `ScaffoldPlaceholder` record and no-op `Apply(ScaffoldPlaceholder)`

---

## Items completed

| Item | Description |
|------|-------------|
| S3a | `CritterBids.Auctions.csproj` — first `ProjectReference` to `CritterBids.Contracts.csproj` |
| S3b | `AuctionsModule.cs` — `opts.Events.AddEventType<BiddingOpened>()` inside the existing `ConfigureMarten` callback; zero other `AddEventType<T>` calls added |
| S3c | `src/CritterBids.Auctions/ListingPublishedHandler.cs` — new static handler consuming `CritterBids.Contracts.Selling.ListingPublished`, producing `CritterBids.Contracts.Auctions.BiddingOpened`, starting a `Listing` stream via `session.Events.StartStream<Listing>` |
| S3d | Idempotency via `FetchStreamStateAsync(listingId)` pre-check; non-null result → early return; at-least-once re-delivery produces no duplicate event and no propagated exception |
| S3e | `Program.cs` — `opts.PublishMessage<ListingPublished>().ToRabbitQueue("auctions-selling-events")` and `opts.ListenToRabbitQueue("auctions-selling-events")` added inside the existing RabbitMQ-guarded block, mirroring the `selling-participants-events` shape |
| S3f | `tests/CritterBids.Auctions.Tests/BiddingOpenedConsumerTests.cs` — two integration tests: `ListingPublished_FromSelling_ProducesBiddingOpened` and `ListingPublished_Duplicate_IsIdempotent` |
| S3g | `AuctionsTestFixture` — no tracking helpers added; Listings-minimal shape preserved (direct handler invocation in tests, explicit `SaveChangesAsync`) |
| S3h | This retrospective |

Commit sequence landed:

| Commit | SHA | Items |
|--------|-----|-------|
| `feat(auctions): implement ListingPublished consumer with stream-state idempotency` | `bdd8d60` | S3a, S3b, S3c, S3d |
| `feat(auctions): route ListingPublished to auctions queue; add consumer tests` | `dabef67` | S3e, S3f (S3g folded in — fixture unchanged from S2) |
| `docs: write M3-S3 retrospective` | _this commit_ | S3h |

> **Commit-1 message deviation.** The prompt's commit-1 template reads `feat(auctions): add Contracts reference; register BiddingOpened; consume ListingPublished and start Listing stream`. `fce7f3e` — the prompt-doc commit that landed in a prior session — already carried that exact message. A duplicate message against different content would pollute the log, so commit-1 here was reworded to describe what actually landed.

---

## S3a — Contracts ProjectReference

### Diff

```xml
<ItemGroup>
  <ProjectReference Include="..\CritterBids.Contracts\CritterBids.Contracts.csproj" />
</ItemGroup>
```

First and only `ProjectReference` in the Auctions csproj. Placed alphabetically — trivially, since it is the single reference. S2 deferred this by design; S3 is the session that needed both the produced type (`Contracts.Auctions.BiddingOpened`) and the consumed type (`Contracts.Selling.ListingPublished`).

---

## S3b — AddEventType<BiddingOpened>

### Placement

```csharp
services.ConfigureMarten(opts =>
{
    opts.Schema.For<Listing>().DatabaseSchemaName("auctions");
    opts.Events.AddEventType<BiddingOpened>();
    opts.Projections.LiveStreamAggregation<Listing>();
});
```

### Why now, not at scaffold time

M2 key learning: registering event types ahead of `Apply()` methods causes silent null returns from `AggregateStreamAsync<T>`. Auctions' event vocabulary registers at first-use session, not bulk-registered at scaffold time. `BiddingOpened` is the first event produced by Auctions (in this session), so its registration lands here. The bid-and-friends batch (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowOptionRemoved`) stays unregistered until S4. The closing-outcome batch (`BuyItNowPurchased`, `BiddingClosed`, `ListingSold`, `ListingPassed`) stays unregistered until S5.

---

## S3c — ListingPublishedHandler

### Handler / structure after

```csharp
public static class ListingPublishedHandler
{
    public static async Task Handle(
        ListingPublished message,
        IDocumentSession session)
    {
        var existing = await session.Events.FetchStreamStateAsync(message.ListingId);
        if (existing is not null)
            return;

        var duration = message.Duration!.Value;

        var opened = new BiddingOpened(
            ListingId: message.ListingId,
            SellerId: message.SellerId,
            StartingBid: message.StartingBid,
            ReserveThreshold: message.ReservePrice,
            BuyItNowPrice: message.BuyItNow,
            ScheduledCloseAt: message.PublishedAt.Add(duration),
            ExtendedBiddingEnabled: message.ExtendedBiddingEnabled,
            ExtendedBiddingTriggerWindow: message.ExtendedBiddingTriggerWindow,
            ExtendedBiddingExtension: message.ExtendedBiddingExtension,
            MaxDuration: duration,
            OpenedAt: DateTimeOffset.UtcNow);

        session.Events.StartStream<Listing>(message.ListingId, opened);
    }
}
```

### Why this shape

| Concern | Decision | Rejected alternative |
|---|---|---|
| Return type | `Task` (void-async) | `Task<IStartStream?>` returning `MartenOps.StartStream<Listing>(...)`. Wolverine materializes the `IStartStream` only when the handler runs through the message pipeline; direct-invocation tests (see S3f) bypass the pipeline and the stream never appends. |
| Session work | Direct `session.Events.StartStream<Listing>(...)`; no `session.Store` or explicit `SaveChangesAsync` in the handler body | `MartenOps` side-effect types (same reason as above) |
| Idempotency | `FetchStreamStateAsync(listingId)` pre-check; non-null → silent return | Canonical `ConcurrencyException` throw-and-retry from `integration-messaging.md` — would violate the S3 acceptance criterion "no handler-level exception propagates" |
| Transaction commit | Relies on `Program.cs` `opts.Policies.AutoApplyTransactions()` in production; tests call `SaveChangesAsync` explicitly | Calling `SaveChangesAsync` inside the handler body — redundant under AutoApplyTransactions |
| Stream ID | `ListingPublished.ListingId` (upstream UUID v7 flows through) | Regenerating via `Guid.CreateVersion7()` — breaks cross-BC correlation |

### `Duration!.Value` unwrap

`ListingPublished.Duration` is `TimeSpan?` — Flash listings carry `null`, Timed listings carry a value. `BiddingOpened.MaxDuration` (and `ScheduledCloseAt`, which derives from it) are non-nullable. M3 is Timed-only per `docs/milestones/M3-auctions-bc.md` §3; the Flash path belongs to the M4 Session aggregate, not to this handler. The bang-unwrap is safe under the M3 scope contract and is commented as such in the handler body — not an invented default, an explicit scope constraint.

---

## S3d — Idempotency approach (prompt Open Question #1)

The prompt's Open Question #1 named three canonical approaches documented in `wolverine-message-handlers.md` — stream-state check, append-only-if-not-present, inbox-level dedup — and then named the **stream-state check** as the default if none of the skill-documented forms cleanly fits the "first event on a new stream" case.

Stream-state check was the default **and** what was implemented. Rationale:

- `FetchStreamStateAsync(id)` returning `null` is an unambiguous "stream does not exist" signal — it is exactly the state needed to decide whether a first-event `StartStream` is safe.
- The canonical concurrency-throw-and-retry pattern lets `ConcurrencyException` propagate on duplicate delivery; the S3 idempotency criterion explicitly requires **no handler-level exception propagates** on duplicate delivery. The canonical form does not satisfy this criterion.
- Inbox-level dedup requires Wolverine's transactional inbox configured for deduplication; M2 decision stands that this is not configured. Not available.

Verbatim runtime evidence that this path is correct: `ListingPublished_Duplicate_IsIdempotent` invokes the handler twice against the same `ListingId` across separate `IDocumentSession` instances (mirroring a real RabbitMQ re-delivery, which would not share session state with the first delivery), and asserts `events.Count.ShouldBe(1)` after both calls. Test is green.

---

## S3e — Program.cs routing

### Diff

```csharp
opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
    .ToRabbitQueue("auctions-selling-events");
opts.ListenToRabbitQueue("auctions-selling-events");
```

Placed after the `listings-selling-events` block, inside the existing RabbitMQ-guarded (`!string.IsNullOrEmpty(rabbitMqUri)`) scope. Same shape as `selling-participants-events` and `listings-selling-events`.

### Queue-per-consumer rationale

`ListingPublished` is now published to **two** queues — `listings-selling-events` and `auctions-selling-events`. Each consumer BC gets its own queue, not a shared queue with competing consumers. This matches `integration-messaging.md` `<consumer>-<publisher>-<category>` naming and keeps retry/DLQ/lag semantics independent per BC. A Listings-side consumer failure does not stop the Auctions-side consumer from making progress.

### Negative-space assertions

- `Program.cs` contains **zero** `listings-auctions-events` references (S5 or S6 work).
- `Program.cs` contains **zero** publish lines for Auctions-produced events (no `BiddingOpened`, `ListingSold`, etc. — Auctions does not yet publish cross-BC).

---

## S3f — Integration tests

### Test shape

```csharp
await using (var session = _fixture.GetDocumentSession())
{
    await ListingPublishedHandler.Handle(message, session);
    await session.SaveChangesAsync();
}

await using var querySession = _fixture.GetDocumentSession();
var events = await querySession.Events.FetchStreamAsync(listingId);
events.Count.ShouldBe(1);
var opened = events[0].Data.ShouldBeOfType<BiddingOpened>();
```

Direct handler invocation with an explicit `SaveChangesAsync`. The duplicate-delivery test uses two separate sessions for the two `Handle` calls so the second call genuinely re-reads stream state from Postgres — what a re-delivered RabbitMQ envelope would do.

### Why direct invocation, not `IMessageBus.InvokeAsync`

This was the S3 decision point surfaced by a runtime failure. See "Discovery: sticky-handler blocker" below.

### Assertions

Both happy-path fields and idempotency count. The happy-path test asserts every `BiddingOpened` field derives correctly from the `ListingPublished` source — `ReserveThreshold` from `ReservePrice`, `BuyItNowPrice` from `BuyItNow`, `ScheduledCloseAt` from `PublishedAt.Add(duration)`, `OpenedAt` in a ±1-minute range around `UtcNow`. This is the payload-parity check the prompt's W002-9 language calls out.

---

## S3g — AuctionsTestFixture (mirror-Listings-minimal chosen)

Prompt Open Question #4 named the choice: **mirror SellingTestFixture (tracking helpers)** vs **mirror Listings-minimal (direct invocation + queries)**. Listings-minimal was chosen; the fixture did not need modification from its S2 shape.

### Why mirror-Listings-minimal won

- The two S3 tests do not need Wolverine to perform message routing or middleware work — they exercise the handler's pure domain logic (idempotency check, payload construction, `StartStream` call). Direct invocation covers the assertion surface.
- Mirror-SellingTestFixture's `ExecuteAndWaitAsync` — tried first — runs `IMessageBus.InvokeAsync`, which **fails** with a sticky-handler error under `DisableAllExternalWolverineTransports()` (see below).
- Keeping the fixture minimal defers the decision on whether Auctions needs a full tracking-helper fixture until a future slice actually requires through-the-bus routing (e.g. saga timing tests in S5).

---

## Discovery: sticky-handler blocker

### Verbatim error message

```
Wolverine.NoHandlerForEndpointException:
No handlers for message type CritterBids.Contracts.Selling.ListingPublished at this endpoint.
This is usually because of 'sticky' handler to endpoint configuration.
```

### Root cause

`opts.ListenToRabbitQueue("auctions-selling-events")` in `Program.cs` binds `ListingPublishedHandler` as a **sticky handler** on that specific endpoint — Wolverine's way of modeling "this handler processes messages arriving on this queue only." When the test fixture calls `DisableAllExternalWolverineTransports()`, the RabbitMQ endpoint does not exist, so `IMessageBus.InvokeAsync(message)` cannot find any endpoint where the handler is live.

Two paths exist for restoring bus-level invocation in tests:

1. Register a duplicate non-sticky handler for test-mode — pollutes the production code path.
2. Configure the test fixture to promote the sticky handler to a local endpoint — adds fixture complexity the tests don't otherwise need.

A third path exists: **don't go through the bus at all**. Invoke the handler method directly with an injected session. This is the Listings-BC precedent (`CatalogListingViewTests.SeedCatalogEntry`). Chosen — the production handler code is identical either way, and the test asserts what the handler actually does.

### Sequence (for the next session hitting the same wall)

1. First attempt used `ExecuteAndWaitAsync<T>(message)` (mirroring `SellingTestFixture.ExecuteAndWaitAsync`) and `MartenOps.StartStream<Listing>(...)` as the handler's return type.
2. Tests failed with the sticky-handler error above.
3. Handler was rewritten to `Task` return, direct `session.Events.StartStream<Listing>(...)` body.
4. Tests were rewritten to direct handler invocation + explicit `SaveChangesAsync`.
5. Fixture's speculative `ExecuteAndWaitAsync` helper + `using Wolverine.Tracking` were reverted — no longer needed.

### Skill gap surfaced

`docs/skills/critter-stack-testing-patterns.md` should explicitly document that a RabbitMQ `ListenToRabbitQueue` registration makes the handler sticky, which interacts badly with `DisableAllExternalWolverineTransports()`. The Listings-minimal direct-invocation pattern is the documented resolution — but the **causal chain** (routing → sticky → fixture-disabled → invoke fails) is the part future sessions will want named. Not edited in-session per the prompt's "no mid-session skill edits" rule; recording here for the next skills-maintenance pass.

---

## Test results

| Phase | Auctions Tests | Solution Total | Result |
|-------|---------------|----------------|--------|
| Baseline (S2 close) | 1 | 45 | Green |
| After S3a–S3d (code only, no new tests) | 1 | 45 | Green (build verified) |
| After S3e–S3f initial (ExecuteAndWaitAsync path) | 1 passing + 2 failing | 45 + 2 failing = 47 attempted | Red — sticky-handler blocker |
| After pivot to direct invocation | 3 | 47 | Green |

Final: **47 tests green** (1 Api + 1 Contracts + 3 Auctions + 4 Listings + 6 Participants + 32 Selling). 45-test baseline preserved; 2 new tests landed per prompt.

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings (unchanged from baseline).
- `dotnet test CritterBids.slnx` — 47 passed, 0 failed, 0 skipped.
- `CritterBids.Auctions.csproj` `ProjectReference` count: 1 (Contracts).
- `AuctionsModule` `AddEventType<T>()` calls: 1 (`BiddingOpened`).
- `Program.cs` RabbitMQ `ListenToRabbitQueue` calls: 3 (`selling-participants-events`, `listings-selling-events`, `auctions-selling-events`).
- `Program.cs` `listings-auctions-events` references: 0.
- `Listing.cs` — `ScaffoldPlaceholder` record and `Apply(ScaffoldPlaceholder)` method present and unchanged.
- `src/CritterBids.Contracts/Auctions/BiddingOpened.cs` — unchanged from S1 stub.
- Handlers using `MartenOps.StartStream`: 0 (chosen shape is direct `session.Events.StartStream`).
- `IMessageBus` usages in `CritterBids.Auctions`: 0.

---

## Key learnings

1. **`ListenToRabbitQueue` creates a sticky handler — `DisableAllExternalWolverineTransports()` then strands bus-level invocation.** The combination is not obvious from reading either call in isolation. The resolution — direct handler invocation in tests — is cheap, but future sessions hitting the same verbatim error message deserve to find the causal chain documented. Record it, don't rediscover it.

2. **Prompt Open Questions are pre-mortems, not open design debates.** Open Question #1 named `FetchStreamStateAsync` as the fallback before the session started; Open Question #4 named mirror-Listings-minimal as an acceptable path. Both preemptive calls were correct and saved rediscovery cost at implementation time. Retros should preserve which Open Question triggered which decision — that's the part prompts can't know in advance.

3. **Same payload shape across two consumers = two separate queues, not a shared queue.** Auctions and Listings both consume `ListingPublished`, but each via its own queue (`auctions-selling-events`, `listings-selling-events`). `integration-messaging.md`'s `<consumer>-<publisher>-<category>` naming makes this mechanical: the consumer BC name prefixes the queue, so adding a new consumer always adds a new queue.

4. **Nullable asymmetry between `ListingPublished.Duration` (Flash-tolerant) and `BiddingOpened.MaxDuration` (Timed-only) is a scope marker, not a bug.** Flash listings flow through the M4 Session aggregate, not the M3 Listing aggregate. The M3 handler's `Duration!.Value` unwrap is safe under the milestone scope contract; the comment in the handler body names that contract explicitly. Future M4 work will introduce a sibling handler that carries the Flash path to a different aggregate type.

5. **Live aggregate + sticky handler + `LiveStreamAggregation<T>` + `UseMandatoryStreamTypeDeclaration` — four settings in different files must agree.** Event type registered in `AuctionsModule`, aggregate type parameter on `LiveStreamAggregation`, stream-type declaration on `StartStream<Listing>`, and event envelope on the store's `AddEventType` list. Missing any of the four produces a silent or late failure. The S3 green-test state verifies all four are in sync for `BiddingOpened` + `Listing`.

---

## Verification checklist

- [x] `src/CritterBids.Auctions/CritterBids.Auctions.csproj` contains a `<ProjectReference>` to `CritterBids.Contracts.csproj`.
- [x] `src/CritterBids.Auctions/AuctionsModule.cs` — `opts.Events.AddEventType<BiddingOpened>()` present inside the existing `ConfigureMarten` callback; zero other `AddEventType<T>` calls.
- [x] A new Wolverine handler file exists under `src/CritterBids.Auctions/` that consumes `CritterBids.Contracts.Selling.ListingPublished` and starts a `Listing` event stream appending `CritterBids.Contracts.Auctions.BiddingOpened`.
- [x] Handler uses `ListingPublished.ListingId` as the stream ID; does not regenerate.
- [x] Handler populates `BiddingOpened` from `ListingPublished` per the W002-9 resolved payload shape; no hardcoded defaults for W002-9-owned fields.
- [x] `src/CritterBids.Api/Program.cs` — `opts.PublishMessage<ListingPublished>().ToRabbitQueue("auctions-selling-events")` present in the publish block; `opts.ListenToRabbitQueue("auctions-selling-events")` present in the listen block; both inside the existing RabbitMQ-guarded block; placed alongside the existing `selling-participants-events` routing.
- [x] `Program.cs` contains zero `listings-auctions-events` lines and zero publish lines for any other Auctions-produced event.
- [x] `src/CritterBids.Auctions/Listing.cs` — `ScaffoldPlaceholder` record and its paired no-op `Apply(ScaffoldPlaceholder)` method still present and unchanged.
- [x] `src/CritterBids.Contracts/Auctions/BiddingOpened.cs` unchanged from the S1 stub.
- [x] `tests/CritterBids.Auctions.Tests/BiddingOpenedConsumerTests.cs` exists with the two specified tests, both integration-level, both green.
- [x] `AuctionsTestFixture` did **not** gain tracking helpers — Listings-minimal shape chosen per Open Question #4.
- [x] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [x] `dotnet test CritterBids.slnx` — all green; 45-test baseline preserved; new total is 47 (45 + 2).
- [x] This retrospective records: the Contracts `ProjectReference` diff, the `AddEventType<BiddingOpened>()` placement, the handler idempotency approach chosen with rationale, the `Program.cs` routing diff, the fixture decision, skill gap surfaced, and a "what M3-S4 should know" note.

---

## What remains / next session (M3-S4) should verify

### `ScaffoldPlaceholder` removal timing

`Listing.cs` still carries the S2 scaffold:

```csharp
private sealed record ScaffoldPlaceholder;
public void Apply(ScaffoldPlaceholder _) { /* no-op */ }
```

Its purpose is to satisfy the Marten-8 `LiveStreamAggregation<T>`/`SingleStreamProjection<T>` validator, which rejects aggregates with zero `Apply`/`Create`/`ShouldDelete` methods at `ConfigureMarten` time. S3 did **not** remove the scaffold, because removing it before S4 ships its real `Apply(BiddingOpened)` method would re-trigger the M3-S2 blocker.

S4 should:
1. Add a real `Apply(BiddingOpened)` (or `Create(BiddingOpened)` — S4 will make the call) that initializes the aggregate state from the first event on the stream.
2. Remove the `ScaffoldPlaceholder` record and its paired `Apply` method in the **same** commit that adds the real `Apply`.
3. Verify the boot smoke test (`AuctionsModule_BootsClean`) and the S3 consumer tests remain green.

### First real `Apply(BiddingOpened)` placement

`BiddingOpened` is now the stream's first event. S4's real `Apply` or `Create` method on `Listing` will consume it to establish aggregate identity, bidding state (current bid, bid count, reserve-met flag, scheduled close), and extended-bidding configuration. The S1 W002-9 resolution already shaped the payload for that consumption — nothing further is needed from the event side.

### Handler shape precedent set

Future Auctions handlers (S4's `PlaceBidHandler`, `BuyItNowHandler`; S5's saga handlers) can either:

- **Follow this S3 shape** (`Task` return, direct `session.Events.Append`/`StartStream`), suitable when tests invoke the handler directly.
- **Use `MartenOps`/`[WriteAggregate]`** (aggregate-handler-workflow shape), suitable when the handler produces a stream-typed return and tests exercise the full Wolverine pipeline.

The decision hinges on whether the tests need to route through the bus. The sticky-handler failure pattern documented above is the signal that a test needs one path or the other — not both.

### Listings-minimal fixture holds until it doesn't

`AuctionsTestFixture` is still the minimal shape. When S5's `AuctionClosingSaga` lands, its tests will likely need through-the-bus routing (to exercise scheduled-message timing). That session can add tracking helpers mirroring `SellingTestFixture` at that point — not speculatively now.

### Out of scope for M3-S4 (reminders)

- `listings-auctions-events` queue for Auctions-published events (Listings consumer) — S5 or S6.
- `CatalogListingView` auction-status field additions — S6.
- Saga artifacts (scheduled messages, cancel-and-reschedule) — S5.
- Any HTTP endpoints on Auctions (no `PlaceBid`/`BuyNow` controller surface) — the M4 session will address if HTTP is the dispatch path or if only message handlers are.

### Skill-maintenance backlog items surfaced

- `docs/skills/critter-stack-testing-patterns.md` — document the `ListenToRabbitQueue` → sticky-handler → `DisableAllExternalWolverineTransports` failure mode with the verbatim `NoHandlerForEndpointException` message. Name the direct-invocation pattern as the resolution for handlers that don't need through-the-bus routing in tests.
- `docs/skills/wolverine-message-handlers.md` — the "first event on a new stream" idempotency case should cite `FetchStreamStateAsync` + early-return as the canonical shape when the handler must not propagate `ConcurrencyException` on re-delivery. The existing canonical-retry pattern does not cover this case.
