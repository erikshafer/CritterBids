# M4-S6: Listings Catalog — Session Membership + Withdrawn Status + ADR-014 Sub-Question

**Milestone:** M4 — Auctions BC Completion
**Slice:** S6 of 7 (penultimate M4 implementation slice; S7 = retrospective + skills + M4 close)
**Narrative:** `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 ("The Flash session starts and the lot board comes alive") — Moment 3's bidder-facing surface is the lot board on SwiftFerret42's phone reflecting the started session. M4-S5 closed the Auctions-side fan-out (the events flow); M4-S6 closes the Listings-side reflection (the catalog renders it). After S6, Moment 3's lived-code audit can move from `defer` to `green` (or surfaces a `narrative-update` finding if S6's lived shape diverges).
**Agent:** @PSA
**Estimated scope:** one PR; ~5 new test methods + handler(s) + 2 modified files + ADR amendment; ~6-8 new/modified files total
**Baseline:** 148 tests passing (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 65 Auctions) · `dotnet build` 0 errors, 24 pre-existing NU1904 NuGet warnings (Marten) · M4-S5 closed at the squash-merge of PR #37. At session open: `CatalogListingView` has no `SessionId` / `SessionStartedAt` fields and no `"Withdrawn"` Status transition; no Listings-BC handler exists for the Auctions Session trio (`SessionCreated` / `ListingAttachedToSession` / `SessionStarted`) or for Selling's `ListingWithdrawn`; `AuctionStatusHandler.Handle(BiddingOpened)` has no `"Withdrawn"`-preservation guard and will flip a withdrawn listing's Status to `"Open"` on the M4-S5 fan-out's emission for a withdrawn-attached listing (OQ3 Path α from the S5 retro, untested); ADR 014 is Accepted with the multi-source-sibling sub-question explicitly deferred to "a third lived application that legitimately requires multi-source coordination — most likely M4-S6's `SessionMembershipHandler`."

---

## Goal

Land the Listings BC's session-membership and withdrawn-status extension to `CatalogListingView`, resolve ADR-014's deferred multi-source-sibling sub-question (Sub-Option A vs Sub-Option B) at session open, and explicitly test the cross-BC composition the M4-S5 fan-out introduced (OQ3 Path α — `BiddingOpened` arriving at the catalog for a previously-withdrawn listing). At S6 close, the catalog reflects session membership (`SessionId`, `SessionStartedAt`) when a listing is attached to and started in a Flash session, the status transition to `"Withdrawn"` is wired end-to-end through Selling's `WithdrawListing` producer (shipped at M4-S2), and the cross-BC composition — fan-out emitting `BiddingOpened` for a withdrawn listing — is observed to land in a defined terminal state under the status-preservation discipline ADR 014 codifies.

S6 is the lowest-risk implementation slice of M4 per milestone doc §9 — the M3-S6 + M5-S6 sibling-handler precedent is pattern-stable, and the M4-S5 retro handed forward a complete view of the upstream events and their lived emission shape. The two real decisions S6 lands are: (1) the ADR-014 sub-question — pre-empted at session open, not mid-implementation, because both sub-options carry the same test count and the M4-S5 retro produced no evidence biasing either; (2) the OQ3 Path α terminal-state observation — converted from an unobserved candidate-path enumeration in the S5 retro into a load-bearing assertion in `CrossBcWithdrawnSessionFanOutTests` (or the equivalent named test). The status-preservation-guard amendment to `AuctionStatusHandler` is the smallest production-code surface needed to make that assertion green.

This slice is the **third lived application** of ADR 014's Path A pattern (after M3-S6 `AuctionStatusHandler` and M5-S6 `SettlementStatusHandler`) and the **first multi-source application** if Sub-Option B is chosen. S6 amends ADR 014 with the third example and the sub-question's resolution.

## Context to load

- `docs/milestones/M4-auctions-bc-completion.md` — §2 (Listings BC catalog extension scope + ADR 014 sub-question framing), §6 (catalog projection extension convention + M4-D5 framing), §7 (`CritterBids.Listings.Tests` row mapping — 4 scenarios), §8 (M4-D5 disposition — "Resolve in S6"), §9 (S6 sizing — "lowest-risk implementation session")
- `docs/decisions/014-cross-bc-read-model-extension-shape.md` — full ADR, especially §"Sub-question — Multi-source siblings (deferred)" and §"Decision". The ADR is Accepted; this slice amends it with the third application + the sub-question's lived resolution.
- `docs/retrospectives/M4-S5-session-aggregate-retrospective.md` — particularly §"What M4-S6 should know" (the handoff payload: PublishedListings shape, sibling-handler ADR sub-question evidence note, fan-out idempotency mechanism, cascade-bucket assignments, bid-increment helper status, **and the OQ3 Path α observation gap with three candidate downstream paths**)
- `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 — the lot-board-comes-alive narrative; S6's lived catalog reflects it. Re-read at session open to set the "what the bidder sees" target.
- `src/CritterBids.Listings/AuctionStatusHandler.cs` (existing — read the `BiddingOpened` method specifically; the Withdrawn-preservation guard amendment lands inline here) and `src/CritterBids.Listings/SettlementStatusHandler.cs` (the canonical reference for the status-preservation-guard shape — load-and-guard, no-op on non-expected-pre-state, no throw)
- `src/CritterBids.Listings/CatalogListingView.cs` (the additive-fields target) and `src/CritterBids.Listings/ListingPublishedHandler.cs` (the seed handler — per ADR 014 §"Consequences" §4, may need a load-and-preserve amendment if the new fields are at risk under re-delivery)
- Skill files: `docs/skills/marten-projections.md` §"View Extension Across Milestones" + §"Handler-Driven Projections — Tolerant Upsert" + §"Status-Preservation Guards" (the M5-S6 amendment landed all three subsections); `docs/skills/wolverine-message-handlers.md` §"OutgoingMessages — Producer Pattern" only if the cross-BC composition test needs Auctions-side dispatch wiring
- Workshop reference: `docs/workshops/002-scenarios.md` §5 — read-only context for what an attached-then-started Flash listing looks like end-to-end. S6 implements the catalog's render; the scenarios themselves shipped at M4-S5.

