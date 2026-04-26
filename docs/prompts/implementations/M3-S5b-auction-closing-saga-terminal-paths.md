# M3-S5b: Auction Closing Saga — Close Evaluation + Terminal Paths

**Milestone:** M3 — Auctions BC
**Slice:** S5b of 9 (follows S5; completes the auction closing saga scope per milestone doc §7 §3 scenarios 3.5–3.11)
**Agent:** @PSA
**Estimated scope:** one PR; 7 new scenario tests; ~4–6 new/modified files
**Baseline:** 72 tests green · `dotnet build` 0 errors, 0 warnings · M3-S5 closed. At S5 close: `AuctionClosingSaga` live with skeleton + forward-path handlers (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, Start-on-`BiddingOpened`); `Handle(CloseAuction)` is a stub returning `new OutgoingMessages()` with a TODO referencing this slice; `UseFastEventForwarding = true` on `IntegrateWithWolverine` in both `Program.cs` and `AuctionsTestFixture`; `UseDurableLocalQueues()` wired globally; `[SagaIdentityFrom(nameof(X.ListingId))]` established as the correlation convention; `BidCount` monotonicity established as the idempotency convention; `PlaceBidDispatchTests.SeedOpenListing` seeds a saga doc to honor the "open listing → live saga" runtime invariant.

---

## Goal

Complete the Auction Closing saga. Replace the `Handle(CloseAuction)` stub with real close-evaluation logic (sold-vs-passed decision, outcome-event emission, terminal transition), add terminal handlers for `BuyItNowPurchased` and `ListingWithdrawn`, and land the seven remaining saga scenarios (3.5–3.11) from `002-scenarios.md` §3. Apply the two conventions S5 established — `[SagaIdentityFrom(nameof(X.ListingId))]` for correlation, `if (Status == AuctionClosingStatus.Resolved) return;` for terminal idempotency — to every new handler without reopening the design. S5b also picks up one carry-forward test-seed fix to `BuyNowDispatchTests.SeedOpenListing` mirroring the regression precedent `PlaceBidDispatchTests` established in S5 — the moment `Handle(BuyItNowPurchased)` lands on the saga, the existing seed pattern will detonate without it.

After this slice, the saga is feature-complete for M3: a listing opens for bids, takes competing manual bids, fires reserve and extended-bidding signals, closes on its scheduled timer (rescheduled or original), and produces `BiddingClosed` + one of `ListingSold` / `ListingPassed` / `BuyItNowPurchased` / no-outcome-on-withdrawal. S6 then extends `CatalogListingView` to consume those outcomes.

## Context to load

