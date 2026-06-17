# M9-S7: CatalogListingView Cross-Queue Race Fix - Retrospective

**Date:** 2026-06-16
**Milestone:** M9 - Seller Console
**Slice:** S7 - Listings cross-queue race fix (backend bug, pre-close housekeeping)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M9-S7-listings-cross-queue-race-fix.md`
**Duration:** ~1.5h

## Baseline

- Clean main at `7ba744f` (M9-S6 PR #110 + CI restructure #111 shipped); branched to `m9-s7-listings-cross-queue-race-fix`
- Full backend suite: 326 tests green; Listings BC: 22 tests
- `CatalogListingView` written by five handler classes / twelve handler methods on three RabbitMQ queues (`listings-selling-events`, `listings-auctions-events`, `listings-settlement-events`), **all via `session.Store`** (full-document upsert, no concurrency guard)
- Documented bug (memory `listings-cross-queue-race`, observed 2026-06-13): two queue handlers `LoadAsync` the same listing as null, both `Store` a fresh row, last writer wins — selling fields (`Title`, `SellerId`, `StartingBid`) silently lost
- `ListingsModule` schema config was bare (`DatabaseSchemaName("listings")` only); no retry policy registered

## Items completed

The prompt's `## In scope` items are preserved below by their original numbers. Items 1–2
describe the pre-spike `UseNumericRevisions` hypothesis that OQ-1's spike **refuted**; they were
attempted, reverted, and superseded by the `Insert`-on-create mechanism. See the spike subsection.

| Item | Prompt intent | Outcome |
|------|---------------|---------|
| 1 | Add `Version` property to `CatalogListingView` | **Superseded** — added to run the spike, then reverted. `Insert` needs no version field |
| 2 | `UseNumericRevisions(true)` in `ListingsModule` | **Superseded** — spike proved it does not enforce on a plain `Store`; reverted |
| 3 | `ListingsConcurrencyRetryPolicies : IWolverineExtension` | **Done** — retries on `DocumentAlreadyExistsException` (not `ConcurrencyException`, per spike) |
| 4 | Concurrency race test, both orderings | **Done** — 2 bidirectional tests |
| 5 | Existing baseline preserved | **Done** — 326 → 328, no existing test modified |
| OQ-1 | Resolve the enforcement question first (the gate) | **Done** — live-Postgres spike; result reshaped items 1–3 |

## OQ-1 spike: numeric revisions do not enforce on a plain `Store`

The slice was gated on one question: does `UseNumericRevisions(true)` make the losing concurrent
`Store` throw? A diagnostic test reproduced the exact data-loss interleaving (both sessions stage
against `null`; selling commits first, auction second) under two write strategies against a real
Testcontainers Postgres. Verbatim findings:

```
[A] Store vs Store (live handlers):
      loser threw : <no exception>
      final Title : ''  (empty/minimal => last-writer-wins data loss)
      final Status: Open
      final Version: 2
[B] Insert vs Insert (candidate create-path):
      loser threw : JasperFx.DocumentAlreadyExistsException: Document already exists
                    CritterBids.Listings.CatalogListingView: 019ed3b1-...
      final Title : 'SELLING-RECORD'
      final Status: Published
      final Version: 1
```

**Why the prompt's lean was wrong.** Scenario A's `Version: 2` is the tell — `UseNumericRevisions`
*bumped* the revision (selling=1 → auction=2) but the second `Store` still committed and clobbered
the data. This is the same lesson the Auctions saga docstrings (`AuctionClosingSaga`,
`ProxyBidManagerSaga`) record from M8-S3c: numeric revisions only *enforce* when the write path
emits a revision-checked statement. The sagas get that from Wolverine's saga-persistence frame
(`UpdateSagaRevisionFrame`, gated on `IRevisioned`); a plain read-model handler calling
`session.Store` has no such frame. Marten's own docs corroborate: `Store` is a non-enforcing upsert
("just assigns the next revision"); `UpdateRevision`/`Insert` are the enforcing operations.

**Why `Insert` (and not `UpdateRevision`).** The observed bug is an *insert-insert* race — both
writers believe the row is new. `Insert` makes the losing writer collide on the document primary
key (`DocumentAlreadyExistsException`), which is exactly the trip-wire needed, with no version
column to maintain. `UpdateRevision` would only matter for an *update-update* race on an existing
row, which was never the observed failure (deferred — see OQ-3).

The `IRevisioned` + `UseNumericRevisions` edits made to run the spike were reverted; the spike test
was deleted.

## Insert-on-create + retry

Each handler already carried correct *merge* logic (the M5-S6/M4-S6 load-and-preserve blocks). The
only missing piece was a trip-wire that forces a re-merge instead of a silent overwrite. The change
is therefore minimal per site: switch the **create branch** from `Store` to `Insert`; leave the
merge branch on `Store`.

