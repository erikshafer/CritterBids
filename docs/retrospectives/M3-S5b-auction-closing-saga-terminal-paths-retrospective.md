# M3-S5b: Auction Closing Saga — Close Evaluation + Terminal Paths — Retrospective

**Date:** 2026-04-18
**Milestone:** M3 — Auctions BC
**Slice:** S5b of 9 (paired with S5; closes the auction closing saga scope per milestone doc §7 §3 scenarios 3.5–3.11)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/M3-S5b-auction-closing-saga-terminal-paths.md`

---

## Baseline

- 72 tests passing (1 Api + 1 Contracts + 4 Listings + 6 Participants + 28 Auctions + 32 Selling) — verified at S5b start
- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- S5 closed with `AuctionClosingSaga` skeleton + forward path live (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, Start-on-`BiddingOpened`); `Handle(CloseAuction)` was a stub returning `new OutgoingMessages()`; 7 `AddEventType<T>()` registrations; `UseFastEventForwarding = true` and `UseDurableLocalQueues()` already wired in `Program.cs` and `AuctionsTestFixture`
- `[SagaIdentityFrom(nameof(X.ListingId))]` correlation convention and `BidCount` monotonicity idempotency convention established and reused without redesign
- `PlaceBidDispatchTests.SeedOpenListing` already seeded a saga doc per S5 retro §Regression — S5b applies the same pattern to `BuyNowDispatchTests` per item 8

## Session outcome

- 79 tests passing (+7 scenarios in `AuctionClosingSagaTests`) — exactly the prompt's target
- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `Handle(CloseAuction)` real implementation: cascades `BiddingClosed` + one of `ListingSold` / `ListingPassed` (`NoBids` or `ReserveNotMet`); `Status = Resolved`; `MarkCompleted()`
- Two terminal handlers added: `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` — uniform shape (Resolved-guard, explicit cancel of pending CloseAuction, MarkCompleted, no outcome events)
- One static `NotFound(CloseAuction)` named-method convention added on the saga to silently no-op when a pending CloseAuction fires for a saga whose document was already deleted by MarkCompleted (workshop scenario 3.9)
- `ListingWithdrawn` contract authored in `CritterBids.Contracts.Selling` (OQ6 Path A); registered as a Marten event type (OQ5-related — see below); the Selling-side publisher remains deferred per M3 §3 (test fixture is the synthetic producer)
- `AuctionsModule.AddEventType<T>()` count: 7 → 8 (just `ListingWithdrawn`; outcome events stay bus-only per OQ5)
- Three additive fixture helpers: `SeedAuctionClosingSagaAsync`, `SeedListingStreamAsync`, `AppendListingWithdrawnAsync`
- One carry-forward fix to `BuyNowDispatchTests.SeedOpenListing` per item 8 (mirrors `PlaceBidDispatchTests` precedent from S5)
- No `Program.cs` changes; no skill append needed

---

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| 1 | `AuctionClosingSaga.Handle(CloseAuction)` real implementation: outcome decision, cascade, terminal | dbb521b |
| 2 | `AuctionClosingSaga.Handle(BuyItNowPurchased)` terminal handler | 32fd451 |
| 3 | `AuctionClosingSaga.Handle(ListingWithdrawn)` terminal handler | af2e42b |
| 4 | `AuctionsModule.AddEventType<ListingWithdrawn>()` (count 7→8); outcome events NOT registered (OQ5 Path ◦) | 9114d3e |
| 5 | `CritterBids.Contracts.Selling.ListingWithdrawn` (OQ6 Path A) | 9114d3e |
| 6 | `AuctionClosingSagaTests.cs` — 7 new scenarios (3.5–3.11) | dbb521b, 32fd451, af2e42b |
| 7 | `AuctionsTestFixture` additive helpers: `SeedAuctionClosingSagaAsync`, `SeedListingStreamAsync`, `AppendListingWithdrawnAsync` | dbb521b |
| 8 | `BuyNowDispatchTests.SeedOpenListing` saga-doc seed (carry-forward) | 310adc5 |
| 9 | `wolverine-sagas.md` skill append | **Skipped** — see Skill discussion below |
| 10 | This retrospective | (final commit) |

Out-of-item additions required by first-use surprises:

| Change | Reason |
|--------|--------|
| `AuctionClosingSaga.NotFound(CloseAuction)` static named-method | Required by Wolverine's `SagaChain` codegen branch — without it, a CloseAuction that fires after `MarkCompleted()` deleted the saga doc throws `UnknownSagaException`. See OQ4 finding below. |
| `Handle(CloseAuction)` reads `SellerId` via `session.Events.AggregateStreamAsync<Listing>` | `StartAuctionClosingSagaHandler` is frozen from S5 and does not capture `SellerId` on the saga state. The `ListingSold` contract requires it. Loading the live `Listing` projection (populated by `Apply(BiddingOpened)`) is the cheapest path that preserves the frozen-handler invariant. |

---

## S5b-1 — `Handle(CloseAuction)` real implementation

### Outcome decision shape

```csharp
public async Task<OutgoingMessages> Handle(
    [SagaIdentityFrom(nameof(CloseAuction.ListingId))] CloseAuction message,
    IDocumentSession session, TimeProvider time, CancellationToken cancellationToken)
{
    if (Status == AuctionClosingStatus.Resolved) return new OutgoingMessages();

    var now = time.GetUtcNow();
    var messages = new OutgoingMessages { new BiddingClosed(ListingId, now) };

    if (BidCount > 0 && ReserveHasBeenMet) {
        var listing = await session.Events.AggregateStreamAsync<Listing>(ListingId, token: cancellationToken);
        messages.Add(new ListingSold(ListingId, listing!.SellerId, CurrentHighBidderId!.Value,
            CurrentHighBid, BidCount, now));
    } else if (BidCount > 0) {
        messages.Add(new ListingPassed(ListingId, "ReserveNotMet", CurrentHighBid, BidCount, now));
    } else {
        messages.Add(new ListingPassed(ListingId, "NoBids", null, BidCount, now));
    }

    Status = AuctionClosingStatus.Resolved;
    MarkCompleted();
    return messages;
}
```

### Why this approach

- **Cascade via `OutgoingMessages` return value** — OQ3 Path I. `wolverine-sagas.md` §"Cascading messages from a saga" prescribes returning `OutgoingMessages` over imperative `IMessageBus.PublishAsync`. The latter would also break the "zero `IMessageBus.PublishAsync` in saga handlers" invariant established in S5 retro §build state.
- **`BiddingClosed` always precedes the outcome** — uniform consumer contract for S6's `CatalogListingView`. Per `BiddingClosed.cs:5-7` ("Emitted by the Auction Closing saga when the scheduled close fires (and on the terminal path for reserve-met / reserve-not-met / no-bids)"), this matches the contract author's stated intent. OQ1 Path B applies only to BIN + Withdrawn terminal paths, where the contract docs explicitly opt out.
- **`SellerId` from `AggregateStreamAsync<Listing>`, not from saga state.** The frozen `StartAuctionClosingSagaHandler` does not capture `SellerId`, and `BiddingOpened` carries it (verified by reading `BiddingOpened.cs`). Loading the live aggregate via `session.Events.AggregateStreamAsync<Listing>(ListingId)` reads what `Apply(BiddingOpened)` already populated. Two alternatives rejected: (a) modifying the saga state to capture `SellerId` would require touching the frozen start handler; (b) adding `SellerId` as a `[SagaIdentityFrom]` companion is not the attribute's purpose.
- **No outcome events on the early-return branch.** Scenario 3.9 — a `CloseAuction` arriving for a saga already terminated by `BuyItNowPurchased` — must produce zero cascaded events. `return new OutgoingMessages()` (empty collection) is the saga-idiomatic equivalent of "no cascade".

### Tests

| Scenario | Method | Assertion focus |
|---|---|---|
| 3.5 | `Close_ReserveMet_ProducesListingSold` | `BiddingClosed` + `ListingSold` cascaded; `HammerPrice` from saga `CurrentHighBid`; `WinnerId` from `CurrentHighBidderId`; `SellerId` resolved from Listing aggregate; saga deleted |
| 3.6 | `Close_ReserveNotMet_ProducesListingPassed` | `BiddingClosed` + `ListingPassed(Reason="ReserveNotMet", HighestBid=CurrentHighBid)`; saga deleted |
| 3.7 | `Close_NoBids_ProducesListingPassed` | `BiddingClosed` + `ListingPassed(Reason="NoBids", HighestBid=null)`; saga deleted |
| 3.11 | `Close_AfterExtension_UsesRescheduledTime` | Seeds `Status = Extended` with `ScheduledCloseAt > original`; close evaluation runs the same outcome logic — extension is structurally invisible to `Handle(CloseAuction)` |

---

## S5b-2 + S5b-3 — Terminal handlers (`BuyItNowPurchased`, `ListingWithdrawn`)

### Uniform shape

```csharp
public async Task Handle(
    [SagaIdentityFrom(nameof(X.ListingId))] X message,
    IMessageStore messageStore, CancellationToken cancellationToken)
{
    if (Status == AuctionClosingStatus.Resolved) return;
    await CancelPendingCloseAsync(messageStore, ScheduledCloseAt, cancellationToken);
    Status = AuctionClosingStatus.Resolved;
    MarkCompleted();
}
```

Both handlers share this exact body. Two methods rather than a generic helper because Wolverine's convention-based discovery binds on the parameter type — each entry-point must be its own method anyway.

### Structural metrics — saga before/after S5b

| Metric | After S5 | After S5b |
|---|---|---|
| Real handlers on `AuctionClosingSaga` | 3 + 1 stub | **6** (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `CloseAuction`, `BuyItNowPurchased`, `ListingWithdrawn`) |
| Static `NotFound(CloseAuction)` named-method | 0 | 1 |
| Handlers using `[SagaIdentityFrom]` | 4 of 4 | **6 of 6** |
| `MarkCompleted()` calls | 0 | **3** (one per terminal handler + `Handle(CloseAuction)` outcome path) |
| `OutgoingMessages` returns | 0 | **1** (`Handle(CloseAuction)`) |
| `AddEventType<T>()` count in `AuctionsModule` | 7 | **8** (`ListingWithdrawn` added; outcome events stay bus-only) |

### Why no `BiddingClosed` on the BIN / Withdrawn paths (OQ1 Path B)

`BiddingClosed.cs:18-20` is explicit: "Not emitted on the BuyItNow terminal path (scenario 3.8) — BIN is its own outcome and skips the mechanical close signal." For withdrawal, `ListingPassed.cs:6-7` says "Not emitted on the ListingWithdrawn path — withdrawal is a separate terminal event outside the Auctions BC vocabulary." The contracts authored in S1 had already settled this — S5b is honouring those decisions, not re-litigating them. The prompt's recommendation toward Path A (uniform consumer contract, `BiddingClosed` everywhere) was rejected on the strength of the contract author's explicit intent.

### Idempotency assertions

Scenarios 3.8 and 3.10 both verify pending `CloseAuction` is cancelled (via `QueryPendingCloseAuctionsAsync` returning empty) — proves OQ2 Path (a) explicit-cancel discipline is wired. Scenario 3.9 (`CloseAuction_AfterBuyItNow_NoOp`) seeds a `Resolved` saga directly and dispatches `CloseAuction`; the early-return branch fires and saga state is byte-identical before and after.

---

## S5b-8 — `BuyNowDispatchTests` carry-forward

`BuyNowDispatchTests.SeedOpenListing` appends `BiddingOpened` directly via `session.Events.StartStream<Listing>(...)`. That session is not handler-scoped, so `UseFastEventForwarding` does not fire and no saga gets started (per S5 retro §OQ4 finding). With S5b's `Handle(BuyItNowPurchased)` live, the cascaded `BuyItNowPurchased` from `BuyNowHandler` forwards to the saga and throws:

```
System.AggregateException : One or more errors occurred. (Could not find an expected saga document
of type CritterBids.Auctions.AuctionClosingSaga for id '019da3ce-c9d7-71b4-af21-455a68908de2'.
Note: new Sagas will not be available in storage until the first message succeeds.)
---- Wolverine.Persistence.Sagas.UnknownSagaException
```

Fix: seed an `AuctionClosingSaga` document via the new `_fixture.SeedAuctionClosingSagaAsync(listingId, AuctionClosingStatus.AwaitingBids, ...)` helper before invoking `BuyNow`. Mirrors `PlaceBidDispatchTests` precedent (commit 47f7cc4) exactly. Production `BuyNowHandler.cs` and the test invocation body are byte-identical.

Landed as standalone commit 310adc5 ahead of 32fd451 so the suite was green at every commit. **`git bisect` discipline.**

---

## Open Questions — answered

### OQ1 — Do BIN / Withdrawn handlers emit `BiddingClosed`? → **Path B (no)**

**Source of truth:** `src/CritterBids.Contracts/Auctions/BiddingClosed.cs:18-20` — "Not emitted on the BuyItNow terminal path (scenario 3.8)" — and `src/CritterBids.Contracts/Auctions/ListingPassed.cs:6-7` — "Not emitted on the ListingWithdrawn path". S1 authored these contract docs; S5b honours them. The prompt's Path A recommendation (uniform `BiddingClosed`) would have contradicted the existing contract; chose Path B without further negotiation.

Workshop 002-scenarios.md §3.8 lists `BuyItNowPurchased` as the sole terminal output. §3.10 lists no terminal output at all (withdrawal is outside the BC vocabulary). The contract docs are correct.

### OQ2 — Do terminal handlers explicitly cancel the pending `CloseAuction`? → **Path (a) — explicit cancel**

`Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` each call `CancelPendingCloseAsync(messageStore, ScheduledCloseAt, cancellationToken)` before `MarkCompleted()`. Belt-and-suspenders alongside two other safety nets:

1. `MarkCompleted()` deletes the saga document (per OQ4 below)
2. The static `NotFound(CloseAuction)` method silently no-ops if a CloseAuction slips through cancellation

Scenarios 3.8 and 3.10 both assert `QueryPendingCloseAuctionsAsync()` returns empty — verifies the explicit cancel path. Without explicit cancel, the scheduled CloseAuction would still fire, hit the NotFound branch, and execute correctly — but the scheduled-message store would be observably noisier in production. S5 retro recommended Path (a); S5b confirmed.

### OQ3 — How are outcome events emitted? → **Path I — `OutgoingMessages` cascade**

**Source of truth:** `C:\Code\JasperFx\ai-skills\wolverine\handlers\building-handlers.md:272-285` — documented `OutgoingMessages` return-value cascade pattern; the saga handler returns `Task<OutgoingMessages>` and Wolverine cascades each contained message via the standard outbox in the same transaction as the saga document write. This is the saga-idiomatic mechanism per `wolverine-sagas.md` §"Cascading messages from a saga".

Path II (append + forward via `UseFastEventForwarding`) was rejected because:
- It would couple saga emission to stream append (tighter than the skill prescribes)
- It would require registering `BiddingClosed` / `ListingSold` / `ListingPassed` as Marten event types (bringing the count to 11) for no compensating benefit at S5b scope
- OQ5 ultimately settled bus-only (Path ◦) as the consistent choice — Path I and OQ5 Path ◦ are mutually reinforcing

**Test observation pattern:** `Host.TrackActivity().DoNotAssertOnExceptionsDetected().InvokeMessageAndWaitAsync(...)` returns an `ITrackedSession`. Cascaded messages with no routing rule (RabbitMQ disabled in tests, no `opts.Publish` for outcome events until M3-S6) land in `tracked.NoRoutes`. The envelope record carries the message body for assertions via `tracked.NoRoutes.MessagesOf<T>()`. This is the uniform "capture outcome events" helper shape (item 7) — implemented inline rather than as a fixture method because the call site is a single fluent chain.

### OQ4 — Saga document lifecycle under `.UseNumericRevisions(true)` + `MarkCompleted()` → **physically deleted**

**Source of truth:** `C:\Code\JasperFx\wolverine\src\Wolverine\Saga.cs:12-28` — `MarkCompleted()` sets the internal `IsCompleted` flag, which the saga persistence layer reads at commit time and emits a `Delete` against the saga document instead of an upsert. After commit, `session.LoadAsync<AuctionClosingSaga>(listingId)` returns `null`. All four terminal-path tests (3.5, 3.7, 3.8, 3.10) assert exactly this — `(await _fixture.LoadSaga<AuctionClosingSaga>(listingId)).ShouldBeNull()`.

**First-use surprise — `UnknownSagaException` on post-MarkCompleted CloseAuction.** First test run for scenario 3.9 surfaced:

```
System.AggregateException : One or more errors occurred. (Could not find an expected saga document
of type CritterBids.Auctions.AuctionClosingSaga for id '019da4...')
---- Wolverine.Persistence.Sagas.UnknownSagaException
```

Cause: scenario 3.9 seeds a `Resolved` saga then dispatches `CloseAuction` — the seeded doc IS present, so the test passed. But scenarios 3.8 and 3.10 (BIN / Withdrawn) terminate via `MarkCompleted()` first, and although they cancel the pending CloseAuction explicitly, a fast race or non-cancelled CloseAuction would arrive at a deleted saga and throw.

**Fix:** Wolverine's `SagaChain` codegen has an undocumented escape hatch. **Source of truth:** `C:\Code\JasperFx\wolverine\src\Wolverine\Persistence\Sagas\SagaChain.cs:24,354-366` — when a static method named `NotFound` exists on the saga type matching the message signature, the codegen routes the "saga not found" branch to that method instead of emitting `AssertSagaStateExistsFrame` (which throws). Added:

```csharp
public static OutgoingMessages NotFound(CloseAuction message) => new();
```

Silent no-op. No exception, no observable side effect, no log noise. Matches the workshop scenario 3.9 expected behaviour exactly.

### OQ5 — Outcome events appended to stream or bus-only? → **Path ◦ (bus-only)**

`AddEventType<T>()` count is 8, not 11. `BiddingClosed`, `ListingSold`, `ListingPassed` are NOT registered as Marten event types. They cascade via `OutgoingMessages` only.

Rationale: integration events with explicit "Consumed by:" lists across multiple BCs (per their contract docs) are routed via Wolverine's outbox, not stored on the producing BC's event streams. The saga IS the source of truth for close state — no BC needs to event-source-replay outcome events from a Listing stream. Per S5 retro §What S5b should know item 5, the only required addition was `AddEventType<ListingWithdrawn>()` (consumed event, needs registration for typed payload resolution on stream replay/forwarding); the produced events stay off the streams.

**S6 implication:** `CatalogListingView`'s consumer must subscribe to `listings-auctions-events` queue from commit-one — no replay path. This is already the convention per `docs/skills/integration-messaging.md` L2.

### OQ6 — `ListingWithdrawn` location → **Path A (`CritterBids.Contracts.Selling`)**

`src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` — minimum-viable payload `(Guid ListingId)`. Full contract doc explains: Selling-side publisher deferred per M3 §3; saga consumes today; future Selling-side withdrawal command lands without contract churn (per `docs/skills/integration-messaging.md` L2 contract-stability discipline).

Path B (`CritterBids.Auctions`) rejected — would have forced a relocation when Selling implements withdrawal. Path C (fixture-only synthetic) rejected — production saga code would have nothing to subscribe to. Path A pays the contract cost up front for zero rework later.

No ADR flagged. Withdrawal-reason field deferred to the Selling-side authoring session — adding optional fields later is a non-breaking contract change.

---

## Test results

| Phase | Auctions tests | Full solution | Result |
|-------|----------------|---------------|--------|
| Baseline (S5 close) | 28 | 72 | all green |
| After Commit 1 (`ListingWithdrawn` contract + event registration) | 28 | 72 | all green (unchanged surface) |
| After Commit 2 (`Handle(CloseAuction)` real + scenarios 3.5/3.6/3.7/3.11) | 32 | 76 | all green |
| After Commit 3 (`Handle(BuyItNowPurchased)` + scenarios 3.8/3.9, before BuyNowDispatchTests fix) | 33 | — | **1 regression** in `BuyNowDispatchTests.BuyNow_DispatchedViaBus_AppendsBuyItNowPurchasedToTaggedStream` (`UnknownSagaException`); BIN scenarios 3.8/3.9 green |
| Fix: seed saga doc in `BuyNowDispatchTests.SeedOpenListing` (Commit 4 — landed AHEAD of Commit 3 in git history to keep main green) | 33 | 77 | all green |
| After Commit 5 (`Handle(ListingWithdrawn)` + scenario 3.10) | 35 | 79 | all green |

Final: 79 tests passing, 0 skipped, 0 failing. `dotnet build CritterBids.slnx` → 0 errors, 0 warnings.

**Commit ordering note.** Per the prompt's commit sequence, Commit 4 (BuyNowDispatchTests fix) was authored as a follow-up to Commit 3 (BIN handler). In the actual landing order the fix went FIRST (310adc5) then the handler (32fd451), so every commit on `main` is independently green for `git bisect`. Saved as a learning for future "feat needs prior test infra" sessions.

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` — 79 passed, 0 skipped, 0 failed
- `AddEventType<T>()` count in `AuctionsModule.cs`: **8** (S5's 7 + `ListingWithdrawn`)
- Saga document schema registrations: **1** (`AuctionClosingSaga`, unchanged from S5)
- Real handlers on `AuctionClosingSaga`: **6**; static `NotFound` methods: **1**
- `MarkCompleted()` calls in `src/CritterBids.Auctions/`: **3** (one per terminal path: `Handle(CloseAuction)` outcome, `Handle(BuyItNowPurchased)`, `Handle(ListingWithdrawn)`)
- `OutgoingMessages` returns from saga handlers: **2** signature points (`Handle(CloseAuction)`, static `NotFound`)
- `IMessageBus.PublishAsync` calls inside saga handler bodies: **0** (invariant from S5 preserved)
- `bus.ScheduleAsync` calls inside saga handler bodies: **1** (`Handle(ExtendedBiddingTriggered)`, unchanged from S5)
- `messageStore.ScheduledMessages.CancelAsync` calls: **1** (the `CancelPendingCloseAsync` helper, called from 3 handlers)
- `Program.cs` byte-level diff vs S5 close: **none**
- `CritterBids.Auctions.csproj` `ProjectReference` count: **1** (Contracts only)
- `throw new NotImplementedException()` / `[Obsolete]` / `#pragma warning disable` in production: **0**
- S4/S4b/S5 frozen production files byte-level diff: `PlaceBidHandler.cs`, `BuyNowHandler.cs`, `BidConsistencyState.cs`, `Listing.cs`, `BidRejectionAudit.cs`, `StartAuctionClosingSagaHandler.cs` all unchanged
- S4/S4b/S5 frozen test files diff: `PlaceBidHandlerTests.cs`, `BuyNowHandlerTests.cs`, `PlaceBidDispatchTests.cs`, scenario tests 3.1–3.4 unchanged; `BuyNowDispatchTests.cs` changed (saga seed only, per item 8)

---

## First-use surprises worth capturing

1. **Wolverine's `NotFound` named-method convention is undocumented in the AI Skills repo.** Discovered by reading `C:\Code\JasperFx\wolverine\src\Wolverine\Persistence\Sagas\SagaChain.cs:24,354-366` after the first scenario 3.8 run threw `UnknownSagaException` despite explicit cancellation — the race between cancel and fire is real. The static `public static OutgoingMessages NotFound(CloseAuction)` shape is the silent-handle escape. Worth folding into `wolverine-sagas.md` in a future skill pass (see Skill discussion below).
2. **Saga state cannot reach back to handler-construction-time data without a frozen-handler change.** `Handle(CloseAuction)` needs `SellerId` for `ListingSold`; the start handler doesn't capture it; the only clean path is `session.Events.AggregateStreamAsync<Listing>(ListingId)` to re-read the live aggregate. Acceptable cost — the listing aggregate is small and the read happens once per close. Future close-aggregating sagas should consider capturing fields the start handler can populate cheaply.
3. **Tests observe cascaded outcomes via `tracked.NoRoutes.MessagesOf<T>()` when no routing rule exists.** Without an `opts.Publish` rule for the outcome events (deferred to M3-S6 with the `listings-auctions-events` queue wiring), Wolverine's tracking-session classifies cascaded messages as `NoRoutes` rather than `Sent`. The envelope still carries the body, so assertions work, but the mental model "Sent = fired" is wrong here. Worth noting in `critter-stack-testing-patterns.md` next time it's revised — this is a Wolverine outbox-tracking detail that the existing memory entry [Wolverine outbox tracking requires routing](C:\Users\Erik\.claude\projects\C--Code-CritterBids\memory\feedback_wolverine_outbox_tracking.md) flagged from CritterSupply but was not yet documented for CritterBids.
4. **`IMessageBus` is scoped — resolving from `Host.Services` (root) throws.** `ScheduleCloseAuctionAsync` test helper failed first run with `InvalidOperationException : Cannot resolve scoped service 'Wolverine.IMessageBus' from root provider.` Fix: `await using var scope = _fixture.Host.Services.CreateAsyncScope(); var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();`. The Wolverine-idiomatic alternative `host.MessageBus()` extension does the same scope dance internally — chose the explicit scope for legibility in test code.

---

## Skill file — append not written (item 9)

Evaluated the four first-use surprises against `docs/skills/wolverine-sagas.md` and `C:\Code\JasperFx\ai-skills\wolverine\sagas\`:

- **Surprise 1 (`NotFound` convention)** — genuinely missing from both the local skill file and the AI Skills repo source. Worth a skill-pass append, but the append should be authored as a self-contained section ("Saga not found — silent vs throwing") with full source citation rather than ad-hoc. Deferred to a broader pass.
- **Surprise 2 (re-reading aggregate state for emission)** — pattern, not a Wolverine API gap. Belongs in a "Saga state minimality" discussion rather than the API reference.
- **Surprise 3 (`tracked.NoRoutes` vs `Sent`)** — testing-infrastructure detail. Belongs in `critter-stack-testing-patterns.md` if anywhere; the existing memory entry on outbox-tracking-requires-routing covers the "why" already.
- **Surprise 4 (`IMessageBus` scope)** — DI hygiene, not Wolverine-specific. Not skill-file material.

Following the S5 precedent (no append for surprises that are "things the skill did not cover but should have eventually" rather than "things the skill predicted wrongly"), and the S4b precedent before that, no skill append for S5b either. The accumulated skill-pass debt is now real (S4b, S5, S5b each produced first-use findings worth folding in) — recommended that M3-S6 or an M3-end skill-pass session bulk-fold them.

---

## Verification checklist

- [x] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [x] `dotnet test CritterBids.slnx` — 72-test baseline preserved; +7 new tests green; zero skipped, zero failing; **total 79**
- [x] `Handle(CloseAuction)` is real (no TODO, no `new OutgoingMessages()`-as-noop without branching); `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` exist with `[SagaIdentityFrom(nameof(X.ListingId))]` and `Status == Resolved` idempotency guard; terminal transitions call `MarkCompleted()`
- [x] `AuctionsModule.cs` — `AddEventType<T>()` count is **8** (OQ5 Path ◦; outcome events bus-only)
- [x] `CritterBids.Contracts.Selling/ListingWithdrawn.cs` exists with `Guid ListingId` payload
- [x] All 7 test method names exactly per milestone doc §7 §3 rows 3.5–3.11, each green
- [x] Scenario 3.5 — `ListingSold` with `HammerPrice` from saga state; `BiddingClosed` also emitted
- [x] Scenario 3.6 — `ListingPassed(Reason="ReserveNotMet")`
- [x] Scenario 3.7 — `ListingPassed(Reason="NoBids")`
- [x] Scenario 3.8 — saga `Status == Resolved` (deleted, returns null on reload); pending `CloseAuction` set empty; no `BiddingClosed` (OQ1 Path B)
- [x] Scenario 3.9 — `CloseAuction` arriving at Resolved saga produces zero outcome events; saga state byte-identical
- [x] Scenario 3.10 — saga deleted after `ListingWithdrawn`; no outcome events; pending `CloseAuction` set empty
- [x] Scenario 3.11 — close evaluation fires at extended `ScheduledCloseAt`
- [x] `BuyNowDispatchTests.cs` — `SeedOpenListing` seeds saga doc; production `BuyNowHandler.cs` byte-identical
- [x] Zero `IMessageBus.PublishAsync` in saga handler bodies
- [x] `Program.cs` diff vs S5: **none**
- [x] `CritterBids.Auctions.csproj` `ProjectReference` count: **1**
- [x] All S4/S4b/S5 frozen production files byte-identical
- [x] Scenario tests 3.1–3.4 from S5 unchanged and green
- [x] No `[Obsolete]` / `#pragma warning disable` / `throw new NotImplementedException()` in production
- [x] This retrospective exists and meets content requirements

---

## What M3-S6 should know

1. **Final outcome-event payload shapes (as actually emitted by the saga):**
   - `BiddingClosed(Guid ListingId, DateTimeOffset ClosedAt)` — emitted on `Handle(CloseAuction)` only
   - `ListingSold(Guid ListingId, Guid SellerId, Guid WinnerId, decimal HammerPrice, int BidCount, DateTimeOffset SoldAt)` — `SellerId` is sourced from the live `Listing` aggregate at close time; reliable
   - `ListingPassed(Guid ListingId, string Reason, decimal? HighestBid, int BidCount, DateTimeOffset PassedAt)` — `Reason` is one of two string literals: `"NoBids"` or `"ReserveNotMet"`; `HighestBid` is `null` when `Reason="NoBids"`, otherwise the high-bid amount at close
   - `BuyItNowPurchased` — terminal on its own, no `BiddingClosed` precedes it; S6's catalog projection must subscribe directly if a "sold via BIN" status is desired
2. **Outcome events are bus-only — no stream replay path.** Per OQ5 Path ◦. `CatalogListingView`'s subscriber MUST be wired before the first auction closes in any environment that needs the projection populated. There is no "rebuild the projection from listing streams" recovery path for these outcomes — they only ever existed on the bus.
3. **Per-listing event ordering on the queue** (when wired):
   - Sold path: `BiddingClosed` → `ListingSold`
   - Reserve-not-met or no-bids path: `BiddingClosed` → `ListingPassed`
   - BuyItNow path: `BuyItNowPurchased` (sole event for that listing's close)
   - Withdrawal path: `ListingWithdrawn` (sole event; produced by Selling once that BC implements withdrawal — until then no withdrawal events appear on this queue in production)
   The two-event sold/passed paths cascade atomically inside the same outbox transaction, so per-listing ordering is guaranteed even under concurrent listing closes.
4. **Saga-side invariant: `BiddingClosed` always precedes the outcome event on the timer paths.** S6's `CatalogListingView` consumer can rely on this for state-machine modelling: `Status="Closed"` (from `BiddingClosed`) is always set before `Status="Sold"` / `Status="Passed"` on the same listing.
5. **`BuyItNowPurchased` is owned by Auctions BC and produced by `BuyNowHandler` (DCB-driven), not by the saga.** The saga consumes it as a terminal-event signal; the `OutgoingMessages` cascade goes from `BuyNowHandler` → `OutgoingMessages` → bus → saga's `Handle(BuyItNowPurchased)`. S6's catalog handler should subscribe to `BuyItNowPurchased` directly rather than expecting it to follow a `BiddingClosed`.
6. **`AddEventType<T>()` count is 8.** If S6 adds outcome-event stream-append (Path ✻) for replay reasons that did not surface in S5b, three additions bring it to 11.
7. **`UseFastEventForwarding` and `UseDurableLocalQueues` are wired in `Program.cs` and `AuctionsTestFixture`.** S6 inherits both. No new infrastructure wiring required for catalog projection unless the projection introduces its own scheduled-message needs.
8. **The `listings-auctions-events` RabbitMQ queue is unwired at S5b close.** Outcome events cascade to `tracked.NoRoutes` in tests and would cascade to nowhere in production. S6's first task block is the queue wiring with `opts.Publish<BiddingClosed/ListingSold/ListingPassed>().ToRabbitQueue("listings-auctions-events")` (verify exact API surface from `C:\Code\JasperFx\wolverine\` source first — contract publish API has been less stable than handler-side over Wolverine 4→5).
9. **`ListingWithdrawn` is registered as a Marten event type** but no production publisher exists at M3 close. The Selling-side `WithdrawListing` command is deferred to a future Selling BC session per M3 §3. S6 should NOT wire a withdrawal handler in Listings until Selling implements the publisher — premature consumer would fire only on test-fixture synthetic events.

---

## Cross-session notes

- **S5 retro's "What M3-S5b should know" predictions:** all 10 honoured. Specifically: (1) `[SagaIdentityFrom]` reused without ceremony; (2) `Status == Resolved` idempotency guard reused without ceremony; (3) forwarding required no additional wiring; (4) explicit-cancel Path (a) chosen as recommended; (5) `AddEventType<ListingWithdrawn>()` brought count to 8 as predicted; (6) `BuyNowDispatchTests.SeedOpenListing` carry-forward landed exactly per S5 precedent; (7) `UseDurableLocalQueues` already wired; (8) `CancelPendingCloseAsync` shape inherited; (9) concurrency retry not duplicated; (10) skill append still deferred — bulk-pass session recommended.
- **Saga is feature-complete for M3.** Next saga work is M4's Proxy Bid Manager. The Auctions BC scope after S5b: DCB place-bid, DCB buy-it-now, full saga orchestration (start, forward path, close evaluation, three terminal paths), 35 tests covering all paths.
- **Frozen-file discipline scoreboard for the slice.** Production files unchanged: `BiddingOpened.cs`, `BidPlaced.cs`, `BidRejected.cs`, `ReserveMet.cs`, `ExtendedBiddingTriggered.cs`, `BuyItNowOptionRemoved.cs`, `BuyItNowPurchased.cs`, `ListingPublishedHandler.cs`, `PlaceBidHandler.cs`, `BuyNowHandler.cs`, `BidConsistencyState.cs`, `Listing.cs`, `ListingStreamId.cs`, `BidRejectionAudit.cs`, `StartAuctionClosingSagaHandler.cs`, `Program.cs`. Test files unchanged: `PlaceBidHandlerTests.cs`, `BuyNowHandlerTests.cs`, `PlaceBidDispatchTests.cs`, the four S5 scenario tests (3.1–3.4) inside `AuctionClosingSagaTests.cs`. The "byte-identical" gate held under audit.