- `docs/milestones/M3-auctions-bc.md` — §7 saga test rows 3.5–3.11, §2 integration contracts (`BiddingClosed`, `ListingSold`, `ListingPassed`, `ListingPassed.Reason` enum), §3 non-goals (Selling-side withdrawal command remains deferred; S5b uses synthetic `ListingWithdrawn` in fixture)
- `docs/workshops/002-scenarios.md` — §3 scenarios 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11 only
- `docs/skills/wolverine-sagas.md` — primary local skill; `MarkCompleted()` semantics, outcome-event emission via `OutgoingMessages`, terminal-path patterns, saga-doc lifecycle under `.UseNumericRevisions(true)`
- `docs/retrospectives/M3-S5-auction-closing-saga-skeleton-retrospective.md` — **especially "What M3-S5b should know"** (10 enumerated carry-forwards) and the `CancelPendingCloseAsync` reference shape in the saga source citation; OQ4 grounding (`C:\Code\JasperFx\wolverine\src\Wolverine.Marten\MartenIntegration.cs`) and OQ1 grounding (`C:\Code\JasperFx\wolverine\src\Wolverine\Persistence\Sagas\SagaIdentityFromAttribute.cs`) as the precedent for how this session cites its findings
- `src/CritterBids.Auctions/` S5 production baseline — `AuctionClosingSaga.cs`, `StartAuctionClosingSagaHandler.cs`, `AuctionsModule.cs` (7 existing `AddEventType<T>()` registrations, saga-document schema with numeric revisions, both concurrency retry policies), `AssemblyAttributes.cs`, and `src/CritterBids.Api/Program.cs` (forwarding + durable-local-queues wiring). Plus `src/CritterBids.Contracts/Auctions/` — the S1-authored stubs for `BiddingClosed`, `ListingSold`, `ListingPassed`; confirm payload shapes, particularly `ListingPassed.Reason` values `NoBids` and `ReserveNotMet`, before emission.
- `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` plus `AuctionClosingSagaTests.cs` and `PlaceBidDispatchTests.cs` — the fixture helpers S5 added (`LoadSaga<T>`, `QueryPendingCloseAuctionsAsync`, `CancelAllScheduledCloseAuctionsAsync`), the 4 existing saga scenario tests to preserve byte-identical, and the seeded-saga precedent in `SeedOpenListing` that S5b applies to `BuyNowDispatchTests` per item 8
- **Reference documentation — three tiers, in this order.** First: **the JasperFx AI Skills repo** at `C:\Code\JasperFx\ai-skills\` (specifically `wolverine/`, `marten/`, `polecat/`, `integrations/` subdirectories — these are the upstream, version-aligned skill files from which CritterBids' local `docs/skills/` were extracted; they are often richer than the local copies and are the authoritative source when a skill-level question arises). Second: **pristine source repos** at `C:\Code\JasperFx\wolverine\`, `C:\Code\JasperFx\marten\`, `C:\Code\JasperFx\polecat\`, `C:\Code\JasperFx\alba\`, and `C:\Code\JasperFx\CritterStackSamples\` — consult for API-surface questions (saga `MarkCompleted` behaviour, `OutgoingMessages` cascading semantics, Marten saga document lifecycle under numeric revisions, `UseFastEventForwarding` internals, `ScheduledMessageQuery` shape, etc.). Third: **Context7** with library IDs `/jasperfx/wolverine` and `/jasperfx/marten` for supplementary published docs. Do not answer API questions from training-data memory alone. Every first-use claim about Wolverine/Marten/Polecat/Alba behaviour in S5b cites its source — this is how the S5 retro grounded all four of its Open Questions.

## In scope (numbered)

1. `src/CritterBids.Auctions/AuctionClosingSaga.cs` — replace the stubbed `Handle(CloseAuction)` with the real close-evaluation handler. Behaviour:
   - Idempotency guard: `if (Status == AuctionClosingStatus.Resolved) return new OutgoingMessages();` (scenario 3.9 covers this arrival path)
   - Decide outcome from saga state:
     - `ListingSold` when `ReserveHasBeenMet && BidCount > 0` (scenario 3.5)
     - `ListingPassed` with `Reason = ReserveNotMet` when `BidCount > 0 && !ReserveHasBeenMet` (scenario 3.6)
     - `ListingPassed` with `Reason = NoBids` when `BidCount == 0` (scenario 3.7)
   - Emit `BiddingClosed` followed by the outcome event (`ListingSold` or `ListingPassed`) — atomicity and emission convention per OQ3 below
   - Transition `Status = Resolved` and `MarkCompleted()` per OQ4 below
2. `src/CritterBids.Auctions/AuctionClosingSaga.cs` — new `Handle(BuyItNowPurchased)` terminal handler:
   - `[SagaIdentityFrom(nameof(BuyItNowPurchased.ListingId))]` on the message parameter (inherited convention)
   - Idempotency guard: `if (Status == AuctionClosingStatus.Resolved) return;` (scenario 3.8/3.9 primary + BIN replay safety)
   - Cancels the pending `CloseAuction` via the established `CancelPendingCloseAsync` helper — or relies on `Handle(CloseAuction)`'s Resolved-guard — per OQ2 below
   - Emits `BiddingClosed` per OQ1 below (whether terminal paths emit `BiddingClosed`)
   - No `ListingSold` / `ListingPassed` emission — `BuyItNowPurchased` is itself the terminal outcome contract
   - Transition `Status = Resolved` and `MarkCompleted()`
3. `src/CritterBids.Auctions/AuctionClosingSaga.cs` — new `Handle(ListingWithdrawn)` terminal handler:
   - `[SagaIdentityFrom(nameof(ListingWithdrawn.ListingId))]` on the message parameter
   - Idempotency guard: `if (Status == AuctionClosingStatus.Resolved) return;`
   - Cancels the pending `CloseAuction` per OQ2 resolution
   - Emits NO outcome events per milestone doc §3 "terminates without evaluation" — though whether `BiddingClosed` is emitted hinges on OQ1 resolution
   - Transition `Status = Resolved` and `MarkCompleted()`
4. `src/CritterBids.Auctions/AuctionsModule.cs` — additive registrations per OQ1 / OQ5 resolution. At minimum:
   - `AddEventType<ListingWithdrawn>()` (bringing the count from 7 to 8) — required regardless of where the type is defined, because scenario 3.10's fixture seed appends it to a Marten stream, and Marten requires the type registered for stream replay / projection consistency
   - `AddEventType<BiddingClosed>()`, `AddEventType<ListingSold>()`, `AddEventType<ListingPassed>()` ONLY IF OQ5 resolves that outcome events are appended to the listing's primary stream (append-and-publish pattern). If OQ5 resolves they are bus-only, these registrations are not added.
   - No change to the saga-document schema registration from S5
5. `CritterBids.Contracts.Selling/ListingWithdrawn.cs` OR equivalent location per OQ6 — the `ListingWithdrawn` event type. If it already exists from an earlier Selling-BC authoring (verify), this item is verification-only. If it does not exist, author a minimum-viable record carrying `ListingId` and whatever payload the milestone or workshop specifies. **M3 does NOT wire a Selling-side publisher for it** — that stays deferred per milestone doc §3. The saga consumes it; the fixture synthesizes it.
6. `tests/CritterBids.Auctions.Tests/AuctionClosingSagaTests.cs` — extend with 7 new scenario tests, method names **exactly** per milestone doc §7 §3 rows:
   - `Close_ReserveMet_ProducesListingSold`
   - `Close_ReserveNotMet_ProducesListingPassed`
   - `Close_NoBids_ProducesListingPassed`
   - `BuyItNowPurchased_CompletesSaga`
   - `CloseAuction_AfterBuyItNow_NoOp`
   - `ListingWithdrawn_TerminatesWithoutEvaluation`
   - `Close_AfterExtension_UsesRescheduledTime`
   Each test seeds a live saga state appropriate to its scenario (leveraging `LoadSaga<T>` helper from S5 and a new seed helper per item 7). Outcome-event assertions verify emission through whichever mechanism OQ3 lands on (bus inbox, message store, or projected stream events). Scenarios 3.8 and 3.10 additionally assert the pending `CloseAuction` is cancelled (via `QueryPendingCloseAuctionsAsync` returning zero for the listing) per OQ2 resolution.
7. `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — additive only:
   - Helper to seed the saga in a specified pre-terminal state (parameters for `BidCount`, `CurrentHighBid`, `CurrentHighBidderId`, `ReserveHasBeenMet`, `ScheduledCloseAt`, `Status`) — supports the 7 new scenarios without each re-running the full `BiddingOpened → BidPlaced → ReserveMet → ExtendedBiddingTriggered` replay
   - Helper to append a synthetic `ListingWithdrawn` to the listing's primary stream, tagged with `ListingStreamId` so `UseFastEventForwarding` picks it up (per S5 retro §5 "synthetic `ListingWithdrawn`")
   - Helper to assert saga terminal state — checks the saga document's `Status == Resolved` (and, depending on OQ4, whether the document persists or is deleted)
   - Helper to capture outcome events emitted to the Wolverine bus — approach depends on OQ3 resolution (`TrackedSession`, `Host.ExecuteAndWaitAsync`, in-memory test inbox, or equivalent)
   No changes to existing fixture behaviour; S4/S4b/S5-era test behaviours remain byte-identical.
