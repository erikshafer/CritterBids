# M3-S6: Listings Catalog — Auction-Status Extension

**Milestone:** M3 — Auctions BC
**Slice:** S6 of 9 (follows S5b which closed the Auctions saga; penultimate M3 implementation session before S7 retrospective + M3 close)
**Agent:** @PSA
**Estimated scope:** one PR; 5 new scenario tests (6 if OQ3 includes BIN); ~4–6 new/modified files
**Baseline:** 79 tests green · `dotnet build` 0 errors, 0 warnings · M3-S5b closed. At S5b close: `AuctionClosingSaga` feature-complete (6 real handlers + 1 static `NotFound`; 3 `MarkCompleted()` calls, 1 `OutgoingMessages` cascade return); outcome events `BiddingClosed` / `ListingSold` / `ListingPassed` emit via `OutgoingMessages` cascade and are **bus-only**, not stream-appended (OQ5 Path ◦); `BuyItNowPurchased` is a terminal event with no preceding `BiddingClosed` per OQ1 Path B; `ListingWithdrawn` is a production type in `CritterBids.Contracts.Selling` with **no Selling-side publisher** at M3 close (fixture-synthetic only). `AuctionsModule.AddEventType<T>()` count is 8. The `listings-auctions-events` RabbitMQ queue is **unwired** — outcome events cascade to `tracked.NoRoutes` in tests and would cascade to nowhere in production. This slice is the queue wiring + the Listings-side consumer + the catalog-view field extension.

---

## Goal

Land the Listings BC's auction-status extension. `CatalogListingView` grows with the fields a UI or Operations dashboard needs to render a live auction state — status, current high bid, bid count, scheduled close, final outcome (hammer price, passed reason). A new handler class `AuctionStatusHandler` (or an extension to `ListingPublishedHandler` per OQ1) consumes the five auction integration events `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed` — five upsert transitions on the view, each loading the existing document, mutating, and storing back. The `listings-auctions-events` RabbitMQ queue is wired in `Program.cs` — publish rules on the Auctions end, a listen rule on the Listings end — making S5b's bus-only outcome events land on the bus in production for the first time. Five integration tests per milestone doc §7 `CritterBids.Listings.Tests` cover the five status transitions.

After this slice, the end-to-end demo path from `ParticipantSessionStarted` (M1) through `ListingSold` / `ListingPassed` (M3) runs over RabbitMQ with five integration hops and the catalog view reflects the full listing lifecycle. S7 then consolidates accumulated skill updates and writes the M3 retrospective.

## Context to load

