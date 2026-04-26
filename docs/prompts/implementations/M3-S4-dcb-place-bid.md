# M3-S4: DCB PlaceBid Handler

**Milestone:** M3 — Auctions BC
**Slice:** S4 of 8 (S4 and S4b are the pre-emptive split of the originally-planned joint S4 per milestone doc §9)
**Agent:** @PSA
**Estimated scope:** one PR; 16 new tests; ~7–10 new files
**Baseline:** 47 tests green · `dotnet build` 0 errors, 0 warnings · M3-S3 closed (`BiddingOpened` consumer live; `ScaffoldPlaceholder` still present on `Listing.cs`). `docs/skills/dynamic-consistency-boundary.md` was corrected pre-flight (2026-04-17) against the canonical Wolverine University example — three-method pattern, tagging API, and `Guid Id` claim all updated. Skill content is authoritative for this session.

---

## Goal

Land the first Dynamic Consistency Boundary and the first real `Listing` aggregate state in CritterBids. `BidConsistencyState` with `[BoundaryModel]` establishes the DCB shape for bid acceptance; the `Load(command) => EventTagQuery` sibling method drives cross-stream event selection. `PlaceBidHandler` enforces all 15 bid scenarios from `002-scenarios.md` §1. The `ScaffoldPlaceholder` shim on `Listing.cs` retires in the same commit as a real `Apply(BiddingOpened)` (or `Create`, per skill guidance). PlaceBid ships with one `IMessageBus` dispatch test per the M2.5 precedent. BuyNow is pre-emptively deferred to M3-S4b, which applies the same DCB pattern to the 4 BuyNow scenarios with minimal new ground to cover. After this slice, a timed listing takes real bids under real consistency rules; S4b adds the BIN short-circuit; S5 wires the saga to close them.

## Canonical reference

Jeremy Miller's University example at `C:\Code\JasperFx\wolverine\src\Persistence\MartenTests\Dcb\University\` is the authoritative DCB shape. Specifically:

- `BoundaryModelSubscribeStudentToCourse.cs` — the `#region sample_wolverine_dcb_boundary_model_handler` target; exact handler shape to follow.
- `ChangeCourseCapacity.cs` — three parallel handler shapes for the same command (manual, `[BoundaryModel]`, `[WriteAggregate]`). CritterBids uses the `[BoundaryModel]` variant.
- `boundary_model_workflow_tests.cs` — fixture setup and through-the-bus test shape using `theHost.InvokeMessageAndWaitAsync(command)`.
- State classes (`SubscriptionState`, `CourseState`, etc.) — per-event `Apply(T)` methods, private-setter properties, no `Guid Id` required.

The `[BoundaryModel]` pattern is canonical because Jeremy's `#region sample_wolverine_dcb_boundary_model_handler` marker names it as the Wolverine-docs demo. `[WriteAggregate]` also composes with DCB but is not the shape to use in CritterBids — the milestone doc §6's `[WriteAggregate]` convention scopes to non-DCB aggregate commands.

## Context to load