(Eight items — at the seven-item soft cap from the AUTHORING.md template. The narrative + workshop pair counts as one context unit per AUTHORING.md rule 3's joint-authority clause.)

## In scope (numbered)

1. **`src/CritterBids.Listings/CatalogListingView.cs`** — extend additively with two session-membership fields:
   - `SessionId: Guid?` — populated by `ListingAttachedToSession`; null for non-flash listings; null when the listing is detached (no detach event exists in M4 — the field never reverts).
   - `SessionStartedAt: DateTimeOffset?` — populated by `SessionStarted`; null until the session starts; nullable forever for any listing whose attached session never started or who was never attached.

   The existing M2 fields + M3-S6 auction-status fields + M5-S6 settlement-status field stay byte-identical. Property order follows existing convention (M2 / M3-S6 / M5-S6 groupings; M4-S6 fields land in a new `// ─── M4-S6 session-membership fields ──` group block).

   Document the `Status` `"Withdrawn"` value in the existing Status-transitions XML comment block — add the line `//   Withdrawn terminal:  "Published" → "Withdrawn"` and `//                       "Open"      → "Withdrawn"` (or whichever shape Open Question 1's resolution lands on). Per M4-D5 (milestone doc §8): the new value is a string on the existing `Status` field (not a new field, not overloaded on `ClosedReason` — `ClosedReason` doesn't exist; `PassedReason` does and is unrelated).

2. **Session-membership handler(s) — class topology per Open Question 1 resolution.**
   - **Sub-Option A (one sibling per source BC):** ships **two** new handler classes —
     - `src/CritterBids.Listings/AuctionsSessionHandler.cs` — sibling consuming `ListingAttachedToSession` + `SessionStarted` from `listings-auctions-events`. Mirrors `AuctionStatusHandler`'s shape. `SessionCreated` has no Listings-side field consequence and is **not handled** (per milestone doc §6 — the catalog has no per-session record, only per-listing fields). If it accidentally routes to a no-op handler that's a separate question; recommended: do not write the no-op.
     - `src/CritterBids.Listings/SellingListingWithdrawnHandler.cs` — sibling consuming `ListingWithdrawn` from `listings-selling-events`. Mirrors `ListingPublishedHandler`'s shape (Selling-source). Sets `Status = "Withdrawn"` with the status-preservation discipline per Open Question 4.
   - **Sub-Option B (one sibling per logical feature):** ships **one** new handler class —
     - `src/CritterBids.Listings/SessionMembershipHandler.cs` — sibling consuming `ListingAttachedToSession` + `SessionStarted` (Auctions source) **and** `ListingWithdrawn` (Selling source) in a single static class. First multi-source sibling in the codebase.

   Both sub-options use the M5-S6 `SettlementStatusHandler` shape: static class, one `Handle` method per event type, tolerant-upsert primitive (`LoadAsync` ?? `new CatalogListingView { Id = message.ListingId }`), `session.Store` with record `with`. No `OutgoingMessages`. No `IMessageBus`.

3. **`src/CritterBids.Listings/AuctionStatusHandler.cs`** — modify `Handle(BiddingOpened)` to add a `"Withdrawn"`-preservation guard. Status-preservation discipline from ADR 014 §"Decision" §4 + M5-S6 `SettlementStatusHandler`'s precedent: if the loaded `view.Status == "Withdrawn"`, the `BiddingOpened` arrival no-ops without throwing. The session-membership fields (`SessionId`, `SessionStartedAt`) are NOT cleared (they record that the listing was attached + the session started, both true historical facts even when the listing is withdrawn). The `Status` field is preserved at `"Withdrawn"` — that is the terminal state per M4-S6's resolution of OQ3 Path α.

   Document the guard in the method's XML comment block: this is the load-bearing edit for OQ3 Path α's terminal-state pin. Adjust the existing class-level XML comment to reference the M4-S6 amendment for traceability.

   No other `Handle` methods on `AuctionStatusHandler` need amending — `BiddingOpened` is the only one the fan-out emits. If S6 surfaces a need to guard any of the other five methods, flag and halt; the S5 retro names this method as the load-bearing one.

4. **`src/CritterBids.Listings/ListingPublishedHandler.cs`** — **verify, modify only if needed.** Per ADR 014 §"Consequences" §4 ("seed-handler load-and-preserve discipline"), the seed handler must preserve any downstream-handler state on re-delivery. M5-S6 added load-and-preserve for `Status`, `SettledAt`, and the auction-status fields. Verify at session open whether the M5-S6 amendment already preserves the new M4-S6 fields (`SessionId`, `SessionStartedAt`); they're nullable + additive, so the M5-S6 amendment may already cover them implicitly via the `with` expression's omitted-field semantics. If the M5-S6 amendment used an explicit allow-list (named field-preserving `with` block) rather than load-the-existing-row-and-overlay, the M4-S6 fields need explicit naming. **No speculative edit** — read the file, confirm, edit only if the verification surfaces a gap. Note in retro either way.

5. **No `src/CritterBids.Api/Program.cs` change.** The session trio (`SessionCreated` / `ListingAttachedToSession` / `SessionStarted`) is already published over `listings-auctions-events` (M4-S5 added the publish routes); the Listings BC already listens to that queue (M3-S6); `ListingWithdrawn` already publishes over `listings-selling-events` (M4-S2); Listings already listens (M2-S7). No routing rules to add. If S6 surfaces a need to change `Program.cs`, flag and halt — that diff is unexpected.

6. **`tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs`** — extend with the four milestone-doc §7 scenarios. Method names exactly per milestone doc §7:
   - `ListingAttachedToSession_SetsSessionId` — seed a `CatalogListingView` at `Status = "Published"` (via the M2 `ListingPublished` flow or direct fixture seed); dispatch `ListingAttachedToSession` via `IMessageBus`; assert `view.SessionId == message.SessionId`.
   - `SessionStarted_SetsSessionStartedAtForMemberListings` — seed N (≥ 2) views attached to the same session; dispatch `SessionStarted` with N `ListingIds`; assert each of the N views has `SessionStartedAt == message.StartedAt`. Listings not in the session are untouched.
   - `ListingWithdrawn_SetsCatalogStatusWithdrawn` — seed a view at `Status = "Published"` (or `"Open"`, both arrival states should land); dispatch `ListingWithdrawn`; assert `view.Status == "Withdrawn"`, `view.ClosedAt == message.WithdrawnAt`. Per Open Question 4, the from-`"Sold"` / from-`"Settled"` arrival states no-op.
   - `SiblingHandlers_CoexistOnSameView_NoOverwrites` — sequence-of-events test: dispatch `ListingPublished` → `BiddingOpened` → `ListingAttachedToSession` → `BidPlaced` → `SessionStarted` (or whichever arrival order the test exercises; one realistic Flash-session order). Assert all M2 / M3-S6 / M4-S6 fields land at their expected values with no mutual overwrite. The test exists to pin the additive-field discipline across siblings, not to exercise a specific scenario.

7. **`tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs`** (additional) — author the cross-BC composition test that closes OQ3 Path α from the S5 retro. **Test method name:** `BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus`. Sequence:
   - Seed a view at `Status = "Published"` (via the M2 `ListingPublished` flow or direct fixture seed)
   - Dispatch `ListingAttachedToSession` (Status stays Published, SessionId set)
   - Dispatch `ListingWithdrawn` (Status → "Withdrawn", ClosedAt set)
   - Dispatch `BiddingOpened` (the fan-out's emission for the withdrawn-attached listing)
   - Assert: `view.Status == "Withdrawn"` (NOT `"Open"`), `view.ScheduledCloseAt` either unchanged or set per Open Question 5, `view.SessionId` preserved, `view.SessionStartedAt` not yet set (since this test does not dispatch `SessionStarted`).

   This test is **load-bearing for the M4 milestone doc §3 stance** ("Defensive pre-filtering at `StartSession` time is post-MVP hardening"). Without this test the §3 stance is assumption; with it, the §3 stance is observed behavior backed by the status-preservation guard from item 3. The test fails on the unguarded `AuctionStatusHandler` and goes green after item 3 lands.

8. **`tests/CritterBids.Listings.Tests/Fixtures/ListingsTestFixture.cs`** — additive only. New helper(s) as needed:
   - `SeedSessionAttachedListingAsync(sessionId, listingId, ...)` — convenience for tests that want a baseline view at `Status = "Published"` with `SessionId` populated, without going through the full `ListingPublished` → `ListingAttachedToSession` dispatch chain.
   - If item 7's cross-BC composition test surfaces a need for a Withdrawn-baseline helper, add it (`SeedWithdrawnListingAsync`).

   Existing fixture behaviour stays byte-identical. M3-S6 + M5-S6 tests pass without any fixture-side change.

9. **`docs/decisions/014-cross-bc-read-model-extension-shape.md`** — **amend** with the third lived application + the sub-question's resolution. Specific edits:
   - Add the M4-S6 row to the §"Option A" handler table (or the chosen sub-option's class topology).
   - Amend §"Sub-question — Multi-source siblings (deferred)" with the lived resolution: replace "deferred to a third lived application" with the chosen sub-option's name + the M4-S6 evidence (handler class names, fields owned, source BCs). If Sub-Option B is chosen, document that `SessionMembershipHandler` is the first multi-source sibling in the codebase + its discipline (single transactional scope across both sources, no special-case logic per source).
   - Add an entry to §"Decision" §"Shape constraints" noting the multi-source resolution if Sub-Option B was chosen (e.g., "Multi-source handlers are permitted when the source BCs' events all populate the same logical field set"), or strengthen the single-source language if Sub-Option A was chosen.
   - Update §"Consequences" to reflect the third application + any new shape constraint.
   - Change the date line to a dual date: `2026-05-17 (initial) / 2026-05-19 (M4-S6 amendment — third lived application, sub-question resolved)`.

   The ADR's status stays Accepted. Re-author of the body is out of scope.

10. **`docs/skills/marten-projections.md` §"View Extension Across Milestones"** — append the third example (M4-S6 session-membership handler + the Withdrawn-status amendment to `AuctionStatusHandler`). The §"Status-Preservation Guards" subsection (M5-S6) gets the `"Withdrawn"`-preservation example. If the sub-question resolution chose Sub-Option B, append a subsection §"Multi-Source Siblings" with the M4-S6 lived shape as the only example.

   Append-only. No rewrites of existing M5-S6 sections.

11. **`docs/retrospectives/M4-S6-listings-catalog-session-and-withdrawn-retrospective.md`** — written last. Gate below.

## Explicitly out of scope

- **ADR 014 re-authoring.** The ADR is Accepted at M5-S6. S6 amends only; the §"Context" / §"Options Considered" sections stay byte-identical.
- **Modification to any `src/CritterBids.Auctions/` file.** Byte-level diff zero on the entire Auctions BC. The fan-out handler shipped at M4-S5; this slice is downstream. If S6 surfaces a need to modify Auctions (e.g., the fan-out's per-listing payload shape needs adjustment for catalog rendering), **flag and halt** — that work belongs in a separate slice.
- **Modification to any `src/CritterBids.Selling/` file.** Byte-level diff zero. `ListingWithdrawn` shipped at M4-S2.
- **Modification to any `src/CritterBids.Settlement/` file.** Byte-level diff zero.
- **Modification to any `src/CritterBids.Contracts/` file.** Byte-level diff zero. All five M4-relevant contracts (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`, `ListingWithdrawn`, the existing six Auctions contracts consumed by `AuctionStatusHandler`) are already present.
- **Modification to existing `src/CritterBids.Listings/` files beyond items 3 + (conditionally) 4.** `AuctionStatusHandler.Handle(BiddingOpened)` gets the Withdrawn-preservation guard; `ListingPublishedHandler` is verified and amended only if the seed-handler load-and-preserve discipline requires it.
- **HTTP endpoint surface** for the new fields. M6. The frontends consume the extended view shape transparently when M6's `GET /api/listings/{id}` is wired.
- **Watchlist fields, `ParticipantBidHistoryView`, frontend rendering, real authentication.** Same deferrals as M3 / M4-S5.
- **Defensive pre-filtering at `StartSession` time.** Per M4 milestone doc §3 — post-MVP hardening. The OQ3 Path α composition test in item 7 makes the §3 stance load-bearing; S6 does not migrate to defensive pre-filtering.
- **Detach-listing-from-session, session rescheduling, session cancellation.** Per milestone doc §3 — out of M4 scope entirely.
- **Bid-increment helper extraction.** Threshold of three lived co-located copies remains uncrossed at M4-S5 close (two copies: `PlaceBidHandler` + `ProxyBidManagerSaga`). S6's projection work does not introduce a third copy; if any does surface unexpectedly, flag in the retro and do not cross the threshold opportunistically.
- **Local hygiene (pruning the `m4-s5-session-aggregate` branch after PR #37's squash-merge, `git pull` on `main`).** Operator-confirmation-required destructive ops; not S6's scope.
- **Bus-side cascade-bucket reshuffle.** The new handlers' integration tests use `SendMessageAndWaitAsync` per the M3-S6 / M5-S6 sibling-handler precedent (cross-BC events with multiple Listings-local handlers — at minimum the test sends an event for which two sibling handlers exist). If a test surfaces a `tracked.NoRoutes` vs `tracked.Sent` ambiguity that the M5-S6 retro doesn't already cover, flag in the retro; do not unilaterally restructure prior tests.
- **Skill bulk-pass for `wolverine-message-handlers.md`.** S6 has latitude to fold first-use findings into `marten-projections.md` only (item 10). The wolverine-handlers file stays out of scope; if a finding warrants append there, defer to M4-S7 (retro + skills + M4 close).

## Conventions to pin or follow

Inherit all conventions from CLAUDE.md and prior slices (M3-S6 sibling-handler topology, M5-S6 status-preservation guard + load-and-preserve seed discipline, M4-S5 fan-out + PublishedListings precedents, ADR 014 Path A shape). New conventions surfaced or sub-question-resolved in this slice:

- **ADR 014 sub-question resolution.** Per Open Question 1, the call (Sub-Option A or Sub-Option B) is made at session open via `AskUserQuestion` and pinned in code for the rest of the slice. Both sub-options have the same test count and the same end-user-visible behavior; the choice shapes the codebase's class topology going forward. Once pinned, every subsequent multi-source extension follows the chosen shape.
- **Status-preservation guard on `AuctionStatusHandler.Handle(BiddingOpened)`.** Inherit the M5-S6 `SettlementStatusHandler` shape: load by id, no-op if the existing view's status is `"Withdrawn"`. The other five `AuctionStatusHandler.Handle` methods do NOT get the guard in this slice — only `BiddingOpened` is on the OQ3 Path α composition path. If a future slice surfaces a need for a wider guard, that's a separate amendment.
- **Status vocabulary extension.** `"Withdrawn"` joins `"Published"`, `"Open"`, `"Closed"`, `"Sold"`, `"Passed"`, `"Settled"` per M4-D5. The full vocabulary is now 7 strings. The catalog never enforces a finite enum at storage; cross-BC coordination at workshop-update time is the discipline. Document the expansion in the retro.
- **`SessionId` / `SessionStartedAt` field nullability.** Both nullable forever — non-flash listings, listings never attached to a started session, and listings attached to never-started sessions all carry nulls. No backfill, no default.
- **Cross-BC composition test discipline.** Item 7's `BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus` is the lived assertion that the M4 milestone doc §3 "post-MVP hardening" stance is load-bearing on observed behavior. The S5 retro's three candidate downstream paths collapse to one observed path per S6's resolution. Pin the observed path in retro + ADR amendment.
- **Sibling-handler class naming convention.** If Sub-Option A: per-source naming (`AuctionsSessionHandler`, `SellingListingWithdrawnHandler` — source BC in the prefix). If Sub-Option B: feature-scope naming (`SessionMembershipHandler` — the logical feature name). The naming convention is pinned by the sub-option choice and applies to every future application. Document in the ADR amendment.
- **Seed-handler load-and-preserve verification.** Item 4 verifies (without speculatively editing) whether `ListingPublishedHandler`'s M5-S6 amendment already preserves the new M4-S6 fields. If it does, no edit; record the verification in the retro. If it doesn't, edit minimally and pin the discipline (named field-preservation vs implicit `with` semantics).

## Commit sequence (proposed)

1. `feat(listings): extend CatalogListingView with SessionId + SessionStartedAt fields` — item 1 only. Additive record properties. Compiles; existing 148-test baseline preserved.
2. `feat(listings): {AuctionsSessionHandler + SellingListingWithdrawnHandler|SessionMembershipHandler}` — item 2, sub-option pinned at session open. Production handler(s) only; no tests yet.
3. `fix(listings): AuctionStatusHandler preserves Withdrawn status on BiddingOpened` — item 3, the load-bearing OQ3 Path α amendment.
4. `test(listings): four catalog session + withdrawn scenarios per milestone doc §7` — item 6 (4 tests) + item 8 (fixture helper).
5. `test(listings): BiddingOpened after ListingWithdrawn preserves Withdrawn status` — item 7, the cross-BC composition test closing OQ3 Path α.
6. *(conditional on item 4 verification)* `fix(listings): ListingPublishedHandler preserves M4-S6 fields on re-delivery` — only if item 4's verification surfaces a gap.
7. `docs(adr): amend ADR-014 with M4-S6 third application + sub-question resolution` — item 9.
8. `docs(skills): append M4-S6 examples to marten-projections.md` — item 10.
9. `docs: write M4-S6 retrospective` — item 11.

Each implementation commit ships its production code + its scenario test atomically so `git bisect` stays clean at every SHA — same discipline as M3-S6 / M4-S5.

## Acceptance criteria

- [ ] `dotnet build` — 0 errors, 0 new warnings beyond the pre-existing 24 NU1904 NuGet warnings (Marten)
- [ ] `dotnet test` — 148-test baseline preserved; +4 milestone-doc §7 scenarios + 1 cross-BC composition test = **153 total** (1 Api + 1 Contracts + 6 Participants + **19 Listings** + 36 Selling + 25 Settlement + 65 Auctions); zero skipped, zero failing
- [ ] `src/CritterBids.Listings/CatalogListingView.cs` — extended with `SessionId: Guid?` + `SessionStartedAt: DateTimeOffset?`; existing M2 / M3-S6 / M5-S6 fields byte-identical; Status-transitions XML comment block updated to include `"Withdrawn"` arrival paths
- [ ] Sub-Option A: `src/CritterBids.Listings/AuctionsSessionHandler.cs` + `src/CritterBids.Listings/SellingListingWithdrawnHandler.cs` both exist; OR Sub-Option B: `src/CritterBids.Listings/SessionMembershipHandler.cs` exists and consumes from both source BCs in a single class
- [ ] `src/CritterBids.Listings/AuctionStatusHandler.cs` — `Handle(BiddingOpened)` has a `"Withdrawn"`-preservation guard at the top (load, check `view.Status == "Withdrawn"`, return without storing); class-level XML comment references the M4-S6 amendment; the other five `Handle` methods are byte-identical to M3-S6 close
- [ ] `src/CritterBids.Listings/ListingPublishedHandler.cs` — either byte-identical to M5-S6 close (item 4 verification surfaced no gap) OR amended minimally with named field-preservation for the M4-S6 fields (verification surfaced a gap, retro documents which)
- [ ] `src/CritterBids.Api/Program.cs` — byte-identical to M4-S5 close (no new routing rules; no new queue listen registrations)
- [ ] All 4 milestone-doc §7 test methods in `CatalogListingViewTests.cs` named exactly per milestone doc §7, each green
- [ ] `BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus` (item 7) green; asserts `view.Status == "Withdrawn"` after the `ListingWithdrawn` → `BiddingOpened` composition
- [ ] `docs/decisions/014-cross-bc-read-model-extension-shape.md` — amended with the third application + sub-question resolution; status still Accepted; date line updated to dual-date format
- [ ] `docs/skills/marten-projections.md` — third example appended under §"View Extension Across Milestones"; if Sub-Option B chosen, new §"Multi-Source Siblings" subsection appended
- [ ] `src/CritterBids.Auctions/**/*.cs`, `src/CritterBids.Selling/**/*.cs`, `src/CritterBids.Settlement/**/*.cs`, `src/CritterBids.Contracts/**/*.cs` — all byte-identical to M4-S5 close
- [ ] No new project references; `CritterBids.Listings.csproj` reference graph unchanged
- [ ] No `[Obsolete]`, no `#pragma warning disable`, no `throw new NotImplementedException()` in production code
- [ ] `docs/retrospectives/M4-S6-listings-catalog-session-and-withdrawn-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test` shows 153 passing and `dotnet build` shows no new warnings beyond the pre-existing 24 NU1904.

Retrospective content requirements:

- Baseline numbers (148 before, 153 after) with a phase table matching the M4-S5 retro shape
- Per-item status table mirroring the "In scope (numbered)" list with commit hashes
- Each of the six Open Questions answered with which path was taken and why; for OQ1 (sub-question A vs B), record the rationale + the lived class topology + the naming convention pinned for future applications; for OQ3 Path α (item 7's cross-BC composition), record the observed terminal state (which of the three S5-retro candidate paths the system actually exhibits)
- Whether the item 4 `ListingPublishedHandler` verification surfaced a gap; if so, the named field-preservation amendment shape; if not, an explicit "M5-S6's amendment already covers M4-S6's additive fields via the `with` semantics" observation
- Whether the item 10 skill append landed; if so, the appended sections listed; if not, an explicit "nothing new surfaced beyond what `marten-projections.md` §V/§"Status-Preservation Guards" already covers" observation
- Any blocker encountered — verbatim error message, root cause, fix path — with particular attention to:
  - Wolverine handler-discovery interaction with the sub-option choice (if Sub-Option B, the multi-source single-class shape's first lived dispatch may surface ambiguity)
  - Status-preservation guard composing with Wolverine's `AutoApplyTransactions` (the guard returns without storing — verify no empty-transaction artefact)
  - Cross-queue arrival-order race on the OQ3 Path α composition test (`ListingWithdrawn` from `listings-selling-events` vs `BiddingOpened` from `listings-auctions-events` — same race surface as M3-S6 OQ4)
  - The M2 / M3-S6 / M5-S6 sibling-handler tests' coexistence on the extended view
- A **"What M4-S7 should know"** section covering at minimum:
  - Listings BC test count at S6 close (19, up from 14)
  - Total test count at S6 close (153, up from 148)
  - The ADR 014 amendment shape — sub-question resolved + third application example landed; whether the §"Decision" shape constraints needed strengthening
  - The OQ3 Path α observed terminal state pinned in retro + ADR amendment (which of the S5 retro's three candidate paths the system exhibits in lived test)
  - Final `CatalogListingView` field inventory at M4 close — 8 M2 + 10 M3-S6 + 1 M5-S6 + 2 M4-S6 = 21 fields; this is the single source of truth for the M4 retrospective's "what the catalog looks like at M4 close" summary
  - Bid-increment helper status (still at two co-located copies — threshold of three uncrossed — confirm S6 did not introduce a third)
  - Any cascade-bucket assignments worth noting for M4-S7's retrospective work
  - Aspire / Rabbit operational posture — any first-use surprises with the multi-source handler topology (only if Sub-Option B chosen)
  - Whether anything in S6's lived shape diverges from narrative 001 Moment 3 — if yes, name the divergence and flag whether it's a `narrative-update` or `workshop-update` finding per ADR 016 Phase 2 discipline

## Open questions (pre-mortems — flag, do not guess)

1. **ADR 014 sub-question — Sub-Option A vs Sub-Option B sibling-handler scoping. DECIDE AT SESSION OPEN via `AskUserQuestion` before any handler code lands.** The M4 milestone doc §8 frames this; ADR 014 §"Sub-question — Multi-source siblings (deferred)" formalizes it. The M4-S5 retro's "What M4-S6 should know" section explicitly notes: *"S5's `PublishedListingsHandler` consumes only Selling-source events… it doesn't bear on the Option A vs Option B question directly. … ADR 014's resolution is M4-S6's call; S5's evidence is incidental."*

   - **Sub-Option A — one sibling class per source BC.** Two new classes: `AuctionsSessionHandler` (Auctions-sourced, two events: `ListingAttachedToSession`, `SessionStarted`) + `SellingListingWithdrawnHandler` (Selling-sourced, one event: `ListingWithdrawn`). Symmetric with the M3-S6 + M5-S6 single-source precedent. More files but stricter isolation; the "source BC" axis is the organizing principle.
   - **Sub-Option B — one sibling class per logical feature.** One new class: `SessionMembershipHandler` consuming from both Auctions and Selling (three events total). Fewer files; the "logical feature" axis (session lifecycle + listing-state-affecting-session) is the organizing principle. Sets precedent for multi-source siblings that may propagate to Settlement / Obligations / Relay.

   Neither sub-option carries a test-count advantage. The choice shapes the codebase's future class topology — once pinned, every subsequent multi-source extension should follow the chosen shape (or amend ADR 014 again). The M4-S5 retro produced no evidence biasing either; this slice is the first lived data point. **Recommend deciding at session open, not mid-implementation.**

2. **OQ3 Path α — observed terminal state for `BiddingOpened` arriving at a withdrawn listing.** The M4-S5 retro flagged this as an observation gap: the M4-S5 fan-out emits `BiddingOpened` for every listing in `SessionStarted.ListingIds`, including listings whose `PublishedListings.Status` is `Withdrawn`. The S5 retro names three candidate downstream paths:

   - **Path 1:** Listing stream gets `BiddingOpened` (DCB accepts because the stream is empty); Auction Closing saga starts; bidders see a withdrawn listing as "Open" briefly. Transient bad UX.
   - **Path 2:** Auction Closing saga's earlier `ListingWithdrawn` already terminated a saga (or no saga existed). New `BiddingOpened` starts a fresh saga atop the terminated one — bug.
   - **Path 3:** Listings catalog projection's Withdrawn-status field (M4-S6 — i.e. THIS slice) overrides any `"Open"` flip from the `BiddingOpened` forwarded event. Bidders never see the withdrawn listing as open. The catalog handler is the source of truth.

   This slice **converts Path 3 from a candidate to the observed terminal state via item 3 (Withdrawn-preservation guard) + item 7 (the test that asserts it).** That makes the milestone doc §3 "post-MVP hardening" stance load-bearing on observed behavior, not assumption.

   **Open at session open:** does the lived test confirm Path 3 cleanly, or surface that Path 1 or Path 2 also runs in parallel (i.e., the catalog handler preserves Withdrawn, but the Auctions-side Listing stream + Auction Closing saga independently exhibits Path 1 or Path 2)? S6's test scope is the Listings-side terminal state; the Auctions-side terminal (whether a fresh saga starts or DCB rejects) is **not** S6's scope — but if a Listings-side test surfaces an Auctions-side anomaly (e.g., the test's `BiddingOpened` dispatch triggers a Wolverine cascade that touches the Auctions saga store), **flag in the retro and defer to M4-S7 or post-M4 follow-up**. Do not unilaterally widen S6's scope to chase down the Auctions-side observation.

3. **`AuctionStatusHandler.Handle(BiddingOpened)` guard placement — top-of-method-load-and-return vs `with`-expression-with-conditional-fields.** Two shapes:
   - **Path A — top-of-method guard:** load, check `view.Status == "Withdrawn"`, early return without `session.Store`. M5-S6 `SettlementStatusHandler` precedent (matches the `existing.Status != "Sold"` return early pattern). Cleanest diff. The Auctions handler currently has no top-of-method guard for any other transition; this is the first.
   - **Path B — conditional `with` expression:** load, ternary `view.Status == "Withdrawn" ? view : (view with { Status = "Open", ... })`. Single store. Marten still writes the row even on no-op (wasteful, possibly mutates `Version`).

   **Recommend Path A** — explicit return, no Marten write. Cite M5-S6 `SettlementStatusHandler.cs:56` as the source-of-precedent. If Path B emerges as structurally simpler in implementation (unlikely), record in retro.

4. **`ListingWithdrawn` status-preservation discipline — terminal-state preservation under late arrival.** Per the M5-S6 `SettlementStatusHandler` shape: only the legal pre-state(s) transition. For `ListingWithdrawn`:
   - Arrival from `"Published"` → `"Withdrawn"` (legal — listing was up, now withdrawn)
   - Arrival from `"Open"` → `"Withdrawn"` (legal — bidding was open, now withdrawn — the Workshop 002 §5 "attach withdrawn between attach and start" path produces this)
   - Arrival from `"Closed"` / `"Sold"` / `"Passed"` / `"Settled"` → **no-op** (illegal — the listing's terminal already happened; a late-arriving Withdrawn must not regress the row)

   The M4 milestone doc §8's M4-D5 row says "Resolve in S6." Resolution: **legal pre-states are `"Published"` and `"Open"` only**; all other arrival states no-op without throwing. Document the guard at item 2 (whichever handler class lands the `ListingWithdrawn` consumer).

5. **`ScheduledCloseAt` behaviour under the OQ3 Path α composition test.** The `BiddingOpened.ScheduledCloseAt` field carries a future timestamp. If the guard in item 3 no-ops on `view.Status == "Withdrawn"`, `ScheduledCloseAt` is NOT updated. Two paths:
   - **Path α (recommended):** the guard is total — no fields updated when the guard fires. `ScheduledCloseAt` stays at whatever value it had before (could be null if the listing was Withdrawn before any prior `BiddingOpened`).
   - **Path β:** the guard preserves `Status` but allows `ScheduledCloseAt` update. Semantically ambiguous (the listing isn't actually open, but the close timestamp would be advertised).

   **Recommend Path α** — symmetric with M5-S6 `SettlementStatusHandler`. Document in item 7's test assertion (`view.ScheduledCloseAt` is whatever it was before the composition, not the `BiddingOpened.ScheduledCloseAt` value).

6. **Sub-Option B Wolverine handler discovery — does a single static class with three `Handle` methods (two from one queue, one from another) compose with Wolverine's convention-based discovery?** If Sub-Option B is chosen, this is the first lived multi-source sibling. Wolverine routes by message type, not by class topology, so a `Handle(ListingAttachedToSession)` + `Handle(SessionStarted)` + `Handle(ListingWithdrawn)` in one class should compose correctly with the existing `listings-auctions-events` and `listings-selling-events` queue subscriptions.

   **Verify at first dispatch test** (likely the OQ3 Path α composition test, since it crosses both queues). If discovery surfaces ambiguity, the fix shape is one of: (a) explicit handler-method routing attribute (Wolverine `[MessageHandler]` or equivalent), (b) split per Sub-Option A retroactively. **Halt and consult if discovery fails** — do not paper over with reflection workarounds.

   If Sub-Option A is chosen this OQ is moot — both classes are single-source, single-queue, indistinguishable from the M3-S6 / M5-S6 precedent.

---

## Session sizing notes

- **S6 is the lowest-risk implementation session of M4** per milestone doc §9. Pattern-stable by M3-S6 + M5-S6 precedent; the only new surface is the ADR-014 sub-question + the OQ3 Path α composition test (one assertion). No new infrastructure, no new BC, no new contract; everything S6 needs already exists in the upstream BCs.
- **Scope ceiling: ~7-8 files + 5 tests + 1 ADR amendment + 1 skill amendment.** Below M4-S5 (~14 files + 14 tests) and well below M4-S3 / M4-S4 (the M4 risk nodes). One PR.
- **No split slot pre-drafted.** S6 has no S6b. If S6 overflows its budget, M4-S7 (retro + skills + M4 close) absorbs the tail or the residual lands flagged. Two surfaces are most likely to overflow: (a) item 4's `ListingPublishedHandler` verification surfaces a non-trivial seed-handler refactor; (b) Sub-Option B's first-lived-multi-source-sibling dispatch surfaces a Wolverine discovery ambiguity per OQ6. Either justifies an unplanned `M4-S6b` if it lands mid-session.
- **S7 (the next slice) is the M4 close** — retro + skills + M4 retrospective doc + ADR 014 final verification. S6's retro names anything S7 must absorb.
- **The local-hygiene work the user flagged** (pruning the m4-s5-session-aggregate local branch, pulling main one merge behind origin) is **not** S6 scope. Operator-confirmation-required.

## Document history

- **v0.1** (2026-05-19): Authored at the close of M4-S5 per the retro's "What M4-S6 should know" handoff payload. The six Open Questions are framed by S5's lived discoveries (PublishedListings shape, fan-out idempotency mechanism, OQ3 Path α observation gap) and ADR 014's M5-S6-amended sub-question deferral. The decision to land both ADR-014's sub-question call AND the OQ3 Path α observation gap in this slice is grounded in the M4-S5 retro's explicit naming of both as the natural M4-S6 surface. Narrative 001 Moment 3 remains the joint-authoritative narrative per CLAUDE.md rule 3.
