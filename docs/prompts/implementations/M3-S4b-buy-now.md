# M3-S4b: DCB BuyNow Handler

**Milestone:** M3 — Auctions BC
**Slice:** S4b of 8 (follows S4; pair pre-emptively split from the originally-planned joint S4 per milestone doc §9)
**Agent:** @PSA
**Estimated scope:** one PR; 5 new tests; ~3–5 new/modified files
**Baseline:** 63 tests green · `dotnet build` 0 errors, 0 warnings · M3-S4 closed. At S4 close: `PlaceBidHandler` live with canonical `[BoundaryModel]` shape, `BidConsistencyState` live with all `BiddingOpened` fields populated (including `BuyItNowPrice` and `BuyItNowAvailable`), real `Listing.Apply(BiddingOpened)` landed, `ScaffoldPlaceholder` removed, six `AddEventType<T>()` registrations, DCB pattern established and documented in the skill append.

---

## Goal

Apply the established `[BoundaryModel]` DCB pattern from S4 to the BuyNow short-circuit path. `BuyNowHandler` enforces the 4 scenarios from `002-scenarios.md` §2, reusing `BidConsistencyState` — extended with `Apply(BuyItNowPurchased)` so the boundary model reflects terminal state after a BIN purchase. One `IMessageBus` dispatch test per the M2.5 precedent. After this slice, the full bid-vs-BuyNow decision fabric is in place; S5 wires the saga to close auctions.

This is an intentionally small slice. The hard parts — DCB pattern, boundary model projection over two streams, Marten 8 wrinkles, live-stream-projection interaction — were resolved in S4. S4b's job is to apply the precedent without reopening the design.

## Context to load