- `docs/milestones/M3-auctions-bc.md` — §7 Listings test rows (5 scenarios), §2 cross-BC wiring table (`listings-auctions-events` direction, queue naming), §6 catalog-projection-extension convention
- `docs/retrospectives/M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` — especially **"What M3-S6 should know"** (9 enumerated carry-forwards: final payload shapes, bus-only emission, per-listing event ordering on the queue, `BuyItNowPurchased` subscription recommendation, queue-wiring API-surface verification caveat, `ListingWithdrawn` production status) and the OQ5 grounding (saga is the source of truth for close state — no replay path)
- `src/CritterBids.Listings/` baseline — `CatalogListingView.cs` (8 fields at M2 close), `ListingPublishedHandler.cs` (static class, `session.Store` upsert shape), `ListingsModule.cs` (single `Schema.For<CatalogListingView>().DatabaseSchemaName("listings")` registration). These are the byte-level reference for what stays frozen vs extends.
- `src/CritterBids.Contracts/Auctions/` — `BiddingOpened.cs`, `BidPlaced.cs`, `BiddingClosed.cs`, `ListingSold.cs`, `ListingPassed.cs`, `BuyItNowPurchased.cs` payload shapes and "Consumed by:" docblocks. The first five contracts explicitly name Listings as a consumer; confirm payload completeness before writing the handler.
- `docs/prompts/implementations/M2-S7-listings-bc-and-read-paths.md` + `docs/retrospectives/M2-S7-listings-bc-and-read-paths-retrospective.md` — the projection-extension precedent; the M2 queue-wiring shape (`opts.PublishMessage<T>().ToRabbitQueue(...)` + `opts.ListenToRabbitQueue(...)`) for `listings-selling-events`
- `docs/skills/marten-projections.md` + `docs/skills/integration-messaging.md` — projection-upsert shape, queue naming convention (`<consumer>-<publisher>-<category>`), L2 payload-completeness rule, consumer idempotency under at-least-once delivery
- **Reference documentation — three tiers, S5b order.** First: `C:\Code\JasperFx\ai-skills\` (specifically `wolverine/integrations/`, `wolverine/http/`, `marten/documents/`). Second: pristine source at `C:\Code\JasperFx\wolverine\` and `C:\Code\JasperFx\marten\` — consult for the publish/listen API-surface questions flagged in S5b's "What M3-S6 should know" §8 (*"verify exact API surface from Wolverine source first — contract publish API has been less stable than handler-side over Wolverine 4→5"*). Third: Context7 (`/jasperfx/wolverine`, `/jasperfx/marten`). Every first-use claim about the publish/listen wiring API in S6 cites its source — S5b's reference-doc discipline remains in effect.

## In scope (numbered)

1. `src/CritterBids.Listings/CatalogListingView.cs` — extend additively with auction-status fields. Minimum set to cover the 5 milestone-doc scenarios:
   - `Status` — see OQ2 (string vs enum). Initial value on M2-publish is `"Published"` (or equivalent); transitions: `"Open"` on `BiddingOpened`, `"Closed"` on `BiddingClosed`, `"Sold"` on `ListingSold`, `"Passed"` on `ListingPassed`. Per OQ3, potentially `"Sold"` on `BuyItNowPurchased`.
   - `ScheduledCloseAt` — nullable `DateTimeOffset`; populated from `BiddingOpened.ScheduledCloseAt`. Whether it rolls forward on `ExtendedBiddingTriggered` is OQ4 — the saga owns the authoritative close time; whether the catalog mirrors it is a projection design call.
   - `CurrentHighBid` — nullable `decimal`; updated on each `BidPlaced`.
   - `CurrentHighBidderId` — nullable `Guid`; updated on each `BidPlaced`, subject to the privacy question in OQ5.
   - `BidCount` — `int`, default 0; set to `BidPlaced.BidCount` authoritatively (OQ6 rationale).
   - `HammerPrice` — nullable `decimal`; populated from `ListingSold.HammerPrice`.
   - `WinnerId` — nullable `Guid`; populated from `ListingSold.WinnerId` (or `BuyItNowPurchased.BuyerId` per OQ3).
   - `PassedReason` — nullable `string`; populated from `ListingPassed.Reason` (one of `"NoBids"` or `"ReserveNotMet"`).
   - `FinalHighestBid` — nullable `decimal`; populated from `ListingPassed.HighestBid` (null when `Reason = "NoBids"`).
   - `ClosedAt` — nullable `DateTimeOffset`; populated from whichever terminal arrived (`BiddingClosed.ClosedAt`, `ListingSold.SoldAt`, `ListingPassed.PassedAt`, or `BuyItNowPurchased.PurchasedAt` per OQ3).

   The existing M2 fields (`Id`, `SellerId`, `Title`, `Format`, `StartingBid`, `BuyItNow`, `Duration`, `PublishedAt`) stay byte-identical. Property order per the S1 / M2-S7 style (init-only records, M2 fields first, auction fields grouped after).

2. Handler(s) consuming the five auction integration events. Per OQ1, layout is one of:
   - **Path A:** extend `ListingPublishedHandler` with four additional static `Handle(TEvent, IDocumentSession)` methods — one per new event type. Wolverine's convention-based discovery binds each by message type.
   - **Path B:** add a sibling static class (recommended name: `AuctionStatusHandler`) in `src/CritterBids.Listings/` — same namespace, one `Handle` method per event type. M2's `ListingPublishedHandler.cs` stays byte-identical.

   Each handler: `LoadAsync<CatalogListingView>(message.ListingId)` → mutate fields → `session.Store` → return. `AutoApplyTransactions` commits after handler return. No `OutgoingMessages`, no `IMessageBus`. The load-null arrival ordering question is OQ4; the idempotency shape is OQ6.

3. `src/CritterBids.Listings/ListingsModule.cs` — no schema changes are required. `CatalogListingView` is already registered in the `listings` schema at M2-S7 close, and Marten handles additive document field changes transparently on write. If schema evolution surfaces a first-use surprise (test-DB artifacts, Postgres column drift under Testcontainers reuse, etc.), record in the retro and **flag — do not work around silently**.

4. `src/CritterBids.Api/Program.cs` — wire the `listings-auctions-events` queue on both sides:
   - **Publish side** (five rules minimum; six if OQ3 includes BIN): `opts.PublishMessage<BiddingOpened>().ToRabbitQueue("listings-auctions-events")` and siblings for `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`. The M2-S7 precedent for `listings-selling-events` is the shape to match.
   - **Listen side**: `opts.ListenToRabbitQueue("listings-auctions-events")`.
   - **Verify the API surface first** against `C:\Code\JasperFx\wolverine\` pristine source per S5b's explicit warning — the publish API has moved between Wolverine 4 and 5. Cite the specific file path that grounded whichever API shape was used in the retro.

5. `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs` — 5 new tests, method names **exactly** per milestone doc §7 `CritterBids.Listings.Tests`:
   - `BiddingOpened_SetsCatalogStatusOpen`
   - `BidPlaced_UpdatesCatalogHighBid`
   - `BiddingClosed_SetsCatalogStatusClosed`
   - `ListingSold_SetsCatalogStatusSold`
   - `ListingPassed_SetsCatalogStatusPassed`

   Each test seeds a baseline `CatalogListingView` (via the M2 `ListingPublished` pipeline or direct fixture seed), dispatches the Auctions-side event via `Host.InvokeMessageAndWaitAsync`, and asserts the view's fields after handler commit. Out-of-order arrival behaviour (`BidPlaced` before `BiddingOpened`) is the pre-mortem in OQ4 — the tests must at minimum cover the happy-path arrival order; defensive-arrival coverage depends on OQ4's resolution.

6. `tests/CritterBids.Listings.Tests/Fixtures/` — additive only:
   - Helper to seed a `CatalogListingView` in its published-but-not-opened baseline state, parameterized on listing id / seller / timestamps. Supports tests that don't need the full M2 pipeline to establish the baseline.
   - Helper to dispatch an Auctions-side integration event and wait for Wolverine commit — mirror the existing M2-S7 fixture shape with the minimum delta.
   - Zero changes to existing fixture behaviour; M2-S7 tests (the 4 baseline `CatalogListingViewTests` scenarios) stay byte-identical.

7. *(Optional — M3-D2 call)* `docs/skills/marten-projections.md` OR `docs/skills/domain-event-conventions.md` — milestone doc §8 M3-D2 asks this session to call whether the catalog-extension pattern ("extend an existing view with new fields and handlers rather than introduce a new view") is a pattern-level learning worth documenting now. If yes, append a self-contained subsection naming the precedent (M2-S7 base + M3-S6 extension). If no, record the null call in the retro with rationale. Path A (document in `marten-projections.md`) is the structural fit; Path B (`domain-event-conventions.md`) fits only if the pattern framing emphasises event-type ownership over projection shape.

8. *(Optional — scope-permitting)* `docs/skills/wolverine-sagas.md` bulk skill pass — per S5b retro §"Skill file — append not written (item 9)", four accumulated first-use findings from S4b / S5 / S5b are pending fold-in (`NotFound` named-method convention, saga state minimality re-read pattern, `tracked.NoRoutes` vs `Sent` in test harness, scoped `IMessageBus` resolution). S6 has latitude to land this pass if scope permits. **All four with source citations or none** — partial passes are not acceptable. If deferred, add an explicit line item to the retro's "What M3-S7 should know."

9. *(Conditional on OQ3 — include BIN consumer in slice)* Additional handler method (on whichever class OQ1 lands on) for `BuyItNowPurchased` — transitions `CatalogListingView.Status = "Sold"` with `HammerPrice = message.Price` and `WinnerId = message.BuyerId`, `ClosedAt = message.PurchasedAt`. No preceding `BiddingClosed` per S5b OQ1 Path B — the handler must handle `Status` transition from `"Open"` (or earlier states) directly. Covered by a sixth test `BuyItNowPurchased_SetsCatalogStatusSold`. If OQ3 defers, this item is omitted and the S7 retro names the follow-up slice.

10. `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md` — written last. Gate below.

## Explicitly out of scope

- Any modification to `src/CritterBids.Auctions/` production code — frozen from S5b close (byte-level diff limited to whitespace at most). The saga is feature-complete for M3.
- Any change to `src/CritterBids.Auctions/AuctionsModule.cs` — `AddEventType<T>()` count stays at 8; outcome events remain bus-only per S5b OQ5. If S6 surfaces a need to stream-append outcome events (e.g., for projection rebuild), **flag as an ADR-level question** — do not modify unilaterally.
- Any change to `ListingPublishedHandler.cs`'s M2 `Handle(ListingPublished)` method behaviour — additive-only if OQ1 lands on Path A; byte-identical if Path B.
- Any change to `CatalogListingView`'s M2 fields (`Id`, `SellerId`, `Title`, `Format`, `StartingBid`, `BuyItNow`, `Duration`, `PublishedAt`) — additions only, existing fields byte-identical.
- Any change to M2-S7 tests — the 4 existing `CatalogListingViewTests` baseline scenarios stay byte-identical and green.
- `ListingWithdrawn` consumption in Listings — the Selling-side publisher does not exist at M3 close per milestone doc §3; a premature Listings consumer would fire only on test-fixture synthetic events. Deferred until Selling implements withdrawal.
- `ParticipantBidHistoryView` — W001-9 still targets Listings or Auctions; not in M3 scope per milestone doc §3.
- Watchlist fields (`LotWatchAdded` / `LotWatchRemoved`) — post-M3 per milestone doc §3.
- Frontend consumption of the extended view — M6.
- Any change to `CritterBids.Contracts.Auctions.*` payloads — S1 authored the nine contracts with L2 completeness; if a payload field is missing for a legitimate projection need, **flag rather than modify**. Contract stability is the established discipline.
- Proxy Bid Manager, Session aggregate, flash auction format — M4.
- `GET /api/listings` endpoint changes beyond returning the extended view shape for free. If new query endpoints are required (e.g., filter by `Status = "Open"`), defer to a post-M3 follow-up.
- Concurrency / soak testing on the catalog projection under concurrent bid load — M3-D1 deferral still holds.
- Any `Program.cs` change beyond the queue wiring in item 4 — call out any additional diff in the retro with rationale.
- Rewriting existing sections of `marten-projections.md` / `domain-event-conventions.md` / `integration-messaging.md`. Item 7 is append-only.

## Conventions to pin or follow

Inherit all conventions from M3-S5b and prior. No new behavioural conventions introduced; three M2 / M3 conventions are reinforced:

- **Upsert shape.** Handler loads via `IDocumentSession.LoadAsync<CatalogListingView>(id)`, mutates a copy (records are immutable — `with` expressions or a fresh `new` with preserved M2 fields), and `session.Store`s. `AutoApplyTransactions` commits after handler return. The M2-S7 precedent stands.
- **Queue naming.** `<consumer>-<publisher>-<category>` per `integration-messaging.md` — `listings-auctions-events` fits exactly (consumer = Listings, publisher = Auctions, category = events). Existing `listings-selling-events` (M2) and `selling-participants-events` (M1) are the precedents.
- **Payload completeness on consume.** Per `integration-messaging.md` L2 — the consumer reads whatever fields are on the contract and does not call back to the source BC. Every field needed for the projection must already be on the event payload; verify against the S1-authored contracts before writing each handler.
- **Contract stability.** If an auction contract is missing a field the projection needs, flag rather than modify. S1 authored the contracts with all future consumers in mind; a gap here is a signal, not a license.
- **Reference-doc discipline.** S5b's rule stands: every first-use claim about Wolverine / Marten / Alba behaviour in code or in the retrospective cites its source — AI Skills repo path, pristine local repo file, `CritterStackSamples` example, or Context7 library reference. Training-memory citations are insufficient. OQ4 (out-of-order arrival under RabbitMQ), OQ6 (projection idempotency semantics), and the item 4 queue-wiring API-surface verification are the three points that most need this bar in S6.

S6 pins one new behavioural convention worth flagging in the retro:

- **Projection-extension pattern.** Extending `CatalogListingView` with auction-status fields and a sibling handler class — rather than introducing a new view per bounded-context-producing-events — is the first in-repo application of this pattern. M3-D2 (milestone doc §8) asks this session whether the pattern belongs in `marten-projections.md` or `domain-event-conventions.md`. Whatever the call, the pattern framing is: **one view per logical entity** (a listing has one catalog row across its entire lifecycle), **handlers per event-source BC** (ListingPublishedHandler for Selling-sourced, AuctionStatusHandler for Auctions-sourced), **additive field growth across milestones** (M2 publishes the base, M3 adds auction state, future milestones add settlement / watchlist / etc.).

## Commit sequence (proposed)

1. `feat(listings): extend CatalogListingView with auction-status fields` — item 1. Additive record properties only; no handlers yet. Solution compiles; M2 tests stay green against the unchanged `ListingPublishedHandler`. This is the compiles-but-not-wired checkpoint.
2. `feat(api): wire listings-auctions-events queue publish and listen rules` — item 4. No handler on the listen side yet; Wolverine registers the listener at host startup. M2 tests stay green.
3. `feat(listings): consume BiddingOpened to set catalog Status=Open` — item 2 (handler scaffold + `BiddingOpened` method) + scenario `BiddingOpened_SetsCatalogStatusOpen` + whichever fixture helper from item 6 is first needed.
4. `feat(listings): consume BidPlaced to track current high bid and count` — item 2 (`BidPlaced` method) + scenario `BidPlaced_UpdatesCatalogHighBid`.
5. `feat(listings): consume BiddingClosed to set catalog Status=Closed` — item 2 (`BiddingClosed` method) + scenario `BiddingClosed_SetsCatalogStatusClosed`.
6. `feat(listings): consume ListingSold to finalize catalog with hammer price` — item 2 (`ListingSold` method) + scenario `ListingSold_SetsCatalogStatusSold`.
7. `feat(listings): consume ListingPassed to finalize catalog with passed reason` — item 2 (`ListingPassed` method) + scenario `ListingPassed_SetsCatalogStatusPassed`.
8. *(conditional on OQ3 Path (a))* `feat(listings): consume BuyItNowPurchased to finalize catalog via BIN path` — item 9 + scenario `BuyItNowPurchased_SetsCatalogStatusSold`. Omit if OQ3 defers.
9. *(optional, M3-D2 dependent)* `docs(skills): document projection-extension pattern` — item 7.
10. *(optional, scope-permitting)* `docs(skills): bulk-fold accumulated M3 saga skill findings` — item 8.
11. `docs: write M3-S6 retrospective` — item 10.

Each implementation commit ships its production code + its scenario test atomically so `git bisect` stays clean at every SHA. S5b established this discipline (its commit 4 landed ahead of commit 3 for exactly this reason) — S6 inherits it without ceremony.

## Acceptance criteria

- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test CritterBids.slnx` — 79-test baseline preserved; +5 new tests green (+6 if OQ3 includes BIN); zero skipped, zero failing; **total 84 (or 85 with BIN)**
- [ ] `src/CritterBids.Listings/CatalogListingView.cs` — extended with at least the 10 auction-status fields from item 1; the 8 M2 fields byte-identical
- [ ] Handler(s) exist consuming `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed` — layout per OQ1 resolution; each handler uses the M2-S7 `LoadAsync` + mutate + `Store` upsert shape; no `IMessageBus` references; no `OutgoingMessages` returns
- [ ] `src/CritterBids.Api/Program.cs` — `listings-auctions-events` queue wired on both publish and listen sides; five publish rules minimum (six if OQ3 includes `BuyItNowPurchased`); the API surface used is cited in the retrospective with a specific source file path
- [ ] All 5 (or 6) test methods in `CatalogListingViewTests.cs` named exactly per milestone doc §7, each green
- [ ] `BiddingOpened_SetsCatalogStatusOpen` — asserts `Status` transitions to the "Open" state (string or enum per OQ2); `ScheduledCloseAt` populated from the event
- [ ] `BidPlaced_UpdatesCatalogHighBid` — asserts `CurrentHighBid = message.Amount`; `BidCount = message.BidCount` (authoritative, not incremented); `CurrentHighBidderId` populated per OQ5 resolution
- [ ] `BiddingClosed_SetsCatalogStatusClosed` — asserts `Status` transitions to "Closed"; `ClosedAt` populated from the event
- [ ] `ListingSold_SetsCatalogStatusSold` — asserts `Status` transitions to "Sold"; `HammerPrice`, `WinnerId`, `ClosedAt` populated
- [ ] `ListingPassed_SetsCatalogStatusPassed` — asserts `Status` transitions to "Passed"; `PassedReason` ∈ {`"NoBids"`, `"ReserveNotMet"`}; `FinalHighestBid` null when `Reason = "NoBids"`, populated otherwise
- [ ] Zero direct references from `CritterBids.Listings` to `CritterBids.Auctions` — cross-BC coupling stays contract-only per the modular monolith rule in CLAUDE.md
- [ ] `CritterBids.Listings.csproj` `ProjectReference` count unchanged (Contracts only)
- [ ] `src/CritterBids.Auctions/` byte-level diff vs S5b close: **none** — no production code, no module config, no test file
- [ ] `src/CritterBids.Listings/ListingPublishedHandler.cs` byte-level diff vs M2-S7 close: **none** if OQ1 lands on Path B; additive-only if Path A
- [ ] `src/CritterBids.Listings/CatalogListingView.cs` — the 8 M2 fields byte-identical
- [ ] M2-S7 `CatalogListingViewTests` scenarios (the 4 baseline tests) unchanged and green
- [ ] No `[Obsolete]`, `#pragma warning disable`, or `throw new NotImplementedException()` in production
- [ ] `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is **not optional** and is **not a footnote**. It is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test CritterBids.slnx` shows all tests green and `dotnet build` shows 0 errors + 0 warnings. If any test fails or any warning lands, fix the code first, then write the retro.