8. `tests/CritterBids.Auctions.Tests/BuyNowDispatchTests.cs` — carry-forward test-seed fix. `SeedOpenListing` (or equivalent seed method) gains `session.Store(new AuctionClosingSaga { Id = listingId, ... })` to honor the "open listing → live saga" invariant, matching the S5 precedent applied to `PlaceBidDispatchTests`. The moment `Handle(BuyItNowPurchased)` lands on the saga, this test will fail with `UnknownSagaException` without the seed. This item is necessary, not optional. Production `BuyNowHandler.cs` and the test's invocation shape stay byte-identical.
9. *(Optional)* `docs/skills/wolverine-sagas.md` — append a "CritterBids M3-S5b learnings" subsection if and only if first-use of `MarkCompleted()` + outcome-event emission from a saga handler surfaces something the skill did not predict (e.g., saga-doc lifecycle on completion, multi-event `OutgoingMessages` ordering guarantees, test-harness visibility of cascaded messages). The S5 retro noted four API-level gaps deferred to a future broader skill pass; S5b is not that pass. Add only if S5b's specific scope produces a net-new, self-contained learning. If the AI Skills repo at `C:\Code\JasperFx\ai-skills\wolverine\` already documents what S5b discovers, the action is to port that content into the local skill file (with attribution) rather than re-derive it.
10. `docs/retrospectives/M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` — written last. Gate below.

