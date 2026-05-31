# M7-S3: Lot Board + Bid-Activity Feed - Retrospective

**Date:** 2026-05-31
**Milestone:** M7 - Operations BC
**Slice:** S3 of 7 - lot board upsert view + bid-activity append feed
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M7-S3-lot-board-bid-activity-feed.md`

## Baseline

- M7-S2 scaffold merged: `CritterBids.Operations` project, `AddOperationsModule()`, the
  `operations` Marten schema, `SettlementQueueView` (the first Path A consumer), the
  `operations-settlement-events` `ListenToRabbitQueue()` consumer, and the per-project
  `*BcDiscoveryExclusion` test-isolation pattern.
- Operations BC carried one document type (`SettlementQueueView`) and one handler
  (`SettlementQueueHandler`); `CritterBids.Operations.Tests` held its own fixture excluding the six
  foreign BCs and applying `DisableAllExternalWolverineTransports()`.
- Full solution green at baseline; `dotnet build` 0 errors / 0 warnings.
- Operations consumed only Settlement-sourced events; no Auctions/Selling event had a second
  in-process handler, so no foreign fixture excluded Operations.

## Items completed

| Item | Description |
|------|-------------|
| S3.1 | `LotBoardView` `sealed record` (W006 §2 field set) + `LotBoardStatus` enum + `LotBoardStatusRules` |
| S3.2 | Two ADR 014 Sub-Option A sibling handlers — `LotBoardSellingHandler`, `LotBoardAuctionsHandler` |
| S3.3 | Terminal-state-preservation guard (no regress to `Open`) — folded into the status rank model |
| S3.4 | `BidActivityEntry` append/feed `sealed record` (W006 §3) + idempotent `BidActivityHandler` |
| S3.5 | `AddOperationsModule()` additive `ConfigureMarten` for both docs (+ feed indexes) |
| S3.6 | `Program.cs` routing — `operations-auctions-events` + `operations-selling-events` consumers |
| S3.7 | Cross-BC discovery exclusions — extended to Auctions + Selling fixtures (red-run empirical set) |
| S3.8 | `CritterBids.Operations.Tests` — lot-board projection tests + bid-activity append tests + schema-mapping tests |

## S3.1: LotBoardView + status model

**Why this approach.** W006 §2 freezes the derivation chain `Draft → Open → Sold|Passed|Withdrawn`,
not the member names, so `LotBoardStatus` lives Operations-internal (no Contracts type), mirroring
`SettlementQueueStatus`'s placement from S2. The terminal-preservation requirement (a late
`BidPlaced` must not regress `Sold`/`Passed`/`Withdrawn` to `Open`) was implemented as a **monotone
absorbing rank** rather than a one-off "don't regress from terminal" check:

```
Draft = 0, Open = 1, Sold|Passed|Withdrawn = 2
Advance(current, candidate) => Rank(current) >= Rank(candidate) ? current : candidate
```

A rubber-duck pass on the design rejected the narrower "no regress to Open" guard in favour of the
rank: the rank simultaneously satisfies three invariants the prompt names — terminal never regresses
to `Open` (W006 §2), a late `ListingPublished` (Draft) never regresses an `Open`/terminal row to
`Draft` (ADR 014 seed-handler rule), and a second differing terminal is first-wins (out of W006
scope, but a safe deterministic default). One predicate covers all three; three predicates would
have drifted.

**Structural metrics.**

| Metric | Value |
|--------|-------|
| `LotBoardView` type | `sealed record`, keyed `Id => ListingId` |
| Collections | none (no `List<T>`; W006 §2 is flat) |
| Status members | 5 (`Draft`, `Open`, `Sold`, `Passed`, `Withdrawn`) |
| `IDocumentSession` writes via `session.Store` | upsert (load-or-create) |

## S3.2: Two source-keyed sibling handlers

**Why this approach.** ADR 014 Sub-Option A is one handler class per source BC, both upserting the
single `ListingId`-keyed document — `LotBoardSellingHandler` (`ListingPublished`, `ListingWithdrawn`)
and `LotBoardAuctionsHandler` (`BiddingOpened`, `BidPlaced`, `ListingSold`, `ListingPassed`). Same
shape as the S2 `SettlementQueueHandler`: `static` handler, `Task` return, writes only through the
injected `IDocumentSession`, no `OutgoingMessages`, no `IMessageBus`.

- **`SellerId` set-once** via a `Guid.Empty` sentinel across `ListingPublished`/`BiddingOpened`/
  `ListingSold` — real seller IDs are UUID v7 and never `Guid.Empty`, so the sentinel is safe.
  `ListingSold` populates `SellerId` when it is the first carrier (seed-via-auction case).
- **`LastUpdatedAt` latest-wins** via `Latest(existing, incoming) => incoming > existing ? incoming
  : existing`, fed from each event's own timestamp; an older event never rewinds it.
- **`BidPlaced` figure-regression guard** (rubber-duck refinement): `CurrentBid`/`BidCount` are
  applied only when `!IsTerminal(view.Status) && message.BidCount >= view.BidCount`, so an
  out-of-order older bid never rewinds the figures and a late bid after close leaves the final
  hammer figures intact. `LastUpdatedAt` still advances latest-wins regardless.

## S3.4: Bid-activity append feed

**Why this approach.** W006 §6 / ADR 014 call out the feed as the distinct **append** shape — one
immutable row per `BidId`, not upsert-in-place. The handler is load-check-then-skip: if a row for the
`BidId` already exists it returns (explicit no-op dedupe on at-least-once re-delivery), else it
`Store`s a new `BidActivityEntry`. Indexes on `ListingId` and `PlacedAt` were added in
`ConfigureMarten` because the feed's only query axes are "this listing's bids, in order." Chose the
explicit load-check over a blind `Store` so the dedupe semantics are legible and test-assertable as a
row count rather than relying on upsert-overwrite masking the duplicate.

## S3.6 / S3.7: Routing and the sticky-handler discovery

**Discovery / resolution (the slice's central surprise).** `BidPlaced` now has **two** Operations
handlers in one assembly (`LotBoardAuctionsHandler` and `BidActivityHandler`). Under the
modular-monolith `MultipleHandlerBehavior.Separated` setting (Program.cs), each handler gets its own
sticky local queue, and there is no inline handler "at this endpoint." The Operations projection
tests' first red run produced, verbatim:

```
Wolverine.Runtime.Handlers.NoHandlerForEndpointException : No handlers for message type
CritterBids.Contracts.Auctions.BidPlaced at this endpoint. This is usually because of 'sticky'
handler to endpoint configuration.
```

Root cause: `InvokeMessageAndWaitAsync` invokes inline; a fanned-out (Separated) message must be
**routed**, not invoked. Resolution: dispatch `BidPlaced` in the Operations tests via
`SendMessageAndWaitAsync` (the same call the Auctions `ProxyBidManagerSagaTests` already use for
`BidPlaced`, and the documented "M4-S4 pre-emptive fix" in `AuctionClosingSagaTests`). Single-handler
events (`BiddingOpened`, `ListingSold`, `ListingPassed`, `ListingPublished`, `ListingWithdrawn`)
still use `InvokeMessageAndWaitAsync`.

**Empirical exclusion set.** A full-suite red run named exactly two foreign fixtures that newly break
from discovering the Operations lot-board/bid handlers:

- **`CritterBids.Auctions.Tests`** — 9 failures, all `NoHandlerForEndpointException` on
  `BiddingOpened`/`BidPlaced`/etc. The Operations second handler flips those types to sticky
  Separated routing, breaking the fixture's inline `InvokeMessageAndWaitAsync` saga/dispatch calls.
- **`CritterBids.Selling.Tests`** — 1 failure,
  `WithdrawListing_ViaWolverineDispatch_TransitionsAggregateAndEmitsContractEvent`. A *local*
  Operations consumer for `ListingWithdrawn` changes how the published contract event is classified
  in the tracked session (routed/Executed locally rather than surfacing in `tracked.Sent`), so the
  test's `MessagesOf<ListingWithdrawn>()` assertion saw `[]`.

`Listings`, `Relay`, `Settlement`, and `Obligations` fixtures stayed green and were left untouched —
the prompt's candidate list (Listings, Auctions, Relay, "and others") over-predicted; only Auctions
and Selling actually broke. Both fixes reuse the existing `OperationsBcDiscoveryExclusion` per-project
pattern (each test assembly defines its own internal copy) — no new exclusion class authored.

**`ListingWithdrawn` routing asymmetry (Open Question, resolved).** `ListingWithdrawn` is a
Selling-sourced event, but it rides the `operations-auctions-events` queue per the milestone §2 queue
table literal (the selling queue lists only `ListingPublished`). Handler grouping stays by source BC
(`LotBoardSellingHandler` owns it); the queue is transport only. Flagged here as the documented
asymmetry — not a blocker.

## Test results

| Phase | Scope | Result |
|-------|-------|--------|
| Operations projection tests authored | `CritterBids.Operations.Tests` | 6 fail (sticky `BidPlaced` via Invoke) |
| `BidPlaced` switched to `SendMessageAndWaitAsync` | `CritterBids.Operations.Tests` | 18/18 pass |
| Full-suite red run | solution | Auctions 9 fail, Selling 1 fail |
| `OperationsBcDiscoveryExclusion` added to Auctions + Selling fixtures | solution | all green |

Final: Operations 18, Auctions 65, Selling 36, Listings 20, Settlement 25, Obligations 13, Relay 36,
Participants 6, Api 1, Contracts 1 — **all green, 0 failures**.

## Build state at session close

- `dotnet build CritterBids.slnx`: 0 errors, 0 warnings (no delta from baseline).
- Operations handlers returning `OutgoingMessages`: 0. `IMessageBus` references in Operations
  handlers: 0 (pure Path A consumer; test-guarded by the `tracked.Sent`-empty assertion in
  `LotBoardHandlerTests`).
- New `AddMarten()` calls in `OperationsModule`: 0 (additive `ConfigureMarten` only).
- New `CritterBids.Contracts.*` types: 0. "Event"-suffixed type names: 0. "paddle" references: 0.
- New `OperationsBcDiscoveryExclusion` classes authored: 0 (reused the per-project pattern).
- New Operations document types in the `operations` schema: 2 (`LotBoardView`, `BidActivityEntry`).

## Key learnings

1. **A second in-process handler on a high-fan-out event silently changes the testing contract of
   every other BC's fixture.** Adding `LotBoardAuctionsHandler` to `BidPlaced` flipped it to sticky
   Separated routing solution-wide; the only safe way to find the blast radius is a red full-suite
   run, not reasoning about the candidate list. The prompt's predicted set (Listings, Auctions,
   Relay, …) was wrong; the actual set was Auctions + Selling.
2. **`InvokeMessageAndWaitAsync` is for single-handler messages only.** Once a message fans out under
   `MultipleHandlerBehavior.Separated`, tests must use `SendMessageAndWaitAsync` (route) — Invoke
   throws `NoHandlerForEndpointException`. This is a recurring CritterBids trap (already hit in M4-S4).
3. **A local consumer can break a *producer's* outgoing-message assertion.** Selling's withdraw test
   broke not because Selling changed but because Operations newly consumed `ListingWithdrawn`
   in-process, moving it from `tracked.Sent` to local `Executed`. Foreign-BC fixtures must exclude any
   BC whose handlers would co-consume the events they assert on.
4. **A monotone absorbing rank collapses three separate projection guards into one predicate.**
   Terminal-no-regress, seed-late-no-regress-to-Draft, and first-terminal-wins all fall out of
   `Rank(current) >= Rank(candidate) ? current : candidate`.
5. **Pure-consumer `tracked.Sent`-empty is cleanest on the single-handler path.** The feed's dedupe
   test publishes `BidPlaced` (which then appears in `Sent`), so the "emits nothing" assertion lives
   on the lot-board test's Invoke-of-`ListingPublished` path; the feed test proves its half (no
   duplicate row) by row count.

## Findings against narrative

Per the prompt's `**Narrative:** none` metadata, this slice anchors to no narrative Moment. The lot
board and bid-activity feed are W006 §2/§3 source-audit surfaces; narrative 008 (the
dispute/escalation queue) lands in S4. No `narrative-update`, `workshop-update`, `code-update`, or
`document-as-intentional` finding is raised. W006 is a frozen field spec, not a behavior narrative,
so no Document-History row is owed.

## Spec delta - landed?

**No spec consequence (as the prompt declared).** The prompt's `## Spec delta` section stated no
narrative or workshop Document-History row is owed — W006 is a freeze, and the lot board / bid
feed anchor to no narrative Moment. That held: no narrative, workshop, or ADR was amended. Both views
are seeded end-to-end against real Postgres via Testcontainers — the lot board across its full
`Draft → Open → Sold` lifecycle plus the `Passed` and `Withdrawn` terminal paths, with the
set-once `SellerId`, terminal-no-regress, and latest-wins `LastUpdatedAt` guards exercised; the bid
feed across N-bids-→-N-rows and idempotent re-delivery. The W006 §2/§3 field sets and guards are
exercised exactly as the freeze specifies.