Retrospective content requirements:

- Baseline numbers (79 tests before, 84 or 85 after) with a phase table matching the S5b retro shape
- Per-item status table mirroring the "In scope (numbered)" list with commit references
- Each of the six Open Questions below answered with which path was taken and why — citations for OQ1, OQ2, OQ4, OQ6 grounded in AI Skills / pristine-repo / `CritterStackSamples` per the reference-doc discipline. Training-memory citations are insufficient.
- The M3-D2 call (document projection-extension pattern now, or defer) with rationale
- Whether the item 8 skill bulk-pass landed; if deferred, a line item for M3-S7's scope
- Any blocker: verbatim error message, root cause, fix path. Particular attention to first-use surprises around out-of-order event arrival under RabbitMQ, the `opts.PublishMessage<T>()` API shape on Wolverine 5, `LoadAsync` returning null on a view that hasn't been published yet, or any projection-extension concurrency wrinkle under `AutoApplyTransactions`.
- A **"What M3-S7 should know"** section covering at minimum:
  - Whether the accumulated skill-pass debt (S4b, S5, S5b first-use findings + any S6 findings) was discharged in this slice or still awaits S7
  - Final `CatalogListingView` field inventory as emitted post-S6 — the single source of truth for the M3 retrospective's "what the catalog looks like at M3 close" summary
  - Any ADR candidate surfaced (e.g., if OQ5 resolved to redact `CurrentHighBidderId`, the rationale lives in a new ADR)
  - The Listings-side `BuyItNowPurchased` subscription status (included in slice or deferred per OQ3) and the expected follow-up slice number if deferred
  - The `listings-auctions-events` queue's operational posture at M3 close — wired in both dev and tests, confirmed in the Aspire dashboard, no known gaps
  - Any scope that surfaced in S6 which *must* land before M3 close, so S7's prompt can size itself correctly

