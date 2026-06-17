# M9-S7: CatalogListingView Cross-Queue Race Fix

**Milestone:** M9 (Seller Console)
**Slice:** S7 (listings cross-queue race fix — backend bug, pre-close housekeeping)
**Narrative:** none — this is a structural concurrency bug in the Listings BC read-model projection, not a user-facing journey
**Agent:** @PSA
**Estimated scope:** one PR, ~5 files

## Goal

Fix the CatalogListingView last-writer-wins race condition between the `listings-selling-events` and `listings-auctions-events` queue handlers. When `ListingPublishedHandler` and `AuctionStatusHandler` (or `AuctionsSessionHandler`) concurrently `LoadAsync` the same document as null, both create a new `CatalogListingView`, and the last `Store()` to commit overwrites the first — silently losing selling fields (title, format, sellerId, startingBid) when the auction handler commits last.

The fix applies Marten's `UseNumericRevisions` to `CatalogListingView` so that concurrent writes cause a `ConcurrencyException` instead of silent overwrites, paired with a Wolverine retry policy that re-executes the losing handler against the now-current document.

## Context to load

- `CLAUDE.md`: routing layer and global conventions
- `src/CritterBids.Listings/CatalogListingView.cs`: the document at risk
- `src/CritterBids.Listings/ListingsModule.cs`: module registration (add `UseNumericRevisions` here)
- `src/CritterBids.Listings/ListingPublishedHandler.cs`: the seed handler (selling fields)
- `src/CritterBids.Listings/AuctionStatusHandler.cs`: the status handler (auction fields) — primary racing counterpart
- `src/CritterBids.Auctions/AuctionsModule.cs` (lines 140–160): existing `AuctionsConcurrencyRetryPolicies` pattern to follow
- `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs`: existing test suite
- `tests/CritterBids.Listings.Tests/Fixtures/ListingsTestFixture.cs`: test fixture

## In scope

> **Post-spike correction (M9-S7):** Items 1–3 describe the pre-spike `UseNumericRevisions`
> hypothesis. OQ-1's live spike refuted it — a plain `session.Store` under numeric revisions bumps
> `mt_version` but still commits and last-writer-wins (no exception). The **implemented** mechanism
> is `Insert`-on-create across all twelve `CatalogListingView` write methods (five handler classes), paired with a retry policy
> keyed on `JasperFx.DocumentAlreadyExistsException` (the primary-key collision — not
> `ConcurrencyException`). No `Version` property and no `UseNumericRevisions` were kept. See OQ-1
> (resolved) below and the retrospective for the full reasoning.

1. **`CatalogListingView` versioning:** Add a `Version` property to `CatalogListingView` for Marten numeric revision tracking.

2. **`ListingsModule` schema configuration:** Add `UseNumericRevisions(true)` to the `opts.Schema.For<CatalogListingView>()` chain.

3. **Listings concurrency retry policy:** Create a `ListingsConcurrencyRetryPolicies : IWolverineExtension` (following the Auctions/Settlement BC pattern) that retries on `ConcurrencyException` with cooldown. Register it in `ListingsModule.AddListingsModule()`.

4. **Concurrency race test:** Add a test in `CatalogListingViewTests.cs` that proves the race is handled — specifically, that when the auction handler's document already exists (simulating the first writer winning), the seed handler's re-execution preserves the auction fields while filling in the selling fields. The reverse order too. The existing `SiblingHandlers_CoexistOnSameView_NoOverwrites` test exercises sequential ordering; the new test targets the concurrency mechanism itself.

5. **Existing test baseline preserved:** All 307+ backend tests pass, including the existing 19 Listings tests.

## Explicitly out of scope

- **Operations BC cross-queue race.** The Operations BC has a similar multi-handler read-model pattern (`DisputeQueueView`, `SellerPerformanceView`). Whether it needs the same fix is a separate evaluation — not this slice.
- **Marten Patch API.** The memory listed this as Option 2 — field-level updates to avoid full-document overwrite. The `UseNumericRevisions` approach is lighter and follows the established Auctions BC precedent.
- **Queue consolidation.** Option 3 from the memory — merging queues to serialize delivery. This would defeat the separated-handler design (ADR 027) that M8-S3c established.
- **New domain events or contract changes.** This is purely a read-model concurrency fix.
- **Frontend changes.** The `CatalogListingView.Version` field is internal to Marten's concurrency tracking; it does not surface in API responses unless explicitly projected.
- **The M9-S7 (now M9-S8) e2e close slice.** That slice's scope (Playwright, CI, doc refresh, M9 retro) is unchanged; it shifts to S8.

