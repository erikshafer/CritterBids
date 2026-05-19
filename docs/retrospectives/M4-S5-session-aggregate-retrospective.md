# M4-S5: Session Aggregate + SessionStarted Fan-Out + PublishedListings Projection — Retrospective

**Date:** 2026-05-19
**Milestone:** M4 — Auctions BC Completion
**Slice:** S5 of 7 (S5b pre-drafted slot unused — base S5 absorbed all in-scope work)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M4-S5-session-aggregate.md`
**Baseline:** 134 tests passing · `dotnet build` 0 errors, 24 NU1904 NuGet warnings · M4-S4 closed at the squash-merge of PR #35 (`723c4a6`)

---

## Baseline

- 134 tests passing at session open (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 51 Auctions)
- `dotnet build` — 0 errors, 24 pre-existing NU1904 Marten vulnerability warnings (unchanged across M3 / M4 / M5)
- Session aggregate did not exist; `CreateSession` / `AttachListingToSession` / `StartSession` commands did not exist; `SessionStartedHandler` did not exist; `PublishedListings` projection did not exist
- `ListingPublishedHandler.cs:46` unconditionally unwrapped `message.Duration!.Value` and would `NullReferenceException` on Flash listings (`Duration == null`)
- The seven Auctions contract stubs authored at M4-S1 (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`) had no producer or in-BC consumer yet

---

## Phase table

| Phase | After commit | New tests | Total tests | Build | Note |
|-------|--------------|-----------|-------------|-------|------|
| Baseline | `723c4a6` | — | 134 | Green | Session open |
| PublishedListings projection + handler + Marten + Program.cs routes | `44f6af1` | 0 | 134 | Green | Source-only addition |
| ListingPublishedHandler Flash guard | `35ca492` | 0 | 134 | Green | M3 timed path preserved; existing BiddingOpenedConsumerTests still green |
| PublishedListings projection idempotency tests | `9de698c` | +2 | 136 | Green | First-delivery + terminal-state preservation on re-delivery |
| Session aggregate + Apply + Marten schema | `4e7b282` | 0 | 136 | Green | Source-only addition |
| Three Session commands + 7 scenarios | `c3d341d` | +7 | 143 | Green | All seven §5 scenarios green on first run |
| SessionStarted fan-out handler + 2 fan-out tests | `65c3e33` | +2 | 145 | Green | Pre-query stream-state mechanism worked on first run |
| Three Session dispatch tests | `60bcc54` | +3 | 148 | Green | OQ8 [WriteAggregate] codegen verified; CreateSession test needed IMessageBus.InvokeAsync<T> fallback |
| Retrospective | this commit | 0 | 148 | Green | Slice close |