## Open questions (pre-mortems — flag, do not guess)

1. **Handler layout — extend `ListingPublishedHandler` or add a sibling class?**
   - **Path A:** four new static `Handle(TEvent, IDocumentSession)` methods on the existing `ListingPublishedHandler`. Single file; Wolverine discovers by message type. Class name (`ListingPublishedHandler`) stops accurately describing contents.
   - **Path B:** new sibling static class `AuctionStatusHandler` in the same namespace; M2's `ListingPublishedHandler.cs` stays byte-identical.

   **Recommend Path B** — preserves M2 frozen-file discipline, the extension is legible in the PR diff, the class name describes its purpose. Flag if Wolverine's handler discovery has an edge case that makes sibling classes in the same namespace ambiguous (unlikely — pristine repo usage shows this pattern routinely). Cite Wolverine's handler-discovery source file if Path B needs defending.

2. **`Status` as string or enum on `CatalogListingView`?** M2's `Format` field is a string (`"Flash"` or `"Timed"`); symmetry argues for string.
   - **Path A (string):** transitions by string-literal assignment; consistent with M2's `Format`; zero shared-enum cross-BC dependency.
   - **Path B (enum):** `CatalogListingStatus { Published, Open, Closed, Sold, Passed }` defined in `CritterBids.Listings`; type-safe but any future BC wanting to filter by status needs to reference it.

   **Recommend Path A** — consistency with the M2-S7 `Format` precedent and avoids a shared-enum dependency. Flag if string-based status comparison creates a downstream issue (e.g., a handler forgets a case and silently does nothing). Cite the M2-S7 `Format` precedent in the retro.