## Verification checklist

- [x] `LotBoardView` is a `sealed record` keyed by `ListingId` carrying exactly the W006 §2 field
  set; a lot-board `Status` enum realises `Draft → Open → Sold|Passed|Withdrawn`.
- [x] Two ADR 014 Sub-Option A sibling handlers (Selling-source + Auctions-source), each a tolerant
  upsert; `Status` per W006 §2; set-once `SellerId` spanning `ListingPublished`/`BiddingOpened`/
  `ListingSold`; latest-wins `LastUpdatedAt` that does not rewind.
- [x] Seed-late / load-and-preserve case covered by a test (`ListingPublished` after an Auctions
  event fills catalog fields without clobbering auction state or regressing to `Draft`).
- [x] Terminal-state-preservation guard implemented and asserted by a late-`BidPlaced`-after-terminal
  test.
- [x] Bid-activity feed is a `sealed record` append/feed keyed by `BidId` (W006 §3 fields); handler
  appends one row per `BidPlaced` and is idempotent on `BidId`, asserted by a test.
- [x] `AddOperationsModule()` additively registers both doc types via `ConfigureMarten` in the
  `operations` schema; no `AddMarten()` in the module; no saga/aggregate/event-stream registration.
- [x] `Program.cs` has both new `ListenToRabbitQueue()` consumers, publish-route additions for the
  S3-consumed events only, and `AutoProvision()` covering both queues; no session-event route added;
  no upstream BC code changed.
