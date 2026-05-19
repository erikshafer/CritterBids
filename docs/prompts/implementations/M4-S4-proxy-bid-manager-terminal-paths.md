# M4-S4: Proxy Bid Manager Saga — Terminal Paths + Exhaustion + Bidding War

**Milestone:** M4 — Auctions BC Completion
**Slice:** S4 of 7 (with pre-drafted S4b split slot for §4.10 + §4.11 if scope overflows; see §Session sizing notes)
**Narrative:** none — proxy mechanics route `separate-narrative` in `docs/narratives/001-bidder-wins-flash-auction.md` Moment 5 cumulative deferred section ("IsProxy flag and proxy bidding journey, slices 5.5 / 5.6"). Scope is scenario-anchored to Workshop 002 §4.
**Agent:** @PSA
**Estimated scope:** one PR; 7 new scenario tests; ~12–14 new/modified files
**Baseline:** 125 tests passing (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 42 Auctions) · `dotnet build` 0 errors, 24 pre-existing NU1904 NuGet warnings (Marten) · M4-S3 closed at the squash-merge of PR #33. At session open: `ProxyBidManagerSaga` exists with the reactive `Handle(ProxyBidObserved)` covering own-bid (monotone `LastBidAmount`) and competing-bid (auto-bid up to `MaxAmount`) branches; the exhaustion branch is a `TODO(M4-S4)` comment; no terminal handlers; `BidderCreditCeiling` defaults to `0m` on saga construction (M4-S3 OQ4 Path c — deferred to S4); `ProxyBidDispatchHandler` fans `BidPlaced` to one `ProxyBidObserved` per active saga.

---

## Goal

Land the Proxy Bid Manager saga's terminal paths, exhaustion-emission branch, and the credit-ceiling cap that completes Workshop 002 §4. Scope covers seven scenarios: §4.3 (competing bid exhaustion via `MaxAmount`), §4.6 / §4.7 / §4.8 (terminal handlers for `ListingSold` / `ListingPassed` / `ListingWithdrawn`), §4.9 (credit-ceiling cap exhaustion), §4.10 (two-proxy bidding war saga-to-saga cascade), §4.11 (register-while-outbid). At S4 close, the Proxy Bid Manager saga is feature-complete for MVP — every reactive path and every terminal path is exercised by integration tests.