A shared extension centralises the idiom for the ten `?? new` sites:

```csharp
internal static void InsertOrStore(this IDocumentSession session, CatalogListingView? existing, CatalogListingView next)
{
    if (existing is null) session.Insert(next);   // create: concurrent creator collides
    else                  session.Store(next);    // merge: load-and-preserve, single writer post-retry
}
```

The two handlers that already branched explicitly on `existing is null` (`SettlementStatusHandler`,
`SellingListingWithdrawnHandler`) got a direct `session.Insert(new …)` in that branch.

Retry policy mirrors `AuctionsConcurrencyRetryPolicies` exactly — an `IWolverineExtension`
registered as a singleton, not inline in `UseWolverine()`:

```csharp
options.OnException<DocumentAlreadyExistsException>()
    .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
```

On collision, Wolverine re-runs the losing handler; its `LoadAsync` now returns the committed row,
so it takes the merge path and both field sets survive.

| Metric | Before | After |
|--------|--------|-------|
| `CatalogListingView` create-branch writes via `session.Store` | 12 | 0 |
| create-branch writes via `session.Insert` | 0 | 12 (10 via `InsertOrStore`, 2 direct) |
| `IWolverineExtension` retry policies in Listings BC | 0 | 1 |
| `UseNumericRevisions` on `CatalogListingView` | 0 | 0 |
| `IRevisioned` implementations in Listings BC | 0 | 0 |
| Domain events / contracts added | 0 | 0 |

## Discovery: `existing` name collision

The `AuctionStatusHandler` `replace_all` split (`?? new` → captured `existing`) was clean, but
`AuctionsSessionHandler.Handle(SessionStarted)` already binds `existing` to the batch-query result,
so reusing the name in the loop failed to compile:

```
src/CritterBids.Listings/AuctionsSessionHandler.cs(68,57): error CS0136: A local or parameter
named 'existing' cannot be declared in this scope because that name is used in an enclosing local
scope to define a local or parameter
```

Resolution: the loop variable kept its original name `current`; `InsertOrStore(current, …)`.

## Test results

| Phase | Listings Tests | Full suite | Result |
|-------|---------------|-----------|--------|
| Baseline (M9-S6 close) | 22 | 326 | Pass |
| OQ-1 spike (temporary) | — | — | Diagnostic (deleted) |
| After Insert + retry + 2 tests | 24 (+2) | 328 (+2) | Pass |
| **Final** | **24** | **328** | **Pass** |

The two new tests (`CrossQueueCreateRace_SellingCommitsFirst_AuctionRetryPreservesBothFieldSets`,
`CrossQueueCreateRace_AuctionCommitsFirst_SellingRetryPreservesBothFieldSets`) each assert the
collision throws `DocumentAlreadyExistsException` *and* the simulated retry merges both field sets.
One full-suite run showed a transient `Selling` failure (1/45) that passed on isolated re-run and on
a clean full re-run — Testcontainers startup contention (~10 Postgres containers), not a regression
(Selling has no reference to Listings).

## Build state at session close

