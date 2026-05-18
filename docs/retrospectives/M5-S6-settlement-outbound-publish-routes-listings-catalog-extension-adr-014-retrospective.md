# M5-S6: Settlement Outbound Publish Routes + Listings Catalog `Settled` Status + ADR-014 + M5 Close — Retrospective

**Date:** 2026-05-17
**Milestone:** M5 — Settlement BC (slice 6 of 6; M5 milestone closer)
**Slice:** S6 of 6
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M5-S6-settlement-outbound-publish-routes-listings-catalog-extension-adr-014.md`
**Narrative (joint authority):** `docs/narratives/002-winner-clears-settlement.md`

---

## Baseline

- 111 tests passing at M5-S5 close (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + 24 Settlement); `dotnet build CritterBids.slnx` 0 errors, 24 pre-existing NU1904 Marten vulnerability warnings; M5-S5 closed at SHA `e28210a`
- `src/CritterBids.Settlement/` carries the full M5-S5 surface — seven-phase saga with `Handle(FailSettlement)`, BIN-source start handler overload, `BidderCreditView` projection, four Marten document tables in the settlement schema
- `src/CritterBids.Listings/` carries M2-S7 + M3-S6 catalog surface — `ListingPublishedHandler` (M2 base) + `AuctionStatusHandler` (M3-S6 auction-status fields). No Settlement-side handler yet
- `src/CritterBids.Api/Program.cs` carries six RabbitMQ publish routes from M5-S3 / M5-S5 (Settlement-inbound from Selling, Auctions, Participants); no outbound Settlement publish routes
- ADR-014 placeholder in `docs/decisions/README.md` (status: `🔒 Reserved for M4-S6 authorship`)
- ADR-007 Gate 4 status: re-deferred at M4-S1; trigger fired at M5-S1 (PR #25); unamended through M5-S5
- M5 milestone status: `Planning`; six slices planned, five shipped; ADR-007 Gate 4 + M5 milestone retro are the gating M5-close-blocking items

---

## Items completed

| Item | Description |
|------|-------------|
| S6a | `src/CritterBids.Api/Program.cs` — new `listings-settlement-events` queue route (Settlement publishes `SettlementCompleted`; Listings listens). Mirrors the `listings-auctions-events` shape from M3-S6 |
| S6b | `src/CritterBids.Listings/SettlementStatusHandler.cs` (new) — `public static class` with single `Handle(SettlementCompleted, IDocumentSession, CancellationToken)`. Tolerant-upsert + `"Sold"` → `"Settled"` transition guard + status-preservation on non-`"Sold"` arrival states. Sibling to `AuctionStatusHandler` per ADR-014 Path A |
| S6c | `src/CritterBids.Listings/CatalogListingView.cs` — added nullable `SettledAt` field in a new M5-S6 additive block. Amended `Status` field doc comment to extend the transition list with `"Sold"` → `"Settled"` and the BIN-path / Passed-terminal callouts. Amended the class-level `<summary>` to name `SettlementStatusHandler` and the `listings-settlement-events` queue |
| S6d | `src/CritterBids.Listings/ListingPublishedHandler.cs` — **Q3 resolution.** Amended from unconditional `session.Store(new CatalogListingView { ... })` to load-and-preserve pattern mirroring `PendingSettlementHandler.Handle(ListingPublished)`. Signature changed `void` → `async Task` + `CancellationToken`. Preserves any downstream-handler state (`Status`, `ClosedAt`, `SettledAt`, all auction-status fields) on re-delivery; M2 fields always overwritten with the publish payload |
| S6e | `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs` — `SeedCatalogEntry` helper updated for the new async handler signature (one-line ripple from S6d) |
| S6f | `tests/CritterBids.Listings.Tests/SettlementStatusHandlerTests.cs` (new) — three `[Fact]`s: `"Sold"` → `"Settled"` happy-path with field preservation; `"Passed"` status-preservation guard; tolerant-upsert on missing row (cross-queue race) |
| S6g | `src/CritterBids.Api/Program.cs` — two more publish routes: `SellerPayoutIssued` → `relay-settlement-events` (Workstream B per Q1 = B.2) and `PaymentFailed` → `operations-settlement-events` (per M5-S5 retro item #1 — flips the v0.1 prompt's deferred stance). No `ListenToRabbitQueue` for either (post-M5 consumers) |
| S6h | `tests/CritterBids.Settlement.Tests/SellerPayoutIssuedPublishRouteTests.cs` (new) — single `[Fact]` exercising the full §9.1 saga flow and asserting `SellerPayoutIssued` lands in `tracked.NoRoutes` with the W003-canonical payload (`PayoutAmount: 76.50, FeeDeducted: 8.50`). NoRoutes pattern per the M3-S5b `AuctionClosingSagaTests` precedent — see Key Learnings §1 below for the tracked.Sent-vs-NoRoutes investigation |
| S6i | `docs/decisions/014-cross-bc-read-model-extension-shape.md` (new) — ADR-014 body authored. Status: ✅ Accepted. Path A (one view, sibling handlers per source BC, additive fields). Multi-source sibling sub-question deferred to a third lived application. Evidence: M3-S6 `AuctionStatusHandler` + M5-S6 `SettlementStatusHandler` |
| S6j | `docs/decisions/README.md` — ADR-014 row flipped `🔒 Reserved` → `✅ Accepted`; reservation paragraph updated; ADR-007 row updated to reflect M5-S6 Gate 4 closure |
| S6k | `docs/decisions/007-uuid-strategy.md` — new section "Event Row ID Decision — Closed by Lived Fact (M5 close, 2026-05-17)" added; status line at the top updated to reflect Gate 4 closure; gates table extended with the M5-S6 status |
| S6l | `docs/skills/marten-projections.md` §"View Extension Across Milestones" — diagram updated (`SettlementStatusHandler` moved from "M4 (planned)" to "M5-S6"; `ObligationsStatusHandler` moved to "post-M5"); commented M4 settlement-fields block in the view example replaced with the actual M5-S6 `SettledAt` field; "In-repo ground" line updated to cite M5-S6 as the second lived example and cross-reference ADR-014 |
| S6m | `docs/milestones/M5-settlement-bc.md` — Status `Planning` → `Shipped (M5-S6 closed 2026-05-17)`; v0.2 entry added to Document History summarizing M5 close |
| S6n | This retrospective |
| S6o | `docs/retrospectives/M5-retrospective.md` (new) — M5 milestone-level retro per the M3 milestone retro precedent |

The prompt structured scope as three commits:

| Commit | Items covered | SHA |
|--------|---------------|-----|
| 1 — `feat(settlement,listings): wire listings-settlement-events RabbitMQ route; author SettlementStatusHandler; extend CatalogListingView with SettledAt; amend ListingPublishedHandler to preserve downstream-handler state` | S6a, S6b, S6c, S6d, S6e, S6f | `b61995a` |
| 2 — `feat(settlement): wire SellerPayoutIssued + PaymentFailed cross-BC publish routes; tracked.NoRoutes test for the payout dispatch` | S6g, S6h | `7a68237` |
| 3 — `docs(decisions,settlement): author ADR-014 + close ADR-007 Gate 4 + M5 milestone retros + supporting doc forwarding` | S6i, S6j, S6k, S6l, S6m, S6n, S6o | (this commit) |

---

## Workstream A — `SettlementCompleted` route + `SettlementStatusHandler` + `CatalogListingView.SettledAt` + Q3 amendment

### Shape

Three sibling handler classes now own disjoint field sets on `CatalogListingView`:

```
ListingPublishedHandler (M2-S7, amended M5-S6) ← Selling: ListingPublished
                                                  Owns: M2 base fields + preserve guard