## Explicitly out of scope

- Any modification to `BidConsistencyState`, `Listing`, `PlaceBidHandler`, `BuyNowHandler`, `BidRejectionAudit` production code — frozen from S4/S4b close (byte-level diff limited to whitespace at most)
- Any modification to S5's forward-path saga handlers (`Handle(BidPlaced)`, `Handle(ReserveMet)`, `Handle(ExtendedBiddingTriggered)`) or `StartAuctionClosingSagaHandler` — frozen from S5 close; S5b adds new handlers on `AuctionClosingSaga` but does not touch the three existing real ones or the start handler
- Any modification to S4/S4b test files other than `BuyNowDispatchTests.cs` seed (item 8) — the saga-seed fix is the minimum mechanical change and must not reshape the test's intent
- Any modification to S5-era saga scenario tests 3.1–3.4 — frozen from S5 close
- `listings-auctions-events` RabbitMQ queue wiring — S6
- `CatalogListingView` auction-status field extension — S6
- Consumer handler in Listings BC for `BiddingClosed` / `ListingSold` / `ListingPassed` — S6
- Selling-side `WithdrawListing` command or `ListingWithdrawn` event publication pipeline — per milestone doc §3, unscheduled (remains a future Selling BC session)
- Any new `CritterBids.Contracts.Auctions.*` contract — S1 already authored the nine contracts; S5b only populates the emission side from the saga. If an S1 stub proves incomplete at payload level, **flag rather than modify** — contract changes mid-saga-work are expensive and need their own discussion.
- Proxy Bid Manager saga — M4
- Session aggregate / flash format — M4
- Concurrency soak / load testing on the saga — M3-D1 deferral still holds
- Any change to `Program.cs` beyond what OQ1/OQ5 might require — S5's forwarding + durable-local-queues wiring is sufficient for S5b; any new diff is called out in the retro with rationale
- Rewriting existing sections of `wolverine-sagas.md`. Item 9 is append-only.

## Conventions to pin or follow

Inherit all conventions from M3-S5 and prior. No new behavioural conventions introduced; three S5-established conventions are applied to the new handlers:

- **Correlation:** every saga handler parameter for an integration event carries `[SagaIdentityFrom(nameof(X.ListingId))]`. `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` follow the precedent without ceremony.
- **Terminal idempotency:** `if (Status == AuctionClosingStatus.Resolved) return;` (or `return new OutgoingMessages();` where an `OutgoingMessages` return is required) as the first line of every terminal handler and the real `Handle(CloseAuction)`. No hash sets; no per-message dedup storage.
- **Cancel semantics:** the `CancelPendingCloseAsync` helper on the saga (introduced in S5 for `Handle(ExtendedBiddingTriggered)`) is the canonical shape. Any new caller uses the same `±100ms execution-time window + MessageType filter` approach. Do not reinvent.

