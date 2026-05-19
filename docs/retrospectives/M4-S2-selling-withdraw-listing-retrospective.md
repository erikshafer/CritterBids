# M4-S2: Selling `WithdrawListing` Command + Real `ListingWithdrawn` Producer — Retrospective

**Date:** 2026-05-18
**Milestone:** M4 — Auctions BC Completion
**Slice:** S2 of 7 (plus pre-drafted S4b and S5b split slots)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M4-S2-selling-withdraw-listing.md`
**Baseline:** 115 tests passing (post-M5) · `dotnet build` 0 errors, 0 warnings · M4-S1 complete

---

## Baseline

- 115 tests passing at session open (1 Api + 1 Contracts + 6 Participants + 14 Listings + 32 Selling + 25 Settlement + 36 Auctions per M5 retro)
- `dotnet build` — 0 errors, 24 pre-existing NU1904 Marten vulnerability warnings (unchanged through M5)
- `src/CritterBids.Selling/` has no `WithdrawListing.cs` and no Selling-internal `ListingWithdrawn.cs`; `SellerListing` lacks an `Apply(ListingWithdrawn)`; `SellingModule` registers six event types
- `src/CritterBids.Api/Program.cs` line 81-82 already wires `Contracts.Selling.ListingWithdrawn → settlement-selling-events` from M5-S3 (pre-wired ahead of M4-S2 with explanatory comment)
- `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs` excludes Settlement BC handler discovery; does not exclude Auctions or Listings BC handlers
- The M3-S5b `AuctionsTestFixture.AppendListingWithdrawnAsync` still hand-synthesizes `Contracts.Selling.ListingWithdrawn` for scenario 3.10

---

## Items completed

| Item | Description |
|------|-------------|
| S2k | M4-S2 prompt refreshed for M5-induced drift: baseline 86 → 115; M5-S3 settlement-selling-events front-loading noted; expected post-session count 90-91 → 119-120 |
| S2a | `src/CritterBids.Selling/WithdrawListing.cs` — sealed record `(ListingId, WithdrawnBy)`; static `WithdrawListingHandler.Handle` returns `(Events, OutgoingMessages)` with `[WriteAggregate(nameof(...))]` on the loaded `SellerListing`; throws `InvalidListingStateException` if not Published; `using ContractListingWithdrawn = ...` alias mirrors `SubmitListing.cs` line 3 |
| S2b | `src/CritterBids.Selling/ListingWithdrawn.cs` — Selling-internal sealed record `(ListingId, WithdrawnAt)`; distinct CLR type from the `CritterBids.Contracts.Selling.ListingWithdrawn` contract |
| S2c | `SellerListing.Apply(ListingWithdrawn)` — one-line transition `Status = ListingStatus.Withdrawn`; no `WithdrawnAt`/`WithdrawnBy` writes (audit data lives on the stream) |
| S2d | `SellingModule.ConfigureMarten` — added `opts.Events.AddEventType<ListingWithdrawn>()` alongside the existing six |
| S2e | `src/CritterBids.Api/Program.cs` — two new `opts.PublishMessage<Contracts.Selling.ListingWithdrawn>().ToRabbitQueue(...)` rules for `auctions-selling-events` and `listings-selling-events`, placed next to the existing Selling `ListingPublished` fan-out with a comment pointing to the M5-S3 block below |
| S2f | `tests/CritterBids.Selling.Tests/WithdrawListingTests.cs` — three `[Fact]` methods: `WithdrawListing_Published_ProducesListingWithdrawn` (happy path), `WithdrawListing_NotPublished_Rejected` (Draft state), `WithdrawListing_AlreadyWithdrawn_Rejected` (Withdrawn state) |
| S2g | `tests/CritterBids.Selling.Tests/WithdrawListingDispatchTests.cs` — one `[Fact]` method dispatching via `IMessageBus` through `SellingTestFixture`; asserts aggregate transition to `Withdrawn` AND `tracked.NoRoutes` contains the outbound contract event (per M5-S6 retro Key Learning #1 on fixture-stance routing assertions) |
| S2h | `tests/CritterBids.Auctions.Tests/RealSellingProducerSagaTerminationTests.cs` — new test class with its own self-hosted Alba runtime registering both `AddSellingModule()` and `AddAuctionsModule()` so local routing carries `Contracts.Selling.ListingWithdrawn` from Selling's handler to the Auctions saga handler in-process; asserts saga deletion, `SellerListing.Status = Withdrawn`, and absence of `BiddingClosed`/`ListingSold`/`ListingPassed` outcomes |
| S2i | `AuctionsTestFixture.AppendListingWithdrawnAsync` docstring updated — now names the M4-S2 real producer, marks the helper a unit-test shortcut, and points to the new integration test; behaviour unchanged |
| S2j | **Scope deviation:** `SellingTestFixture` extended with `AuctionsBcDiscoveryExclusion` and `ListingsBcDiscoveryExclusion` mirroring the Settlement-fixture pattern. The Auctions exclusion is load-bearing (without it, `AuctionClosingSaga.Handle(ListingWithdrawn)` throws `UnknownSagaException` in the dispatch test); the Listings exclusion is pre-emptive consistency |
| S2l | This retrospective |

Commit mapping:

| Commit | Items covered |
|--------|---------------|
| 0 — `docs(m4-s2)` prompt refresh | S2k |
| 1 — `feat(selling)` command + handler + domain event | S2a, S2b, S2c, S2d |
| 2 — `feat(api)` routing rules | S2e |
| 3 — `test(selling)` handler + dispatch tests + fixture exclusions | S2f, S2g, S2j |
| 4 — `test(auctions)` integration test + fixture docstring + retro | S2h, S2i, S2l |

---

## S2a — `WithdrawListing` command and handler

### Why two-namespace `ListingWithdrawn`

Selling needs a *domain* event on the `SellerListing` stream (for replay) and a *contract* event for cross-BC dispatch. They share a CLR name but live in different namespaces (`CritterBids.Selling` vs `CritterBids.Contracts.Selling`). The handler file uses `using ContractListingWithdrawn = CritterBids.Contracts.Selling.ListingWithdrawn` to disambiguate — same shape as `SubmitListing.cs:3`'s `ContractListingPublished` alias. The aggregate's `Apply(ListingWithdrawn)` resolves to the Selling-internal type by namespace; the handler's `OutgoingMessages.Add(new ContractListingWithdrawn(...))` reaches the contract.

The minimal-replay-payload domain event carries only `(ListingId, WithdrawnAt)` — the `WithdrawnBy`/`Reason` fields are audit data on the contract event and not consumed by the aggregate's `Apply`. Per `wolverine-message-handlers.md` and the M2 precedent.

### Why one exception type, not a new one

`InvalidListingStateException` (defined at `UpdateDraftListing.cs:18`) is the established Selling-side state-guard exception; `SubmitListingHandler` throws the same type for the same kind of guard. Inventing a parallel `CannotWithdrawListingException` would split error handling for callers without changing behaviour.

### Why `WithdrawnAt` at handler entry, not outbox dispatch

One `DateTimeOffset.UtcNow` at handler entry is reused for both the domain event's stamp and the contract event's `WithdrawnAt`. The contract docstring at `Contracts/Selling/ListingWithdrawn.cs:58` pins this rationale — outbox-dispatch time would drift from the moment the seller actually requested withdrawal, which is the audit-relevant instant.

---

## S2e — Program.cs routing — two rules, not three

### Discovery on session open

The M4-S2 prompt as originally drafted (2026-04-20) named two routing rules. The prompt's framing was correct, but the *reason* was different in 2026-04-20 (only two consumers existed: Auctions and Listings) than in 2026-05-18 (three consumers exist, but M5-S3 already wired the third route in anticipation of M4-S2 landing). The session-open prompt refresh (Commit 0) added a "M5 drift note" to the prompt explaining why the change shape stays at two rules.

Program.cs:81-82 (M5-S3 commit) reads:
```
opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>()
    .ToRabbitQueue("settlement-selling-events");