AuctionStatusHandler (M3-S6)                   ← Auctions: 6 events
                                                  Owns: auction-status fields
SettlementStatusHandler (M5-S6)                ← Settlement: SettlementCompleted
                                                  Owns: "Sold" → "Settled" + SettledAt
```

The `Status` field is the shared workflow-position vocabulary; each handler owns a subset of transitions. The M5-S3 `PendingSettlementHandler` status-preservation discipline is now carried across all three Listings sibling handlers — a non-matching arrival state no-ops without throwing.

### Q3 resolution — `ListingPublishedHandler` amendment

The original M2-S7 handler did an unconditional `session.Store(new CatalogListingView { ... })`. The M5-S6 `SettlementStatusHandler`'s tolerant-upsert posture creates a structurally near-impossible race: if `SettlementCompleted` arrived before `ListingPublished`, the handler would create a minimal row at `Status = "Settled"`, then the original `ListingPublishedHandler` would overwrite back to `Status = "Published"` — losing the terminal state.

The M5-S6 amendment changes `ListingPublishedHandler` to mirror `PendingSettlementHandler.Handle(ListingPublished)`'s load-and-preserve pattern:
- M2 fields (the contract's authoritative source) are always overwritten with the publish payload
- Downstream-handler fields (Status, ClosedAt, SettledAt, all auction-status fields) are preserved from `existing` if present, default if first delivery
- Signature changes `void` → `async Task` + `CancellationToken`

The 11 existing M2 + M3-S6 baseline tests in `CatalogListingViewTests.cs` pass unchanged — the load-and-preserve behavior is byte-equivalent when no prior row exists, which is what every existing test exercises.

### `SettlementStatusHandlerTests.cs` shape

Three facts mirror the M3-S6 `AuctionStatusHandler` direct-invocation pattern (per `project_wolverine_sticky_handler` memory — `opts.ListenToRabbitQueue` creates a sticky binding, and the test fixture's `DisableAllExternalWolverineTransports` means `Host.InvokeMessageAndWaitAsync` raises `NoHandlerForEndpointException`; direct invocation is the supported alternative):

1. **`Handle_TransitionsCatalogListingViewFromSoldToSettled`** — seed view at `Status = "Sold"` (post-`ListingSold`); dispatch `SettlementCompleted`; assert `Status = "Settled"`, `SettledAt = message.CompletedAt`, all prior fields preserved
2. **`Handle_OnPassedListing_PreservesPassedStatus`** — defensive: seed view at `Status = "Passed"`; dispatch `SettlementCompleted`; assert `Status` unchanged, `SettledAt` remains null. The financial workflow's reserve-not-met branch emits `PaymentFailed` not `SettlementCompleted`, so this case should never arrive in practice — the guard is structural correctness
3. **`Handle_OnMissingRow_TolerantUpsertCreatesMinimalSettledRow`** — defensive: no prior row; dispatch `SettlementCompleted`; assert minimal row created with `Id`, `Status = "Settled"`, `SettledAt`; M2 fields default-initialized. The M5-S6 amendment to `ListingPublishedHandler` will preserve the Settled state when `ListingPublished` later arrives

---

## Workstream B — `SellerPayoutIssued` + `PaymentFailed` publish routes

### Q1 resolved to B.2 + Q-Add-1 wired all three routes

Per the prompt's Open Question Q1 and the M5-S5 retro item #1 recommendation. Two publish routes added to `Program.cs`:

- `SellerPayoutIssued` → `relay-settlement-events` (with one tracked.NoRoutes test asserting emission)
- `PaymentFailed` → `operations-settlement-events` (no test; queue-topology completeness only)

No `ListenToRabbitQueue` calls on either — Relay BC and Operations BC are post-M5. When those BCs ship, the consumer side wires up without requiring any Settlement-side change.

### Why `tracked.NoRoutes` instead of `tracked.Sent`

The prompt framed Workstream B's test as "exercise the publish route end-to-end via tracked.Sent." The first implementation attempt followed that framing literally:

```csharp
var tracked = await _fixture.Host.TrackActivity()
    .IncludeExternalTransports()
    .InvokeMessageAndWaitAsync(listingSold);