- [x] No `[Authorize]`/`StaffOnly`/auth registration in the slice; auth state unchanged; Operations
  adds no HTTP endpoint.
- [x] `OperationsBcDiscoveryExclusion` extended to exactly the fixtures the red run named (Auctions +
  Selling); no cross-BC handler leakage; no new exclusion class authored.
- [x] Operations handlers return no `OutgoingMessages` and make no `IMessageBus` call; pure-consumer
  contract test-guarded by a `tracked.Sent`-empty assertion.
- [x] Operations tests contain the end-to-end lot-board projection tests and bid-activity append
  tests against real Postgres via Testcontainers, all green.
- [x] `dotnet build` passes (0 errors, 0 warnings); full `dotnet test` green, no regressions.
- [x] No new `CritterBids.Contracts.*` type; no "Event"-suffixed name; no "paddle" reference.
- [x] This retrospective written with the `**Prompt:**` header and the `## Spec delta - landed?`
  paragraph.
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

- **In scope for M7, deferred to S4:** `OperationsObligationsView` and the dispute/escalation queue
  (narrative 008).
- **In scope for M7, deferred to S5:** the session/participant board and the session events
  (`SessionCreated`/`SessionStarted`/`ListingAttachedToSession`) that already ride the
  `operations-auctions-events` queue but are intentionally not routed/handled here.
- **In scope for M7, deferred to S6:** auth gating (`[Authorize]`/`StaffOnly`) and the Operations
  query/read HTTP endpoints — both views are write-only consumers today, with no query surface.
- **Watch:** the `ListingWithdrawn` queue-routing asymmetry (Selling-sourced event on the
  `operations-auctions-events` queue) is intentional per milestone §2 but is the kind of thing a
  future queue-topology review should re-confirm.