```
with a comment ending: "Selling's own publisher is deferred per M3 §3, but the queue is in place for when it lands." That landed today. M4-S2 adds the other two rules with an inline comment pointing readers down to the M5-S3 block.

---

## S2j — Selling fixture exclusion expansion (scope deviation)

### What surfaced

The new dispatch test failed on first run with:

```
System.AggregateException : One or more errors occurred. (Could not find an
expected saga document of type CritterBids.Auctions.AuctionClosingSaga for id
'019e3e27-7e4d-72a3-809e-654422ee34b9'. Note: new Sagas will not be available
in storage until the first message succeeds.)
---- Wolverine.Persistence.Sagas.UnknownSagaException ...
at Internal.Generated.WolverineHandlers.ListingWithdrawnHandler1352858020.HandleAsync
```

The Auctions `AuctionClosingSaga.Handle(ListingWithdrawn)` (the M3-S5b saga handler) was discovered in the Selling fixture's Wolverine handler scan and tried to load a saga document that doesn't exist in the Selling-only Postgres schema.

### Root cause and fix

`Program.cs:30` includes `typeof(Listing).Assembly` in handler discovery for the production host. The `SellingTestFixture` runs `Program` and therefore inherits the same discovery, so foreign-BC handlers are loaded unless explicitly excluded. The Settlement exclusion existed (M5-S3 added it for the same reason); Auctions and Listings did not.

Fix: add `AuctionsBcDiscoveryExclusion` and `ListingsBcDiscoveryExclusion` to the Selling fixture, mirroring the Settlement-fixture posture exactly (per `tests/CritterBids.Settlement.Tests/Fixtures/SettlementTestFixture.cs:80-86`). The Auctions exclusion is load-bearing; the Listings exclusion is pre-emptive consistency for when Listings consumes `ListingWithdrawn` at M4-S6.

### Why this isn't a per-test workaround

The exclusions live on the shared `SellingTestFixture`, not on the failing dispatch test class. The fixture should reflect the same cross-BC isolation posture the Settlement fixture already adopts — anything less leaves the next Selling-side producer addition susceptible to the same `UnknownSagaException` surprise. Per project memory `project_cross_bc_handler_isolation.md`: shared event types with handlers in two BCs need a `*BcDiscoveryExclusion` in each foreign fixture.

---

## S2h — Cross-BC integration test fixture

### Why a new self-hosted fixture, not reuse of `AuctionsTestFixture`

The Auctions fixture excludes Selling BC handlers (and `ISellerRegistrationService` is not registered) so it cannot host the `WithdrawListing` dispatch. The Selling fixture (after S2j) excludes Auctions handlers so it cannot observe the saga termination. Reusing either would require dropping a load-bearing exclusion and ripple-breaking all other tests in that project.

The prompt's open questions §"Cross-BC integration test transport choice" anticipated this; option (a) was "a shared fixture that does not disable external Wolverine transports" and option (b) was "an Alba composition-root test at the API layer." Option (a) is what landed — a *single*-test self-hosted fixture (no shared Testcontainers cost across tests) that registers both `AddSellingModule()` and `AddAuctionsModule()` but keeps external transports disabled. Wolverine's local routing carries the contract event from Selling's handler to the Auctions saga handler in-process, which is exactly the production path with the network hop stubbed out.

### Why `tracked.Sent` *and* `tracked.NoRoutes` assertions

The cross-BC integration test uses `_host.TrackActivity().ExecuteAndWaitAsync(ctx => ctx.InvokeAsync(new WithdrawListing(...)))` rather than `InvokeMessageAndWaitAsync`. Both buckets are checked for the three outcome events to belt-and-suspender the "no closing outcomes on withdrawal" assertion — either bucket gaining one of those messages would be a regression. Same pattern as `AuctionClosingSagaTests.ListingWithdrawn_TerminatesWithoutEvaluation`'s `tracked.NoRoutes` assertions extended to also check `tracked.Sent`.

### Test cost

Approximately 6 seconds for Postgres container startup + Alba host bootstrap + the actual test. Acceptable cost for a single integration test; not worth a shared collection fixture given there is exactly one cross-BC scenario at this scope.

---

## Test results

| Phase | Total | Δ from baseline | Result |
|-------|-------|-----------------|--------|
| Baseline (M5 close) | 115 | — | ✅ (1 Api + 1 Contracts + 6 Participants + 14 Listings + 32 Selling + 25 Settlement + 36 Auctions) |
| After S2a-S2d (production code only) | 115 | 0 | ✅ build clean |
| After S2e (routing rules) | 115 | 0 | ✅ build clean |
| After S2f, S2g (Selling tests, pre-fixture-fix) | 118 | +3 unit / **−1 fail** dispatch | ❌ `UnknownSagaException` from cross-BC handler discovery |
| After S2j (fixture exclusions) | 119 | +4 (3 unit + 1 dispatch) | ✅ Selling project at 36 |
| After S2h (cross-BC integration test) | 120 | +5 (4 Selling + 1 Auctions) | ✅ Auctions project at 37 |

Final: **120 passing** (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 37 Auctions). Matches prompt's "120 total" projection exactly.

---

## Build state at session close

- `dotnet build`: 0 errors, 24 NU1904 warnings (unchanged from M5 baseline)
- `dotnet test`: 120 passing, 0 failing, 0 skipped
- `CritterBids.Contracts.Selling.ListingWithdrawn` producers: 1 (was 0 from real code, 2 from fixture synthesis)
- `CritterBids.Selling.ListingWithdrawn` domain-event references: 4 (record def, `SellerListing.Apply`, `SellingModule.AddEventType`, `WithdrawListingTests` build)
- `WithdrawListing` command call sites: 2 (the two new test files)
- Files touching `CritterBids.Auctions/*.cs` other than test files: 0 — M4-S2 did not modify Auctions BC code per non-goal
- Files touching `CritterBids.Contracts/**`: 0 — contracts final at M4-S1 close
- New ADRs: 0 (no ADR 014, no ADR 015 trigger)
- Marten event-type registrations on `SellerListing` stream: 7 (was 6; +ListingWithdrawn)

---

## Key learnings

1. **Cross-BC handler discovery is fixture-stance not test-stance.** When a Selling-side producer is added for an event that has handlers in another BC, the Selling test fixture immediately gains responsibility for excluding that BC's handlers — not because the new test is special, but because the fixture's posture toward foreign-BC handler discovery now applies to a wider event set. The Settlement-fixture pattern (`Selling+Auctions+Listings` exclusions) is the right default for any single-BC fixture; the Selling fixture had been one exclusion short of that standard and only the M4-S2 producer surfaced the gap.

2. **Pre-wired routes in anticipation of a future producer are a cheap but powerful invariant.** M5-S3 added `Contracts.Selling.ListingWithdrawn → settlement-selling-events` in its routing block with a comment naming the as-yet-absent Selling producer. That route sat dormant for ~30 days then fired automatically when M4-S2 landed the producer — no follow-up Settlement-side commit required. Pattern candidate for downstream BC additions: when a future consumer needs an upstream contract, wire the route at consumer-authoring time even if the producer is months away.

3. **`tracked.Sent` vs `tracked.NoRoutes` is fixture-stance plus dispatch-method.** The M5-S6 retro Key Learning #1 framed this as a fixture-stance question; M4-S2's dispatch test reinforces it. With `DisableAllExternalWolverineTransports()`, outbound external-routed messages always land on `NoRoutes`. The dispatch test's `tracked.NoRoutes.MessagesOf<ContractListingWithdrawn>().ShouldHaveSingleItem()` is the same assertion shape M5-S6 settled on.

4. **A single-test self-hosted Alba fixture is a viable cross-BC integration pattern.** When neither BC's existing fixture can host the round-trip (one excludes the other), spinning up a dedicated `IAsyncLifetime` host for one test costs ~6s of Postgres startup but avoids both a shared-fixture refactor and any real-RabbitMQ Testcontainers cost. Use when the cross-BC scenario is genuinely one test, not a class of tests. For three or more such tests, extract the fixture.

5. **Prompt drift after intervening milestones is real and small.** The M4-S2 prompt was authored 2026-04-20 with an 86-test baseline. M5 shipped six slices between then and 2026-05-18. The actual drift to fix was three lines (baseline, settlement-route note, expected count) — most of the prompt's shape held. The session-open habit of "re-read the prompt against the current code state before executing" caught it; treating the prompt as immutable from authorship would have surfaced the same friction mid-session as M5-S6's Key Learning #1 surprise.

---

## Findings against narrative

The M4-S2 prompt does not anchor to a narrative — the `Narrative:` metadata line was added across the prompt template at foundation-refresh Phase 5, and the M4-S1 / M4-S2 prompts predate that practice. The slice implements scope from `docs/milestones/M4-auctions-bc-completion.md` §2 "Selling BC — `WithdrawListing` command" and §7 "`CritterBids.Selling.Tests` (S2)"; no narrative covers the seller-side withdrawal journey today.

If a future narrative is authored covering seller-initiated withdrawal (a candidate post-MVP narrative slot — "seller withdraws a published listing"), it would consume the M4-S2 producer as its dramatized moment. **Follow-up:** no immediate narrative-authoring prompt warranted; the seller-side journeys at narratives 003 (anonymous-session) and 004 (publish-and-withdraw) already dramatize publication. Narrative 004 may extend to cover the production-path producer at a future cleanup pass — flagged but not actioned in M4-S2.

---

## Verification checklist

- [x] `src/CritterBids.Selling/WithdrawListing.cs` — new file; sealed record + static handler returning `(Events, OutgoingMessages)`, guarded on Published status
- [x] `src/CritterBids.Selling/ListingWithdrawn.cs` — new file; Selling-internal sealed record `(ListingId, WithdrawnAt)`
- [x] `src/CritterBids.Selling/SellerListing.cs` — new `Apply(ListingWithdrawn)` setting `Status = Withdrawn`
- [x] `src/CritterBids.Selling/SellingModule.cs` — `opts.Events.AddEventType<ListingWithdrawn>()` added
- [x] `src/CritterBids.Api/Program.cs` — two new `PublishMessage<...ListingWithdrawn>().ToRabbitQueue(...)` lines for `auctions-selling-events` and `listings-selling-events`; no listener changes; no other Program.cs edits
- [x] `tests/CritterBids.Selling.Tests/WithdrawListingTests.cs` — three `[Fact]` methods (happy path, reject-not-published, reject-already-withdrawn)
- [x] `tests/CritterBids.Selling.Tests/WithdrawListingDispatchTests.cs` — one `[Fact]` method through `IMessageBus`, asserting aggregate transition + `tracked.NoRoutes` for the outbound contract event
- [x] `tests/CritterBids.Auctions.Tests/RealSellingProducerSagaTerminationTests.cs` — one new test class with self-hosted combined-BC fixture; drives saga terminal path via the real Selling producer
- [x] `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — `AppendListingWithdrawnAsync` docstring updated to mark unit-test shortcut and point to the integration test; behaviour unchanged
- [x] `dotnet build` — 0 errors, 24 NU1904 warnings (unchanged from M5)
- [x] `dotnet test` — 120 passing (1 + 1 + 6 + 14 + 36 + 25 + 37)
- [x] `docs/retrospectives/M4-S2-selling-withdraw-listing-retrospective.md` — this document

---

## Scope deviations

1. **`SellingTestFixture` cross-BC exclusion expansion** — added `AuctionsBcDiscoveryExclusion` and `ListingsBcDiscoveryExclusion` mirroring the Settlement-fixture posture. Necessary consequence of the new producer firing events that have foreign-BC saga consumers; documented at S2j above. No production code touched.
2. **Prompt amendment commit** — three minor edits to `docs/prompts/implementations/M4-S2-selling-withdraw-listing.md` (baseline count, M5 drift note, expected count) committed as Commit 0 before the implementation work. Per AUTHORING.md rule 10 — prompts are refreshed before the session runs, not amended mid-session.

No production-code scope creep beyond the prompt. No new ADRs. No contract changes. No Auctions BC code modified (only Auctions-side test code).

---

## What remains / next session should verify

- **M4-S3** (Proxy Bid Manager saga authoring foundations) is the next slice. With M4-S2 landed, the `Contracts.Selling.ListingWithdrawn` real producer flows to the Proxy saga's terminal handler at M4-S4 without any further plumbing — scenario 4.8 (`ListingWithdrawn_CompletesSaga`) can exercise the real producer when it's authored.
- **Free M5 coverage** — Settlement's `PendingSettlementHandler.Handle(ListingWithdrawn)` (the `PendingSettlement → Expired` transition from M5-S3) is now real-producer-driven for the first time in production. The Settlement tests still synthesize the event for unit-level coverage, but the integration path through `settlement-selling-events` is no longer hypothetical. No Settlement-side change required.
- **Cross-BC integration test pattern** — the self-hosted single-test fixture in `RealSellingProducerSagaTerminationTests.cs` is the second instance of "two-BC Wolverine runtime in one test" (after the various M5 settlement integration tests, which mostly used the Settlement fixture with seeded upstream events). If M4-S6 or M5-related future work needs another such test, consider whether the pattern earns a shared `MultiBcTestFixture` extraction.
- **Listings consumption of `ListingWithdrawn`** — deferred to M4-S6 per scope. The new `ListingsBcDiscoveryExclusion` in the Selling fixture pre-empts the same handler-discovery surprise when that work lands.
- **`AppendListingWithdrawnAsync` final fate** — kept as a unit-test shortcut per M4-D6 disposition; the M4 plan §8 row notes "if the session surfaces a cleaner boundary — e.g. moving the helper onto a `SagaReplayShortcuts` static class — record the option for S7 consolidation but do not refactor this session." Nothing surfaced; defer to S7.
- **Skill file amendments** — none earned this session. `wolverine-message-handlers.md` and `integration-messaging.md` remain untouched per the prompt's explicit non-goal; M4-S2 introduces no novel pattern and the existing patterns documented in those skill files were applied verbatim.