var sent = tracked.Sent.MessagesOf<SellerPayoutIssued>().ToList();
sent.ShouldHaveSingleItem();  // FAIL: 0 items
```

Diagnostic output (added temporarily to investigate) showed `Sent: 0, NoRoutes: 1, Executed: 0`. The message was emitted by the saga but landed in `tracked.NoRoutes`, not `tracked.Sent`.

The cause: the Settlement test fixture calls `services.DisableAllExternalWolverineTransports()`, which **strips the RabbitMQ publish route entirely rather than stubbing it**. The route registered in `Program.cs` is gone in the test process. Adding `.IncludeExternalTransports()` to the tracker doesn't help because there's no external transport to include.

The Auctions BC's M3-S5b `AuctionClosingSagaTests` already follows the established codebase pattern for this case: `tracked.NoRoutes.MessagesOf<BiddingClosed>().ShouldHaveSingleItem()`. The Participants BC takes a different approach — its fixture adds a stub local-queue route via `WolverineExtension.PublishMessage<T>().ToLocalQueue("...-stub")` so `tracked.Sent` surfaces the message. Both patterns assert the saga's emission contract; neither *truly* asserts the production publish route wiring (the wire goes through `ConfigureServices` plumbing that test fixtures bypass).

M5-S6 follows the Auctions pattern. The test docstring explains the choice and points to the M3-S5b precedent. **The production route wiring is asserted by code review of `Program.cs`, not by the test.** This is recorded in the M5 retro under Key Learnings.

---

## Workstream C — ADR-014 authoring

### Body shape

Mirrors the ADR-019 layout (Context, Options Considered, Decision, Revisit trigger, Consequences). The Options section explicitly covers Path A (chosen), Path B (one view per source BC + UI-side join, rejected), Path C (native MultiStreamProjection, rejected by BC-isolation rule), and the multi-source-sibling sub-question (deferred). The Decision section binds future contributing BCs to five concrete constraints: one handler class per source BC, tolerant-upsert per handler, disjoint field sets, status-preservation guards, and the seed-handler load-and-preserve discipline.

### Multi-source sub-question deferral

Per Open Question Q4: defer the sub-question to a third lived application that actually needs multi-source coordination — the natural candidate remains M4-S6's `SessionMembershipHandler` (originally framed as multi-source: `SessionCreated`/`ListingAttachedToSession`/`SessionStarted` from Auctions plus `ListingWithdrawn` from Selling).

Both M3-S6 and M5-S6 are single-source siblings; resolving the multi-source framing on single-source evidence would be premature. The ADR's body records the framework (Sub-Option A vs Sub-Option B) and explicitly defers; M4-S6 (or whatever ships third) picks A or B with its own lived evidence and amends ADR-014.

### Cross-references shipped

- `docs/decisions/README.md` ADR-014 row: `🔒 Reserved` → `✅ Accepted`; reservation paragraph in §"Naming Convention" rewritten to reflect M5-S6 authorship
- `docs/skills/marten-projections.md §"View Extension Across Milestones"`: diagram updated with M5-S6 as the lived example; example view updated with the actual `SettledAt` field; "In-repo ground" line now names M5-S6 as the second example and cross-references ADR-014

---

## Workstream D — Documentation forwarding

### ADR-007 Gate 4 — closed by lived fact

Per Open Question Q2 / Q5: close with engine-default lived-fact evidence. New section "Event Row ID Decision — Closed by Lived Fact (M5 close, 2026-05-17)" added to `docs/decisions/007-uuid-strategy.md`. Status line at the top updated. The closure rationale: M5 shipped three event-row surfaces (`PendingSettlement` projection, financial event stream, `BidderCreditView` projection) under engine-default row IDs through M5-S3/S4/S5 without surfacing any row-ID friction. The Settlement BC is the last Marten BC the CritterBids backend ships in M5 scope; future BCs are post-M5 and any row-ID strategy adopted now would not apply uniformly to them anyway. The original re-deferral rationale (insert-locality benefits surface under sustained high-write load not exercised by the test suite) is acknowledged; if a future production incident motivates row-ID friction analysis, it becomes a separate ADR with its own evidence.

### M5 milestone doc status flip

`docs/milestones/M5-settlement-bc.md` — Status `Planning` → `Shipped (M5-S6 closed 2026-05-17)`. v0.2 entry added to Document History summarizing M5 close: six slices shipped, 116 tests passing, ADR-014 authored at M5-S6, ADR-007 Gate 4 closed by lived fact, `PaymentFailed` publish route wired per the M5-S5 retro recommendation.

---

## Findings against narrative

The slice operated against narrative 002 as a Moment-grain implementation reference. Moment 5 of narrative 002 dramatizes the bidder-visible outcome (the "you won" SignalR push); the catalog catalog `Status = "Settled"` transition is the offstage-but-load-bearing read-model state that supports any future "your settled listings" view. The narrative does not require S6 amendment.

| Lane | Action |
|---|---|
| `narrative-update` | None. Narrative 002 Moment 5's bidder-visible outcome is the Relay broadcast surface (post-M5); the M5-S6 catalog transition is the offstage state that supports it |
| `workshop-update` | None directly. ADR-014's body authoring is the substantial workshop-grade artifact landing in S6; it carries its own decision authority |
| `code-update` | The `ListingPublishedHandler` Q3 amendment is the only behavioral change to lived code (load-and-preserve replaces unconditional store). All existing tests pass without amendment |
| `document-as-intentional` | The `tracked.NoRoutes` vs `tracked.Sent` pattern for cross-BC publish-route tests is a codebase-established pattern (M3-S5b precedent) that the M5-S6 prompt's "tracked.Sent" framing didn't acknowledge. Documented in Key Learning §1 below and in the test file's class-level docstring |

The cumulative narrative 002 findings ledger at M5 close: F001 ✓ (PR #20), F002 ✓ (PR #25), F003 ✓ minimum-scope (PR #20), F004 ✓ (PR #25), F005 ✓ (PR #25). No new findings against narrative 002 in S6.

---

## Key learnings

### 1. `tracked.Sent` is unreachable for external-routed messages when the fixture disables external transports

The M5-S6 prompt's "B.2 — route + tracked.Sent test" framing assumed `IncludeExternalTransports()` would surface the message. It does not — `DisableAllExternalWolverineTransports()` removes the route entirely (doesn't stub it), so the message lands in `tracked.NoRoutes` and `IncludeExternalTransports()` has nothing to include.

The two codebase-established workarounds:
- **`tracked.NoRoutes` assertion** (M3-S5b Auctions precedent). The saga's emission contract is asserted; the production publish route wiring is asserted by code review of `Program.cs`.
- **Stub local-queue route via `IWolverineExtension`** (M1-S6 Participants precedent — `SellingBcDiscoveryExclusion.Configure` adds `options.PublishMessage<SellerRegistrationCompleted>().ToLocalQueue("selling-participants-stub")`). The stub route makes `tracked.Sent` surface the message; the test asserts via Sent. The production route is still asserted by code review (the test would pass even without the production wire).

Neither pattern proves the production publish route wiring end-to-end. End-to-end proof would require a test that uses real RabbitMQ (Testcontainers + no `DisableAllExternalWolverineTransports`) — heavy, slow, and not the codebase's lived idiom.

M5-S6 followed the M3-S5b NoRoutes pattern. Both this finding and the prompt-framing-vs-reality gap are recorded for M5-S6's "what next session should know" hand-off if a future skill amendment lands.

### 2. The Q3 amendment to `ListingPublishedHandler` was higher-impact-than-scoped

The M5-S6 prompt's Q3 framing presented three options for the lazy-init-race correctness gap:
- (a) Amend `ListingPublishedHandler` to mirror `PendingSettlementHandler`
- (b) Don't amend; throw on missing row in `SettlementStatusHandler`
- (c) Don't amend; document the race as known minor risk

Option (a) was chosen. The amendment added ~15 lines of field-preserving logic and changed the signature `void` → `async Task` + `CancellationToken`. The test surface ripple was minimal — the `CatalogListingViewTests.SeedCatalogEntry` helper needed a one-line await update — but the **handler's behavior contract changed** from "blindly seed" to "seed-and-preserve." This is documented in the handler's triple-slash docstring and in ADR-014's Decision §5 (seed-handler load-and-preserve discipline).

The change is correctness-grade rather than behavior-grade — the existing tests pass unchanged because the new behavior is byte-equivalent on the no-prior-row path that all existing tests exercise. But future maintainers reading `ListingPublishedHandler` should understand that the load-and-preserve discipline is now part of the M5-S6 ADR-014 contract, not a local optimization.

### 3. Cross-BC fixture exclusion matrix is unchanged at M5-S6 close

The Listings.Tests fixture already excluded Settlement (M5-S3 precedent for `PendingSettlementHandler`). The new `SettlementStatusHandler` is in the `CritterBids.Listings` assembly itself — an inbound consumer of a Settlement event, not a Settlement-side handler — so the existing exclusion matrix needs no extension. Settlement.Tests fixture already excludes Listings.

The cumulative matrix at M5 close (unchanged from M5-S5):

| Fixture | Excludes |
|---|---|
| Participants | Selling |
| Selling | (none) |
| Listings | Auctions, Selling, Settlement |
| Auctions | Settlement, Selling, Listings |
| Settlement | Selling, Auctions, Listings |

### 4. `DisableAllExternalWolverineTransports` is the silent ceiling on tracked.Sent semantics in tests

Worth a future skill amendment in `critter-stack-testing-patterns.md` Problem 4 (the `tracked.NoRoutes` vs `tracked.Sent` problem already documented from M3-S5b). The specific addition: clarify that `IncludeExternalTransports()` only helps when external transports are *not disabled* — under `DisableAllExternalWolverineTransports`, external-routed messages land in NoRoutes regardless of the tracker configuration. This is the third codebase encounter with the pattern (M3-S5b + M5-S6); a skill callout earns the documentation pass at the next skill-maintenance session.

---

## Findings against the wolverine-sagas / marten-projections skills

- **`wolverine-sagas.md`** — no amendments in S6. The saga shape is unchanged from M5-S5; cross-BC publish routes are infrastructure-side, not saga-side.
- **`marten-projections.md §"View Extension Across Milestones"`** — amended at S6. Diagram entries for `SettlementStatusHandler` updated from "M4 (planned)" to "M5-S6"; example view's commented M4 settlement block replaced with the actual `SettledAt` field; "In-repo ground" line names the M5-S6 second example and cross-references ADR-014. This is the one-line callout the prompt specified.
- **`critter-stack-testing-patterns.md`** — defer to a future skills-maintenance pass per Key Learning §4 above. The `IncludeExternalTransports()`-vs-`DisableAllExternalWolverineTransports` clarification builds on the existing Problem 4 surface without restructuring it.

---

## Verification checklist

- [x] `src/CritterBids.Api/Program.cs` — `listings-settlement-events` publish + listen route added; `relay-settlement-events` and `operations-settlement-events` publish routes added (no listeners)
- [x] `src/CritterBids.Listings/SettlementStatusHandler.cs` exists with `Handle(SettlementCompleted, IDocumentSession, CancellationToken)` per the tolerant-upsert + `"Sold"` → `"Settled"` transition guard shape
- [x] `src/CritterBids.Listings/CatalogListingView.cs` — `SettledAt` nullable field added; `Status` doc comment extended with the new transitions; class summary names `SettlementStatusHandler`
- [x] `src/CritterBids.Listings/ListingPublishedHandler.cs` — Q3 amendment landed (load-and-preserve pattern; signature `async Task` + `CancellationToken`)
- [x] `tests/CritterBids.Listings.Tests/SettlementStatusHandlerTests.cs` exists with three `[Fact]`s covering the `"Sold"` → `"Settled"` happy path, the `"Passed"` guard, and the tolerant-upsert-on-missing-row path
- [x] `tests/CritterBids.Settlement.Tests/SellerPayoutIssuedPublishRouteTests.cs` exists with one `[Fact]` exercising the §9.1 flow and asserting via `tracked.NoRoutes`
- [x] `docs/decisions/014-cross-bc-read-model-extension-shape.md` exists; status ✅ Accepted; body per Workstream C outline
- [x] `docs/decisions/README.md` ADR-014 row updated; reservation paragraph updated; ADR-007 row updated to reflect Gate 4 closure
- [x] `docs/decisions/007-uuid-strategy.md` — Gate 4 closed by lived fact; new section added; status line updated
- [x] `docs/skills/marten-projections.md §"View Extension Across Milestones"` — one-line callout + diagram + example view update
- [x] `docs/milestones/M5-settlement-bc.md` — Status `Planning` → `Shipped`; v0.2 doc-history entry
- [x] `docs/retrospectives/M5-S6-...-retrospective.md` — this file
- [x] `docs/retrospectives/M5-retrospective.md` — M5 milestone-level retro (next file authored in this commit)
- [x] `dotnet build CritterBids.slnx` — 0 errors (24 pre-existing NU1904 Marten warnings unchanged)
- [x] `dotnet test CritterBids.slnx` — all green; 115 tests pass at M5 close (1 Api + 36 Auctions + 1 Contracts + 14 Listings + 6 Participants + 32 Selling + 25 Settlement)

---

## What M5 retro should record

S6 produced no new M5-close-blocking items. The three items M5-S5's retro forwarded for M5-retro disposition are all resolved in this slice:

1. **ADR 007 Gate 4 status** — Closed by lived fact at M5-S6. ADR amended; status line + new section + gates table all reflect closure.
2. **W003 Phase 1 Part 7 `BidderCreditView` lazy-init posture** — Not in M5-S6 scope (no behavior change to the M5-S5 surface). Recorded as `document-as-intentional` (MVP-permanent) in the M5 milestone retro per the prompt's framing.
3. **Duplicate-`ListingSold`-after-Failed contract** — Not in M5-S6 scope. The M5-S5 test (`FailSettlement_DuplicateDispatch_DoesNotRegressTerminalState`) asserts the saga document is removed at `MarkCompleted()`; whether a second `ListingSold` arrival should be rejected by `PendingSettlement.Status == Failed` is a saga-lifecycle question the M5 retro acknowledges and forwards to post-M5 (no concrete trigger; surfaces only if production sees the pattern).

Plus one new item from S6:

4. **`tracked.Sent` vs `tracked.NoRoutes` skill amendment** — `critter-stack-testing-patterns.md` Problem 4 could be extended with the `IncludeExternalTransports`-vs-`DisableAllExternalWolverineTransports` interaction (Key Learning §4 above). Defer to a future skills-maintenance pass.

---

## Document history

- **v0.1** (2026-05-17): Authored at M5-S6 close. Three commits per the prompt's commit sequence: Workstream A (route + handler + view extension + Q3 amendment + tests) in commit 1; Workstream B (two publish routes + tracked.NoRoutes test) in commit 2; Workstreams C + D + this retro in commit 3. All four prompt Open Questions resolved to the "Recommended" option (B.2; wire PaymentFailed; amend ListingPublishedHandler; close ADR-007 Gate 4 by lived fact). Q4 ADR-014 multi-source sub-question deferred per the prompt's default. Test surface: 111 baseline + 4 new = 115 actual at M5 close (3 Listings + 1 Settlement). M5 milestone closed in commit 3 with the status flip + v0.2 doc-history entry + the milestone-level retro.
