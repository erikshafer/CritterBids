# M4-S6: Listings Catalog — Session Membership + Withdrawn Status + ADR-014 Sub-Question — Retrospective

**Date:** 2026-05-20
**Milestone:** M4 — Auctions BC Completion
**Slice:** S6 of 7 (penultimate M4 implementation slice; S6b pre-drafted slot unused — base S6 absorbed all in-scope work)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M4-S6-listings-catalog-session-and-withdrawn.md`
**Baseline:** 148 tests passing · `dotnet build` 0 errors, 24 NU1904 NuGet warnings · M4-S5 closed at the squash-merge of PR #37 (`b995193`); the M4-S6 prompt PR (#38) `71526e1` reset the local branch to main before implementation work began.

---

## Baseline

- 148 tests passing at session open (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 65 Auctions)
- `dotnet build` — 0 errors, 24 pre-existing NU1904 Marten vulnerability warnings (unchanged across M3 / M4 / M5)
- `CatalogListingView` did not carry `SessionId` / `SessionStartedAt`; the `Status` vocabulary did not include `"Withdrawn"`
- No Listings-BC handler existed for the Auctions Session trio or for Selling's `ListingWithdrawn`
- `AuctionStatusHandler.Handle(BiddingOpened)` had no `"Withdrawn"`-preservation guard — the M4-S5 fan-out's emission for a withdrawn-attached listing would flip Status back to `"Open"` (OQ3 Path α observation gap from the M4-S5 retro)
- ADR-014 status was Accepted with the multi-source-sibling sub-question explicitly deferred to "a third lived application that legitimately requires multi-source coordination — most likely M4-S6's `SessionMembershipHandler`"

---

## Phase table

| Phase | After commit | New tests | Total tests | Build | Note |
|-------|--------------|-----------|-------------|-------|------|
| Baseline | `71526e1` | — | 148 | Green | Session open; branch reset to main after PR #38 squash-merge |
| CatalogListingView extension | `9a53e83` | 0 | 148 | Green | Additive nullable fields; Status XML comment block extended for Withdrawn |
| AuctionsSessionHandler + SellingListingWithdrawnHandler | `c517cab` | 0 | 148 | Green | Sub-Option A: two new sibling classes; mirrors M5-S6 SettlementStatusHandler shape |
| AuctionStatusHandler Withdrawn-preservation guard | `0b97d80` | 0 | 148 | Green | Top-of-method load-and-return guard on Handle(BiddingOpened); other five methods byte-identical |
| ListingPublishedHandler M4-S6 field preservation | `f24e62a` | 0 | 148 | Green | Item-4 verification surfaced a gap (named field allow-list, not implicit `with`); two new preservation lines added |
| Six §7 + cross-BC composition tests | `4ea473d` | +6 | 154 | Green | One Theory contributes +2 (the Published / Open pre-state pair); all green on first run |
| ADR-014 amendment | `a20b513` | 0 | 154 | Green | Sub-Option A pinned; status stays Accepted; dual-date line |
| marten-projections.md append | `7aebe37` | 0 | 154 | Green | View Extension Across Milestones extended + new §"Status-Preservation Guards" subsection authored |
| Retrospective | this commit | 0 | 154 | Green | Slice close |

Test count by project at close: 1 Api + 1 Contracts + 6 Participants + **20 Listings** + 36 Selling + 25 Settlement + 65 Auctions = **154** (one above the prompt's nominal exit criterion of 153; the difference is a `[Theory]` with two `InlineData` rows for the two legal `ListingWithdrawn` pre-states landing as two test methods rather than one — see §"Per-item status table" item 6 and the Decisions Inheriting Forward note below).

---

## Per-item status table

| # | Description | Commit |
|---|-------------|--------|
| 1 | `src/CritterBids.Listings/CatalogListingView.cs` — extended additively with `SessionId: Guid?` + `SessionStartedAt: DateTimeOffset?`; Status-transitions XML comment block grew with the two `"Withdrawn"` arrival paths. M2 / M3-S6 / M5-S6 fields byte-identical. | `9a53e83` |
| 2 | `src/CritterBids.Listings/AuctionsSessionHandler.cs` + `src/CritterBids.Listings/SellingListingWithdrawnHandler.cs` — Sub-Option A pinned at session open. `AuctionsSessionHandler` handles `ListingAttachedToSession` (single-listing upsert) + `SessionStarted` (batch-load via `Query<>...Where(Contains)` then iterate). `SellingListingWithdrawnHandler` carries the two-pre-state guard per OQ4. `SessionCreated` intentionally not handled. | `c517cab` |
| 3 | `src/CritterBids.Listings/AuctionStatusHandler.cs` — `Handle(BiddingOpened)` gains the top-of-method Withdrawn-preservation guard (`if (view.Status == "Withdrawn") return;`). Class-level XML comment block references the M4-S6 amendment. Other five `Handle` methods byte-identical to M3-S6 close. | `0b97d80` |
| 4 | `src/CritterBids.Listings/ListingPublishedHandler.cs` — **verification surfaced a gap.** The M5-S6 amendment uses a named-field allow-list (constructs a fresh `CatalogListingView` and copies each downstream-handler field via `existing?.Field`), not implicit `with` semantics. The two new M4-S6 fields required two new preservation lines (`SessionId = existing?.SessionId`, `SessionStartedAt = existing?.SessionStartedAt`). Convention pinned in the XML comment block: every new sibling-handler field below the M2 block adds its own preservation line. | `f24e62a` |
| 5 | `src/CritterBids.Api/Program.cs` — byte-identical to M4-S5 close. No new publish routes, no new queue listen registrations. The three M4-S5 publish routes (Session trio → `listings-auctions-events`) and the M4-S2 routes (`ListingWithdrawn` → `listings-selling-events` + `auctions-selling-events`) cover all M4-S6 traffic. | — |
| 6 | `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs` — four §7 scenarios authored. `ListingAttachedToSession_SetsSessionId` and `SessionStarted_SetsSessionStartedAtForMemberListings` are single `[Fact]`s. `ListingWithdrawn_SetsCatalogStatusWithdrawn` is a `[Theory]` with two `InlineData` rows (Published, Open) per OQ4's "both arrival states should land" framing — counted as +2 at xUnit's grain. `SiblingHandlers_CoexistOnSameView_NoOverwrites` exercises the realistic Flash arrival order across all sibling handlers. | `4ea473d` |
| 7 | `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs` — `BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus` cross-BC composition test. Sequence: seed at Published → ListingAttachedToSession → ListingWithdrawn → BiddingOpened (the fan-out's emission). Asserts Status stays `"Withdrawn"`, `ScheduledCloseAt` stays null (total guard per OQ5 Path α), SessionId preserved, ClosedAt = WithdrawnAt. **Test fails on the unguarded `AuctionStatusHandler` and goes green after item 3.** | `4ea473d` |
| 8 | `tests/CritterBids.Listings.Tests/Fixtures/ListingsTestFixture.cs` — `SeedSessionAttachedListingAsync(sessionId, listingId, ...)` added; existing helpers byte-identical. `SeedWithdrawnListingAsync` not needed — the cross-BC composition test sequences events rather than seeding from a Withdrawn baseline. | `4ea473d` |
| 9 | `docs/decisions/014-cross-bc-read-model-extension-shape.md` — amended with the third application + the Sub-Option A resolution. Handler table grew by two rows (the M4-S6 split pair). §"Sub-question" rewritten with the rationale and the rejection grounds for Sub-Option B. §"Decision" §1 strengthened from "no multi-source handlers until the sub-question resolves" to the unconditional single-source-per-sibling rule. §3 records the seven-string Status vocabulary. §4 names the two M4-S6 lived guards. §5 documents the named-field-allow-list verification finding. §Consequences updated with the OQ3 Path α observed terminal state + the four single-source siblings + one seed handler. Date moved to dual-date format. Status stays Accepted. | `a20b513` |
| 10 | `docs/skills/marten-projections.md` — §"View Extension Across Milestones" diagram + code sketch extended with the M4-S6 fields and handlers. New subsection §"Status-Preservation Guards" authored with the three lived examples (M5-S6 SettlementCompleted, M4-S6 Withdrawn-preservation, M4-S6 ListingWithdrawn pre-state filter) and the Path A vs Path B placement choice. The prompt anticipated this subsection existed from M5-S6; it did not — authored fresh at M4-S6. | `7aebe37` |
| 11 | This retrospective. | this commit |

---

## Open Questions — resolutions

### OQ1 — ADR-014 sub-question — **Sub-Option A (one sibling class per source BC)** (pinned at session open)

**Resolution.** User pinned Sub-Option A at session open via `AskUserQuestion` before any handler code landed. Two new classes ship: `AuctionsSessionHandler` (Auctions-sourced, two events: `ListingAttachedToSession`, `SessionStarted`) and `SellingListingWithdrawnHandler` (Selling-sourced, one event: `ListingWithdrawn`). Symmetric with the M3-S6 + M5-S6 single-source precedent.

**Rationale recorded in the ADR amendment.** Sub-Option B's rejection grounds were three concrete costs:

1. **Organizing-principle bifurcation.** The "source BC" axis was load-bearing under M3-S6 + M5-S6; introducing a second axis ("logical feature") for multi-source handlers would have created two organizing principles where the codebase had one.
2. **Wolverine discovery risk.** A single static class consuming from two different RabbitMQ queues was an unverified composition. Sub-Option B would have made M4-S6 the first lived test of that composition, blocking the slice on an unnecessary halt-and-consult risk.
3. **The two M4-S6 feature groups are not a single logical feature.** Sessions are an Auctions-internal lifecycle the catalog reflects; withdrawal is a Selling-side terminal that affects every downstream BC differently. Bundling them on the "M4-S6 feature scope" axis would have masked that distinction.

**Naming convention pinned.** Source-prefixed: `Auctions*Handler`, `Selling*Handler`, `Settlement*Handler`. Existing single-source siblings (`AuctionStatusHandler`, `ListingPublishedHandler`, `SettlementStatusHandler`) are grandfathered.

**Future application.** Any future BC needing multi-source coordination either splits into per-source siblings (the established pattern) or amends ADR-014 with new evidence justifying Sub-Option B for that specific application. Expectation going forward: "per-source sibling unless proven otherwise."

### OQ2 — OQ3 Path α observed terminal state — **Path 3 (catalog handler is the source of truth)** (pinned by the lived test)

**Resolution.** The M4-S5 retro's three candidate downstream paths collapsed to one observed lived path at M4-S6:

- **Path 1** (transient bad UX — Listing stream gets `BiddingOpened`, AuctionClosingSaga starts, listing eventually closes via normal saga flow): not observed by the Listings-side test, but the Auctions-side terminal is out of S6's scope per the prompt's framing.
- **Path 2** (saga conflict — new `BiddingOpened` starts a fresh saga on a previously terminated one): not observed by S6's test scope.
- **Path 3** (catalog handler preserves Withdrawn; bidders never see the withdrawn listing as open): **observed by `BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus`.** The test asserts `Status` stays `"Withdrawn"`, `ScheduledCloseAt` stays null (total guard per OQ5 Path α), `SessionId` preserved, `ClosedAt` = withdrawal timestamp.

**Auctions-side terminal observation deferred.** S6 explicitly did not widen scope to chase whether the Auctions-side `Listing` stream accepts the `BiddingOpened` append for a withdrawn listing, or whether the saga store exhibits a fresh-saga-on-terminated bug. Per the prompt's OQ2 directive, this remains a candidate for M4-S7 or post-M4 follow-up. The Listings-side terminal is now pinned; the Auctions-side is not, and the M4 milestone doc §3 "post-MVP hardening" stance carries forward on that surface.

### OQ3 — `AuctionStatusHandler.Handle(BiddingOpened)` guard placement — **Path A (top-of-method load-and-return)** (pinned at implementation time)

**Resolution.** Implemented Path A — load by id, check `view.Status == "Withdrawn"`, early return without `session.Store`. Mirrors the M5-S6 `SettlementStatusHandler.Handle(SettlementCompleted)` precedent verbatim. No Marten write on the no-op branch; no empty-transaction artefact; no Version bump.

**Path B not chosen.** A conditional `with` expression (`view.Status == "Withdrawn" ? view : (view with { ... })`) was considered and rejected: Marten would still execute the `Store` on the no-op branch, wasting a write and possibly bumping `Version`. Path A's clean no-op composes correctly with `AutoApplyTransactions` (no envelope to commit) — verified by the cross-BC composition test landing green on first run.

### OQ4 — `ListingWithdrawn` status-preservation discipline — **Legal pre-states `"Published"` and `"Open"` only; everything else no-ops**

**Resolution.** Implemented as `if (existing.Status is not ("Published" or "Open")) return;` in `SellingListingWithdrawnHandler.Handle(ListingWithdrawn)`. Arrivals against `"Closed"`, `"Sold"`, `"Passed"`, `"Settled"` no-op without throwing — those terminals are absorbing.

**Theory test landed both legal pre-states.** `ListingWithdrawn_SetsCatalogStatusWithdrawn` runs as `[Theory]` with two `InlineData` rows (Published, Open). The Open arrival case mirrors Workshop 002 §5's "attach withdrawn between attach and start" path (Open is reached via `BiddingOpened` in the test arrange step before `ListingWithdrawn` dispatches). Both rows assert `Status == "Withdrawn"` and `ClosedAt == WithdrawnAt`.

### OQ5 — `ScheduledCloseAt` behaviour under the OQ3 Path α composition test — **Path α (total guard — no fields updated when the guard fires)**

**Resolution.** The Withdrawn-preservation guard is total: when `view.Status == "Withdrawn"`, the guard returns before any `with` expression runs, so `ScheduledCloseAt` is NOT advanced even though `BiddingOpened.ScheduledCloseAt` carries a future timestamp. Symmetric with the M5-S6 `SettlementStatusHandler` shape.

**Asserted in the composition test.** `view.ScheduledCloseAt.ShouldBeNull()` after the `BiddingOpened` dispatch — the field stays at its pre-composition value (null, since the listing was never opened). Path β (partial guard — preserve Status but advance ScheduledCloseAt) would have rendered the catalog as "the listing is withdrawn but its close timestamp would still be advertised," which is semantically ambiguous.

### OQ6 — Sub-Option B Wolverine handler discovery — **Moot (Sub-Option A chosen)**

**Resolution.** Both M4-S6 handler classes are single-source, single-queue, indistinguishable from the M3-S6 / M5-S6 precedent. Wolverine handler discovery composed cleanly on first run; no `*BcDiscoveryExclusion` additions to the Listings fixture were needed because the existing `SellingBcDiscoveryExclusion` and `SettlementBcDiscoveryExclusion` already cover the BC namespaces, and both new handlers live in `CritterBids.Listings`.

**Hypothetical Sub-Option B scenario.** If Sub-Option B had been chosen, the open question was whether a single static class with three `Handle` methods (two from one queue, one from another) would compose with Wolverine's convention-based discovery. The question is moot for the M4-S6 lived shape; if a future slice picks Sub-Option B, this OQ becomes load-bearing again.

---

## Blockers encountered

**None.** All six commits landed green on first run. The cross-BC composition test was authored against the not-yet-amended `AuctionStatusHandler`, but because commit 3 (the guard) preceded commit 4 (the tests), the test never saw a red state — it was green on first execution per the staged commit sequence.

The pre-existing 24 NU1904 NuGet warnings (Marten 8.35.0 known vulnerability) carried forward unchanged; no new warnings introduced.

---

## Decisions inheriting forward

### Sub-Option A pinned for all future multi-source surfaces

ADR-014's §"Decision" §1 strengthens from a conditional ("no multi-source handlers ship until the sub-question resolves") to the unconditional rule: **one handler class per source BC, single-source per sibling.** Future BCs (Settlement when it intersects more catalog state; Obligations / Relay / Operations when their projection surfaces land) follow the per-source split unless they amend ADR-014 with concrete evidence justifying Sub-Option B for that specific application. Default response: "per-source sibling unless proven otherwise."

The source-prefixed naming convention (`Auctions*Handler`, `Selling*Handler`, `Settlement*Handler`) is part of the resolution. Existing grandfathered names (`AuctionStatusHandler`, `ListingPublishedHandler`, `SettlementStatusHandler`) stay; new sibling classes carry the source prefix.

### Named-field-allow-list discipline in seed handlers

M5-S6's `ListingPublishedHandler` amendment chose explicit per-field preservation lines rather than implicit record-`with` semantics. M4-S6's verification surfaced this and extended the block with two new preservation lines (`SessionId`, `SessionStartedAt`). The rule pinned in the handler's XML comment and in ADR-014 §"Decision" §5: **every new sibling-handler field below the M2 block adds its own preservation line in the seed handler.**

Mechanical implication: future M*-S* milestones that extend `CatalogListingView` carry a per-field seed-handler edit alongside the additive view-extension edit. The two changes ride in the same commit (or two commits in the same PR) so the preservation discipline never drifts behind the view shape.

### `"Withdrawn"` joins the Status vocabulary as a 7th absorbing terminal

At M4-S6 close the `CatalogListingView.Status` field carries seven string values: `"Published"`, `"Open"`, `"Closed"`, `"Sold"`, `"Passed"`, `"Settled"`, `"Withdrawn"`. Six are terminal (everything except `"Open"`); two land via M4-S6 (`"Withdrawn"` as a new terminal, and the `"Sold"` → `"Settled"` transition is unchanged from M5-S6). The catalog never enforces a finite enum at the storage layer; cross-BC coordination at workshop-update time remains the discipline.

### Status-preservation guards are a named pattern in the skill file

`marten-projections.md` gains §"Status-Preservation Guards" as a standalone subsection. The pattern is now reachable from the skill index rather than scattered across the M5-S3 PendingSettlement and M5-S6 SettlementStatusHandler examples. The three lived guards (M5-S6 `"Sold"` → `"Settled"`, M4-S6 `"Withdrawn"`-preservation, M4-S6 `ListingWithdrawn` pre-state filter) all share the top-of-method load-and-return shape and the total-no-op-or-total-advance discipline.

### Composition tests for cross-BC terminal-state pinning are a precedent

`BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus` is the first in-repo test that asserts a cross-BC composition's terminal state explicitly. The S5 retro's three candidate downstream paths collapse to one observed path via the test's assertions; the milestone doc §3 "post-MVP hardening" stance is now load-bearing on observed behaviour, not assumption. Future cross-BC composition surfaces with similar "we think the system does X but we haven't verified it" framings should consider adding their own composition test.

### M5-S6 status-preservation-guards subsection of `marten-projections.md` did not actually exist

The M4-S6 prompt asserted "the M5-S6 amendment landed all three subsections" including a §"Status-Preservation Guards" — but the file's M5-S6 state had only §"Single-Source-Seeded Caches" under §"Handler-Driven Projections — Tolerant Upsert," which covered preservation implicitly inside the PendingSettlement narrative. M4-S6 authored the §"Status-Preservation Guards" subsection fresh. This is documentation drift between the prompt-author's mental model and the lived skill file; the M4-S6 skill-append work absorbs it cleanly but the lesson for prompt authoring is "verify the section exists before referencing it as the amendment target."

---

## What M4-S7 should know

### Listings BC test count at S6 close

**20 Listings tests** (up from 14 at S5 close). Composition: 4 pre-existing M2 catalog tests + 5 M3-S6 auction-status tests + 1 M3-S6 BIN test + 4 M4-S6 §7 scenarios (with one `[Theory]` contributing 2 rows for a total of 5 xUnit-grain methods on the §7 scenarios) + 1 M4-S6 cross-BC composition test = 20. Total solution count: **154** (one above the prompt's nominal 153; the `[Theory]` is the difference).

### Total test count at S6 close

**154 passing, 0 skipped, 0 failing.** Test count by project:

| Project | M4-S5 close | M4-S6 delta | M4-S6 close |
|---------|-------------|-------------|-------------|
| `CritterBids.Api.Tests` | 1 | 0 | 1 |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 |
| `CritterBids.Listings.Tests` | 14 | **+6** | **20** |
| `CritterBids.Selling.Tests` | 36 | 0 | 36 |
| `CritterBids.Settlement.Tests` | 25 | 0 | 25 |
| `CritterBids.Auctions.Tests` | 65 | 0 | 65 |
| **Total** | **148** | **+6** | **154** |

`dotnet build`: 0 errors · 24 NU1904 NuGet warnings (unchanged from baseline).

### ADR-014 amendment shape

Status stays Accepted. Body amended in §"Option A" (handler table extended), §"Sub-question" (resolved to Sub-Option A with Sub-Option B's rejection grounds documented), §"Decision" (shape constraints strengthened — single-source unconditional, status vocabulary at seven, status-preservation guards examples added, seed-handler discipline pinned to named-field allow-list), §"Consequences" (third application + OQ3 Path α terminal pin + named-field discipline + four single-source siblings + seed handler), and §"References" (four new file references + M4-S6 retro link). Date line moved to dual-date format. The §"Context" and §"Options Considered" sections stayed byte-identical per the explicit out-of-scope clause.

### OQ3 Path α observed terminal state pinned in retro + ADR amendment

Path 3 (catalog handler is the source of truth) is the observed lived terminal for the Listings-side composition. The Auctions-side terminal (whether the saga store exhibits Path 1 or Path 2) is **not** S6's scope and remains an open question for M4-S7 or post-M4 follow-up. S7's retrospective work could surface whether the M4 milestone doc §3 "post-MVP hardening" stance needs strengthening on the Auctions-side surface, or whether the Listings-side terminal pin is sufficient guarantee for the MVP user-facing posture.

### Final `CatalogListingView` field inventory at M4 close

At M4 close, `CatalogListingView` carries 21 fields total:

- **8 M2-S7 fields:** `Id`, `SellerId`, `Title`, `Format`, `StartingBid`, `BuyItNow`, `Duration`, `PublishedAt`
- **10 M3-S6 fields:** `Status`, `ScheduledCloseAt`, `CurrentHighBid`, `CurrentHighBidderId`, `BidCount`, `HammerPrice`, `WinnerId`, `PassedReason`, `FinalHighestBid`, `ClosedAt`
- **1 M5-S6 field:** `SettledAt`
- **2 M4-S6 fields:** `SessionId`, `SessionStartedAt`

This is the single source of truth for the M4 retrospective's "what the catalog looks like at M4 close" summary. M6 (frontends) consumes this shape transparently; M5-deferred features (Obligations, Relay status surfaces) add further fields post-M4 per ADR-014's expand-additively rule.

### Bid-increment helper status — unchanged at S6 close

Still two co-located inline copies (`PlaceBidHandler` + `ProxyBidManagerSaga`). Threshold of three still uncrossed. S6's projection work did not introduce a third copy — bid-increment math has no role in catalog handlers; the catalog reflects bid state but doesn't compute it.

### Cascade-bucket assignments — no new flips surfaced in S6

S6 did not author any TrackActivity-based dispatch tests (all six new tests use direct handler invocation per the M3-S6 in-fixture comment block). No new cascade-bucket assignments to add to the running ledger. M4-S7's retrospective work inherits the M4-S4 / M4-S5 ledger unchanged.

### Aspire / Rabbit operational posture

S6 didn't exercise the bus dispatch path — direct handler invocation throughout — so no first-use surprises with Aspire or Rabbit landed. The multi-source-handler-topology risk surfaced in OQ6 was moot under Sub-Option A. If a future slice picks Sub-Option B (per the future-application clause in ADR-014 §"Sub-question"), the operational posture for multi-source dispatch becomes the first lived data point for that slice.

### Narrative 001 Moment 3 lived-code audit can now move from `defer` to `green`

Narrative 001 Moment 3's deferred lived-code audit explicitly named M4-S5 + M4-S6 as the trigger:

> *"Lived-code audit of the cascade. (`defer`; the Flash session aggregate, `StartSession` command handler, `SessionStartedHandler` fan-out, and Listings-side `SessionMembershipHandler` are scheduled for M4-S5 and M4-S6 per the M4-S1 retro. Until those slices ship, the cascade as narrated is forward-spec only. See Finding 006.)"*

M4-S5 landed the Auctions-side aggregate, commands, and fan-out. M4-S6 lands the Listings-side reflection (`AuctionsSessionHandler`, not `SessionMembershipHandler` per the sub-question resolution — the narrative's named class differs from the lived class). The narrative's Moment 3 lived-code audit can now run; the M4-S7 retrospective should consider whether the audit surfaces a `narrative-update` finding (the lived class name differs from the narrated `SessionMembershipHandler`) or whether the divergence is small enough to defer-with-note. The narrative names the **logical feature** ("session membership"); the lived **class topology** is Sub-Option A. Both are correct; the narrative's prose may benefit from a one-line note that the implementation lives in two source-split classes.

### Other items M4-S7 should consider

- **The narrative-update finding above** is the cleanest candidate for an inline narrative amendment at S7 (or in a separate Phase 2.5 follow-up if the narrative file is in code-update phase already).
- **The Auctions-side terminal for OQ3 Path α** is not pinned; M4-S7 could either pin it explicitly (by authoring an Auctions-side composition test) or defer-with-note to post-M4.
- **The S6 skill append authored a new §"Status-Preservation Guards" subsection** that the M5-S6 retrospective claimed already existed. M4-S7 could either fold this into a "lessons learned" item in the M4 retrospective or correct the M5-S6 retro's claim if it surfaces during the retro-of-retros pass.
- **Operations BC's future `SessionCatalog` view** was named in `SessionCreated`'s contract docstring as a candidate post-M5 deliverable. S6 did not author it; M4-S7 doesn't need to either, but the M4 retro should note that the post-M4 Operations work has one cleanly named candidate slice (the per-session summary view).

---

## Test count summary

| Project | M4-S5 close | M4-S6 delta | M4-S6 close |
|---------|-------------|-------------|-------------|
| `CritterBids.Api.Tests` | 1 | 0 | 1 |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 |
| `CritterBids.Listings.Tests` | 14 | **+6** | **20** |
| `CritterBids.Selling.Tests` | 36 | 0 | 36 |
| `CritterBids.Settlement.Tests` | 25 | 0 | 25 |
| `CritterBids.Auctions.Tests` | 65 | 0 | 65 |
| **Total** | **148** | **+6** | **154** |

`dotnet build`: 0 errors · 24 NU1904 NuGet warnings (unchanged from baseline).