- `docs/milestones/M3-auctions-bc.md` — §7 acceptance tests (§2 BuyNow rows)
- `docs/workshops/002-scenarios.md` — §2 (4 BuyNow scenarios)
- `docs/skills/dynamic-consistency-boundary.md` — now with M3-S4 learnings appended; the skill's canonical guidance applies unchanged to BuyNow
- `docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md` — fresh precedent; PlaceBidHandler shape, test shape, Open-Question outcomes
- `src/CritterBids.Auctions/PlaceBidHandler.cs` — the exact handler shape to mirror
- `src/CritterBids.Auctions/BidConsistencyState.cs` — the existing boundary model to extend
- `C:\Code\JasperFx\wolverine\src\Persistence\MartenTests\Dcb\University\` — canonical reference code (for consistency check only; S4 already established the pattern for this repo)

## In scope (numbered)

1. `src/CritterBids.Auctions/AuctionsModule.cs` — add `opts.Events.AddEventType<BuyItNowPurchased>()`. Final count after S4b: **seven** `AddEventType<T>()` calls (S4's six + this one). No new tag-type registrations unless item 3 requires a new tag; verify against scenario 2.3 (credit ceiling) and 2.4 (listing closed).
2. `src/CritterBids.Auctions/BidConsistencyState.cs` — extend:
   - Add `Apply(BuyItNowPurchased)` that marks the listing as no longer eligible (e.g. `IsOpen = false` and `BuyItNowAvailable = false`). The existing `Apply(BiddingOpened)` already populates `BuyItNowPrice` and `BuyItNowAvailable` — no change needed there.
   - Add fields only if a scenario explicitly demands them. Do not pre-emptively broaden the state.
3. `src/CritterBids.Auctions/BuyNowHandler.cs` — static class, **exact same three-method shape as `PlaceBidHandler`**: `public static EventTagQuery Load(BuyNow command)`; optional `public static HandlerContinuation Validate(BuyNow command, BidConsistencyState state, ILogger logger)`; `public static TEvent? Handle(BuyNow command, [BoundaryModel] BidConsistencyState state)`. Returns `BuyItNowPurchased` on happy path. Rejection shape is the same pattern PlaceBidHandler chose in S4 (exceptions vs `BidRejected` vs a distinct `BuyNowRejected` — see Open Question 1).
4. `tests/CritterBids.Auctions.Tests/BuyNowHandlerTests.cs` — 4 integration tests, one per §2 scenario, method names exactly per milestone doc §7 §2 rows. Test shape choices follow S4's precedent (through-the-bus for happy path; direct invocation for pure validation logic).
5. `tests/CritterBids.Auctions.Tests/BuyNowDispatchTests.cs` — 1 integration test dispatching `BuyNow` via `IMessageBus.InvokeAsync`.
6. *(Optional)* `docs/skills/dynamic-consistency-boundary.md` — append a short "CritterBids M3-S4b notes" subsection if and only if something new surfaced that the S4 append didn't cover. If the slice runs as expected (apply known pattern → green tests), skip this item and record the "nothing new" observation in the retro instead.
7. `docs/retrospectives/M3-S4b-buy-now-retrospective.md` — written last. Gate below.

## Explicitly out of scope

- Any rewrite of `PlaceBidHandler`, the S4-established `BidConsistencyState` structure, or `Listing.Apply(BiddingOpened)`. Additions only to `BidConsistencyState` (per item 2); everything else is frozen.
- Auction Closing saga — S5
- `BiddingClosed`, `ListingSold`, `ListingPassed` registrations and handlers — S5
- `CatalogListingView` auction-status fields — S6
- `listings-auctions-events` RabbitMQ queue — S5 or S6
- Proxy Bid Manager — M4
- Session aggregate / flash format — M4
- Selling-side `WithdrawListing` — unscheduled
- HTTP endpoints for `BuyNow` — message-driven in M3
- Any change to `BiddingOpened`, `ListingPublished`, or any contract payload — frozen
- Any `Program.cs` change — S3's three queues still stand
- Rewriting existing sections of `dynamic-consistency-boundary.md`. Item 6 is optional and append-only.

## Conventions to pin or follow

Inherit all conventions from M3-S4 via `PlaceBidHandler` precedent. No new conventions are introduced in this slice. Specifically:

- `BuyNowHandler` uses `[BoundaryModel]` with `Load` + `Handle` + optional `Validate`. Not `[WriteAggregate]`. No `IEventBoundary<TState>` on Handle.
- `BuyNowHandler` matches `PlaceBidHandler`'s rejection shape exactly (whatever S4 chose — exceptions, nullable returns, or `BidRejected` emission).
- Test fixture stays as S4 left it; extend additively only if the dispatch test requires it.
- Zero `IMessageBus` in production Auctions code.
- `CritterBids.Auctions.csproj` `ProjectReference` count stays at 1 (Contracts).

## Commit sequence (proposed)

1. `feat(auctions): register BuyItNowPurchased event type` — item 1
2. `feat(auctions): extend BidConsistencyState with Apply(BuyItNowPurchased)` — item 2
3. `feat(auctions): implement BuyNowHandler covering §2 scenarios` — items 3, 4, 5
4. *(optional)* `docs(skills): append M3-S4b notes to dynamic-consistency-boundary.md` — item 6
5. `docs: write M3-S4b retrospective` — item 7

## Acceptance criteria

- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test CritterBids.slnx` — 63-test baseline preserved; +5 new tests green; zero skipped, zero failing; total 68
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — `AddEventType<T>()` count is **seven** (S4's six plus `BuyItNowPurchased`)
- [ ] `src/CritterBids.Auctions/BidConsistencyState.cs` — has `Apply(BuyItNowPurchased)`; no other structural changes to S4's state class beyond what §2 scenarios require
- [ ] `src/CritterBids.Auctions/BuyNowHandler.cs` — static class with `Load(command) => EventTagQuery` and `Handle(command, [BoundaryModel] BidConsistencyState state)`. Optional `Validate` permitted. Zero `[WriteAggregate]`. Zero `IEventBoundary<T>` on Handle. Shape matches `PlaceBidHandler`.
- [ ] All 4 test methods in `BuyNowHandlerTests.cs` named exactly per milestone doc §7 §2 and green
- [ ] `BuyNowDispatchTests.cs` — 1 test invoking `BuyNow` through `IMessageBus`, green
- [ ] `src/CritterBids.Auctions/` contains zero `IMessageBus` references
- [ ] `src/CritterBids.Api/Program.cs` unchanged from S3 close
- [ ] `CritterBids.Auctions.csproj` `ProjectReference` count is 1 (Contracts only)
- [ ] `PlaceBidHandler.cs`, `PlaceBidHandlerTests.cs`, `PlaceBidDispatchTests.cs`, `Listing.cs` all unchanged from S4 close (byte-level diff limited to whitespace at most)
- [ ] `docs/retrospectives/M3-S4b-buy-now-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is **not optional** and is **not a footnote**. It is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test CritterBids.slnx` shows all tests green and `dotnet build` shows 0 errors + 0 warnings. If any test fails or any warning lands, fix the code first, then write the retro.

Retrospective content requirements:
- Baseline numbers (63 tests before, 68 after)
- Per-item status table mirroring the "In scope (numbered)" list
- Each of the two Open Questions below answered with which path was taken and why
- Whether the skill append in item 6 was written, and if so what it covered — or an explicit "nothing new surfaced" observation with a brief rationale
- Any blocker encountered: verbatim error message, root cause, fix path
- "What M3-S5 should know" section — the saga consumes `BuyItNowPurchased` produced here (per milestone doc scenario 3.8, 3.9); saga-side assumptions about its payload, whether it closes the listing's bidding stream, and whether it triggers a `BiddingClosed`/`ListingSold` emission need to be stated

## Open questions (pre-mortems — flag, do not guess)

1. **Rejection event shape for BuyNow.** The 3 rejection scenarios (2.2 option removed, 2.3 exceeds credit ceiling, 2.4 listing closed) need a rejection output. S4's `PlaceBidHandler` established the project's rejection convention for DCB handlers — `BidRejected`, a nullable return that no-ops, an exception throw, or a distinct `BuyNowRejected` audit event. Mirror exactly whatever S4 chose. If S4's choice was `BidRejected` as a universal rejection event, use it (the name is slightly awkward for BuyNow rejections but the type is already in scope). If S4 chose exceptions or nullable-return no-ops, mirror that. If S4's choice was `BidRejected` and the name feels genuinely wrong for BuyNow, flag — propose `BuyNowRejected` as a sibling audit event type — but do not add a new event type speculatively.

2. **Additional state on `BidConsistencyState`.** Scenarios 2.1–2.4 read: `BuyItNowAvailable` (present from S4), `BuyItNowPrice` (present from S4), bidder credit ceiling / credit used (present from S4), `IsOpen` (present from S4). No new fields should be required. Verify against each of the 4 scenarios before adding anything. If a scenario demands a field that's missing, flag — this would indicate a gap in S4's state design that S4 should have caught.