3. **Does `BuyItNowPurchased` consumption land in S6 or defer?** S5b retro §"What M3-S6 should know" §5 is explicit: *"S6's catalog handler should subscribe to `BuyItNowPurchased` directly rather than expecting it to follow a `BiddingClosed`."* Milestone doc §7 lists only five scenarios — `BuyItNowPurchased_SetsCatalogStatusSold` is not among them.
   - **Path (a) — include:** add handler + sixth test. Pays the consumer-pairing cost at contract time.
   - **Path (b) — defer:** ships exactly the five milestone-doc-listed tests. Strict literal reading of milestone scope.
   - **Path (c) — handler-only:** ship the production code but no new named test; cover via an existing combined test or skip coverage.

   **Recommend Path (a)** — matches S5b's explicit recommendation, avoids a known gap at M3 close, honours the L2 publish-consumer-pairing discipline. Milestone doc's five-scenario list is read as non-exhaustive (the timer-path scenarios specifically), not closed. Flag if Path (a) balloons the slice beyond one-PR scope — fallback is Path (c) for S6 plus a named follow-up, not Path (b).

4. **Out-of-order event arrival under RabbitMQ.** Per-listing ordering is outbox-atomic at the saga side per S5b retro §"What M3-S6 should know" §3, but cross-queue ordering between `listings-selling-events` (`ListingPublished`) and `listings-auctions-events` (`BiddingOpened` and downstream) is **not guaranteed**. Observable orderings:
   - `ListingPublished` → `BiddingOpened` → `BidPlaced` → … (happy path)
   - `BiddingOpened` → `ListingPublished` → `BidPlaced` → … (cross-queue race; rare but possible)
   - `BidPlaced` → `BiddingOpened` → … (cross-queue race; same-queue atomicity prevents intra-queue reorder)

   Projection postures:
   - **Path I — fragile:** assume happy-path. `LoadAsync` on a non-existent view returns null; handler throws. Production detonates on the race.
   - **Path II — tolerant upsert:** `LoadAsync` returning null → create a minimal view with only the fields the current event carries; subsequent `ListingPublished` arrival fills in M2 fields via a property-level merge rather than overwrite.
   - **Path III — Marten `Patch`:** use partial updates keyed on listing id, letting Marten handle insert-or-update without a prior read.

   **Recommend Path II** — most defensive, consistent with bus-only (no replay) per S5b OQ5. Cite Marten's `LoadAsync`-returns-null semantics and `marten-projections.md`'s upsert discipline. Flag if Path III emerges as structurally simpler during implementation — the decision belongs in the retro with rationale. The hard invariant: **no arrival order for the five M3 auction events leaves the projection in an unrecoverable state**.

