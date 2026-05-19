# M4-S3: Proxy Bid Manager Saga — Skeleton + Registration + Reactive Path — Retrospective

**Date:** 2026-05-19
**Milestone:** M4 — Auctions BC Completion
**Slice:** S3 of 7 (no S3b pre-drafted; emergent split authorized at S3 retro if novelty risk surfaces — see §Open Questions)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M4-S3-proxy-bid-manager-saga-skeleton.md`
**Baseline:** 120 tests passing · `dotnet build` 0 errors · M4-S2 closed at `faee5e9`

---

## Baseline

- 120 tests passing at session open (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 37 Auctions)
- `dotnet build` — 0 errors, 24 pre-existing NU1904 Marten vulnerability warnings (unchanged across M3 / M4 / M5)
- `AuctionsIdentityNamespaces.ProxyBidManagerSaga` Guid pinned at M4-S1; no consumers yet
- Six Auctions contract stubs (`RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`) authored at M4-S1 with full future-consumer payload
- `AuctionClosingSaga.Handle([SagaIdentityFrom(nameof(BidPlaced.ListingId))] BidPlaced)` is the existing first `BidPlaced` saga subscriber
- `MultipleHandlerBehavior.Separated` set in `Program.cs:20` since M3-S6

---

## Phase table

| Phase | After commit | New tests | Total tests | Build | Note |
|-------|--------------|-----------|-------------|-------|------|
| Baseline | `6d33f9c` | — | 120 | Green | Session open |
| UUID v5 + identity helper | `285223c` | 0 | 120 | Green | Helpers only |
| Enum + saga state + Marten schema | `3a4c929` | 0 | 120 | Green-on-disk¹ | See "Git-history caveat" |
| Start handler | `657c41c` | 0 | 120 | Green-on-disk¹ | Compiles in workspace |
| Dispatcher + `ProxyBidObserved` | `c41f90c` | 0 | 120 | Green | Branch end-state compiles cleanly |
| Tests (4 saga + 1 dispatch) | `d7e5f69` | +5 | 125 | Green | Acceptance criteria met |
| Skill append | `67b2252` | +0 | 125 | Green | Skill updated |
| Retrospective | this commit | +0 | 125 | Green | Slice close |

¹ The intermediate commits reference types whose files were not yet `git add`-ed at that commit; `dotnet build` succeeds against the working tree (csproj globs all .cs files on disk) but a clean checkout of those commits in isolation would fail to build. The end state of the branch builds cleanly and all 125 tests pass. See "Git-history caveat" below.

---

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| S3a | `src/CritterBids.Auctions/UuidV5.cs` — internal RFC 4122 §4.3 helper, byte-identical to `CritterBids.Settlement.UuidV5` (M5-S4); needed because the Settlement copy is internal and BC-isolation prevents direct reuse | `285223c` |
| S3b | `src/CritterBids.Auctions/AuctionsIdentityHelpers.cs` — sibling to `AuctionsIdentityNamespaces`; exposes `ProxyBidManagerSagaId(Guid listingId, Guid bidderId)` consuming the M4-S1-pinned namespace Guid; colon-delimited name form per Workshop 002 §4.1 | `285223c` |
| S3c | `src/CritterBids.Auctions/ProxyBidManagerStatus.cs` — three-value enum (`Active`, `Exhausted`, `ListingClosed`); all three declared at skeleton per `AuctionClosingStatus` precedent (M3-S5) | `3a4c929` |
| S3d | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs` — `sealed class : Wolverine.Saga` with state fields (`Id`, `ListingId`, `BidderId`, `MaxAmount`, `BidderCreditCeiling`, `LastBidAmount`, `Status`); reactive `Handle([SagaIdentityFrom(nameof(ProxyBidObserved.SagaId))] ProxyBidObserved)` with own-bid (monotone `LastBidAmount`) and competing-bid (`PlaceBid` emission at `nextBid <= MaxAmount`) branches; exhaustion is a `TODO(M4-S4)` comment | `3a4c929` (state) + `c41f90c` (Handle method via the dispatcher introduction) |
| S3e | `src/CritterBids.Auctions/AuctionsModule.cs` — `Schema.For<ProxyBidManagerSaga>().Identity(x => x.Id).UseNumericRevisions(true)` added; no `AddEventType` changes (OQ3 Path a — bus-only emission of `ProxyBidRegistered`) | `3a4c929` |
| S3f | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs` — separate static class; computes `sagaId` via the helper, existence-check via `LoadAsync<ProxyBidManagerSaga>` (idempotent re-registration), emits `ProxyBidRegistered` via `OutgoingMessages`; mirrors `StartSettlementSagaHandler` shape | `657c41c` |
| S3g | `src/CritterBids.Auctions/ProxyBidObserved.cs` — internal Auctions command (public C# accessibility for Wolverine handler discovery; BC-isolation holds via project graph); wraps `BidPlaced` payload with resolved `SagaId` | `c41f90c` |
| S3h | `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs` — non-saga handler for `BidPlaced`; queries active sagas on the listing and emits one `ProxyBidObserved` per match (composite-key correlation bridge — OQ1 Path C resolution) | `c41f90c` |
| S3i | `tests/CritterBids.Auctions.Tests/ProxyBidManagerSagaTests.cs` — four `[Fact]` methods per milestone doc §7 §4: `RegisterProxyBid_StartsSaga_ProducesProxyBidRegistered`, `CompetingBid_ProxyAutoBidsOneIncrementAbove`, `OwnProxyBid_TracksNoReact`, `OwnManualBid_TracksNoReact` | `d7e5f69` |
| S3j | `tests/CritterBids.Auctions.Tests/RegisterProxyBidDispatchTests.cs` — one `[Fact]` dispatching `RegisterProxyBid` via `InvokeMessageAndWaitAsync` | `d7e5f69` |
| S3k | `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — **unchanged** (verify only; within-BC two-saga dispatch did not require a new exclusion) | — |
| S3l | `docs/skills/wolverine-sagas.md` — two new sections appended: "Composite-Key Correlation — the Dispatcher Pattern" and "Multiple Handlers + `MultipleHandlerBehavior.Separated` — Send, Don't Invoke" | `67b2252` |
| S3m | This retrospective | this commit |

