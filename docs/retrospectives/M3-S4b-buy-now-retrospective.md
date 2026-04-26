# M3-S4b: DCB BuyNow Handler — Retrospective

**Date:** 2026-04-18
**Milestone:** M3 — Auctions BC
**Slice:** S4b of 8 (follows S4; pair pre-emptively split from the originally-planned joint S4 per milestone doc §9)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M3-S4b-buy-now.md`

---

## Baseline

- 63 tests passing (1 Api + 1 Contracts + 4 Listings + 6 Participants + 19 Auctions + 32 Selling) — verified at S4b start
- `dotnet build` — 0 errors, 0 warnings
- S4 closed with `PlaceBidHandler` live using the manual-tag, manual-append DCB shape (NOT the canonical `[BoundaryModel]` auto-append shape the S4 prompt anticipated)
- `BidConsistencyState` has `public Guid Id` (empirical Marten 8 requirement) and `Apply` methods for all S4 events — `BuyItNowPrice` and `BuyItNowAvailable` populated from `Apply(BiddingOpened)`
- 6 `AddEventType<T>()` registrations in `AuctionsModule`; DCB tag-type registration and both concurrency retry policies live
- `AuctionsTestFixture` is direct-invocation + bus-dispatch capable without queue routing

## Session outcome

- 68 tests passing (+4 scenarios in `BuyNowHandlerTests`, +1 dispatch in `BuyNowDispatchTests`)
- `dotnet build` — 0 errors, 0 warnings
- `BuyNowHandler` is the second DCB handler in CritterBids — applies S4's precedent one-for-one with zero surprises
- `BidConsistencyState` gained a single `Apply(BuyItNowPurchased)` that flips the listing to terminal state
- 7 event types now registered (the S4 six plus `BuyItNowPurchased`)
- No skill-file append — nothing new surfaced beyond what the S4 append already covers (rationale below)
- Every acceptance criterion from the prompt met

---

## Prompt-vs-reality reconciliation

The S4b prompt was authored before S4 ran, and it predicted S4 would land a canonical `[BoundaryModel]` auto-append handler. S4 didn't — the retro documents the pivot to manual-tag + manual-append. S4b honoured the retro's "What M3-S4b should know" section as the authoritative correction; the prompt's stale claims (items 3/58/78 referencing `[BoundaryModel]` with `Load`/optional `Validate`/`Handle(... [BoundaryModel] ...)`) were not followed. No in-place prompt edits were made — the prompt is treated as the frozen intent, the retro as the forward correction, consistent with the prompt/retro pairing convention in `docs/prompts/README.md`.

---

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| 1 | `AuctionsModule` — `AddEventType<BuyItNowPurchased>()` (7th registration) | `3aec8eb` |
| 2 | `BidConsistencyState.Apply(BuyItNowPurchased)` — flips `IsOpen` and `BuyItNowAvailable` to false | `36544e2` |
| 3 | `BuyNow` command + `BuyNowHandler` covering all 4 §2 scenarios; manual-tag shape matching `PlaceBidHandler` | `0e15a98` |
| 4 | `BuyNowHandlerTests.cs` — 4 integration tests, method names exactly per milestone doc §7 §2 | `0e15a98` |
| 5 | `BuyNowDispatchTests.cs` — 1 dispatch test through `IMessageBus` | `0e15a98` |
| 6 | `dynamic-consistency-boundary.md` append | Skipped — see Open Questions §3 below |
| 7 | This retrospective | (this commit) |

---

## Item 1 — `BuyItNowPurchased` event registration

Single-line addition to `AuctionsModule.ConfigureMarten`. Comment in the file updated to flag that the registration count is seven and that `BuyItNowPurchased` is the terminal event of the BIN short-circuit path. No other ceremony: no tag-type additions (the existing `RegisterTagType<ListingStreamId>().ForAggregate<BidConsistencyState>()` covers BuyNow by construction), no new concurrency policies.

---

## Item 2 — `Apply(BuyItNowPurchased)` on `BidConsistencyState`

Two lines of actual mutation:

```csharp
public void Apply(BuyItNowPurchased @event)
{
    IsOpen = false;
    BuyItNowAvailable = false;
}
```

No new fields added to the class — the S4-landed state was already sufficient for all 4 §2 scenarios (Open Question 2 below). The class-level XML comment was updated to record S4b's contribution and to explain why the terminal projection matters: a sequential second BuyNow attempt on a purchased listing rejects via `BuyItNowNotAvailable`, and the DCB consistency assertion catches concurrent attempts.

---

## Item 3 — `BuyNowHandler` — one-for-one with `PlaceBidHandler`

The handler is the same structural shape S4 settled on:

```csharp
public static async Task HandleAsync(BuyNow command, IDocumentSession session, TimeProvider time)
{
    var query = BuildQuery(command.ListingId);
    var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
    var state = boundary.Aggregate ?? new BidConsistencyState();

    var now = time.GetUtcNow();
    var reason = EvaluateRejection(command, state, now);

    if (reason is not null)
    {
        await AppendRejectionAudit(session, command, state, reason, now);
        return;
    }

    var price = state.BuyItNowPrice!.Value;
    var purchased = new BuyItNowPurchased(command.ListingId, command.BuyerId, price, now);

    var wrapped = session.Events.BuildEvent(purchased);
    wrapped.AddTag(new ListingStreamId(command.ListingId));
    session.Events.Append(command.ListingId, wrapped);
}
```

Pure `Decide(BuyNow, BidConsistencyState, TimeProvider) => Events` sibling mirrors `PlaceBidHandler.Decide` — named `Decide` (not `Handle*`) to avoid Wolverine's compound-handler-discovery matching a second static method.

### Rejection precedence

Ordered the same way PlaceBidHandler orders its checks, with the BIN-specific checks inserted:

1. `ListingNotOpen` — `state.ListingId == Guid.Empty` (no BiddingOpened tagged to this listing)
2. `ListingClosed` — `now >= state.ScheduledCloseAt`
3. `ListingClosed` — `!state.IsOpen` (catches the post-BuyItNowPurchased sequential case)
4. `BuyItNowNotAvailable` — `!state.BuyItNowAvailable || state.BuyItNowPrice is not { }` (scenario 2.2)
5. `ExceedsCreditCeiling` — `price > command.CreditCeiling` (scenario 2.3)

The `IsOpen` check (step 3) is redundant with step 2 for the §2 test set — scenario 2.4 triggers via step 2, and there is no §2 scenario where a terminal BuyItNowPurchased followed by a sequential second BuyNow is exercised. Kept it anyway because a) the projection populates `IsOpen` and not checking it leaves a latent gap, and b) it costs nothing. M3-S5 will land `BiddingClosed` / `ListingSold` / `ListingPassed` and their corresponding `Apply` methods will also flip `IsOpen = false`; the check already handles that future case.

### Tag query

`BuildQuery(listingId)` lists six event types — the five from `PlaceBidHandler.BuildQuery` plus `BuyItNowPurchased`. Including `BuyItNowPurchased` is what makes `Apply(BuyItNowPurchased)` load on a second attempt; without it the state would never see the terminal event.

### Rejection event — `BidRejected` on the shared audit stream

No new audit type was introduced. The prompt's default recommendation (Open Question 1) was met: reuse `BidRejected` and write to the existing `BidRejectionAudit` stream. For BuyNow rejections:

- `BidderId` is the would-be buyer (`command.BuyerId`)
- `AttemptedAmount` is `state.BuyItNowPrice ?? 0m` — the BIN price the buyer would have paid; 0 for scenario 2.2's "option removed" case where the price is unchanged but availability flipped
- `CurrentHighBid` tracks whatever the state carries at rejection time
- `Reason` discriminates: `BuyItNowNotAvailable` / `ExceedsCreditCeiling` / `ListingClosed` / `ListingNotOpen`

PlaceBid and BuyNow rejections can now interleave on the same `BidRejectionAudit` stream for a given listing. This is consistent with the W002-7 "rejections are Auctions-internal audit concern" decision — a unified per-listing rejection ledger reads cleanly in support tooling.

---

## Items 4 & 5 — tests

### `BuyNowHandlerTests.cs` (4 scenarios, method names match milestone doc §7 §2)

| Scenario | Method | Path |
|----------|--------|------|
| 2.1 | `BuyNow_NoPriorBids_ProducesBuyItNowPurchased` | `Decide` |
| 2.2 | `BuyNow_OptionRemoved_Rejected` | Bus |
| 2.3 | `BuyNow_ExceedsCreditCeiling_Rejected` | Bus |
| 2.4 | `BuyNow_ListingClosed_Rejected` | Bus |

Shape choices follow S4's precedent: happy path via `Decide` (pure decision, no bus ceremony); rejection paths via `InvokeMessageAndWaitAsync` with an assertion on the `BidRejectionAudit` stream. Seeding helpers mirror `PlaceBidHandlerTests` exactly (BiddingOpened + optional BidPlaced + optional BuyItNowOptionRemoved). `LoadState` routes through `BuyNowHandler.BuildQuery` so the test and production load paths share the same query shape.

### `BuyNowDispatchTests.cs` (1 dispatch test)

Seeds a BIN-eligible listing, dispatches `BuyNow` through `_fixture.Host.InvokeMessageAndWaitAsync`, and asserts:

- The boundary loads with `IsOpen = false` and `BuyItNowAvailable = false` (proving `Apply(BuyItNowPurchased)` ran through the bus-driven write)
- The listing's primary stream has a `BuyItNowPurchased` event with the correct buyer and price

This covers the "handler registered and routable via `IMessageBus`" half of the exit criterion the 4 scenario tests don't exercise directly. No sticky-handler issues — `BuyNow` is a non-queue-routed internal command, the same pattern `PlaceBid` uses.

---

## Open Questions — answered

### Q1 — Rejection event shape for BuyNow

**Answer: reuse `BidRejected` on the existing `BidRejectionAudit` stream.**

Rationale, per the S4 retro's "What M3-S4b should know" guidance and the prompt's stated default: `BidRejected` is internal to Auctions, audit-only, and has no cross-BC consumer that would be surprised by seeing it tagged to a BuyNow flow. The `Reason` string discriminates the path. Introducing `BuyNowRejected` would have added a sibling type plus a second `BuyNowRejectionAudit` stream with a distinct XOR namespace — pure type proliferation with no operational benefit. The only cost of reuse is a slightly awkward type name on the BuyNow rejection side, which matters less for an internal audit event than it would for a published contract.

### Q2 — Additional state on `BidConsistencyState`

**Answer: no new fields. All §2 scenarios read fields already populated by `Apply(BiddingOpened)`.**

Verified against each scenario:

| Scenario | Field(s) read | Already on state? |
|----------|---------------|-------------------|
| 2.1 | `BuyItNowAvailable`, `BuyItNowPrice` | Yes — both from `Apply(BiddingOpened)` |
| 2.2 | `BuyItNowAvailable` | Yes — flipped by `Apply(BuyItNowOptionRemoved)` |
| 2.3 | `BuyItNowPrice`, `command.CreditCeiling` | Yes + command |
| 2.4 | `ScheduledCloseAt`, `IsOpen` | Yes — both from `Apply(BiddingOpened)` |

The single new mutation added in S4b is `Apply(BuyItNowPurchased)` — a terminal-state projection, not a new field. S4's state design was correct on first pass.

### Q3 — Skill append

**Skipped. Nothing new surfaced.**

The S4 append to `dynamic-consistency-boundary.md` covered: `EventTagQuery` shape, why `[BoundaryModel]` did not fit for `Guid ListingId` contract events, why `ListingStreamId` wraps Guid (.NET 10 Variant/Version properties), the `public Guid Id` requirement on the boundary model, non-composition of `ValidateAsync` with `[BoundaryModel]`, nullable `[BoundaryModel]` state parameter, `UseMandatoryStreamTypeDeclaration` seeding workflow, live-aggregation coexistence with DCB appends, and both concurrency retry policies.

S4b's work was a clean one-for-one re-application of that pattern to a second command. No new wrinkle, no Marten 8 quirk, no handler-discovery edge case. Recording "we applied the pattern and it worked" adds no predictive value for future readers; the S4 append already tells them how the pattern works and why. Documented that outcome here instead.

---

## Test results

| Phase | Auctions tests | Total | Result |
|-------|---------------:|------:|:------|
| Baseline (M3-S4 close) | 19 | 63 | All green |
| After `AddEventType<BuyItNowPurchased>()` (`3aec8eb`) | 19 | 63 | All green |
| After `Apply(BuyItNowPurchased)` (`36544e2`) | 19 | 63 | All green |
| After `BuyNowHandler` + 5 new tests (`0e15a98`) | 24 | 68 | **All green** |

All 5 new tests passed on first build — no iteration required. This is the payoff for the S4 retro being precise about the handler shape: zero time was spent on the dead `[BoundaryModel]` path the prompt originally anticipated.

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` — 68 passed, 0 failed, 0 skipped
- `CritterBids.Auctions.csproj` `ProjectReference` count: 1 (Contracts only)
- `src/CritterBids.Auctions/` `IMessageBus` references: 0
- `AuctionsModule` `AddEventType<T>()` count: 7 (target met)
- `src/CritterBids.Auctions/PlaceBidHandler.cs`, `PlaceBidHandlerTests.cs`, `PlaceBidDispatchTests.cs`, `Listing.cs` — byte-for-byte unchanged from S4 close (verified by diff at `b6fc1b4..HEAD`)
- `src/CritterBids.Api/Program.cs` — unchanged from S3 close