5. **Privacy — does `CurrentHighBidderId` belong on the catalog view?** The contract `BidPlaced.BidderId` carries the participant id. Exposing it on the M2-S7 public `GET /api/listings/{id}` leaks the bidder's id before auction close — different from eBay's masked-username convention for the current high bidder.
   - **Path A:** include `CurrentHighBidderId` on `CatalogListingView` — straightforward; endpoint-layer filtering in M6 auth pass can redact for non-seller viewers.
   - **Path B:** omit; track high bid and count, not identity. Seller-facing views would need a separate read model.
   - **Path C:** include with a documented "internal-only at this layer; redact at endpoint" note — defers the privacy decision to M6.

   **Recommend Path C** — unblocks S6 without prejudging the M6 auth design. Flag if the endpoint-layer redaction plan is unclear enough that M6 would have to walk this back. Cite eBay's or another public-auction-platform public-vs-seller view model as precedent if one is available, or defer explicitly to M6 with dated rationale.

6. **Projection-side idempotency under at-least-once redelivery.** RabbitMQ with at-least-once delivery means each auction event may be delivered more than once. Monotonically-set fields are naturally idempotent (`Status = "Sold"` twice still lands on `"Sold"`), but **`BidPlaced` is not**: naive `BidCount++` under redelivery double-counts.
   - **Path (a):** use `BidPlaced.BidCount` authoritatively — set `CatalogListingView.BidCount = message.BidCount` rather than increment. The DCB guarantees `BidCount` monotonicity at source, so last-write-wins is self-correcting under redelivery.
   - **Path (b):** track `HashSet<Guid> ProcessedBidIds` on the view and dedupe on `BidPlaced.BidId`. Allocation growth proportional to bid count; doesn't survive view rebuilds.

   **Recommend Path (a)** — leverages the `BidCount` monotonicity convention established in the S5 retro (originally for saga idempotency, now reused for projection idempotency). Cite the S5 retro's OQ2 Path (b) resolution as precedent. Flag if a concurrency soak surfaces a counterexample — that is a M3-D1 follow-up, not an S6 workaround.