- `docs/milestones/M3-auctions-bc.md` — authoritative scope, §7 acceptance tests (§1 rows), §6 conventions
- `docs/workshops/002-scenarios.md` — §1 (15 PlaceBid scenarios)
- `docs/skills/dynamic-consistency-boundary.md` — DCB pattern (corrected pre-flight; read top to bottom)
- `docs/skills/wolverine-message-handlers.md` — aggregate handler workflow, dispatch tests
- `docs/skills/critter-stack-testing-patterns.md` — integration fixtures, dispatch-test shape
- `docs/skills/marten-event-sourcing.md` — `AddEventType`, `Create` vs `Apply` on first-event-on-stream
- `C:\Code\JasperFx\wolverine\src\Persistence\MartenTests\Dcb\University\` — canonical reference code; S1 W002-7 decision artifact for `BidRejected` stream placement; `docs/retrospectives/M3-S3-bidding-opened-consumer-retrospective.md`

## In scope (numbered)

1. `src/CritterBids.Auctions/Listing.cs` — real `Apply(BiddingOpened)` or `Create(BiddingOpened)` populating aggregate state from the full event payload (listing id, seller, starting bid, reserve threshold, buy-it-now price, scheduled close, extended-bidding config, max duration, current high bid/bidder null, bid count 0, buy-it-now available true, reserve met false). Skill guidance (`marten-event-sourcing.md`) governs the `Apply` vs `Create` call — see Open Question 1. Only `Apply(BiddingOpened)` in this slice; other `Apply` methods come in later slices as events land in the stream.
2. `src/CritterBids.Auctions/Listing.cs` — remove the `ScaffoldPlaceholder` record and its paired `Apply(ScaffoldPlaceholder)` no-op in the **same commit** as item 1. Never land item 1 without item 2.
3. `src/CritterBids.Auctions/AuctionsModule.cs` — `opts.Events.AddEventType<T>()` for every event the `Listing` aggregate applies or S4 handlers produce: `BidPlaced`, `BidRejected`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowOptionRemoved`. Existing `AddEventType<BiddingOpened>()` unchanged. Final count after S4: **six** `AddEventType<T>()` calls. `BuyItNowPurchased` is deferred to S4b. Also register tag types: `opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>();` and any additional tag types the boundary model loads from.
4. `src/CritterBids.Auctions/BidRejected.cs` — internal audit event type, **not** in `CritterBids.Contracts.Auctions.*`. Stream placement per the S1 W002-7 decision artifact (dedicated per-listing stream, not the primary bidding stream, not a global audit stream — per `dynamic-consistency-boundary.md` "BidRejected Stream Placement" section).
5. `src/CritterBids.Auctions/BidConsistencyState.cs` — DCB boundary model class. Per-event `Apply(T e)` methods. Private-setter properties. `Apply(BiddingOpened)` populates **all** fields from the event payload including `BuyItNowPrice` and `BuyItNowAvailable` — S4b's `BuyNowHandler` will query those without needing to modify this state class. Do not add `public Guid Id { get; set; }` speculatively — the canonical Wolverine state classes omit it. Only add it if the test fixture teardown actually fails with `InvalidDocumentException`, per the skill's "known workaround" guidance.
6. `src/CritterBids.Auctions/PlaceBidHandler.cs` — static class with two required static methods plus one optional pipeline hook: `public static EventTagQuery Load(PlaceBid command)`; optional `public static HandlerContinuation Validate(PlaceBid command, BidConsistencyState state, ILogger logger)`; `public static TEvent? Handle(PlaceBid command, [BoundaryModel] BidConsistencyState state)` returning the produced event (or nullable/null for no-op cases). Return type may be a single event, `IEnumerable<object>`, or nullable for rejection paths producing `BidRejected`. No `[WriteAggregate]`. No `IEventBoundary<TState>` parameter — that belongs to the manual pattern the skill file contrasts with the canonical one. **This handler's shape is the precedent S4b's `BuyNowHandler` mirrors.**
7. `tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — 15 integration tests, one per §1 scenario, method names exactly per milestone doc §7. Tests may invoke through the bus (`host.InvokeMessageAndWaitAsync(command)`, matching `boundary_model_workflow_tests.cs`) or directly against the handler's static methods with a hand-built state — the skill file's unit-testing pattern supports both. Happy-path and reserve-crossing scenarios probably want through-the-bus; pure validation-logic scenarios work fine with direct state construction.
8. `tests/CritterBids.Auctions.Tests/PlaceBidDispatchTests.cs` — 1 integration test dispatching `PlaceBid` via `IMessageBus.InvokeAsync` specifically (not direct Handle-call). Tests the full Wolverine pipeline for the dispatch path.
9. `docs/skills/dynamic-consistency-boundary.md` — append a "CritterBids M3-S4 learnings" section at the end. Scope:
   - The `EventTagQuery` shape actually used (fluent vs imperative) and why that form was cleaner.
   - Whether DCB-appended events (via `boundary.AppendOne` through the `[BoundaryModel]` path) flow into the `Listing` aggregate's `LiveStreamAggregation<Listing>()` projection — empirical answer to Open Question 3, with the test evidence cited.
   - Whether `BidConsistencyState` needed `public Guid Id { get; set; }` empirically — kept or omitted, with the test-teardown behavior observed.
   - Any Marten 8 wrinkle: boot-time validation errors, codegen diagnostics, tag-registration ordering, etc.
   - Any interaction with `UseMandatoryStreamTypeDeclaration` that surfaced — DCB writes bypass named-stream `Append`, so the declaration rule may not apply cleanly in boundary-append paths.
   
   Append-only — the skill's existing sections are already corrected and should not be rewritten in this session.
10. `docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md` — written last. Gate below.

## Explicitly out of scope

- **BuyNow path — deferred to M3-S4b.** Do not scaffold `BuyNowHandler`, do not register `BuyItNowPurchased` with `AddEventType<T>`, do not extend `BidConsistencyState` for BuyNow-only concerns. The `BuyItNowPrice` and `BuyItNowAvailable` fields are populated in `BidConsistencyState.Apply(BiddingOpened)` because PlaceBid scenario 1.1 (first bid removes BIN) reads `BuyItNowAvailable`; that is not a BuyNow hook, that is PlaceBid's own requirement.
- Auction Closing saga — scheduled `CloseAuction`, state machine, cancel-and-reschedule — S5
- `BiddingClosed`, `ListingSold`, `ListingPassed` event registrations and handlers — S5
- `CatalogListingView` auction-status fields and handlers — S6
- `listings-auctions-events` RabbitMQ queue — S5 or S6
- Proxy Bid Manager, `RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted` — M4
- Session aggregate / flash auction format — M4
- Selling-side `WithdrawListing` command — unscheduled
- HTTP endpoints for `PlaceBid` — handlers stay message-driven in M3
- Real authentication — `[AllowAnonymous]` stands through M5
- Any change to the `BiddingOpened` contract payload — locked in S1
- Any change to `ListingPublishedHandler` from S3 — frozen
- Any `Program.cs` change — S3's three queues stand; no new publish or listen rules in S4
- Any rewrite of `dynamic-consistency-boundary.md` existing sections. The pre-flight corrections on 2026-04-17 are authoritative. Item 9 is append-only.

## Conventions to pin or follow

- **DCB handler shape is `[BoundaryModel]`, not `[WriteAggregate]`.** Canonical per `BoundaryModelSubscribeStudentToCourse.cs` in the Wolverine repo and per the corrected skill. Three static methods: `Load(command) => EventTagQuery`, optional `Validate(command, state, logger) => HandlerContinuation`, `Handle(command, [BoundaryModel] TState state)` returning event(s) or null. No `IEventBoundary<TState>` parameter on `Handle`.
- **Milestone doc §6's `[WriteAggregate(nameof(...))]` rule applies to non-DCB aggregate commands.** `PlaceBid` is a DCB handler against `BidConsistencyState`, so the rule does not apply. When CritterBids later adds non-DCB commands targeting `Listing` via `FetchForWriting<Listing>(streamId)`, the `[WriteAggregate]` convention applies to those.
- **`BidConsistencyState` uses per-event `Apply(T)` methods.** Private-setter business properties. Do not add `public Guid Id` speculatively.
- **Tag-type registration goes in `AuctionsModule.ConfigureMarten`**: `RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>()` plus any other tag types `Load(command)` references. `ListingStreamId` is a strong-typed record (avoid raw `Guid` — .NET 10 `Variant`/`Version` trip `ValueTypeInfo`).
- **Tagging on test seeding.** Use `session.Events.BuildEvent(evt)` + `wrapped.WithTag(tagValue)` + `session.Events.Append(streamKey, wrapped)`. `AddTag` and `WithTag` are equivalent; `WithTag` chains. Variadic `WithTag(params object[])` for multi-tag events.
- **Concurrency policies.** Both `opts.OnException<ConcurrencyException>().RetryWithCooldown(...)` and `opts.OnException<DcbConcurrencyException>().RetryWithCooldown(...)` — siblings, not parent-child.
- **`IsProxy: false` hardcoded** on every `BidPlaced`. M4 flips this via a proxy handler with zero contract change.
- **`BidRejected` stays in `CritterBids.Auctions`**, not `CritterBids.Contracts.Auctions`.
- **`AuctionsTestFixture` stays Listings-minimal from S3** unless the dispatch test (item 8) requires tracking helpers. If so, extend additively.
- **`UseMandatoryStreamTypeDeclaration` applies everywhere** — every `StartStream` / `Append` / `FetchStreamAsync` names `<Listing>` explicitly. Exception: `boundary.AppendOne(evt)` from the DCB pipeline.
- **`CritterBids.Auctions.csproj` `ProjectReference` count stays at 1** (Contracts).
- **Zero `IMessageBus` reference in production Auctions code.** Dispatch is test-only.

## Commit sequence (proposed)

1. `feat(auctions): land real Apply(BiddingOpened) on Listing; remove ScaffoldPlaceholder` — items 1, 2
2. `feat(auctions): register bid-batch event types and DCB tag types; add BidRejected internal event` — items 3, 4
3. `feat(auctions): add BidConsistencyState DCB boundary model` — item 5
4. `feat(auctions): implement PlaceBidHandler covering §1 scenarios` — items 6, 7, 8
5. `docs(skills): append M3-S4 learnings to dynamic-consistency-boundary.md` — item 9
6. `docs: write M3-S4 retrospective` — item 10

Commit 4 may split across scenario subgroups (happy-path / rejection / reserve / extension) if the 15-scenario set warrants — but all 15 scenarios ship in this PR, not a follow-up.

## Acceptance criteria

- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test CritterBids.slnx` — 47-test baseline preserved; +16 new tests green; zero skipped, zero failing; total 63
- [ ] `src/CritterBids.Auctions/Listing.cs` — `ScaffoldPlaceholder` record and `Apply(ScaffoldPlaceholder)` method absent
- [ ] `src/CritterBids.Auctions/Listing.cs` — real `Apply(BiddingOpened)` or `Create(BiddingOpened)` method present and fully populated per item 1
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — `AddEventType<T>()` calls for exactly `BiddingOpened`, `BidPlaced`, `BidRejected`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowOptionRemoved` — **six total**, no more, no fewer. `BuyItNowPurchased` is absent.
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — `RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>()` and any additional tag types the `Load` queries reference
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — retry policies for both `ConcurrencyException` and `DcbConcurrencyException`
- [ ] `src/CritterBids.Auctions/BidConsistencyState.cs` — class with per-event `Apply(T)` methods; `Apply(BiddingOpened)` populates all BiddingOpened fields including `BuyItNowPrice` and `BuyItNowAvailable`
- [ ] `PlaceBidHandler` — static class with `Load(command) => EventTagQuery` and `Handle(command, [BoundaryModel] BidConsistencyState state)` returning event(s). Optional `Validate` permitted. Zero `[WriteAggregate]` in the Auctions project. Zero `IEventBoundary<T>` parameters on Handle.
- [ ] All 15 test methods in `PlaceBidHandlerTests.cs` are named exactly per milestone doc §7 and green
- [ ] `PlaceBidDispatchTests.cs` — 1 test invoking `PlaceBid` through `IMessageBus`, green
- [ ] `src/CritterBids.Auctions/` contains zero `IMessageBus` references; `BuyNowHandler.cs` does not exist; `BuyNow` command type is not referenced anywhere in Auctions
- [ ] `src/CritterBids.Api/Program.cs` unchanged from S3 close (byte-level diff limited to whitespace at most)
- [ ] `CritterBids.Auctions.csproj` `ProjectReference` count is 1 (Contracts only)
- [ ] `docs/skills/dynamic-consistency-boundary.md` — "CritterBids M3-S4 learnings" section appended; no edits to existing sections
- [ ] `docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is **not optional** and is **not a footnote**. It is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test CritterBids.slnx` shows all tests green and `dotnet build` shows 0 errors + 0 warnings. If any test fails or any warning lands, fix the code first, then write the retro.

Retrospective content requirements:
- Baseline numbers (47 tests before, 63 after)
- Per-item status table mirroring the "In scope (numbered)" list
- Each of the five Open Questions below answered with which path was taken and why
- First-in-repo DCB learnings: what the canonical Wolverine example made clear, what surprised you, what S4b and S5 will want documented
- `Apply` vs `Create` call for `BiddingOpened` on `Listing`, with rationale (Open Question 1)
- Whether `BidConsistencyState` ended up needing `public Guid Id { get; set; }` — empirical answer (Open Question 2)
- Skill-file append summary — section headings added, key findings recorded
- Any blocker encountered: verbatim error message, root cause, fix path
- "What M3-S4b should know" section — `BuyNowHandler` mirrors `PlaceBidHandler`'s shape; `BidConsistencyState` already has `BuyItNowPrice` and `BuyItNowAvailable` populated from `BiddingOpened`; any fixture tweaks S4 made that S4b inherits
- "What M3-S5 should know" section — the saga consumes `BidPlaced` / `ReserveMet` / `ExtendedBiddingTriggered` produced here, so saga-side assumptions about payload shape, event ordering, and whether DCB-appended events are visible to the `Listing` aggregate's live stream projection need to be stated

## Open questions (pre-mortems — flag, do not guess)

1. **`Apply(BiddingOpened)` vs `Create(BiddingOpened)`.** Marten 8 supports both for first-event-on-stream aggregation. `marten-event-sourcing.md` is authoritative. Pick one; do not mix both on the same aggregate. Record the choice and rationale in the retro.

2. **`BidConsistencyState` and `Guid Id`.** The canonical Wolverine state classes omit this property; do not add it speculatively. If test-harness teardown throws `InvalidDocumentException`, the known workaround is adding `public Guid Id { get; set; }` — apply, then document in the retro and the skill append.

3. **DCB writes vs Listing live-stream projection.** `Listing` is registered with `LiveStreamAggregation<Listing>()` — it rebuilds from events in the listing's stream on each `AggregateStreamAsync<Listing>` call. DCB handlers append events via `boundary.AppendOne(evt)`, which routes through the tag-query-loaded boundary, not via a named stream `Append`. Empirically verify: after a `PlaceBid` dispatches and `BidPlaced` is appended, does `session.Events.AggregateStreamAsync<Listing>(listingId)` reflect the bid in aggregate state? If yes, live aggregation "just works" over DCB-appended events — record the observation in the skill append (item 9). If no, document the gap and the workaround — this will materially affect S5's saga.

4. **Extended-bidding math edges (scenarios 1.11–1.15).** Trigger window, extension duration, and `MaxDuration` cap interact. If the §1 scenarios are ambiguous on boundary cases ("bid arrives exactly at the trigger window boundary," "extension would exceed `MaxDuration` by a sub-second delta," "extension math when the scheduled close is already in the past by clock skew"), resolve by reading `002-auctions-bc-deep-dive.md`. If the workshop is silent, flag — do not invent a rule.

5. **`AuctionsTestFixture` changes.** S3 kept the fixture Listings-minimal and direct-invocation-only. The dispatch test (item 8) exercises `IMessageBus.InvokeAsync`, which under the sticky-handler pattern failed in S3 with `NoHandlerForEndpointException`. `PlaceBid` is dispatched through the bus but **not** routed to a RabbitMQ queue (internal to Auctions, not cross-BC) — so the sticky-handler failure mode should not apply. Verify empirically on first dispatch test. If fixture changes are needed, extend additively — never replace the S3 direct-invocation path.