Commit mapping:

| Commit | Items covered |
|--------|---------------|
| `285223c` — `feat(auctions)` UUID v5 + identity helper | S3a, S3b |
| `3a4c929` — `feat(auctions)` enum + saga state + Marten schema | S3c, S3d (state portion), S3e |
| `657c41c` — `feat(auctions)` start handler | S3f |
| `c41f90c` — `feat(auctions)` dispatcher (OQ1 Path C) | S3d (Handle method), S3g, S3h |
| `d7e5f69` — `test(auctions)` five tests | S3i, S3j |
| `67b2252` — `docs(skills)` skill append | S3l |
| this commit — retrospective | S3m |

---

## Open Questions — resolutions

### OQ1 — Composite-key saga identity wiring — **Path C (dispatcher)**

**Resolution.** Authored `ProxyBidDispatchHandler.Handle(BidPlaced)` + `ProxyBidObserved` Auctions-internal command. The dispatcher queries `Query<ProxyBidManagerSaga>().Where(s => s.ListingId == … && s.Status == Active)` per inbound `BidPlaced` and emits one `ProxyBidObserved` per active saga. The saga's reactive `Handle([SagaIdentityFrom(nameof(ProxyBidObserved.SagaId))] ProxyBidObserved)` then loads via Wolverine's standard property-pull path.

**Why not Path A (resolver-based `[SagaIdentityFrom]`).** Verified against `Wolverine.Persistence.Sagas.PullSagaIdFromMessageFrame` (`C:\Code\JasperFx\wolverine\src\Wolverine\Persistence\Sagas\PullSagaIdFromMessageFrame.cs`): the codegen frame reads `message.{PropertyName}` directly with `member.GetMemberType()` validating that the property type is a valid saga-id type. There is no expression resolver, no method-based identity (`static Guid IdentifyFor(BidPlaced m)`), no delegate hook. The skill file's `{SagaName}Id` and `[SagaIdentityFrom(nameof(X.Y))]` patterns are the full extent of correlation primitives. Path A is unavailable in Wolverine 5.39.3.

