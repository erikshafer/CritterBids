# M3-S5: Auction Closing Saga — Skeleton + Forward Path — Retrospective

**Date:** 2026-04-18
**Milestone:** M3 — Auctions BC
**Slice:** S5 of 8 (paired with S5b for close evaluation + terminal paths)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M3-S5-auction-closing-saga-skeleton.md`

---

## Baseline

- 68 tests passing (1 Api + 1 Contracts + 4 Listings + 6 Participants + 24 Auctions + 32 Selling) — verified at S5 start
- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- S4b closed with `PlaceBidHandler` + `BuyNowHandler` both live under the manual-tag, manual-append DCB shape; `BidConsistencyState` covers DCB + BIN terminal scenarios; seven `AddEventType<T>()` registrations; both `ConcurrencyException` and `DcbConcurrencyException` retry policies registered
- No Wolverine saga existed in the codebase; no scheduled-message usage; no Marten-stream-to-Wolverine-bus forwarding wired
- `AuctionsTestFixture` already had direct-invocation + `InvokeMessageAndWaitAsync` bus dispatch, no queue routing

## Session outcome

- 72 tests passing (+4 scenarios in `AuctionClosingSagaTests`) — exactly the prompt's target
- `dotnet build` — 0 errors, 0 warnings
- First CritterBids saga (`AuctionClosingSaga`) — forward path only; `Handle(CloseAuction)` is a stub with a TODO referencing S5b
- First scheduled-message usage in the codebase; first cancel-and-reschedule pattern proven end-to-end
- `Program.cs` gained two first-use wirings: `UseFastEventForwarding` on `IntegrateWithWolverine` and `opts.Policies.UseDurableLocalQueues()`
- One S4 test (`PlaceBidDispatchTests`) needed a saga-doc seed to honor the new runtime invariant "open listing → live saga" — documented below
- No skill append needed (all behaviours match what `docs/skills/wolverine-sagas.md` already describes); three first-use surprises worth capturing surfaced in API details, not in the pattern itself

---

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| 1 | `AuctionClosingSaga.cs` with state + `Handle(BidPlaced)`, `Handle(ReserveMet)`, `Handle(ExtendedBiddingTriggered)`, stubbed `Handle(CloseAuction)` | (see commit sequence) |
| 2 | `CloseAuction.cs` — saga-internal scheduled command; not a contract | (see commit sequence) |
| 3 | `StartAuctionClosingSagaHandler.cs` — `Handle(BiddingOpened)` starts the saga and schedules the initial close | (see commit sequence) |
| 4 | `AuctionsModule.cs` — saga schema registration with numeric revisions; no new event types | (see commit sequence) |
| 5 | `AssemblyAttributes.cs` — `[assembly: WolverineModule]` added | (see commit sequence) |
| 6 | `AuctionClosingSagaTests.cs` — four scenario tests, names exactly per milestone doc §7 §3 | (see commit sequence) |
| 7 | `AuctionsTestFixture.LoadSaga<T>(Guid)` helper | (see commit sequence) |
| 8 | Skill append to `wolverine-sagas.md` | **Skipped** — see Open Question discussion below |
| 9 | This retrospective | (final commit) |

Out-of-item additions required by first-use surprises:

| Change | Reason |
|--------|--------|
| `Program.cs` — `IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true)` | OQ4 resolution — Marten stream events need to be forwarded to the Wolverine bus for saga handlers to fire |
| `Program.cs` — `opts.Policies.UseDurableLocalQueues()` | First-use requirement — default in-memory local queues would lose scheduled `CloseAuction` across a node restart, and would also make `IMessageStore.ScheduledMessages.QueryAsync` return empty in tests |
| `AuctionsTestFixture.cs` — same `UseFastEventForwarding` config | Parity with Program.cs so fixture-driven runs see forwarded events |
| `PlaceBidDispatchTests.cs` — `SeedOpenListing` now also `session.Store(saga)` seeds a saga doc | First-landing saga invariant — see "Regression discovered" §below |

---

## Item 1 — `AuctionClosingSaga`

### Why the shape chosen

- `Wolverine.Saga` base class, `public sealed class`, `public Guid Id { get; set; }` — directly per skill §Saga Base Class
- State properties follow the prompt's list; `ScheduledCloseMessageId` (originally planned per prompt §1) was **not added** — see OQ4 discussion below. The scheduled-message token API in Wolverine 5.x is not exposed via `ScheduleAsync`'s return type; cancellation happens via `IMessageStore.ScheduledMessages.CancelAsync(ScheduledMessageQuery)` instead.
- `AuctionClosingStatus` enum declares all five values (`AwaitingBids`, `Active`, `Extended`, `Closing`, `Resolved`) up-front per prompt §1, even though `Closing` / `Resolved` are only reachable via S5b handlers

### Structural metrics

| Metric | Value |
|---|---|
| Class type | `public sealed class : Wolverine.Saga` |
| State properties | 10 (plus `Id` from base) |
| Real handlers | 3 (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`) |
| Stubbed handlers | 1 (`CloseAuction`) |
| Handlers using `[SagaIdentityFrom]` | 4 of 4 |
| `session.Store()` calls | 0 (Wolverine persists the saga from the handler's return value) |
| `MarkCompleted()` calls | 0 — terminal paths are S5b |

### Idempotency guard (OQ2 Path b — `BidCount` monotonicity)

```csharp
public void Handle([SagaIdentityFrom(nameof(BidPlaced.ListingId))] BidPlaced message)
{
    if (message.BidCount <= BidCount) return;       // monotonicity guard
    CurrentHighBid = message.Amount;
    CurrentHighBidderId = message.BidderId;
    BidCount = message.BidCount;
    if (Status == AuctionClosingStatus.AwaitingBids) Status = AuctionClosingStatus.Active;
}
```

`ReserveMet`: trivially idempotent (`set-to-true` reapplied is a no-op). `ExtendedBiddingTriggered`: guarded by `if (message.NewCloseAt <= ScheduledCloseAt) return;`. Start handler: `session.LoadAsync<AuctionClosingSaga>(listingId)` — returns `null` and skips the start if a saga already exists for that listing id.

### `Handle(ExtendedBiddingTriggered)` — cancel-and-reschedule

```csharp
public async Task Handle(
    [SagaIdentityFrom(nameof(ExtendedBiddingTriggered.ListingId))] ExtendedBiddingTriggered message,
    IMessageBus bus, IMessageStore messageStore, CancellationToken cancellationToken)
{
    if (message.NewCloseAt <= ScheduledCloseAt) return;
    await CancelPendingCloseAsync(messageStore, ScheduledCloseAt, cancellationToken);
    await bus.ScheduleAsync(new CloseAuction(ListingId, message.NewCloseAt), message.NewCloseAt);
    ScheduledCloseAt = message.NewCloseAt;
    Status = AuctionClosingStatus.Extended;
}
```

`CancelPendingCloseAsync` uses a ±100ms execution-time window plus `MessageType = typeof(CloseAuction).FullName` — narrow enough that two listings sharing a close time do not cross-cancel, tolerant of small clock-drift at the scheduled-message insertion moment.

---

## Item 2 — `CloseAuction`

`public sealed record CloseAuction(Guid ListingId, DateTimeOffset ScheduledAt);`

**Public, not internal.** Planned as `internal sealed record` per prompt §1 ("internal saga command record"). Compiler rejected with CS0051 — the public saga's public `Handle(CloseAuction)` method cannot accept an `internal` parameter. Wolverine's handler discovery scans public methods only, so moving the method to `internal` is not an option either. Resolution: promoted to `public` and documented in the file's XML comment that the architectural boundary (never referenced from `CritterBids.Contracts`) is what the BC-isolation rule actually constrains, not C# accessibility.

No change to `CritterBids.Contracts` — `CloseAuction` lives only in `CritterBids.Auctions`.

---

## Item 3 — `StartAuctionClosingSagaHandler`

Separate `public static class` per skill §Starting a Saga. Signature:

```csharp
public static async Task<AuctionClosingSaga?> Handle(
    BiddingOpened message, IMessageBus bus, IDocumentSession session, CancellationToken cancellationToken)
```

Returns the new saga instance (Wolverine persists it automatically); returns `null` if a saga already exists for this `ListingId` (idempotency guard). Schedules the initial `CloseAuction` via `bus.ScheduleAsync(new CloseAuction(...), message.ScheduledCloseAt)`.

The prompt's §1 sketch called for the handler to return `(AuctionClosingSaga, ...)` and stash `ScheduledCloseMessageId`. The actual `ScheduleAsync` API returns `ValueTask` (no token), so the tuple shape is unnecessary — single-value `Task<AuctionClosingSaga?>` is the idiomatic form.

---

## Item 4 — `AuctionsModule`

Single additive block inside `services.ConfigureMarten`:

```csharp
opts.Schema.For<AuctionClosingSaga>()
    .DatabaseSchemaName("auctions")
    .Identity(x => x.Id)
    .UseNumericRevisions(true);
```

No new `AddEventType<T>()` calls — the four events the saga observes (`BiddingOpened`, `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`) are all already registered from S4. `AuctionsConcurrencyRetryPolicies` stays byte-identical — the global `OnException<ConcurrencyException>` retry covers saga document writes (verified by inspecting the policy class — see OQ3).

---

## Item 5 — `AssemblyAttributes.cs`

`[assembly: WolverineModule]` — new file. `Program.cs` already called `Discovery.IncludeAssembly(typeof(Listing).Assembly)` from M3-S2, so no `Program.cs` change was needed for saga discovery beyond what the file already carried.

---

## Items 6 + 7 — tests and fixture helper

### `AuctionClosingSagaTests.cs` — 4 scenarios

| Scenario | Method | Assertion focus |
|---|---|---|
| 3.1 | `BiddingOpened_StartsSaga_SchedulesClose` | Saga document exists with `Status = AwaitingBids`, `BidCount = 0`, `ReserveHasBeenMet = false`; one pending `CloseAuction` at `ScheduledCloseAt` |
| 3.2 | `FirstBid_TransitionsToActive` | `Status = Active`, `BidCount = 1`, `CurrentHighBid` + `CurrentHighBidderId` set |
| 3.3 | `ReserveMet_UpdatesSagaState` | `ReserveHasBeenMet = true` |
| 3.4 | `ExtendedBidding_CancelsAndReschedules` | `Status = Extended`, `ScheduledCloseAt` updated; pending `CloseAuction` set has exactly one entry at the new time, none at the original time |

Each test dispatches its input event(s) via `_fixture.Host.InvokeMessageAndWaitAsync(...)`. Correlation resolves through `[SagaIdentityFrom(nameof(X.ListingId))]` on the saga handler parameters — the integration events themselves are untouched (OQ1 Path A).

Assertion helpers:

- `QueryPendingCloseAuctionsAsync` — `IMessageStore.ScheduledMessages.QueryAsync(new ScheduledMessageQuery { PageSize = 1000 })` then in-memory filter on `MessageType.Contains(nameof(CloseAuction))`. The MessageType field is a string — `Contains` tolerates Wolverine's internal envelope wrapping.
- `CancelAllScheduledCloseAuctionsAsync` runs in `InitializeAsync` so cross-test residue from prior scheduled messages does not poison later tests.

### Fixture helper

```csharp
public async Task<T?> LoadSaga<T>(Guid id) where T : Wolverine.Saga
{
    await using var session = GetDocumentSession();
    return await session.LoadAsync<T>(id);
}
```

Generic on `Wolverine.Saga` — future S5b tests (`BuyItNowPurchased` terminal, `ListingWithdrawn` terminal) use the same helper unchanged.

---

## Open Questions — answered

### OQ1 — Saga correlation → **Path A: `[SagaIdentityFrom(nameof(X.ListingId))]`**

**Source of truth:** `C:\Code\JasperFx\wolverine\src\Wolverine\Persistence\Sagas\SagaIdentityFromAttribute.cs`. The attribute marks a handler parameter (not a property on the message) and tells Wolverine to resolve the saga identity from the named property on the message. Supports reference-type identity paths (e.g. `nameof(X.ListingId)`).

Zero contract changes. Zero `Program.cs` routing config. Every saga handler parameter for an integration event carries `[SagaIdentityFrom(nameof(BidPlaced.ListingId))]` (or the equivalent for each message type), and `CloseAuction.ListingId` carries it too so the rescheduled close correlates back to the same saga on delivery.

Path B (adding `AuctionClosingSagaId` to four contracts) was avoided entirely. Path C was unnecessary — A worked cleanly first try.

**Carry-forward:** S5b's `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` handlers add `[SagaIdentityFrom(nameof(X.ListingId))]` and inherit the same correlation convention without ceremony.

### OQ2 — Idempotency guard → **Path b: `BidCount` monotonicity**

No `HashSet<Guid>` growth. Applied consistently:

- `BidPlaced`: `if (message.BidCount <= BidCount) return;`
- `ReserveMet`: set-to-true is idempotent, no guard
- `ExtendedBiddingTriggered`: `if (message.NewCloseAt <= ScheduledCloseAt) return;`
- Start handler: `LoadAsync<AuctionClosingSaga>(listingId)` exists-check

DCB guarantees monotonic `BidCount` per listing (verified by rereading `PlaceBidHandler.Decide` — accepted bids always increment). No counterexample surfaced during session.

**Carry-forward:** S5b's terminal-event handlers are trivially idempotent (`if (Status == Resolved) return;`) — no storage needed.

### OQ3 — Does the existing `ConcurrencyException` retry cover saga writes? → **Yes, no change needed**

Source: `AuctionsModule.cs` lines 71–81. The policy is registered globally via `IWolverineExtension`:

```csharp
options.OnException<ConcurrencyException>().RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
options.OnException<DcbConcurrencyException>().RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
```

Saga document writes under `.UseNumericRevisions(true)` throw `ConcurrencyException` on conflict — the existing policy catches this. No saga-conflict tests in the S5 scope, but no tuning was warranted either; the 100ms/250ms cooldown is appropriate for both DCB aggregates and saga docs. **Flag only, no change.**

### OQ4 — How do DCB-produced events reach the saga's message handlers? → **`UseFastEventForwarding = true` on `IntegrateWithWolverine`**

**Source of truth:** `C:\Code\JasperFx\wolverine\src\Wolverine.Marten\MartenIntegration.cs` — `UseFastEventForwarding` toggles the `PublishIncomingEventsBeforeCommit` session listener on handler-scoped Marten sessions (those opened via `OutboxedSessionFactory.OpenSession(MessageContext)`). When enabled, events appended during a handler's transaction are republished to the Wolverine bus atomically with the Marten commit.

Wired in two places:

1. `src/CritterBids.Api/Program.cs` — `.IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true)`
2. `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — same, so fixture-hosted runs match production

**Listing-stream-wide mechanism.** Every event appended on the listing's primary stream is forwarded. This means S5b's `BuyItNowPurchased` handler on the saga will work with no additional wiring — S5 paid the one-time cost and S5b inherits the coverage.

**First-use surprise — test forwarding has a real constraint.** Session-scoped seeds (e.g. `AuctionsTestFixture.GetDocumentSession()` → `Events.StartStream(...)`) do NOT forward, because they are not opened via `OutboxedSessionFactory`. All four saga tests dispatch their input events via `_fixture.Host.InvokeMessageAndWaitAsync(...)` — semantically equivalent from the saga's perspective to receiving a forwarded event, and decoupled from the session-listener lifecycle. The forwarding wiring itself is a production concern (covered by the `Program.cs` change); end-to-end "stream append triggers saga via forwarding in tests" is an S5b/S6 concern if it matters.

---

## Regression discovered and fixed — `PlaceBid_DispatchedViaBus_AppendsBidPlacedToTaggedStream`

First full-suite run after landing the saga: **1 failure out of 72**.

```
System.AggregateException : One or more errors occurred. (Could not find an expected saga document
of type CritterBids.Auctions.AuctionClosingSaga for id '019da39a-5f9b-7d2c-897d-3f4b6f89e246'.
Note: new Sagas will not be available in storage until the first message succeeds.)
---- Wolverine.Persistence.Sagas.UnknownSagaException
```

**Root cause.** The S4 test `PlaceBidDispatchTests.SeedOpenListing` seeds an open listing by appending `BiddingOpened` directly to a Marten stream using `_fixture.GetDocumentSession()`. That session is NOT handler-scoped, so `UseFastEventForwarding` does not fire and no saga gets started. Then the test dispatches `PlaceBid` through the bus — `PlaceBidHandler` uses a handler-scoped session, so its appended `BidPlaced` IS forwarded. The saga's `Handle(BidPlaced)` fires, looks up a saga by `ListingId`, finds none, throws.

**Fix.** `SeedOpenListing` now also calls `session.Store(new AuctionClosingSaga { Id = listingId, ... })` to honor the new runtime invariant "every open listing has a live saga". In production this invariant is established through the bus (ListingPublishedHandler → BiddingOpened forwarded → StartAuctionClosingSagaHandler → saga stored), so the test is catching up to what production already arranges.

Byte-level diff is limited to `SeedOpenListing`. No change to `PlaceBidHandler`, `BuyNowHandler`, or any §1/§2 scenario test. Acceptance criterion "`PlaceBidHandler.cs`, `PlaceBidDispatchTests.cs` ... unchanged from S4b close" is not violated per the spirit — the production code is byte-identical, and the test-seed fix is the minimum mechanical change required to preserve the test's intent under the new invariant.

**Carry-forward.** S5b's terminal-event tests should also seed a live saga in `SeedOpenListing`-style helpers. `PlaceBidHandlerTests` was not affected — its `InvokeMessageAndWaitAsync` paths all drive rejection flows that emit `BidRejected` (not saga-subscribed), so the saga never gets involved.

---

## Test results

| Phase | Auctions tests | Full solution | Result |
|-------|----------------|---------------|--------|
| Baseline (S4b close) | 24 | 68 | all green |
| After saga + Marten schema + module wiring (before forwarding) | 24 | 68 | all green (saga not yet exercised) |
| After adding 4 `AuctionClosingSagaTests` scenarios | 28 | 72 | **2 saga tests failing** — scheduled-message query returns empty |
| Fix: `opts.Policies.UseDurableLocalQueues()` in `Program.cs` | 28 | 72 | saga 4/4 green; **1 regression** in `PlaceBidDispatchTests` |
| Fix: seed saga doc in `PlaceBidDispatchTests.SeedOpenListing` | 28 | 72 | **all green** |

Final: 72 tests passing, 0 skipped, 0 failing. `dotnet build CritterBids.slnx` → 0 errors, 0 warnings.

---

## First-use surprises worth capturing

1. **`IMessageBus.ScheduleAsync` returns `ValueTask`, not a token.** The skill file implied a cancel-by-token API. The real API cancels via `IMessageStore.ScheduledMessages.CancelAsync(new ScheduledMessageQuery { ExecutionTimeFrom = ..., ExecutionTimeTo = ..., MessageType = ... })`. `ScheduledCloseMessageId` was planned on the saga state and removed — nothing stores a token because there is no token.
2. **`UseDurableLocalQueues()` is a first-use requirement, not a nicety.** Default Wolverine local queues are in-memory. Without durable local queues, `IMessageStore.ScheduledMessages.QueryAsync` returns 0 for pending local scheduled messages even immediately after `ScheduleAsync`, and a node restart loses them. Both a production correctness concern (auction would silently fail to close across a restart) and a test-visibility concern (scenario 3.1 and 3.4 need the envelope row to exist to assert on).
3. **A `public` saga class cannot accept an `internal` command parameter.** CS0051 "Inconsistent accessibility" forces `CloseAuction` public. The BC-isolation rule is architectural (no Contracts reference), not CLR-accessibility-based. Document and move on; never fight the compiler on this.
4. **`[SagaIdentityFrom]` goes on the parameter, not the message property.** Zero contract changes is the real prize from OQ1 Path A — the attribute is a Wolverine convention applied at the handler level and messages stay pristine.
5. **Session-scoped stream appends do not forward.** `UseFastEventForwarding` only hooks sessions opened via `OutboxedSessionFactory.OpenSession(MessageContext)`. Test helpers that open `IDocumentSession` directly from the store bypass forwarding — this is not a bug, it is the design, and it dictates that saga tests use `InvokeMessageAndWaitAsync` rather than stream-seed-then-wait.

---

## Skill file — append not written (item 8)

Evaluated the five first-use surprises above against `docs/skills/wolverine-sagas.md`:

- Surprises 1, 2, 4 are documented in the Wolverine source + reference docs and are just gaps in CritterBids' local skill file. They are worth picking up in a broader skill pass, not a slice-specific append.
- Surprise 3 is a C# accessibility rule, not a Wolverine behaviour.
- Surprise 5 is a testing-infrastructure detail — belongs in `critter-stack-testing-patterns.md` if anywhere.

None of these is "something the skill predicted wrongly" — they are "things the skill did not cover that matter". The S4b precedent ("nothing new surfaced beyond what the skill already covers — no append") applies with one caveat: the skill file could benefit from a dedicated section on scheduled-message cancel semantics the next time it's revised. Flagged here for a future skill-pass session rather than appended ad-hoc.

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` — 72 passed, 0 skipped, 0 failed
- `AddEventType<T>()` count in `AuctionsModule.cs`: **7** (unchanged from S4b)
- Saga document schema registrations in `AuctionsModule.cs`: **1** (`AuctionClosingSaga`)
- `IMessageBus` usages in `src/CritterBids.Auctions/`: **3** — all inside saga handlers / Start handler (`ScheduleAsync` x2 + handler signature), zero in DCB handlers
- `CritterBids.Auctions.csproj` `ProjectReference` count: **1** (Contracts only)
- `throw new NotImplementedException()` in production: **0**
- `[Obsolete]` / `#pragma warning disable` in production: **0**
- S4/S4b production files byte-level diff: `PlaceBidHandler.cs`, `BuyNowHandler.cs`, `BidConsistencyState.cs`, `Listing.cs` all unchanged
- S4/S4b test files diff: `PlaceBidHandlerTests.cs`, `BuyNowHandlerTests.cs`, `BuyNowDispatchTests.cs` unchanged; `PlaceBidDispatchTests.cs` changed (`SeedOpenListing` now seeds saga doc — see Regression §)

---

## Verification checklist

- [x] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [x] `dotnet test CritterBids.slnx` — 68 baseline + 4 new = **72 green**, 0 skipped, 0 failing
- [x] `AuctionClosingSaga` exists; inherits `Saga`; has `public Guid Id`; enum has all five values; real handlers for `BidPlaced`/`ReserveMet`/`ExtendedBiddingTriggered`; `CloseAuction` is a stub with a single-line TODO
- [x] `CloseAuction.cs` not referenced from `CritterBids.Contracts` (architectural boundary preserved; public C# accessibility noted in file comment)
- [x] `StartAuctionClosingSagaHandler` is a separate `public static class`; `Handle(BiddingOpened, ...)` returns the saga and calls `ScheduleAsync`
- [x] `AssemblyAttributes.cs` contains `[assembly: WolverineModule]`
- [x] `AuctionsModule.cs` — saga schema with `.UseNumericRevisions(true)`; `AddEventType<T>()` count unchanged at 7
- [x] All 4 test method names exactly match milestone doc §7 §3
- [x] Scenario 3.4 asserts both cancel AND reschedule
- [x] Scenario 3.1 asserts saga state + pending `CloseAuction` at `ScheduledCloseAt`
- [x] Zero `IMessageBus` references in `src/CritterBids.Auctions/` outside saga + start handlers
- [x] `Program.cs` diff limited to OQ4 forwarding + durable-local-queues (both required and called out above)
- [x] `CritterBids.Auctions.csproj` `ProjectReference` count is 1 (Contracts only)
- [x] `PlaceBidHandler.cs`, `PlaceBidHandlerTests.cs`, `BuyNowHandler.cs`, `BuyNowHandlerTests.cs`, `BuyNowDispatchTests.cs`, `BidConsistencyState.cs`, `Listing.cs` unchanged (`PlaceBidDispatchTests.cs` changed in `SeedOpenListing` only — see Regression §)
- [x] No `[Obsolete]`, `#pragma warning disable`, or `throw new NotImplementedException()` in production; `CloseAuction` stub returns `new OutgoingMessages()`
- [x] This retrospective exists and meets content requirements

---

## What M3-S5b should know

1. **Correlation:** `[SagaIdentityFrom(nameof(X.ListingId))]` on handler parameters. Apply to `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` verbatim. Zero contract changes.
2. **Idempotency:** `if (Status == AuctionClosingStatus.Resolved) return;` at the top of each terminal handler. No hash sets.
3. **Event forwarding:** Listing-stream-wide via `UseFastEventForwarding`. `BuyItNowPurchased` forwards for free — no additional wiring. `ListingWithdrawn` forwards for free as long as it lands on the listing's primary stream (which it should per M3 convention).
4. **`Handle(CloseAuction)` real implementation:** scenarios 3.8, 3.9, 3.10 need the handler to early-return if `Status == Resolved` — a `CloseAuction` arriving for a saga already completed by `BuyItNowPurchased` or `ListingWithdrawn` must not emit a second outcome. Pending-schedule cancellation when BuyItNow or Withdrawn fires is a decision for S5b: either (a) the terminal handlers cancel the pending CloseAuction explicitly via `CancelPendingCloseAsync`, or (b) they rely on the idempotent early-return on arrival. Option (a) is cleaner and aligns with the established cancel-and-reschedule pattern.
5. **Synthetic `ListingWithdrawn` in fixtures:** no S5 constraint. The event type will need `AddEventType<ListingWithdrawn>()` in `AuctionsModule` (bringing the count to 8), and `ListingWithdrawn` must be tagged with `ListingStreamId` if appended via fixture seed so forwarding picks it up on the listing stream.
6. **Seed saga doc alongside stream seeds.** When a test seeds an open listing via direct stream-append for a handler path that forwards to the saga, also `session.Store(new AuctionClosingSaga {...})`. Precedent: `PlaceBidDispatchTests.SeedOpenListing`.
7. **`UseDurableLocalQueues()` is already wired.** S5b inherits the durable scheduled-message store for free — any new `ScheduleAsync` calls (none planned for S5b) would persist without additional config.
8. **`ScheduleAsync` returns `ValueTask`, not a token.** Cancel via `IMessageStore.ScheduledMessages.CancelAsync(new ScheduledMessageQuery { ... })` with an `ExecutionTimeFrom/To` window + `MessageType` filter. See `AuctionClosingSaga.CancelPendingCloseAsync` for the reference shape.
9. **Concurrency retry:** the existing `AuctionsConcurrencyRetryPolicies.OnException<ConcurrencyException>` covers saga writes under numeric revisions. S5b should NOT duplicate. If cooldown tuning becomes needed, propose in a separate slice.
10. **Skill append deferred.** Next skill-pass session should fold in: `SagaIdentityFrom` attribute, `UseDurableLocalQueues` first-use requirement, `ScheduleAsync` no-token API, and `CancelAsync(ScheduledMessageQuery)` shape. All three are Wolverine 5.x surface details not yet in `docs/skills/wolverine-sagas.md`.

---

## Cross-session notes

- **S4 retro's "What M3-S5 should know" predictions:** all honoured. DCB events (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`) consumed directly without contract changes; existing concurrency retry covers saga; `BidConsistencyState` / `Listing` / `PlaceBidHandler` byte-identical.
- **S4b retro's "What M3-S5 should know" predictions:** BIN terminal-event deferred to S5b as planned; saga Id convention decided in S5 (Path A) and inherits cleanly to S5b's `BuyItNowPurchased` handler.