- **Errors:** 0
- **Warnings:** 1 pre-existing — `NU1903` MessagePack advisory in `CritterBids.AppHost`, unchanged from baseline, untouched by this slice
- `session.Store` on a `CatalogListingView` **create** path: **0** (was 12)
- `session.Insert` for `CatalogListingView`: **12** (10 via `InsertOrStore`, 2 direct in explicit-null branches)
- `UseNumericRevisions` / `IRevisioned` / `Version` property on `CatalogListingView`: **absent** (spike edits fully reverted; `CatalogListingView.cs` is byte-identical to baseline)
- `OnException<…>` in `ListingsConcurrencyRetryPolicies`: **1** (`DocumentAlreadyExistsException`)
- `OnException<ConcurrencyException>` in Listings BC: **0** (the prompt's original exception type — corrected by the spike)

## Key learnings

1. **`UseNumericRevisions(true)` is not self-enforcing — it requires a revision-checked write path.** A plain `session.Store` bumps `mt_version` and commits anyway (last-writer-wins). Enforcement comes from `UpdateRevision` (or, for sagas, Wolverine's `UpdateSagaRevisionFrame` gated on `IRevisioned`). For a handler-driven read-model projection, neither applies — reach for `Insert` instead. This generalises to every BC's handler-driven projection, not just Listings.

2. **The insert-insert race is closed by the primary key, not by versioning.** `session.Insert` throws `JasperFx.DocumentAlreadyExistsException` on a duplicate id; pairing it with a retry that re-loads and merges is lighter than any revision scheme and adds no document field.

3. **A PK trip-wire is only as strong as its weakest writer.** Every handler method that can be the *first* writer must `Insert` on its create branch; leaving any one on `Store` reopens the overwrite for that interleaving. All twelve `CatalogListingView` write methods were converted, not just the two named in the bug report.

4. **The codebase's own scar tissue answered the open question faster than the docs.** The `AuctionClosingSaga` docstring already recorded the "numeric revisions bump but don't enforce" lesson from M8-S3c. Reading sibling-BC docstrings before spiking would have pre-loaded the answer; the spike confirmed it empirically in ~10 minutes.

5. **Gate-first paid off.** Resolving OQ-1 before writing the fix meant the wrong mechanism (the prompt's written hypothesis) was discarded at near-zero cost, instead of shipping a green-but-inert `UseNumericRevisions` change whose retry would never fire.

## Findings against narrative

The prompt declares `Narrative: none` — this is a structural concurrency fix in a read-model
projection, not a user-facing journey, so it anchors to no narrative and none was amended. No
follow-up narrative is warranted: the catalog's observable behaviour (the fields a bidder/seller
sees) is unchanged; only the durability of those fields under concurrent cross-queue delivery
improved. The catalog read shapes that narratives 001/006 depend on are untouched.

## Spec delta — landed?

Prompt declared **no spec consequence** ("a concurrency bug fix in an existing read-model
projection. No new domain events, contracts, or narrative moments"). Confirmed: zero changes to
`CritterBids.Contracts`, zero new/renamed domain events, no ADR amended. The `CatalogListingView`
wire shape is byte-identical to baseline (the spike's `Version` field was reverted), so no API
response or frontend Zod schema is affected. No narrative, workshop, or ADR Document History row was
required.

## Verification checklist

Mirrors the prompt's acceptance criteria 1:1, annotated where the spike corrected the approach.

- [~] `CatalogListingView` has a `Version` property / `IRevisioned` — **intentionally not done.** OQ-1 proved the Insert mechanism needs neither; the field was reverted to avoid a misleading inert version column
- [~] `ListingsModule.cs` configures `UseNumericRevisions(true)` — **intentionally not done** (same reason)
- [x] A `ListingsConcurrencyRetryPolicies` extension is registered and retries — on `DocumentAlreadyExistsException` (spike-corrected from `ConcurrencyException`)
- [x] A new test proves both handlers racing on the same document preserve all fields from both — 2 tests, both orderings
- [x] All existing Listings tests pass without modification
- [x] Full `dotnet build` clean — 0 errors (1 pre-existing unrelated `AppHost` advisory warning)
- [x] Full `dotnet test` green — 328 (> the prompt's 307+ bar)
- [x] Retrospective written (this file)

## What remains / next session should verify

- **OQ-3 — residual update-update race (deferred, out of scope by design).** Two handlers both loading an *existing* row and both `Store`-ing can still last-writer-win on a contended field. Never the observed bug; the fields are largely authoritative/monotonic. Closing it means converting every merge write to `UpdateRevision` across four handler files ("Insert + revision-enforced merges", the unchosen option). Tracked alongside the analogous **Operations BC** multi-handler read-model pattern (`DisputeQueueView`, `SellerPerformanceView`), which the prompt already scoped out.
- **Raw `23505` path (note, not a task).** Under truly simultaneous commits Marten *should* still surface `DocumentAlreadyExistsException` (it wraps the PK violation), but a raw Npgsql `23505` was not exercised. If a DLQ ever shows one, extend the retry filter.
- **M9 close is now S8, not S7.** This slice took the S7 number; the e2e + housekeeping close slice (Playwright, CI frontend job, `CLAUDE.md`/`STATUS.md` refresh, skills audit, M9 retro) shifts to **M9-S8** (per memory `m9-skills-review`).

## Files changed

**New:**
- `src/CritterBids.Listings/CatalogListingViewWrites.cs` — `IDocumentSession.InsertOrStore` helper

**Modified (src):**
- `src/CritterBids.Listings/ListingsModule.cs` — `ListingsConcurrencyRetryPolicies` extension + registration
- `src/CritterBids.Listings/ListingPublishedHandler.cs` — create branch → `InsertOrStore`
- `src/CritterBids.Listings/AuctionStatusHandler.cs` — 7 methods → `InsertOrStore`
- `src/CritterBids.Listings/AuctionsSessionHandler.cs` — 2 methods → `InsertOrStore`
- `src/CritterBids.Listings/SettlementStatusHandler.cs` — null branch → `Insert`
- `src/CritterBids.Listings/SellingListingWithdrawnHandler.cs` — null branch → `Insert`

**Modified (tests):**
- `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs` — 2 bidirectional race tests + `BuildOpened` helper

**Docs:**
- `docs/prompts/implementations/M9-S7-listings-cross-queue-race-fix.md` — session prompt (OQ-1/OQ-2 resolved, OQ-3 added, post-spike correction banner)
- `docs/retrospectives/M9-S7-listings-cross-queue-race-fix-retrospective.md` — this file

**Unchanged (notable):** `src/CritterBids.Listings/CatalogListingView.cs` — spike edits fully reverted; byte-identical to baseline.