Test count by project at close: 1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + **65 Auctions** = **148** (matches the prompt's exit criterion exactly).

---

## Items completed

| # | Description | Commit |
|---|-------------|--------|
| 1 | `src/CritterBids.Auctions/Session.cs` — sealed record event-sourced aggregate with static `Create(SessionCreated)` + instance `Apply(ListingAttachedToSession)` + `Apply(SessionStarted)` returning new instances via record `with`. First in-Auctions non-Listing aggregate. UUID v7 stream id per M4-D2. | `4e7b282` |
| 2 | `src/CritterBids.Auctions/CreateSession.cs` — create command (Title, DurationMinutes); handler returns `(CreationResponse<Guid>, IStartStream)` via `MartenOps.StartStream<Session>` (OQ4 — inherits M2 Selling shape). | `c3d341d` |
| 3 | `src/CritterBids.Auctions/AttachListingToSession.cs` — mutate command (SessionId, ListingId); `[WriteAggregate(nameof(AttachListingToSession.SessionId))]` + async `LoadAsync<PublishedListings>`; rejects with `ListingNotPublishedException` or `SessionAlreadyStartedException`. | `c3d341d` |
| 4 | `src/CritterBids.Auctions/StartSession.cs` — mutate command (SessionId); `[WriteAggregate(nameof(StartSession.SessionId))]`; rejects with `SessionHasNoListingsException` or `SessionAlreadyStartedException`; emits `SessionStarted` with the full attached-id list. | `c3d341d` |
| 5 | `src/CritterBids.Auctions/SessionStartedHandler.cs` — fan-out handler; iterates `ListingIds`, loads `PublishedListings` per listing, loads Session aggregate for `DurationMinutes`, appends `BiddingOpened` to each Listing stream via `session.Events.StartStream<Listing>`. Pre-query stream-state idempotency. | `65c3e33` |
| 6 | `src/CritterBids.Auctions/PublishedListings.cs` — sealed record Marten document with full BiddingOpened-precursor payload per OQ1 Path A. | `44f6af1` |
| 7 | `src/CritterBids.Auctions/PublishedListingsStatus.cs` — two-value enum (Published, Withdrawn). | `44f6af1` |
| 8 | `src/CritterBids.Auctions/PublishedListingsHandler.cs` — two `Handle` methods (`ListingPublished` + `ListingWithdrawn`) with terminal-status preservation. | `44f6af1` |
| 9 | `src/CritterBids.Auctions/ListingPublishedHandler.cs` — Flash-listing guard added (`if (message.Duration is null) return;`); XML doc updated for the two-path topology (Timed via this handler, Flash via Session fan-out). | `35ca492` |
| 10 | `src/CritterBids.Auctions/AuctionsModule.cs` — `Schema.For<PublishedListings>().DatabaseSchemaName("auctions")`; `LiveStreamAggregation<Session>()`; three Session event-type registrations (SessionCreated, ListingAttachedToSession, SessionStarted). | `44f6af1` + `4e7b282` |
| 11 | `src/CritterBids.Api/Program.cs` — three new publish routes to `listings-auctions-events` queue for the Session trio. No new queue. | `44f6af1` |
| 12 | `tests/CritterBids.Auctions.Tests/SessionAggregateTests.cs` — seven scenario `[Fact]`s (5.1-5.7) named exactly per milestone doc §7; direct handler invocation against real Marten. | `c3d341d` |
| 13 | `tests/CritterBids.Auctions.Tests/SessionStartedFanOutTests.cs` — two fan-out `[Fact]`s: `SessionStarted_ProducesBiddingOpenedPerListing` and `SessionStarted_Redelivery_DoesNotDoubleFireBiddingOpened`. | `65c3e33` |
| 14 | `tests/CritterBids.Auctions.Tests/CreateSessionDispatchTests.cs` — single dispatch `[Fact]` via `IMessageBus.InvokeAsync<CreationResponse<Guid>>` (not TrackActivity — see blocker below). | `60bcc54` |
| 15 | `tests/CritterBids.Auctions.Tests/AttachListingToSessionDispatchTests.cs` — single dispatch `[Fact]` via TrackActivity. OQ8 codegen smoke. | `60bcc54` |
| 16 | `tests/CritterBids.Auctions.Tests/StartSessionDispatchTests.cs` — single dispatch `[Fact]` via TrackActivity. OQ8 codegen smoke. | `60bcc54` |
| 17 | `tests/CritterBids.Auctions.Tests/PublishedListingsProjectionTests.cs` — two `[Fact]`s mirroring `ParticipantCreditCeilingProjectionTests` shape. | `9de698c` |
| 18 | `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — `SeedPublishedListingAsync` helper (workshop defaults explicit) + `SeedSessionAsync` helper (returns the seeded sessionId; supports attached-listing seeding + optional started state for §5.4 / §5.7). | `9de698c` + `c3d341d` |
| 19 | `tests/CritterBids.Auctions.Tests/BiddingOpenedConsumerTests.cs` — verified unchanged; both existing tests pass `Duration = 7d / 3d` (non-null), so the Flash guard is inert for them. No edit. | (verified at `35ca492`) |
| 20 | `docs/skills/marten-projections.md` — **not appended.** Nothing new surfaced beyond what the M5-S3 PendingSettlement section already covers (see Decisions Inheriting Forward §"Skill append discipline" below). | — |
| 21 | This retrospective. | this commit |

---

## Open Questions — resolutions

### OQ1 — PublishedListings field shape — **Path A (full BiddingOpened-precursor payload)** (pinned at session open)

**Resolution.** User pinned Path A at session open via AskUserQuestion before any code landed. Implemented as a sealed record with the full payload: `SellerId`, `StartingBid`, `ReservePrice`, `BuyItNowPrice`, `Duration`, `ExtendedBiddingEnabled`, `ExtendedBiddingTriggerWindow`, `ExtendedBiddingExtension`, `PublishedAt`, `WithdrawnAt`, `Status`. The `BuyItNow` → `BuyItNowPrice` rename mirrors Settlement's `PendingSettlement` (M5-S3) — both projections converge on the Workshop 002 scenario vocabulary.

**Confirmation it was the right call.** `SessionStartedHandler` reads the projection inline for SellerId / StartingBid / ReservePrice / BuyItNowPrice / extended-bidding fields on every fan-out emission — no second cross-projection or aggregate stream load needed per listing. Path B's "minimal status-only" shape would have forced a Listing-aggregate stream load per emission, and Flash listings have empty Listing streams at fan-out time (item 9 Flash guard), so Path B would have required a third lookup mechanism.

**Field shape extension noted.** The M4 milestone doc §6's "no fields beyond what the handler needs" framing was set at M4-S1 when only the `AttachListingToSession` handler's needs were traced. Path A stretches that framing to include the fan-out handler as a consumer. This expansion lands as precedent for the third lived M4-D4 application — Settlement's `BidderCreditView` (M5-S5) and Auctions's own `ParticipantCreditCeiling` (M4-S4) were the prior two. M4-S6 inherits the full-payload precedent.

### OQ2 — Fan-out idempotency mechanism — **Pre-query stream-state, not DCB**

**Resolution.** User pinned "DCB-primary first, halt-and-consult on failure" at session open. At implementation time, the milestone doc §6's "DCB-primary" framing turned out to conflate two distinct mechanisms:

1. **`BidConsistencyState` DCB** — the PlaceBidHandler's bid-acceptance tag-aggregate. Enforces invariants like "no two bidders at the same amount" on a per-listing tag stream. Applies to `PlaceBid` command acceptance, NOT to stream-opening events.
2. **Stream-existence idempotency** — the M3 `ListingPublishedHandler` idiom: `FetchStreamStateAsync(streamId)` → if non-null, skip. Applies to "should I open this listing's stream a second time?"

The fan-out's idempotency requirement is stream-opening, not bid-acceptance. The actual mechanism that landed is the M3 idiom verbatim — `FetchStreamStateAsync` pre-check before `StartStream<Listing>`. This is technically the OQ2 "fallback" shape, but the "primary" framing turned out to be load-bearing only on a mechanism (`BidConsistencyState` DCB) that doesn't apply at stream-opening time.

**No halt-and-consult was needed.** The redelivery test passed on first run with no exception propagation — the M3 idiom works cleanly.

**Pin for M4-S6 / future doc-cleanup.** The milestone doc §6's "DCB-primary" wording should be amended to "stream-existence pre-query" (or to whichever framing the next consumer of this section prefers). M4-S5's lived implementation is the authoritative reference.

### OQ3 — `StartSession` with a withdrawn listing — **Path α (no defensive pre-filter, lived terminal path TBD)**

**Resolution.** Implemented Path α per the milestone doc §3 directive verbatim: the fan-out emits `BiddingOpened` for every `ListingId` in `SessionStarted` regardless of `PublishedListings.Status`. A withdrawn listing's `BiddingOpened` is appended to its (previously empty) Listing stream the same as a Published one.

**Lived terminal path not observed in S5 tests.** S5's `SessionStartedFanOutTests` doesn't include a "Withdrawn listing in `ListingIds`" scenario — neither the prompt nor Workshop 002 §5 calls for one. The lived behaviour when the fan-out emits `BiddingOpened` for a withdrawn listing is therefore unobserved at S5 close. Three candidate downstream paths exist (see Decisions Inheriting Forward), but which one the system actually exhibits is unknown until M5 / M6 surfaces it.

**Data-availability skip is NOT defensive pre-filtering.** The fan-out skips listings with a NULL `PublishedListings` row — it cannot construct the per-listing payload without the projection. This is a data-availability constraint, not the Path α / Path β distinction.

### OQ4 — `CreateSession` command shape — **Hybrid (Settlement-style non-HTTP, Selling-style return)**

**Resolution.** Authored `CreateSessionHandler.Handle` returning `(CreationResponse<Guid>, IStartStream)` via `MartenOps.StartStream<Session>(sessionId, sessionCreated)`. No `[WolverinePost]` attribute — HTTP surface is M6 per the `[AllowAnonymous]`-through-M6 project stance. The return shape inherits M2 Selling `CreateDraftListingHandler` aggregate-creation semantics; the missing `[WolverinePost]` mirrors M5-S4 `StartSettlementSagaHandler`'s non-HTTP shape.

**Why not pure Settlement-style `(Saga?, OutgoingMessages)`.** Session isn't a saga — it's an event-sourced aggregate. The `IStartStream` return is required to open a new Marten event stream for the aggregate, which `OutgoingMessages` alone doesn't provide.

**Why not pure Selling-style with `[WolverinePost]`.** M5-through-M6 stance: no HTTP surface for Auctions commands until M6. The attribute will be added at M6 alongside the ops frontend.

### OQ5 — DurationMinutes carrier on SessionStarted — **Path B (aggregate-load on fan-out)**

**Resolution.** `SessionStartedHandler` loads the Session aggregate via `session.Events.AggregateStreamAsync<Session>(sessionId)` to read `DurationMinutes`. Adds one Marten read per fan-out invocation. `SessionStarted` contract carries only `(SessionId, ListingIds, StartedAt)` per the M4-S1 stub — byte-level diff zero on `src/CritterBids.Contracts/Auctions/SessionStarted.cs`.

**Defensive null guard.** The handler returns silently if `AggregateStreamAsync` returns null. This should never happen in practice (SessionStarted is always preceded by SessionCreated on the same stream), but the guard prevents NRE on an unexpected dispatch shape.

### OQ6 — SessionStarted handler count after S5 — **Single Wolverine handler (the fan-out)**

**Resolution.** Verified at session open and confirmed by the green tests: SessionStarted has exactly ONE Wolverine handler in the Auctions BC after S5 — `SessionStartedHandler.Handle`. The Session aggregate's `Apply(SessionStarted)` is a Marten event-projection apply, not a Wolverine handler — it runs inside `AggregateStreamAsync` during live aggregation, never on the Wolverine message-dispatch path.

**Dispatch test shape.** Both `SessionStartedFanOutTests` use `SendMessageAndWaitAsync` per the prompt's directive. `InvokeMessageAndWaitAsync` would also work given the single-handler topology, but `SendMessageAndWaitAsync` is the right semantic for an integration event with a RabbitMQ publish route and is what the prompt called for. The three command dispatch tests (commit `60bcc54`) use `InvokeMessageAndWaitAsync` — they're single-handler commands, not integration events.

### OQ7 — Multi-handler `ListingPublished` cross-cut — **No regression**

**Resolution.** Cleared at load time before any code change. `grep` across `tests/CritterBids.Auctions.Tests/` for `IMessageBus.InvokeAsync(new ListingPublished(...))` returned zero matches — `BiddingOpenedConsumerTests` calls the handler directly per its M3 docstring. After Commit 1's `PublishedListingsHandler.Handle(ListingPublished)` landed alongside the existing M3 `ListingPublishedHandler.Handle(ListingPublished)`, both BiddingOpenedConsumerTests stayed green (verified at `35ca492` and at the final 148-test gate). The Send-vs-Invoke trap from the wolverine-sagas skill's §"Multiple Handlers + Separated" does not apply to direct-handler-invocation tests.

### OQ8 — `[WriteAggregate]` codegen vs Session — **Codegen resolves cleanly; halt-and-consult unused**

**Resolution.** Both `AttachListingToSession` and `StartSession` use `[WriteAggregate(nameof(X.SessionId))]` from first commit. The dispatch tests in Commit 7 exercise the codegen path against the new sealed-record Session aggregate end-to-end. Both passed on first run.

**No additional `Schema.For<Session>().Identity(...)` registration needed.** `LiveStreamAggregation<Session>()` is sufficient for `[WriteAggregate]` resolution — same shape as the existing Listing live aggregation. Marten 8 handles the sealed-record + functional Apply pattern (static `Create` + instance `Apply` returning new instances via record `with`) cleanly with no additional ceremony.

**Halt-and-consult discipline preemptive.** The OQ8 risk was the first in-Auctions `[WriteAggregate]` shape (Listing uses DCB, not `[WriteAggregate]`). The M2 / M3 precedents from Selling (`WithdrawListingHandler`) and Participants compose with the new aggregate without surprise.

---

## Blockers encountered

### Blocker 1 — CreateSession dispatch test: tracked.Sent/NoRoutes empty for forwarded SessionCreated

**Symptom.** First run of `CreateSession_DispatchedViaBus_AppendsSessionCreatedToNewStream` failed with:

```
Shouldly.ShouldAssertException : tracked.Sent.MessagesOf<SessionCreated>().Concat(
        tracked.NoRoutes.MessagesOf<SessionCreated>())
should have single item but had 0 items
```

**Root cause.** `UseFastEventForwarding`'s forwarded events from a Marten `StartStream` (via `IStartStream` return) do NOT land in `tracked.Sent` or `tracked.NoRoutes` synchronously within `InvokeMessageAndWaitAsync`. The forwarding pipeline runs asynchronously after the immediate handler returns; TrackActivity's tracked-bucket capture has already finalized by then.

**Fix.** Switched from the TrackActivity / `InvokeMessageAndWaitAsync` shape to `IMessageBus.InvokeAsync<CreationResponse<Guid>>` to capture the typed response value directly. The `response.Value` is the new SessionId; the test then queries Marten for the Session aggregate via `AggregateStreamAsync<Session>(response.Value)` and asserts on the rebuilt state. Same end-to-end assurance, different assertion plumbing.

**The AttachListingToSession / StartSession dispatch tests stay on the standard TrackActivity shape** because their handlers return `Events` tuples (not typed responses), and asserting on the aggregate state via `AggregateStreamAsync` after the dispatch is the same shape RegisterProxyBidDispatchTests uses for saga documents.

**Pattern note for the retrospective.** When asserting on cascade-emitted events from aggregate-creation handlers that use `IStartStream`, use `IMessageBus.InvokeAsync<TResponse>` (or `bus.InvokeAsync<T>`) rather than TrackActivity. When asserting on cascade-emitted events from handlers that use `OutgoingMessages` directly (like `StartProxyBidManagerSagaHandler` emitting `ProxyBidRegistered`), TrackActivity captures them in `tracked.NoRoutes` per the M4-S3 / M4-S4 lived pattern.

**Pre-existing increment helper extraction threshold.** Bid-increment math copies stay at two — same as M4-S4 close. M4-S5's fan-out doesn't introduce a new increment user. Threshold of three still uncrossed; defer extraction to whichever slice introduces a genuine third copy.

---

## Decisions inheriting forward

### Skill append discipline — `marten-projections.md` not appended at S5 close

Nothing new surfaced beyond what the M5-S3 PendingSettlement section already covers. The PublishedListings projection mirrors PendingSettlement's tolerant-upsert + terminal-status preservation pattern verbatim; the field-shape decision (Path A full payload) is a per-application choice that's the right thing to record in the retrospective, not in the skill. The multi-handler-on-ListingPublished cross-cut was already covered by M4-S3's skill update on `wolverine-sagas.md` §"Multiple Handlers + Separated".

**OQ2 framing distinction NOT folded into a skill file** because it's a one-slice observation about a milestone-doc imprecision, not a generalizable pattern. The skill file's existing "tolerant upsert" and "stream-state pre-query" sections both supported the lived implementation correctly.

### Pre-query stream-state idempotency pattern is now the established M3 + M4-S5 idiom

Two lived applications: `ListingPublishedHandler` (M3-S3) and `SessionStartedHandler` (M4-S5). Both call `session.Events.FetchStreamStateAsync(streamId)` → early-return if non-null. The pattern is appropriate when:
- The handler's emission is a "first event on a stream" semantic (like `BiddingOpened`)
- Idempotency on redelivery is required
- Stream-existence check is cheaper than the alternative (no `try-catch` overhead, no DCB tag-aggregate load)

M4-S6's `SessionMembershipHandler` will likely consume `SessionStarted` and project session-membership fields onto `CatalogListingView` — a tolerant-upsert pattern, not a stream-opening pattern. The two idioms (stream-existence pre-query vs LoadAsync ?? new tolerant-upsert) coexist; the choice depends on whether the projection's primitive is "stream" or "document".

### The "fan-out via OutgoingMessages" framing in milestone doc §6 doesn't match the lived implementation

Milestone doc §6 says the fan-out "appends unconditionally through OutgoingMessages". The lived implementation appends to Marten streams directly via `session.Events.StartStream<Listing>`, not via OutgoingMessages. UseFastEventForwarding then forwards each appended event as a Wolverine message — both locally (to AuctionClosingSaga's start handler) and externally (to the listings-auctions-events RabbitMQ queue). The end result is the same as "OutgoingMessages with N BiddingOpened", but the mechanism is different.

This matters because:
- Pre-query idempotency works (the stream is the unit of idempotency)
- OutgoingMessages-based idempotency would require a different mechanism (sender-side dedup, which Wolverine doesn't provide for `OutgoingMessages` automatically)

Future fan-out handlers should pick the same shape: append to streams, let UseFastEventForwarding fan out.

### Functional Apply pattern for sealed-record aggregates is supported

Marten 8 + sealed record + static `Create(FirstEvent) => new()` + instance `Apply(Event) => this with { ... }` composes with `LiveStreamAggregation<T>()` and `[WriteAggregate]` cleanly. The M4-S5 implementation is the first in-repo use; future event-sourced aggregates can follow the same shape. Class-based aggregates (like the existing Listing) remain valid; the choice is per-aggregate.

The `IReadOnlyList<Guid>` field with C# 12 collection expression `[..oldList, newItem]` in the `with` block is the cleanest idiom for appending to immutable collections during functional Apply.

---

## What M4-S6 should know

### Auctions BC test count at S5 close

**65 Auctions tests** (up from 51 at S4 close). Composition: 11 AuctionClosingSagaTests + 9 ProxyBidManagerSagaTests + 2 ParticipantCreditCeilingProjectionTests + 1 RegisterProxyBidDispatchTests + 1 RealSellingProducerSagaTerminationTests + 7 SessionAggregateTests (new) + 2 SessionStartedFanOutTests (new) + 3 Session command dispatch tests (new) + 2 PublishedListingsProjectionTests (new) + 27 pre-existing M2/M3-shipped tests. Total solution count: **148**.

### Lived PublishedListings shape — input to ADR 014 evidence

The `PublishedListings` projection landed with the full `BiddingOpened`-precursor payload (OQ1 Path A): SellerId, StartingBid, ReservePrice, BuyItNowPrice, Duration, three extended-bidding fields, PublishedAt, WithdrawnAt, Status. Renamed `BuyItNow → BuyItNowPrice` to match scenario vocabulary and Settlement's `PendingSettlement` (M5-S3).

This is the third lived application of the M4-D4 duplicate-projection pattern. The three lived shapes:
- `Settlement.BidderCreditView` (M5-S5) — per-bidder credit cache
- `Auctions.ParticipantCreditCeiling` (M4-S4) — per-bidder credit cache
- `Auctions.PublishedListings` (M4-S5) — per-listing publish-state cache

**ADR 014 evidence note.** ADR 014 (Cross-BC Read-Model Extension Shape) is the M4-S6 deliverable. The M4-D4 duplicate-projection pattern is STRUCTURALLY DISTINCT from the M3-D2 read-model extension pattern (Path A — sibling handler classes on `CatalogListingView`):
- M4-D4 caches upstream seed data INSIDE the consuming BC so saga / aggregate hot-paths don't cross BC boundaries to read.
- M3-D2 extends a downstream BC's read model (the catalog) with additional fields driven by upstream events.

Both patterns inherit "publish full payload at first commit" from `integration-messaging.md` §L2, but the consumers differ. ADR 014 documents M3-D2 specifically; M4-D4 is a named modular-monolith pattern that doesn't require its own ADR per the M4 milestone doc §6.

### Sibling-handler ADR 014 sub-question — no new evidence from S5

The M4 milestone doc §8 calls out a sub-question in ADR 014: should `SessionMembershipHandler` (M4-S6) be one merged handler consuming from both `listings-auctions-events` AND `listings-selling-events` (Option B — plan's implicit choice), or split into per-source handlers (Option A — M3 precedent)?

S5's `PublishedListingsHandler` consumes only Selling-source events (`ListingPublished` + `ListingWithdrawn`) from a single queue. It's a duplicate-projection handler, not a read-model-extension sibling, so it doesn't bear on the Option A vs Option B question directly. But the fact that a single handler can consume from a single queue with two different event types (rather than requiring two separate handler classes) is a small data point in favor of Option B — fewer files, single transactional scope, no real isolation benefit from splitting.

ADR 014's resolution is M4-S6's call; S5's evidence is incidental.

### SessionStarted fan-out's lived idempotency mechanism

**Pre-query stream-state via `FetchStreamStateAsync`.** Not the `BidConsistencyState` DCB mechanism named in milestone doc §6 (see OQ2 resolution above for why the framing was imprecise). M4-S6's `SessionMembershipHandler` is a Listings-BC projection handler, not a stream-opening handler, so it inherits the tolerant-upsert pattern from M3-S6's `AuctionStatusHandler`, not the stream-existence pre-query pattern from M4-S5's `SessionStartedHandler`.

### Cascade-bucket assignments — no new flips surfaced in S5

The four Auction Closing saga cascade-bucket flips from M4-S4 (`NoRoutes → Sent` for `ListingSold` / `ListingPassed` after the `ProxyBidDispatchHandler` lands) are unchanged. S5 didn't introduce any new local-handler-claims that flip existing assertions. The S5 fan-out test's `SessionStarted` flows through the fan-out handler (one Wolverine handler in Auctions), and the cascaded `BiddingOpened` events flow through AuctionClosingSaga's start handler — but those are downstream effects, not in scope for any existing test's bucket assertions.

### `UseFastEventForwarding` from `IStartStream` doesn't land in tracked.* synchronously

Discovered as Blocker 1 above. The CreateSession dispatch test had to switch from TrackActivity's `tracked.Sent` / `tracked.NoRoutes` assertion shape to `IMessageBus.InvokeAsync<CreationResponse<Guid>>` to capture the response. M4-S6's `SessionMembershipHandler` won't hit this — it's a downstream consumer, not an aggregate-creation handler.

**Mental model for future dispatch tests.** TrackActivity buckets capture messages emitted synchronously by the handler under InvokeMessageAndWaitAsync. Forwarded events from `IStartStream` / `[WriteAggregate]` Marten appends run asynchronously after the handler returns; TrackActivity has already finalized its capture. Use `bus.InvokeAsync<TResponse>` for typed responses; use `AggregateStreamAsync` for aggregate state.

### Bid-increment helper status — unchanged at S5 close

Still two co-located inline copies (PlaceBidHandler + ProxyBidManagerSaga). Threshold of three still uncrossed. S5's fan-out didn't introduce a third copy.

### OQ3 Path α — withdrawn listing in `ListingIds` not exercised in S5 tests

The fan-out emits `BiddingOpened` for every `ListingId` in `SessionStarted`, including listings whose `PublishedListings.Status` is `Withdrawn`. S5's tests don't include a "withdrawn listing in `ListingIds`" scenario — neither the prompt nor Workshop 002 §5 called for one. The lived terminal path is unobserved.

**Candidate downstream paths (none verified):**
- The Listing's stream gets a `BiddingOpened` appended (no DCB rejection because the stream is empty); AuctionClosingSaga starts; the listing eventually closes via the normal saga flow. Bidders see a withdrawn listing as "Open" briefly — not great, but transient.
- The Auction Closing saga's earlier consumption of `ListingWithdrawn` already terminated any saga for the listing. The new `BiddingOpened` reaches the saga-start handler; if the start handler checks for existing-saga-by-id, it would conflict / no-op. If it doesn't, a new saga starts on top of the terminated one — bug.
- The Listings catalog projection's withdrawn-status field (M4-S6) overrides any "Open" flip from the BiddingOpened forwarded event — bidders never see the withdrawn listing as open. M4-S6's handler should be the source of truth on this composition.

**M4-S6 should test this composition explicitly.** Add a `SessionStarted_WithWithdrawnListing` scenario to either `SessionMembershipHandlerTests` (Listings) or a cross-BC integration test. The lived terminal path needs to be pinned before M6 ships, or the M4 milestone doc §3 stance ("Defensive pre-filtering at StartSession time is post-MVP hardening") becomes load-bearing for the actual UX.

### Skill files — no append at S5 close

Per item 20 — nothing new surfaced beyond what the M5-S3 PendingSettlement section and the M4-S3 `wolverine-sagas.md` §"Multiple Handlers + Separated" already cover. M4-S6 should evaluate whether its M3-D2 / ADR 014 work surfaces anything novel against `marten-projections.md` §7.

---

## Test count summary

| Project | M4-S4 close | M4-S5 delta | M4-S5 close |
|---------|-------------|-------------|-------------|
| `CritterBids.Api.Tests` | 1 | 0 | 1 |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 |
| `CritterBids.Listings.Tests` | 14 | 0 | 14 |
| `CritterBids.Selling.Tests` | 36 | 0 | 36 |
| `CritterBids.Settlement.Tests` | 25 | 0 | 25 |
| `CritterBids.Auctions.Tests` | 51 | **+14** | **65** |
| **Total** | **134** | **+14** | **148** |

`dotnet build`: 0 errors · 24 NU1904 NuGet warnings (unchanged from baseline).