S5b pins one new behavioural convention worth flagging in the retro:

- **Outcome-event emission from a saga happens via `OutgoingMessages` cascading from the handler return value**, not via `IMessageBus.PublishAsync` called inside the handler body. This matches the `bus.ScheduleAsync` usage in `Handle(ExtendedBiddingTriggered)` being restricted to scheduling (not fire-and-forget publishing). The saga stays "zero `IMessageBus.PublishAsync` inside the handler body" per the invariant implied by S5's retro §build-state "zero in DCB handlers". OQ3 below settles the specific mechanics.
- **`MarkCompleted()` is the terminal signal.** Whether the saga document is physically deleted or retained with `Status = Resolved` is a Wolverine+Marten implementation detail that OQ4 names. Whatever the answer, the assertion shape used by the 7 new tests is uniform and documented once.

S5b pins one working-practice convention — a rule about **how the session works**, not about what the code looks like:

- **Reference-doc discipline.** Every first-use claim about Wolverine/Marten/Alba/Polecat behaviour in code or in the retrospective cites its source. Acceptable citations: a file path in `C:\Code\JasperFx\ai-skills\` (AI Skills repo), a file path in a pristine local repo (`C:\Code\JasperFx\wolverine\...`, `C:\Code\JasperFx\marten\...`, etc.), a `CritterStackSamples` example, or a Context7 library reference (`/jasperfx/wolverine`, `/jasperfx/marten`). Unacceptable: "I believe the API is...", "the skill file says..." without a specific filename and section, or any claim sourced from training-data memory alone. Precedent: S5's retro cited `C:\Code\JasperFx\wolverine\src\Wolverine.Marten\MartenIntegration.cs` for OQ4 (`UseFastEventForwarding` mechanics) and `C:\Code\JasperFx\wolverine\src\Wolverine\Persistence\Sagas\SagaIdentityFromAttribute.cs` for OQ1 (the correlation API). S5b maintains that bar for OQ1 (terminal-path `BiddingClosed` emission), OQ3 (`OutgoingMessages` cascading mechanics), OQ4 (`MarkCompleted()` and Marten saga-doc lifecycle under numeric revisions), and OQ6 (`ListingWithdrawn` type location).

The `IsProxy: false` hardcoding on `BidPlaced` remains (no M3 change); terminal-path handlers do not observe `BidPlaced` anyway.

## Commit sequence (proposed)

1. `feat(auctions): add ListingWithdrawn contract stub and register event type` — items 4 (partial — `ListingWithdrawn` registration only) + 5. Brings `AddEventType<T>()` count to 8.
2. `feat(auctions): implement Handle(CloseAuction) close evaluation with outcome events` — item 1 + scenarios 3.5, 3.6, 3.7, 3.11 tests + seeded-saga fixture helper from item 7. If OQ5 resolves "append outcome events to stream", also register `BiddingClosed` / `ListingSold` / `ListingPassed` event types here (bringing count to 11).
3. `feat(auctions): terminal saga handler for BuyItNowPurchased` — item 2 + scenarios 3.8 and 3.9 tests
4. `fix(auctions): seed saga doc in BuyNowDispatchTests to honor live-saga invariant` — item 8. Standalone commit so the parallel to `PlaceBidDispatchTests` (landed in S5) is legible in history.
5. `feat(auctions): terminal saga handler for ListingWithdrawn with synthetic-event fixture seed` — item 3 + scenario 3.10 test + synthetic-event fixture helper from item 7
6. *(optional)* `docs(skills): append M3-S5b learnings to wolverine-sagas.md` — item 9, only if something new surfaced
7. `docs: write M3-S5b retrospective` — item 10

## Acceptance criteria

- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test CritterBids.slnx` — 72-test baseline preserved; +7 new tests green; zero skipped, zero failing; **total 79**
- [ ] `src/CritterBids.Auctions/AuctionClosingSaga.cs` — `Handle(CloseAuction)` is real (no TODO comment, no `new OutgoingMessages()`-as-noop without branching); `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` exist, each with `[SagaIdentityFrom(nameof(X.ListingId))]` on the message parameter and the `Status == Resolved` idempotency guard; terminal transitions call `MarkCompleted()`
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — `AddEventType<T>()` count is **8** at minimum (S5's 7 plus `ListingWithdrawn`); **11** if OQ5 resolves outcome events are stream-appended
- [ ] `CritterBids.Contracts.Selling/ListingWithdrawn.cs` (or OQ6-resolved location) exists with a minimum-viable payload including `ListingId`
- [ ] All 7 test methods in `AuctionClosingSagaTests.cs` named exactly per milestone doc §7 §3 rows 3.5–3.11, each green
- [ ] Scenario 3.5 asserts `ListingSold` emitted with hammer-price payload drawn from saga state; `BiddingClosed` also emitted per OQ1 resolution
- [ ] Scenario 3.6 asserts `ListingPassed` emitted with `Reason = ReserveNotMet`
- [ ] Scenario 3.7 asserts `ListingPassed` emitted with `Reason = NoBids`
- [ ] Scenario 3.8 asserts saga `Status == Resolved` after `BuyItNowPurchased`; pending `CloseAuction` set contains zero entries for the listing (per OQ2 if resolution is "cancel explicitly"); `BiddingClosed` emitted per OQ1
- [ ] Scenario 3.9 asserts that a `CloseAuction` arriving at a saga in `Resolved` state produces no outcome events (no second `ListingSold` / `ListingPassed`); saga state byte-identical before and after
- [ ] Scenario 3.10 asserts saga `Status == Resolved` after synthetic `ListingWithdrawn`; no `ListingSold` / `ListingPassed` emitted; pending `CloseAuction` set contains zero entries for the listing
- [ ] Scenario 3.11 asserts close evaluation fires at the extended `ScheduledCloseAt`, not the original — seeds a saga in `Status = Extended` state with `ScheduledCloseAt > original` and dispatches `CloseAuction` at the extended time
- [ ] `tests/CritterBids.Auctions.Tests/BuyNowDispatchTests.cs` — `SeedOpenListing` (or equivalent) seeds a saga document alongside the listing stream. Production `BuyNowHandler.cs` and the test's invocation body are byte-identical.
- [ ] `src/CritterBids.Auctions/` contains zero `IMessageBus.PublishAsync` calls inside saga handler bodies. `bus.ScheduleAsync` (S5) and `messageStore.ScheduledMessages.CancelAsync` (S5) are the only acceptable imperative bus-touches.
- [ ] `src/CritterBids.Api/Program.cs` diff limited to what OQ5 or OQ6 might require; any change called out in the retro with rationale
- [ ] `CritterBids.Auctions.csproj` `ProjectReference` count is 1 (Contracts only)
- [ ] `PlaceBidHandler.cs`, `PlaceBidHandlerTests.cs`, `PlaceBidDispatchTests.cs`, `BuyNowHandler.cs`, `BuyNowHandlerTests.cs`, `BidConsistencyState.cs`, `Listing.cs`, `StartAuctionClosingSagaHandler.cs` all unchanged from S5 close (byte-level diff limited to whitespace at most)
- [ ] Scenario tests 3.1–3.4 from S5 unchanged and still green
- [ ] No `[Obsolete]`, `#pragma warning disable`, or `throw new NotImplementedException()` in production
- [ ] `docs/retrospectives/M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is **not optional** and is **not a footnote**. It is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test CritterBids.slnx` shows all tests green and `dotnet build` shows 0 errors + 0 warnings. If any test fails or any warning lands, fix the code first, then write the retro.

Retrospective content requirements:
- Baseline numbers (72 tests before, 79 after) with a phase table matching the S5 retro shape
- Per-item status table mirroring the "In scope (numbered)" list with commit references
- Each of the six Open Questions below answered with which path was taken and why — and for OQ1, OQ3, OQ4, OQ6, a citation to the primary source (AI Skills repo path, pristine local repo file path, `CritterStackSamples` example, or Context7 library reference) that grounded the decision. Training-memory citations are insufficient per the reference-doc discipline convention.
- Whether the skill append in item 9 was written; if so, the appended sections listed (and whether content was ported from `C:\Code\JasperFx\ai-skills\wolverine\` rather than re-derived); if not, an explicit "nothing new surfaced beyond what the skill already covers" observation with rationale
- Any blocker encountered: verbatim error message, root cause, fix path. Particular attention to first-use surprises around `MarkCompleted()` saga-document lifecycle, cascaded `OutgoingMessages` ordering guarantees, test-harness capture of outcome events, and any `ListingWithdrawn` forwarding or discovery edge case
- A **"What M3-S6 should know"** section covering at minimum:
  - Final payload shapes of `BiddingClosed`, `ListingSold`, `ListingPassed` as emitted by the saga — specifically which fields the `CatalogListingView` projection handlers can rely on being populated
  - Whether outcome events are appended to the listing's primary stream (stream-replay available for projection rebuild) or bus-only (projection must subscribe to the integration queue from the first publish)
  - Expected RabbitMQ queue shape for `listings-auctions-events` — which event types Auctions publishes, in what order per listing lifecycle
  - Any saga-side invariant S6's consumer must respect (e.g., "`BiddingClosed` always precedes the outcome event per listing" if OQ3 lands there)
  - The `BuyItNowPurchased` subscription question — S6's catalog may need to consume `BuyItNowPurchased` directly to set a "sold via BIN" status, rather than only consuming the saga's `BiddingClosed` + outcome pair

## Open questions (pre-mortems — flag, do not guess)

1. **Does `Handle(BuyItNowPurchased)` and/or `Handle(ListingWithdrawn)` emit `BiddingClosed`?** Two paths:
   - **Path A:** both terminal handlers emit `BiddingClosed` as the mechanical close signal — `BiddingClosed` always precedes any terminal outcome (`ListingSold`, `ListingPassed`, `BuyItNowPurchased`, or none in the withdrawal case). Simplest consumer contract for S6: "`BiddingClosed` means bidding is over, regardless of cause."
   - **Path B:** terminal handlers do NOT emit `BiddingClosed`; it is emitted only from `Handle(CloseAuction)` (the timer path). `BuyItNowPurchased` and `ListingWithdrawn` are themselves sufficient close signals. Consumer contract: "any of these four event types means bidding is over."

   Milestone doc §2 calls `BiddingClosed` "mechanical close signal" without specifying whether terminal-via-BIN or terminal-via-withdrawal also produce it. Workshop 002-scenarios.md §3 scenarios 3.8 and 3.10 do not explicitly list `BiddingClosed` as an output. **Flag and decide deliberately.** Recommended: Path A — uniform consumer contract, at the cost of an extra event on terminal paths. S6's `CatalogListingView` projection benefits from the uniformity. Cite whatever precedent informs the decision (AI Skills repo pattern, CritterStackSamples closing-saga example if one exists, or workshop parked-questions doc).

2. **Do terminal handlers explicitly cancel the pending `CloseAuction`?** Per S5 retro's "What M3-S5b should know" §4:
   - **Path (a):** `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)` each call `CancelPendingCloseAsync` before `MarkCompleted()`. Belt-and-suspenders: a pending `CloseAuction` never fires at a resolved saga.
   - **Path (b):** rely solely on `Handle(CloseAuction)`'s `if (Status == Resolved) return;` early-return. Scenario 3.9 is the primary test; cancellation happens via the saga doc being marked completed (depending on OQ4).

   S5 retro leans Path (a) — "cleaner and aligns with the established cancel-and-reschedule pattern." Recommend Path (a). Verify by checking that scenarios 3.8 and 3.10's acceptance criteria (pending `CloseAuction` set is empty after terminal) are cleanly met.

3. **How are outcome events emitted to the bus so S6's eventual subscriber (and the S5b tests) can observe them?** Two candidate mechanisms:
   - **Path I:** handler returns `OutgoingMessages` containing `BiddingClosed` + outcome; Wolverine cascades them via the standard outbox. Tests assert via `Host.ExecuteAndWaitAsync(...)` capturing cascaded messages, or via an in-memory test inbox.
   - **Path II:** handler uses `session.Events.Append(ListingStreamId, BiddingClosed, ListingSold)` (if outcome events are also stream events per OQ5), and `UseFastEventForwarding` republishes them to the bus. Tests assert via the same forwarding-observation harness S5 used.

   Path I is the saga-idiomatic shape per `wolverine-sagas.md`. Path II couples saga emission to stream append, which is tighter than the skill prescribes. **Recommend Path I** unless OQ5 resolves in favour of stream-append for independent reasons. Whatever path is chosen, the S5b test fixture gains a uniform "capture outcome events" helper (item 7) with a documented shape. Cite the Wolverine source or AI Skills file that documents the chosen cascading mechanism.

4. **What happens to the saga document when `MarkCompleted()` is called under `.UseNumericRevisions(true)` storage?** Three possibilities to verify — first in `C:\Code\JasperFx\ai-skills\wolverine\` for the documented behaviour, then in `C:\Code\JasperFx\wolverine\` source for the ground truth:
   - Saga doc is physically deleted — `LoadSaga<AuctionClosingSaga>(listingId)` returns `null` after completion. Scenario 3.9's `CloseAuction_AfterBuyItNow_NoOp` requires deduplication by "saga not found" — Wolverine should already handle this (UnknownSagaException previously, now possibly a silent skip for completed sagas; verify).
   - Saga doc is retained with `Status = Resolved` and a completion flag. `LoadSaga<AuctionClosingSaga>(listingId)` returns the doc; the Resolved-guard fires.
   - Soft-delete via Marten's soft-delete feature. `LoadSaga` returns null by default but the doc is recoverable for audit.

   The test-assertion shape for the 7 scenarios depends on this. Record the finding in the retro and pin the assertion pattern uniformly. **Flag if the Wolverine default differs from what the local skill file implies**; if so, the retro recommends a skill-file update, preferring to port language from the AI Skills repo if it already covers the case.

5. **Are outcome events (`BiddingClosed`, `ListingSold`, `ListingPassed`) appended to the listing's primary stream, or bus-only?**
   - **Path ✻:** append + publish via `UseFastEventForwarding`. Gives event-sourced replay of the listing's full lifecycle on a single stream, including terminal events. Requires `AddEventType<T>()` registration for all three.
   - **Path ◦:** bus-only via `OutgoingMessages`. Lighter weight; the saga "is" the truth for close state. Projection rebuild for S6's `CatalogListingView` must instead subscribe to the queue from commit-one, which is already the convention per `integration-messaging.md` L2.

   Path ✻ is closer to CritterSupply's pattern (event-sourcing-first for terminal state — verify via `C:\Code\CritterSupply\` if a parallel saga landing pattern exists). Path ◦ is closer to "saga-is-the-projection-source" — simpler but less auditable. **Flag.** If Path ✻: add three `AddEventType<T>()` calls, bringing the count to 11. If Path ◦: count stays at 8.

6. **Where does `ListingWithdrawn` live as a type?** Three options:
   - **Path A:** `CritterBids.Contracts.Selling.ListingWithdrawn` — forward-compatible with future Selling-side publication; zero rework when Selling implements withdrawal.
   - **Path B:** `CritterBids.Auctions.ListingWithdrawn` — saga-internal, symmetric to `CloseAuction`. Requires renaming / relocating when Selling implements withdrawal.
   - **Path C:** defined only in the test-fixture synthetic path (no production-referenced type). Scenario 3.10 becomes fixture-private. Unusual but minimal.

   **Recommend Path A** — contract stability, aligned with `integration-messaging.md` L2 discipline. The M2 non-goal on "Selling-side `WithdrawListing` command" remains honoured because S5b does not wire a Selling-side publisher or handler — only the contract exists, and the saga consumes it via synthetic fixture emission. Flag if Path A surfaces an ADR-level question (e.g., "should the contract carry withdrawal reason?") — if so, minimum-viable payload is `ListingId` only, with an ADR flagged in the retro.