S4 is the second-largest M4 session and the headline risk node alongside S3 per the M4 milestone doc §9. Three first-use surfaces land in S4: the **second application** of the M4-D4 duplicate-projection pattern (Auctions-side `ParticipantCreditCeiling` projection consuming `Contracts.Participants.ParticipantSessionStarted` from a new `auctions-participants-events` queue), the **first saga-to-saga cascade** within a single message-processing cycle (scenario §4.10 — the two-proxy bidding war), and the **first multi-handler dispatch of terminal events** within the Auctions BC (each of `ListingSold` / `ListingPassed` / `ListingWithdrawn` gains a second BC-local handler alongside `AuctionClosingSaga`'s existing one, requiring the pre-emptive `SendMessageAndWaitAsync` switch the M4-S3 retro names).

If any of those three surfaces grows past the session's budget, the candidate split is §4.10 + §4.11 → S4b per the milestone doc §9 pre-drafted split slot. S4 base scope is then five scenarios (§4.3, §4.6, §4.7, §4.8, §4.9) with S4b covering the remaining two.

## Context to load

- `docs/milestones/M4-auctions-bc-completion.md` — §6 (proxy saga idempotency conventions; the credit-ceiling cap calc), §7 (§4 test row mapping for 4.3 / 4.6 / 4.7 / 4.8 / 4.9 / 4.10 / 4.11), §9 (S4 risk notes — two-proxy bidding war timing, M4-D4 duplicate-projection ripple)
- `docs/workshops/002-scenarios.md` — §4.3, §4.6, §4.7, §4.8, §4.9, §4.10, §4.11 only (§4.1, §4.2, §4.4, §4.5 are S3-shipped — do not re-implement). **Read §4.9 in full, including the inline correction**: the proxy's defensive bid is `min(competingBid + increment, MaxAmount, BidderCreditCeiling)`; exhaustion fires when that minimum is `<= competingBid`. The first-pass example in §4.9 is wrong; the workshop self-corrects below it.
- `docs/retrospectives/M4-S3-proxy-bid-manager-saga-skeleton-retrospective.md` — "What M4-S4 should know" §"Identity wiring", §"Idempotency convention pinned in S3", §"`BidderCreditCeiling` lookup", §"Two-saga `BidPlaced` dispatch", §"Bid increment helper"
- `docs/skills/wolverine-sagas.md` — primary skill; new sections §"Composite-Key Correlation — the Dispatcher Pattern" and §"Multiple Handlers + `MultipleHandlerBehavior.Separated` — Send, Don't Invoke" (both authored at M4-S3 close) are the load-bearing references for S4's dispatcher extensions and the pre-emptive `AuctionClosingSagaTests` fix
- `docs/skills/marten-projections.md` — for the `ParticipantCreditCeiling` projection (second application of M4-D4 duplicate-projection pattern; first was deferred from S3 to S4, the M4-S5 `PublishedListings` projection is the parallel pattern)
- `src/CritterBids.Auctions/ProxyBidManagerSaga.cs` — S3's saga with the TODO comment in `Handle(ProxyBidObserved)` competing-bid branch; S4 replaces the TODO and adds three terminal handlers
- `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs` — S3's dispatcher with one `Handle(BidPlaced)`; S4 adds three terminal-event dispatcher methods

(Seven files. The §4.1/§4.2/§4.4/§4.5 sections of `002-scenarios.md` are out of scope — they shipped at S3 and do not need re-loading.)

## In scope (numbered)

1. **`src/CritterBids.Auctions/ProxyBidManagerSaga.cs`** — additive only:
   - Replace the `TODO(M4-S4)` comment in `Handle(ProxyBidObserved)`'s competing-bid branch with the **corrected exhaustion calc** (per Open Question 3): `var capped = Math.Min(Math.Min(message.Amount + increment, MaxAmount), BidderCreditCeiling); if (capped <= message.Amount) { emit ProxyBidExhausted; Status = Exhausted; MarkCompleted(); return; } else emit PlaceBid(...) at capped amount`
   - Three new terminal handlers: `Handle([SagaIdentityFrom(nameof(ProxyListingClosedObserved.SagaId))] ProxyListingClosedObserved)` (or three separate handlers, one per terminal event — pinned by Open Question 1). Each sets `Status = ListingClosed; MarkCompleted();`. Idempotent terminal guard: `if (Status != Active) return;` at handler entry.
   - Identity wiring stays per M4-S3 OQ1 Path C (dispatcher pattern, wrapped commands).
2. **`src/CritterBids.Auctions/ProxyBidDispatchHandler.cs`** — additive only:
   - Three new dispatcher methods: `Handle(ListingSold)`, `Handle(ListingPassed)`, `Handle(ListingWithdrawn)`. Each queries `Query<ProxyBidManagerSaga>().Where(s => s.ListingId == message.ListingId && s.Status == ProxyBidManagerStatus.Active)` and emits one wrapped command per active saga.
   - Same fan-out shape as the existing `Handle(BidPlaced)`; the wrapped command shape resolves per Open Question 1.
3. **New wrapped command type(s)** — `ProxyListingClosedObserved` (single command with a discriminator field) **or** three separate commands `ProxyListingSoldObserved` / `ProxyListingPassedObserved` / `ProxyListingWithdrawnObserved`. Decision per Open Question 1. Files placed in `src/CritterBids.Auctions/` alongside `ProxyBidObserved.cs`.
4. **`src/CritterBids.Auctions/ParticipantCreditCeiling.cs`** — new Marten document (M4-D4 duplicate-projection pattern, second application). Schema: `(Guid BidderId, decimal CreditCeiling, DateTimeOffset RegisteredAt)`. Keyed by `BidderId` (= `ParticipantId` — verify the identity mapping at session open per Open Question 4). Idempotent upsert on re-delivery of `ParticipantSessionStarted`.
5. **`src/CritterBids.Auctions/ParticipantCreditCeilingHandler.cs`** — projection handler. `Handle(ParticipantSessionStarted, IDocumentSession)` upserts the document. Tolerant on re-delivery (existing row preserves its data, `RegisteredAt` does not re-stamp).
6. **`src/CritterBids.Auctions/AuctionsModule.cs`** — additive only:
   - `services.ConfigureMarten(opts => opts.Schema.For<ParticipantCreditCeiling>())` (database schema name `auctions`, mirroring the existing types)
   - `AddEventType<ProxyBidExhausted>()` if Open Question 2 resolves to bus-only emission (no Marten stream append); otherwise no change. Recommended: no `AddEventType` (bus-only per the M4-S3 `ProxyBidRegistered` precedent).
7. **`src/CritterBids.Api/Program.cs`** — additive only:
   - New routing rule `opts.PublishMessage<Contracts.Participants.ParticipantSessionStarted>().ToRabbitQueue("auctions-participants-events")` paired with `opts.ListenToRabbitQueue("auctions-participants-events")`. Mirrors the existing `settlement-participants-events` route shape (Program.cs:115-117 at M5-S5).
   - No new publish routes for `ProxyBidExhausted` (consumer is post-M5 Relay per the contract docstring; the route is dormant until then).
8. **`src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs`** — modified:
   - Load `ParticipantCreditCeiling` for `message.BidderId` at saga-start time. If not found, throw a new `ParticipantCreditCeilingNotFoundException` and configure a Wolverine retry policy (per Open Question 5 — likely follows the M5-S4 `PendingSettlementNotFoundException` shape from `SettlementsConcurrencyRetryPolicies`).
   - Populate `saga.BidderCreditCeiling` from the loaded projection's value.
   - Existence-check guard unchanged.
9. **`src/CritterBids.Auctions/AuctionsConcurrencyRetryPolicies.cs`** (modify `AuctionsModule.cs`'s nested class, or extract — agent decides): add `OnException<ParticipantCreditCeilingNotFoundException>().RetryWithCooldown(100ms, 250ms, 500ms)`. Matches the `SettlementsConcurrencyRetryPolicies` pattern from M5-S4.
10. **`tests/CritterBids.Auctions.Tests/ProxyBidManagerSagaTests.cs`** — extend with **seven** new tests, method names exactly per milestone doc §7 §4:
    - `CompetingBid_NextBidExceedsMax_ProducesProxyBidExhausted` (§4.3)
    - `ListingSold_CompletesSaga` (§4.6)
    - `ListingPassed_CompletesSaga` (§4.7)
    - `ListingWithdrawn_CompletesSaga` (§4.8)
    - `CompetingBidAtCeiling_ProducesProxyBidExhausted` (§4.9 — uses the corrected workshop example: competing bid `$200`, `MaxAmount: $300`, `BidderCreditCeiling: $200` → next would be `min(201, 300, 200) = 200`, not strictly greater than competing, exhausted)
    - `TwoProxies_WeakerExhausts_StrongerWins` (§4.10 — both sagas seeded, escalation runs through, weaker proxy emits `ProxyBidExhausted`)
    - `RegisterProxy_WhileOutbid_WaitsForNextCompetingBid` (§4.11 — start the saga via `RegisterProxyBid` dispatch with a pre-existing competing bid in the listing's stream; assert no immediate `PlaceBid` emission)
    All seven tests use `SendMessageAndWaitAsync` when dispatching `BidPlaced` / `ListingSold` / `ListingPassed` / `ListingWithdrawn` — multi-handler dispatch under Separated mode per the wolverine-sagas skill §"Multiple Handlers + Separated" (authored at M4-S3 close). Scenario §4.11 dispatches `RegisterProxyBid` (single handler — keep `InvokeMessageAndWaitAsync`).
11. **`tests/CritterBids.Auctions.Tests/AuctionClosingSagaTests.cs`** — **pre-emptive fix** per the M4-S3 retro:
    - Switch `ListingWithdrawn_TerminatesWithoutEvaluation` from `InvokeMessageAndWaitAsync(new ListingWithdrawn(...))` to `SendMessageAndWaitAsync(new ListingWithdrawn(...))`. After S4 adds `ProxyBidDispatchHandler.Handle(ListingWithdrawn)`, `ListingWithdrawn` has two BC-local handlers and the invoke path would fall through to the empty default endpoint. Decision per Open Question 6 — including whether `BuyItNowPurchased_CompletesSaga` needs the same switch if S4 extends the proxy terminal set to include `BuyItNowPurchased` (Workshop §4.6–4.8 names only the three; Workshop §3.8 specifies BIN as terminal — the saga should arguably terminate on BIN too, but it's not listed in §4).
12. **`tests/CritterBids.Auctions.Tests/RealSellingProducerSagaTerminationTests.cs`** — verify the existing M4-S2 cross-BC integration test (Selling-produced `ListingWithdrawn` → Auctions saga termination) still passes once the Auctions BC has two `ListingWithdrawn` handlers. The test's dispatch shape may need the same `SendMessageAndWaitAsync` switch.
13. **`tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs`** *(verify only, not necessarily a change)* — no new discovery exclusions expected. The within-BC second handler on terminal events is the same shape S3 established for `BidPlaced`. Note in retro if anything surprising surfaces.
14. **`tests/CritterBids.Auctions.Tests/ParticipantCreditCeilingProjectionTests.cs`** *(new)* — one or two `[Fact]` methods verifying the projection's idempotency: first delivery upserts; redelivery preserves the existing row.
15. **`docs/skills/wolverine-sagas.md`** *(optional)* — append a §"M4-S4 learnings" subsection if and only if first-use surfaces something the skill does not predict: saga-to-saga cascade timing under `SendMessageAndWaitAsync` (Open Question 7), the corrected exhaustion-calc shape (Open Question 3), or `ParticipantCreditCeilingNotFoundException` retry-policy shape (Open Question 5). If nothing new surfaces, record "nothing new surfaced beyond what the skill already covers" in the retro per M3-S4b / M4-S2 / M4-S3 precedent.
16. **`docs/retrospectives/M4-S4-proxy-bid-manager-terminal-paths-retrospective.md`** — written last. Gate below.

## Explicitly out of scope

- Session aggregate, `CreateSession` / `AttachListingToSession` / `StartSession`, `SessionStarted → BiddingOpened` fan-out — S5.
- Listings BC catalog extension and `SessionMembershipHandler` — S6.
- Any modification to `AuctionClosingSaga.cs` *production code* (the test file changes in items 11–12 are the only `AuctionClosingSaga`-adjacent surface this slice touches). Byte-level diff on `src/CritterBids.Auctions/AuctionClosingSaga.cs` must be zero.
- Any modification to M4-S1 contract stubs (`RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`) or M4-S2 `Contracts.Selling.ListingWithdrawn`. Byte-level diff on `src/CritterBids.Contracts/**/*.cs` must be zero.
- `BuyItNowPurchased` proxy-termination handler (the proxy treats BIN as terminal in production reasoning but Workshop 002 §4.6–4.8 only lists the three close-style terminals; explicitly flag for post-MVP decision per Open Question 6, do not implement in S4).
- Proxy cancellation or modification after registration — not in Workshop 002 §4 per milestone doc §3 non-goals.
- HTTP endpoint for `RegisterProxyBid` — M6.
- Cross-BC publish route for `ProxyBidExhausted` — Relay is post-M5; the route is wired structurally only if Open Question 2 resolves to "pre-wire dormant route" (default: no pre-wiring; document the deferral in retro per the integration-messaging L2 discipline).
- Any modification to the M4-S5 `PublishedListings` projection precedent (M4-D4 first application, scheduled for S5). The `ParticipantCreditCeiling` projection is the second lived application of the same pattern; S4 implements it independently, S5 builds on the named pattern.
- Rewriting existing sections of `wolverine-sagas.md` or `marten-projections.md` — skill updates are append-only at retro time per M3 / M4-S1 / M4-S3 discipline.

## Conventions to pin or follow

Inherit all conventions from CLAUDE.md and prior milestones (M3-S5 / M4-S3 saga conventions, M4-S1 / M4-S2 contract and fixture conventions, M4-S3 dispatcher pattern + Send-vs-Invoke skill sections). New conventions introduced or pinned in this slice:

- **Bid increment math** — agent decides whether to extract to a shared helper now (`BidIncrement.For(decimal currentHighBid)`) or keep inline. Two existing copies (PlaceBidHandler.cs:174-175 and ProxyBidManagerSaga.cs's competing-bid branch); S4's exhaustion branch and scenario §4.10 introduce a third+ copy. Per CLAUDE.md's "three similar lines is better than a premature abstraction" rule, the threshold is in play. Pin the decision in retro.
- **Exhaustion calc formula** (per Open Question 3) — the workshop-corrected `min(competingBid + increment, MaxAmount, BidderCreditCeiling)`; exhaustion fires when that minimum is `<= competingBid`. Inline; no helper. Symmetric three-value cap reads naturally as a single expression.
- **Saga-to-saga cascade** (per Open Question 7) — first in-repo case where one saga's `OutgoingMessages` emission triggers another saga's reactive handler within a single message-processing cycle. The cascade is bounded (each PlaceBid emission triggers the dispatcher, which fans out to the other saga, which emits another PlaceBid or exhausts). Whichever timing shape S4's first run surfaces (eager in-cycle vs eventual via the bus), pin in the wolverine-sagas skill file at retro time.
- **Duplicate-projection second application** (M4-D4) — the `ParticipantCreditCeiling` projection. Same shape as the M4-S5 `PublishedListings` projection: subscribe to one upstream contract on a single dedicated queue, maintain a small Marten document keyed by the upstream entity's id, tolerant upsert on re-delivery. Recorded as a named pattern; ADR 014 (Cross-BC Read-Model Extension Shape, scheduled for M4-S6) covers a different pattern (read-model extension within Listings); the duplicate-projection pattern is separately documented in `docs/skills/marten-projections.md` at S5 close per milestone doc §"Retrospective skills work".
- **Pre-emptive Send-vs-Invoke fix** — the existing `AuctionClosingSagaTests` tests for `ListingWithdrawn` / `BuyItNowPurchased` must switch from `InvokeMessageAndWaitAsync` to `SendMessageAndWaitAsync` *before* the dispatcher adds the second handler, not after. The two changes (test fix + dispatcher extension) should land in adjacent commits so reviewers can see the cause-effect pairing.
- **`Saga.NotFound(X)` static method** — terminal handlers may receive `ListingSold` / `ListingPassed` / `ListingWithdrawn` after a saga has already terminated (e.g., the same listing closes after a proxy hits exhaustion). The wrapped commands (`ProxyListingClosedObserved` or its split variants per Open Question 1) need the static `NotFound` absorber on `ProxyBidManagerSaga`. Pattern symmetric with `AuctionClosingSaga.NotFound(CloseAuction)` at line 146.
- **`ParticipantCreditCeiling` queue naming** — `auctions-participants-events` (per the existing `settlement-participants-events` naming convention from M5-S5).

## Commit sequence (proposed)

1. `feat(auctions): ParticipantCreditCeiling projection + handler + Marten schema + queue wiring` — items 4, 5, 6 (projection portion), 7
2. `refactor(auctions): StartProxyBidManagerSagaHandler loads BidderCreditCeiling from projection` — item 8 + 9 (retry policy)
3. `test(auctions): switch ListingWithdrawn/BuyItNowPurchased AuctionClosingSaga tests to SendMessageAndWaitAsync` — item 11 (pre-emptive fix, landed before the dispatcher extension)
4. `feat(auctions): ProxyBidDispatchHandler dispatches terminal events to active proxy sagas` — item 2 + 3 (wrapped command type(s))
5. `feat(auctions): ProxyBidManagerSaga terminal handlers (ListingSold / ListingPassed / ListingWithdrawn) + NotFound absorbers` — item 1 (terminal-handler portion) + scenarios 4.6 / 4.7 / 4.8 tests
6. `feat(auctions): ProxyBidManagerSaga exhaustion branch + ProxyBidExhausted emission` — item 1 (exhaustion-branch portion) + scenarios 4.3 / 4.9 tests
7. `test(auctions): two-proxy bidding war (scenario 4.10)` — scenario 4.10 test (saga-to-saga cascade)
8. `test(auctions): register-while-outbid (scenario 4.11)` — scenario 4.11 test
9. `test(auctions): ParticipantCreditCeiling projection idempotency` — item 14
10. *(optional)* `docs(skills): append M4-S4 learnings to wolverine-sagas.md` — item 15, only if something new surfaced
11. `docs: write M4-S4 retrospective` — item 16

## Acceptance criteria

- [ ] `dotnet build` — 0 errors, 0 new warnings beyond the pre-existing 24 NU1904 NuGet warnings (Marten)
- [ ] `dotnet test` — 125-test baseline preserved (existing 120 + 5 S3-shipped); +7 new saga scenario tests + 1-2 projection tests + 0 to 1 pre-emptive-fix verification updates (replacing existing assertions in `AuctionClosingSagaTests`, not adding tests); zero skipped, zero failing; **total 133-134** (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + **50-51 Auctions**)
- [ ] `src/CritterBids.Auctions/ProxyBidManagerSaga.cs` — exhaustion branch landed in `Handle(ProxyBidObserved)` competing-bid path; three terminal handlers added (`ListingSold` / `ListingPassed` / `ListingWithdrawn` wrappers per OQ1); each terminal handler calls `MarkCompleted()` and sets `Status = ListingClosed`; static `NotFound(X)` absorbers added for each terminal wrapped command
- [ ] `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs` — three new `Handle(X)` methods (`ListingSold` / `ListingPassed` / `ListingWithdrawn`), each fanning out to active sagas via `Query<ProxyBidManagerSaga>().Where(...)`
- [ ] `src/CritterBids.Auctions/ParticipantCreditCeiling.cs` + `ParticipantCreditCeilingHandler.cs` exist; schema registered in `AuctionsModule.ConfigureMarten`
- [ ] `src/CritterBids.Api/Program.cs` — one new `PublishMessage<ParticipantSessionStarted>().ToRabbitQueue("auctions-participants-events")` + paired `ListenToRabbitQueue` rule
- [ ] `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs` — loads `ParticipantCreditCeiling` and populates `saga.BidderCreditCeiling`; throws `ParticipantCreditCeilingNotFoundException` on miss; the existing existence-check guard unchanged
- [ ] `AuctionsConcurrencyRetryPolicies` (or sibling) — retry rule for `ParticipantCreditCeilingNotFoundException`
- [ ] All 7 saga scenario test methods in `ProxyBidManagerSagaTests.cs` named exactly per milestone doc §7 §4 and green
- [ ] Scenario 4.10 (`TwoProxies_WeakerExhausts_StrongerWins`) verifies both proxies cascade-react within the tracked session, weaker exhausts at exactly the documented amount ($46 per workshop example), stronger remains Active and wins
- [ ] Scenario 4.11 (`RegisterProxy_WhileOutbid_WaitsForNextCompetingBid`) verifies registration alone produces no `PlaceBid` emission — only `ProxyBidRegistered`
- [ ] `tests/CritterBids.Auctions.Tests/AuctionClosingSagaTests.cs` — `ListingWithdrawn_TerminatesWithoutEvaluation` (and `BuyItNowPurchased_CompletesSaga` if OQ6 resolves to inclusion) switched from `InvokeMessageAndWaitAsync` to `SendMessageAndWaitAsync`; existing assertions otherwise unchanged
- [ ] `src/CritterBids.Auctions/AuctionClosingSaga.cs`, `PlaceBidHandler.cs`, `BuyNowHandler.cs`, `BidConsistencyState.cs`, `Listing.cs` — production code unchanged (byte-level diff zero)
- [ ] `src/CritterBids.Contracts/**/*.cs` — unchanged (byte-level diff zero)
- [ ] No `[Obsolete]`, no `#pragma warning disable`, no `throw new NotImplementedException()` in production code
- [ ] `docs/retrospectives/M4-S4-proxy-bid-manager-terminal-paths-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test` shows 133-134 passing and `dotnet build` shows no new warnings beyond the pre-existing 24 NU1904.

Retrospective content requirements:

- Baseline numbers (125 before, 133-134 after) with a phase table matching M4-S3 retro shape
- Per-item status table mirroring the "In scope (numbered)" list with commit hashes
- Each of the seven Open Questions answered with which path was taken and why; for OQ3 (exhaustion calc), confirm whether the workshop-corrected formula or any session-time refinement was used; for OQ7 (saga-to-saga cascade), include observed timing/behavior from the first-run scenario 4.10 test
- Whether the skill append in item 15 was written; if so, the appended sections listed; if not, an explicit "nothing new surfaced beyond what the skill already covers" observation
- Whether the bid-increment helper was extracted; if so, the file path; if not, an explicit "kept inline per CLAUDE.md threshold rule" with the count of inline copies
- Any blocker encountered — verbatim error message, root cause, fix path — with particular attention to:
  - Saga-to-saga cascade timing under `SendMessageAndWaitAsync` (scenario 4.10)
  - `ParticipantCreditCeiling` projection race condition at saga start (the retry policy is the defense; verify it fires when the projection hasn't caught up)
  - Any new fixture exclusion required (none expected — flag if encountered)
- A **"What M4-S5 should know"** section covering at minimum:
  - Auctions BC test count at S4 close (50-51, up from 42)
  - Duplicate-projection pattern lived twice in M4 (ParticipantCreditCeiling at S4, PublishedListings at S5) — S5 inherits a fully-shipped reference implementation
  - Whether ADR 014 (Cross-BC Read-Model Extension Shape) needs an early-draft author hint at S5 open or stays at S6 per milestone doc §8 M4-D3
  - Any `auctions-participants-events` queue surprises that may affect S5's `auctions-selling-events` extension for `ListingPublished` consumption
  - Bidding-war timing convention (eager vs eventual) — informs whether S5's `SessionStarted → BiddingOpened` fan-out handler can rely on the same cascade timing

## Open questions (pre-mortems — flag, do not guess)

1. **Terminal wrapped-command shape — one polymorphic command or three separate.** The dispatcher needs to deliver a wrapped command per terminal event with the resolved `SagaId`. Two paths:

   - **Path A (recommended for symmetry with `ProxyBidObserved`):** three separate commands `ProxyListingSoldObserved` / `ProxyListingPassedObserved` / `ProxyListingWithdrawnObserved`. Each saga handler matches one terminal type. More files (three new records vs one) but each handler is straightforward and the dispatcher's three `Handle(X)` methods each emit their specific wrapped type.

   - **Path B:** one polymorphic `ProxyListingClosedObserved(Guid SagaId, ListingClosedReason Reason, ...)` with a discriminator enum. Fewer files. One saga handler branches on the reason. Adds a new enum type and makes the saga's terminal branch slightly more complex.

   Flag in retro which path landed and why. Path A matches the M4-S3 `ProxyBidObserved` precedent; Path B trades fewer types for slightly less symmetric handler shape. If Path B is chosen, the enum lives in `CritterBids.Auctions/` (not Contracts — the wrapping is BC-internal).

2. **`ProxyBidExhausted` emission shape — bus-only or audit-stream.** Same shape question that landed at M4-S3 OQ3 for `ProxyBidRegistered`. M4-S3 resolved to bus-only (Path a — emit via `OutgoingMessages`, no `AddEventType`, asserted via `tracked.NoRoutes`). Recommended: same path here for consistency. Flag if the saga's terminal-state audit becomes more compelling — e.g., if the M4-S6 Operations BC "active proxies count" surface needs a historical audit. The cross-BC consumer is still post-M5 Relay; the route is dormant.

3. **Exhaustion calc — verify the workshop-corrected formula.** Workshop 002 §4.9 contains both a wrong-first-pass example and a self-corrected formula:

   - **Formula:** `nextBid = min(competingBid + increment, MaxAmount, BidderCreditCeiling)`. Exhaustion fires when `nextBid <= competingBid`. The proxy's auto-bid amount is the unmodified `nextBid` (capped by all three) when it does fire.

   The S3 implementation only checked `nextBid > MaxAmount`. S4's expansion must consult three values. First-run verification on scenario 4.9 will confirm whether the formula matches expected behaviour. Flag if any edge case surfaces (e.g., what if `BidderCreditCeiling == 0m` — the S3 default — and the saga somehow reaches the competing-bid branch before the credit ceiling lookup populates? The retry policy in item 9 should prevent this, but verify).

4. **`BidderId` ↔ `ParticipantId` identity mapping.** The `Contracts.Participants.ParticipantSessionStarted` contract carries `ParticipantId: Guid` (the Participants-side aggregate id) and `BidderId: string` (the display correlation like "Bidder 4217"). The `ProxyBidManagerSaga.BidderId: Guid` and the `RegisterProxyBid.BidderId: Guid` are both `Guid` — they refer to the `ParticipantId`, not the display-string `BidderId`. Verify at session open by reading `ParticipantSessionStarted.cs` and confirming the convention. The `ParticipantCreditCeiling` projection's key is the `Guid ParticipantId`.

5. **`ParticipantCreditCeilingNotFoundException` retry policy shape.** Mirrors the M5-S4 `PendingSettlementNotFoundException` + `SettlementsConcurrencyRetryPolicies` pattern: a custom retryable exception thrown by the start handler, a Wolverine `OnException<T>().RetryWithCooldown(...)` policy registered as an `IWolverineExtension` in the BC's module. The S4 work:

   - Author `ParticipantCreditCeilingNotFoundException` (sibling to `InvalidSettlementTransitionException` from M5-S4 — sealed Exception subclass with `BidderId` carried for diagnostic context)
   - Add the retry rule to `AuctionsConcurrencyRetryPolicies` alongside the existing `ConcurrencyException` and `DcbConcurrencyException` entries — three entries total
   - Verify the rule fires by writing a test that dispatches `RegisterProxyBid` before the `ParticipantCreditCeiling` projection has caught up; the retry should re-queue and succeed once the projection catches up. (Or the test can seed the projection directly to bypass the race — agent decides; the production race is the real concern, the test is verification.)

6. **Pre-emptive `AuctionClosingSagaTests` fix — scope.** The M4-S3 retro names `ListingWithdrawn_TerminatesWithoutEvaluation` as the test that will regress when S4 adds `ProxyBidDispatchHandler.Handle(ListingWithdrawn)`. Two sub-questions:

   - **Sub-question 6a:** Does the proxy saga also need a `BuyItNowPurchased` terminal handler? Workshop §4.6–4.8 names only the three close-style terminals; Workshop §3.8 specifies BIN as terminal for the auction-closing saga. If the proxy doesn't terminate on `BuyItNowPurchased`, an exhausted/active proxy survives a BIN closure and is orphaned (no future bids, no terminal trigger; the saga document leaks). Two paths:
     - **Path A:** add `BuyItNowPurchased` as a fourth terminal — symmetric with `AuctionClosingSaga`'s set, prevents orphan sagas. Workshop §4 doesn't mandate it but the Workshop §3.8 cross-cut justifies it.
     - **Path B:** defer to post-MVP — the orphan saga isn't observably harmful in the M6 demo; flag in retro.
     Path A is recommended (one more handler is cheap and prevents a real bug); confirm or override at session open.

   - **Sub-question 6b:** Does `RealSellingProducerSagaTerminationTests` (M4-S2) need the same `SendMessageAndWaitAsync` switch? It uses a self-hosted Alba runtime registering both `AddSellingModule()` and `AddAuctionsModule()` — multi-BC. Verify; the fix shape is identical if it does.

7. **Two-proxy bidding war (scenario 4.10) — cascade timing.** Workshop §4.10 asserts: "The escalation happens within a single message processing cycle (each BidPlaced triggers the other proxy), so it completes in milliseconds." First-run question: does `SendMessageAndWaitAsync` (per the M4-S3 send-vs-invoke pattern) wait for the full cascade, or does it return after the first dispatch and require additional waits? The test's assertion shape depends on the answer:

   - **If eager (single-cycle):** the test dispatches one initial `BidPlaced` (proxy-003 firing first) and asserts the final state — proxy-003 exhausted, proxy-002 active at the workshop-documented winning amount. Single `tracked.Sent.MessagesOf<PlaceBid>()` call lists all the cascading bids.
   - **If eventual (across cycles):** the test needs polling or multiple `SendMessageAndWaitAsync` calls to drive the cascade, with a timeout-based assertion shape.

   Flag in retro with which timing the first-run revealed. Either resolves naturally; the test shape adapts. The bigger concern: if cascade timing surprises (e.g., one proxy's `PlaceBid` emission triggers a `BidPlaced` that the dispatcher fans out, but the fan-out doesn't reach the other saga within the cycle), the test may need redesign. Halt and consult if this surfaces.

8. **Marten projection register order — `ParticipantCreditCeiling` document vs `ParticipantSessionStarted` event type.** The projection consumes the contract event; the saga document and the contract event are separate Marten registrations. Confirm at session open that registering the document via `Schema.For<ParticipantCreditCeiling>()` does not require a corresponding `AddEventType<ParticipantSessionStarted>()` — events consumed via Wolverine handlers (the projection's `Handle(ParticipantSessionStarted)`) are not stored in Marten and don't need event-type registration. The M5-S3 `PendingSettlement` projection (consumes `ListingPublished` / `ListingPassed` / `ListingWithdrawn` / `SettlementCompleted` / `PaymentFailed`) is the parallel pattern; check `src/CritterBids.Settlement/SettlementModule.cs` for the registration shape to confirm.

---

## Session sizing notes

- **S4 is the second-largest M4 session** alongside S3 per the milestone doc §9. Seven scenario tests, three new wrapped command types (if OQ1 Path A), a new projection + handler + queue wiring, a custom exception + retry policy, modifications to two existing files (saga + dispatcher), and the pre-emptive AuctionClosingSagaTests fix. The surface count is in the upper half of CritterBids session sizes.
- **S4b split slot pre-drafted** per milestone doc §9. Trigger conditions:
  - Scenario 4.10 (`TwoProxies_WeakerExhausts_StrongerWins`) reveals saga-to-saga cascade behavior that requires non-trivial test infrastructure (e.g., custom waiters, multi-cycle dispatch); OR
  - Acceptance criteria approaching or exceeding 14 items at session midpoint; OR
  - The `ParticipantCreditCeiling` race condition (OQ5) requires more wiring than the M5-S4 `PendingSettlementNotFoundException` pattern can absorb.
  Candidate split boundary: §4.10 + §4.11 → S4b. Base S4 covers §4.3, §4.6, §4.7, §4.8, §4.9 (five scenarios) plus all the infrastructure (projection, queue, dispatcher extension, terminal handlers, exhaustion branch). S4b absorbs the two complex remaining scenarios with cleaner test infrastructure.
- **No further split slots after S4b.** If S4 + S4b both overflow, the M4 retrospective decides whether to defer scenarios to post-M5 or open a new slot at M5 boundary.

## Document history

- **v0.1** (2026-05-19): Authored at the close of M4-S3 per the retro's "What M4-S4 should know" handoff payload. The seven Open Questions are framed by S3's lived discoveries (Path C dispatcher, Send-vs-Invoke under Separated mode, deferred BidderCreditCeiling lookup). The S4b split slot is named explicitly per milestone doc §9.