---

## What M3-S5 should know

- **`BuyItNowPurchased` is registered in `AuctionsModule` and is now a terminal signal on the listing's primary stream.** The Auction Closing saga consumes it per scenario 3.8 — it transitions to `Resolved` with `BuyItNowExercised = true` and calls `MarkCompleted` immediately. No follow-up `BiddingClosed` or `ListingSold` is produced; `BuyItNowPurchased` is the sole terminal event on the BIN path.
- **Saga's scheduled `CloseAuction` must be cancelled when it observes `BuyItNowPurchased`.** Scenario 3.9 covers the "scheduled timer fires after saga completed" case — the no-op is safe because the saga is already resolved, but leaving a dead scheduled message pending is wasteful. Scheduling cancellation is the clean path.
- **`BuyItNowPurchased` payload** carries `(Guid ListingId, Guid BuyerId, decimal Price, DateTimeOffset PurchasedAt)`. The saga should surface `Price` as the settlement amount and `BuyerId` as the winner for any post-saga downstream that reads the saga document.
- **BuyNow events are tagged with `ListingStreamId` AND appended to the listing's primary stream.** Both the DCB boundary (via the `BuyNowHandler.BuildQuery` tag list) and the live `Listing` aggregate see them — so if M3-S5 adds `Apply(BuyItNowPurchased)` to `Listing`, the live projection will pick it up automatically. `Listing` currently has no `Apply(BuyItNowPurchased)`; that's an open call for S5 to make based on which fields the saga and downstream projections need.
- **Rejections from both `PlaceBidHandler` and `BuyNowHandler` share the `BidRejectionAudit` stream per listing.** If M3-S5 or M4 introduces additional rejection-producing handlers, stick with the same stream + `Reason` discrimination to keep the audit trail unified — or introduce a distinct audit stream type with a different XOR namespace if cross-domain isolation is genuinely needed.
- **`BidConsistencyState` is stable — do not refactor it.** Both handlers depend on its exact field set and `Apply` behaviour. The `public Guid Id` is load-bearing (Marten 8 document requirement). M3-S5's saga work is cleanly separable from the DCB state — the saga is its own document type.
- **The manual-tag + manual-append shape is now the CritterBids convention for DCB handlers.** M4's Proxy Bid Manager saga will produce `PlaceBid` commands; those flow through `PlaceBidHandler` unchanged (`IsProxy` flips to true at command-construction time). No new DCB handler ceremony is anticipated in M4.

---

## What M4 (Proxy Bid Manager) should know

- **`BuyItNowPurchased` terminates the proxy saga** the same way `ListingSold` / `ListingPassed` / `ListingWithdrawn` do (scenarios 4.6–4.8 pattern). Proxy registers on a listing, observes terminal events, transitions to `ListingClosed`, and calls `MarkCompleted`. M4 should route `BuyItNowPurchased` through the same terminal handler it uses for `ListingSold`.
- **BuyNow is not proxy-able.** A buyer who executes BuyNow commits immediately to the BIN price — there's no competing-bid ladder for a proxy to auto-bid against. M4's `RegisterProxyBid` path does not need a BuyNow variant.