**Why not Path B (add `ProxyBidManagerSagaId` field to `BidPlaced`).** Incompatible with multi-saga dispatch. A single `BidPlaced` may target N proxy sagas (one per registered bidder on the listing). One Guid field cannot address many sagas. Path B was structurally ruled out before any code change.

**In-repo ground.** `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs` and `src/CritterBids.Auctions/ProxyBidObserved.cs`. Pattern documented in the skill file's new "Composite-Key Correlation — the Dispatcher Pattern" section (commit `67b2252`).

### OQ2 — Two-saga `BidPlaced` dispatch (within-BC) — **non-issue under Path C, blocker surfaced in tests**

**Resolution.** With Path C, `AuctionClosingSaga.Handle(BidPlaced)` (the existing M3-S5 subscriber) and `ProxyBidDispatchHandler.Handle(BidPlaced)` (the new non-saga handler) coexist as two independent endpoints under `MultipleHandlerBehavior.Separated`. The runtime production path works correctly — both handlers fire on every inbound `BidPlaced`, the saga updates its state, the dispatcher fans out one `ProxyBidObserved` per active proxy saga.

**Blocker — InvokeAsync routing surprise.** First test run after introducing the dispatcher (4 of 5 new tests + 1 PlaceBidDispatchTests regression) failed with:

```
Wolverine.Runtime.Handlers.NoHandlerForEndpointException :
  No handlers for message type CritterBids.Contracts.Auctions.BidPlaced at
  endpoint local://critterbids.contracts.auctions.bidplaced/.
  This is usually because of 'sticky' handler to endpoint configuration.
```