## Conventions to pin or follow

- **`UseNumericRevisions` + retry for handler-driven projections under cross-queue concurrency.** This is the first Listings BC use; the Auctions and Obligations BCs already use this pattern for saga concurrency. The Listings BC adds it for read-model concurrency — a novel application of the same mechanism.
- **`IWolverineExtension` for retry policies.** Per the Auctions and Settlement BC precedent, not inline in `UseWolverine()`.

## Spec delta

- No spec consequence: this is a concurrency bug fix in an existing read-model projection. No new domain events, contracts, or narrative moments.

## Acceptance criteria

- [ ] `CatalogListingView` has a `Version` property (or implements `IRevisioned` if needed by Wolverine's retry path)
- [ ] `ListingsModule.cs` configures `UseNumericRevisions(true)` for `CatalogListingView`
- [ ] A `ListingsConcurrencyRetryPolicies` extension is registered and retries on `ConcurrencyException`
- [ ] A new test proves that when both handlers race on the same document, all fields from both handlers are present on the final document
- [ ] All existing Listings tests pass without modification (the `Version` property is additive; `SeedCatalogListingViewAsync` may need a trivial default)
- [ ] Full `dotnet build` clean (0 errors / 0 warnings)
- [ ] Full `dotnet test` green (307+ tests)
- [ ] Retrospective written

## Open questions

- **OQ-1 — RESOLVED (live Postgres spike, both sessions loading null then committing in sequence):**
  The lean was wrong. `UseNumericRevisions(true)` on the schema is **not** sufficient for a plain
  `session.Store` path. The spike showed `Store` bumps `mt_version` (selling=1 → auction=2) but the
  second commit still succeeds and last-writer-wins — selling fields lost, **no exception thrown**.
  This is precisely the lesson the Auctions saga docstrings (`AuctionClosingSaga`) record from
  M8-S3c: numeric revisions only *enforce* when the write path emits a revision-checked statement,
  and for the sagas that path is Wolverine's saga-persistence frame (gated on `IRevisioned`) — which
  a plain read-model handler calling `session.Store` does not have. The mechanism that *does* enforce
  is `session.Insert` on the create path: the document primary key makes the losing concurrent
  creator throw `JasperFx.DocumentAlreadyExistsException`. So `IRevisioned`/`UseNumericRevisions`
  were **reverted** (inert and misleading here); the fix is `Insert`-on-create + retry on
  `DocumentAlreadyExistsException`. The retried handler re-loads the committed row and merges via
  `Store`, preserving both field sets.
- **OQ-2 — RESOLVED:** Yes — *every* handler that can be the first writer must use `Insert` on its
  create branch, not just the `ListingPublished` ↔ `BiddingOpened` pair. The PK trip-wire only holds
  if no create site is left on `Store`; otherwise that interleaving still overwrites silently. All
  five handler classes / twelve write methods (`ListingPublishedHandler` ×1, `AuctionStatusHandler`
  ×7 event methods, `AuctionsSessionHandler` ×2, `SettlementStatusHandler` ×1,
  `SellingListingWithdrawnHandler` ×1) now `Insert`-on-create — via the shared `IDocumentSession.InsertOrStore` helper for the `?? new`
  sites, or a direct `Insert` in the two explicit-null branches. The retry policy is global to the BC
  and keys on `DocumentAlreadyExistsException`. (The original lean assumed `UseNumericRevisions` on
  the document gave automatic protection; OQ-1 showed it does not.)

- **OQ-3 (new, deferred):** The fix closes the **insert-insert** create race. A residual
  **update-update** race remains theoretically possible (two handlers both load an *existing* row and
  both `Store`, last-writer-wins on a contended field) — but it was never the observed bug, the
  fields are largely authoritative/monotonic, and closing it would mean converting every merge write
  to `UpdateRevision` across four handler files (the "Insert + revision-enforced merges" option, not
  chosen). Documented as a carry-forward, alongside the Operations BC's analogous multi-handler
  read-model pattern. Also residual: under truly simultaneous commits Marten *should* still surface
  `DocumentAlreadyExistsException` (it wraps the PK violation), but a raw Npgsql `23505` path was not
  exercised — worth a note if a DLQ ever shows one.