**Root cause.** `IMessageBus.InvokeAsync` is **single-handler-targeted**. With `MultipleHandlerBehavior.Separated` and multiple handlers, each handler is auto-assigned its own local sticky queue per `Wolverine.Runtime.Handlers.HandlerChain.cs:351-353` (queue name = handler type's lowercased full name). The default endpoint `local://{type}/` has no handler attached. `InvokeAsync` falls through to that default endpoint and `HandlerGraph.cs:178-205` throws because no sticky chain is bound there and the incoming endpoint is itself a `LocalQueue` (so the fan-out branch at line 187-202 does not kick in).

**Fix.** Switched the three new tests that dispatch `BidPlaced` (4.2 / 4.4 / 4.5) from `InvokeMessageAndWaitAsync` to `SendMessageAndWaitAsync`. The publish path (`Context.SendAsync` / `Context.PublishAsync`) fans out to each handler's sticky queue. `UseFastEventForwarding` already uses `Context.PublishAsync` (verified in `Wolverine.Marten.PublishIncomingEventsBeforeCommit.cs:24`), so production forwarding of `BidPlaced` events appended by `PlaceBidHandler` works correctly via the same fan-out path.

The 4.1 test (dispatches `RegisterProxyBid` — single handler) and `RegisterProxyBidDispatchTests` (same) continue to use `InvokeMessageAndWaitAsync` because invoke is correct for single-handler messages. `PlaceBidDispatchTests` (existing, dispatches `PlaceBid` — single handler) was unchanged; its earlier "regression" was transient state pollution from the failed proxy tests, not a real regression.

**Folded into the skill file.** New section "Multiple Handlers + `MultipleHandlerBehavior.Separated` — Send, Don't Invoke" in commit `67b2252` with the symptom shape, citations, and the targeted decision rule (invoke for single-handler, send for multi-handler).

### OQ3 — `ProxyBidRegistered` emission shape — **Path (a) bus emission**

**Resolution.** `StartProxyBidManagerSagaHandler` emits `ProxyBidRegistered` via `OutgoingMessages` as a bus message. No cross-BC consumer wired at S3 (Relay is post-M5 per the contract's docstring), so the event lands in `tracked.NoRoutes` — the same fixture-stance assertion pattern used at M5-S6 for `SellerPayoutIssued` / `PaymentFailed` publish-route tests. Scenario 4.1 asserts via `tracked.NoRoutes.MessagesOf<ProxyBidRegistered>().ShouldHaveSingleItem()`.

**No `AddEventType<ProxyBidRegistered>()` added** — the event is not appended to any Marten stream. The saga document itself carries the registration as state (`Status = Active`, `MaxAmount`, `BidderCreditCeiling`).

### OQ4 — `BidderCreditCeiling` lookup at saga start — **Path (c) deferred to S4**

**Resolution.** `BidderCreditCeiling` defaults to `0m` on saga construction in `StartProxyBidManagerSagaHandler`. S3's four scenarios (4.1 / 4.2 / 4.4 / 4.5) do not consult the field; the cap-enforcing branch is S4 scope (scenario 4.9). Path (a) — cross-BC query — was rejected upfront on BC-isolation grounds per the prompt. Path (b) — Auctions-side `ParticipantCreditCeiling` projection (the M4-D4 duplicate-projection pattern applied a second time) — is sized for S4 alongside the cap-enforcing handler.

**No `auctions-participants-events` queue wiring needed in S3.** S4 will need to add this queue plus the projection.

---

## Blockers encountered

### Blocker 1 — `NoHandlerForEndpointException` on multi-handler `BidPlaced` dispatch

Documented under OQ2 above. Root cause: `InvokeAsync` is single-handler-targeted; with Separated mode + 2 handlers, falls through to a default endpoint that has no handler.

**Verbatim error:** see OQ2 quote.

**Source citations:**
- `C:\Code\JasperFx\wolverine\src\Wolverine\Runtime\Handlers\HandlerGraph.cs:178-205` — sticky-handler resolution and the `throw` path when no sticky chain matches the incoming endpoint and the endpoint is itself a `LocalQueue`
- `C:\Code\JasperFx\wolverine\src\Wolverine\Runtime\Handlers\HandlerChain.cs:349-354` — `Separated` mode's per-handler queue assignment convention (`call.HandlerType.FullNameInCode().ToLowerInvariant()`)
- `C:\Code\JasperFx\wolverine\src\Wolverine\Tracking\WolverineHostMessageTrackingExtensions.cs:84` — `InvokeMessageAndWaitAsync` calls `c.InvokeAsync`
- `C:\Code\JasperFx\wolverine\src\Wolverine\Tracking\TrackedSessionConfiguration.cs:218,229,231` — invoke vs send tracking entry points
- `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\PublishIncomingEventsBeforeCommit.cs:24` — `UseFastEventForwarding` uses `_bus.PublishAsync(e)` (publish path), so production forwarding fans out correctly

**Fix path:** switch the dispatch shape in tests where the message has > 1 handler. Folded into the skill file as a targeted decision rule, not a universal "always use send" prescription.

### Blocker 2 — `NoHandlerForEndpointException` recurrence in `PlaceBidDispatchTests`

**Symptom (first observed).** The 4 proxy-test failures from Blocker 1 plus a regression in the existing `PlaceBidDispatchTests` that previously passed at baseline.

**Root cause analysis.** Transient. The failed proxy tests left a `ProxyBidManagerSaga` document in the testcontainers Postgres schema (the fixture's `CleanAllMartenDataAsync()` runs in `InitializeAsync` per test class, not per fact, and only cleans the schemas Marten knows about). The next `PlaceBidDispatchTests` run dispatched `PlaceBid`, which led `PlaceBidHandler` to append `BidPlaced`, which `UseFastEventForwarding` published — and the dispatcher's `Query<ProxyBidManagerSaga>` then hit a still-present proxy saga document, triggered the dispatch chain, and surfaced the same routing issue from Blocker 1 in the cascade.

After fixing Blocker 1 (switching the proxy tests to `SendMessageAndWaitAsync`), the proxy tests left no dirty state behind, and `PlaceBidDispatchTests` cleared.

**Fix path:** no code change. The cleanup behaviour is correct; Blocker 1 was the upstream cause and its fix removed the symptom.

---

## Decisions inheriting forward

### Composite-key correlation pattern

The dispatcher pattern (OQ1 Path C) is now the in-repo precedent for any saga with a derived composite-key id that no inbound contract carries, especially when one inbound event targets many saga instances (one-to-many fan-out). Folded into `docs/skills/wolverine-sagas.md` §"Composite-Key Correlation — the Dispatcher Pattern" (commit `67b2252`).

### Invoke vs Send under Separated mode

`SendMessageAndWaitAsync` is required for test dispatch of a message with multiple handlers; `InvokeMessageAndWaitAsync` remains correct for single-handler messages. Folded into the skill file §"Multiple Handlers + `MultipleHandlerBehavior.Separated` — Send, Don't Invoke" (commit `67b2252`).

### UUID v5 helper duplication

`CritterBids.Auctions.UuidV5` is byte-identical to `CritterBids.Settlement.UuidV5` (M5-S4). The Settlement copy is `internal`; lifting to a shared location would change the public API surface of two BCs simultaneously, which is wider scope than this slice. Tracked as a future cleanup pass — when a third BC needs UUID v5, the lift becomes justified by the rule of three. Until then, duplication is the lower-risk path.

---

## Git-history caveat

Commits `3a4c929` and `657c41c` reference types whose source files were not `git add`-ed yet at commit time (`ProxyBidObserved` referenced by the saga's `Handle` in `3a4c929`; the saga's `Handle` itself implies `ProxyBidObserved`'s presence). `dotnet build` succeeded throughout the slice because the .csproj files glob all `.cs` on disk regardless of git tracking. A clean checkout of those intermediate commits in isolation would fail to build.

The branch end-state (commit `67b2252` and the retro commit) is the reviewable artifact — all 125 tests pass and `dotnet build` is clean. The intermediate commits are stepping stones for diff review, not bisect-safe.

**Disposition for M4-S4:** prefer to `git add` companion files together with the file that introduces the reference. The prompt's proposed commit sequence (item 5 — "reactive Handle(BidPlaced) for competing-bid auto-bid" + scenario 4.2 test) coupled the saga's Handle method with its scenario test; the OQ1 Path C resolution decoupled them because the dispatcher had to land before the saga's Handle could reference `ProxyBidObserved`. Future Path-C-style slices should bundle the "wrapper command + Handle + first test" as one atomic commit.

---

## What M4-S4 should know

### Identity wiring — Path C dispatcher

S4 inherits the dispatcher mechanism for **all** terminal handlers (`Handle(ListingSold)`, `Handle(ListingPassed)`, `Handle(ListingWithdrawn)`) and the exhaustion-emission branch. Two implementation choices for S4:

1. **Reuse `ProxyBidDispatchHandler` and add three more handler methods for `ListingSold` / `ListingPassed` / `ListingWithdrawn`.** Each method queries active sagas on the listing and emits a new `ProxyXxxObserved` command per match. Symmetric with the `BidPlaced` shape.
2. **Single `ProxyListingClosedObserved` command** covering all three terminal events with a discriminator field. Fewer types, more conditional saga logic.

Recommendation: Option 1 — keep the wrapped command shapes parallel to their inbound contracts. Composability is easier to maintain at the cost of three new wrapped types.

### Idempotency convention pinned in S3

- **Own bids** — monotone `LastBidAmount` (no state change on lower-or-equal re-deliveries). Confirmed by scenarios 4.4 / 4.5.
- **Competing bids** — no built-in monotonicity at S3. Re-delivery of the same competing bid would re-emit `PlaceBid`. S4's bidding-war scenario (4.10) will exercise whether this matters in practice; if it does, a processed-bid-ids set or amount-monotonicity guard is the fix.
- **Terminal events** — pattern uniform from `AuctionClosingSaga`: `if (Status != Active) return;` early guard, then `Status = ListingClosed; MarkCompleted();`.

### `ProxyBidRegistered` emission shape — confirms S4's `ProxyBidExhausted` shape

S3 chose Path (a) — bus emission via `OutgoingMessages`, no `AddEventType`. S4's `ProxyBidExhausted` should follow the same shape:

```csharp
return new OutgoingMessages
{
    new ProxyBidExhausted(ListingId, BidderId, MaxAmount, time.GetUtcNow())
};
```

And test assertions via `tracked.NoRoutes.MessagesOf<ProxyBidExhausted>()`.

### `BidderCreditCeiling` lookup — S4's work

S3 deferred to Path (c) — `BidderCreditCeiling = 0m` default. S4 must:

1. Add an `auctions-participants-events` RabbitMQ queue in `Program.cs` subscribing to `Contracts.Participants.ParticipantSessionStarted`
2. Author an Auctions-side `ParticipantCreditCeiling` Marten document keyed by `ParticipantId` (= `BidderId` — verify the identity mapping at S4 open)
3. Author the projection handler that upserts the document on `ParticipantSessionStarted`
4. Modify `StartProxyBidManagerSagaHandler` to `LoadAsync<ParticipantCreditCeiling>(message.BidderId)` and populate the saga's `BidderCreditCeiling`
5. Add an exhaustion branch in `ProxyBidManagerSaga.Handle(ProxyBidObserved)` that consults `BidderCreditCeiling` for scenario 4.9 (proxy auto-bid capped by credit ceiling)

**Cross-reference:** the M4-D4 `PublishedListings` projection (resolved at M4-S1 for `AttachListingToSession` published-status check, scheduled for S5 implementation) is the parallel pattern. If S4 implements `ParticipantCreditCeiling` ahead of S5, the duplicate-projection pattern earns two lived applications by M4 close — a strong evidence-of-pattern data point. Flag as a positive scope ripple in S5's prompt if S4 lands the pattern first.

### Two-saga `BidPlaced` dispatch — no within-BC handler-discovery surprises

The within-BC two-handler `BidPlaced` topology under `MultipleHandlerBehavior.Separated` works correctly **for the production publish path** (`UseFastEventForwarding` uses `Context.PublishAsync` which fans out). The only surprise was on the **test dispatch path** (`InvokeMessageAndWaitAsync` does not fan out), folded into the skill file. S4's three new terminal handlers (`ListingSold` / `ListingPassed` / `ListingWithdrawn`) will increase the within-BC handler count for those three message types from 1 (AuctionClosingSaga only) to 2 (AuctionClosingSaga + new ProxyBidDispatchHandler method per terminal). S4 tests dispatching these terminal events must use `SendMessageAndWaitAsync`, **not** `InvokeMessageAndWaitAsync`.

The existing `AuctionClosingSagaTests` already dispatches `ListingWithdrawn` via `InvokeMessageAndWaitAsync` (`ListingWithdrawn_TerminatesWithoutEvaluation`). After S4 adds `ProxyBidDispatchHandler.Handle(ListingWithdrawn)`, that test may regress with the same symptom as Blocker 1. Pre-emptive fix: switch the existing test from `InvokeMessageAndWaitAsync` to `SendMessageAndWaitAsync` at S4 open. Same for `BuyItNowPurchased_CompletesSaga` if `ProxyBidDispatchHandler` is extended to cover `BuyItNowPurchased` (S4 scope to confirm — Workshop 002 §4.6 / 4.7 / 4.8 only name `ListingSold` / `ListingPassed` / `ListingWithdrawn`, not `BuyItNowPurchased`, but Workshop §3.8 specifies BIN as terminal so the proxy should arguably also terminate on `BuyItNowPurchased`).

### Bid increment helper — keep inline through S4

S3's competing-bid branch has three lines of inline increment math (`$1 under $100, $5 at $100+`). PlaceBidHandler has the same math inline (`PlaceBidHandler.cs:174-175`). Per CLAUDE.md's "premature abstraction" rule, two co-existing copies of the same three-line computation are below the threshold. S4 scenarios that introduce a third copy (the exhaustion branch will likely consult the same increment to determine "next defensive bid"; the bidding-war scenario 4.10 may need it twice in one flow) might cross the threshold. S4 retro decides extraction; a shared `BidIncrement.For(decimal currentHighBid)` helper would live in `src/CritterBids.Auctions/` if extracted.

---

## Test count summary

| Project | M4-S2 close | M4-S3 delta | M4-S3 close |
|---------|-------------|-------------|-------------|
| `CritterBids.Api.Tests` | 1 | 0 | 1 |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 |
| `CritterBids.Listings.Tests` | 14 | 0 | 14 |
| `CritterBids.Selling.Tests` | 36 | 0 | 36 |
| `CritterBids.Settlement.Tests` | 25 | 0 | 25 |
| `CritterBids.Auctions.Tests` | 37 | **+5** | **42** |
| **Total** | **120** | **+5** | **125** |

`dotnet build`: 0 errors · 24 NU1904 warnings (unchanged from baseline).
