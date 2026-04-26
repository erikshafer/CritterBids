# M3-S1: Auctions Foundation Decisions + Contract Stubs — Retrospective

**Date:** 2026-04-16
**Milestone:** M3 — Auctions BC
**Session:** S1 of 7
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M3-S1-auctions-foundation-decisions.md`
**Baseline:** 44 tests passing · `dotnet build` 0 errors, 0 warnings · M2.5 complete (tag `M2.5-close`)

---

## Baseline

- 44 tests passing (1 Api + 1 Contracts + 4 Listings + 6 Participants + 32 Selling)
- `dotnet build` — 0 errors, 0 warnings
- `src/CritterBids.Contracts/` contains `SellerRegistrationCompleted.cs` and `Selling/ListingPublished.cs` only — no `Auctions/` directory
- ADR 007 status: Stream IDs ✅ Accepted; Event Row IDs 🟡 Proposed (Gates 1 and 4 open)
- PARKED-QUESTIONS.md: W002-7, W002-8, W002-9 in Open with target "Auctions BC"
- No Auctions BC project exists yet (scaffolded in M3-S2)

---

## Items completed

| Item | Description |
|------|-------------|
| S1a | ADR 007 amended with "Event Row ID Decision — Deferred" section; status header + Gate 4 line annotated; ADR index row + status key updated |
| S1b | `docs/skills/dynamic-consistency-boundary.md` — new "`BidRejected` Stream Placement (Auctions BC)" subsection under CritterBids Usage; resolves W002-7 |
| S1c | `docs/skills/adding-bc-module.md` — two-flavor Marten/Polecat overview table replaced with all-Marten state per ADR 011; Polecat BC Module Registration section marked Historical; ToC + checklist updated |
| S1d | Nine `sealed record` contract stubs authored in `src/CritterBids.Contracts/Auctions/`; `BiddingOpened.cs` docstring records the W002-9 payload decision inline |
| S1e | `docs/workshops/PARKED-QUESTIONS.md` — W002-7 and W002-9 moved from Open to Resolved with "Resolved In: M3-S1"; W002-8 target updated to "Auctions BC (M4)" with M4 rationale in Notes |
| S1f | This retrospective |

Item codes are internal to this retro — the prompt structured scope as four commits rather than lettered items. The mapping is:

| Commit | Items covered |
|--------|---------------|
| 1 — ADR 007 amendment | S1a |
| 2 — skill updates | S1b, S1c |
| 3 — contract stubs | S1d |
| 4 — PARKED-QUESTIONS + retro | S1e, S1f |

---

## S1a — ADR 007 Gate 4 deferral

### Decision

**Gate 4 formally deferred.** Blocker named: JasperFx team input on recommended event row ID
generation strategy for Auctions-scale write workloads had not been received as of 2026-04-16.
Gate 1 (Marten 8 exposing an application-level event row ID generation seam) also unconfirmed —
without that seam, application-layer UUID v7 assignment to event rows is not possible at all
regardless of JasperFx guidance on whether it would be beneficial.

**Default in effect until re-evaluation:** Marten's engine-assigned event row IDs. No
application-layer assignment is performed for event rows in any BC. Stream IDs remain UUID v7
per the accepted Stream ID Decision section, unchanged.

**Re-evaluation trigger:** Before the M3-S4 prompt is drafted (DCB `PlaceBid` / `BuyNow`
authoring — the first high-write DCB use) OR on receipt of JasperFx guidance, whichever
comes first. If neither fires before the M3-S4 prompt is drafted, M3 ships on the engine
default and a further ADR 007 amendment records the trade-off.

### Why deferral over guessing

The prompt's "Open questions" section explicitly anticipated this outcome:

> "If that input is not in hand at session time, the correct output is a formal deferral with a
> named blocker … not an unilateral decision. Deferral is acceptable; guessing is not."

Two concrete reasons to defer rather than pick UUID v7 unilaterally:

1. **Gate 1 is load-bearing for implementation.** Without a confirmed seam, an application-
   layer UUID v7 strategy risks producing code that compiles but silently does nothing (Marten
   assigning its own row IDs regardless of the attempt), producing a false-positive "done"
   state that the DCB `EventTagQuery` tests would not catch.
2. **The insert-locality benefit is real but not M3-functional-correctness load-bearing.** The
   DCB correctness tests (M3-S4) do not depend on event row ID strategy.

### Structural metrics

| Metric | Before | After |
|--------|--------|-------|
| ADR 007 status header glyph | ✅/🟡 | ✅/⏸ |
| "Event Row ID Decision" sections | 0 | 1 (Deferred) |
| Gate 4 line annotated with state | No | Yes (deferred blockquote) |
| ADR 007 index row summary | Mentions "pending Gates 1 and 4" | Mentions "deferred in M3-S1 … re-evaluate before M3-S4" |
| ADR index status-key glyphs | 3 (✅, 🟡, ~~) | 4 (✅, 🟡, ⏸, ~~) |

---

## S1b — W002-7: `BidRejected` stream placement

### Decision

**Dedicated Marten stream type per listing, tagged with `ListingId`.** Not the listing's
primary stream, not a single global audit stream. Excluded from the DCB `EventTagQuery` by
type-filter (narrowing `AndEventsOfType<...>`), not by a separate stream-filter predicate.

### Why not the listing's primary stream

The primary stream feeds the DCB `BidConsistencyState` boundary model. Mixing rejected events
in would either corrupt `CurrentHighBid` / `BidCount` state or force every `Apply()` method
to filter on acceptance. Either option is fragile under schema evolution — a future `Apply()`
overload added without the filter would silently include rejected bids in state calculations.

### Why not a single global audit stream

Access pattern mismatch. Per-listing audit queries (ops tooling investigating a disputed
rejection) start from a listing ID, not a time range. Per-listing tagging matches the access
pattern without full-table scans. Global audit streams are the correct pattern for
"diagnostic firehose" use cases; `BidRejected` is not diagnostic but a first-class audit trail
scoped to a listing's lifecycle.

### Why type-filter exclusion over stream-filter exclusion

The `EventTagQuery` API is `.For(tag).AndEventsOfType<T1, T2, ...>().Or(tag)...`. Narrowing
the type list is the idiomatic exclusion path; no stream-filter predicate API exists. Keeping
the list of accepted-bid event types in one place (the `EventTagQuery.For(...)` call in
`PlaceBidHandler.Load`) is also a single-source-of-truth win.

### Landing location

New subsection "`BidRejected` Stream Placement (Auctions BC)" in
`docs/skills/dynamic-consistency-boundary.md` under the **CritterBids Usage** section. S4
loads that skill before authoring the `PlaceBid` handler, so the decision sits directly in the
reading path of the session that first applies it.

---

## S1c — `adding-bc-module.md` ADR-011 catch-up

### Change

Pre-session, `docs/skills/adding-bc-module.md` carried this overview table:

| Flavor | BCs | Storage | Module pattern |
|---|---|---|---|
| Marten BC | Selling, Listings, Auctions, Obligations, Relay | PostgreSQL | `services.ConfigureMarten()` |
| Polecat BC | Participants, Settlement, Operations | SQL Server | `AddPolecat()` + `ConfigurePolecat()` |

ADR 011 (All-Marten Pivot) moved every BC to Marten. The table is stale — a reader landing
here before reading ADR 011 would conclude that Participants, Settlement, and Operations are
still Polecat BCs.

### Resolution

- Overview table rewritten as a single Marten row listing all eight BCs; paragraph added
  citing ADR 011 and naming the replaced two-flavor history
- "Polecat BC Module Registration" section title → "Polecat BC Module Registration (Historical)"
  with a blockquote header marking the pattern as "not applied to any current CritterBids BC"
- ToC entry updated
- Checklist item "If fixture co-hosts Polecat BCs: `services.ConfigurePolecat(...)` override
  present" dropped — no current CritterBids BC is Polecat so co-hosting with one is impossible
- References entry pointing at `docs/skills/polecat-event-sourcing.md` left intact per prompt
  instruction ("Leave archived-pattern references … they are historical")

---

## S1d — Nine contract stubs

### Files authored

| File | Fields | Transport queue |
|---|---|---|
| `BiddingOpened.cs` | ListingId, SellerId, StartingBid, ReserveThreshold?, BuyItNowPrice?, ScheduledCloseAt, ExtendedBiddingEnabled, ExtendedBiddingTriggerWindow?, ExtendedBiddingExtension?, MaxDuration, OpenedAt | `listings-auctions-events` |
| `BidPlaced.cs` | ListingId, BidId, BidderId, Amount, BidCount, IsProxy, PlacedAt | `listings-auctions-events` |
| `BuyItNowOptionRemoved.cs` | ListingId, RemovedAt | TBD (post-M3 consumers) |
| `ReserveMet.cs` | ListingId, Amount, MetAt | TBD (post-M3 consumers) |
| `ExtendedBiddingTriggered.cs` | ListingId, PreviousCloseAt, NewCloseAt, TriggeredByBidderId, TriggeredAt | TBD (post-M3 consumers) |
| `BuyItNowPurchased.cs` | ListingId, BuyerId, Price, PurchasedAt | TBD (post-M3 consumers) |
| `BiddingClosed.cs` | ListingId, ClosedAt | `listings-auctions-events` |
| `ListingSold.cs` | ListingId, SellerId, WinnerId, HammerPrice, BidCount, SoldAt | `listings-auctions-events` |
| `ListingPassed.cs` | ListingId, Reason, HighestBid?, BidCount, PassedAt | `listings-auctions-events` |

All files use the namespace `CritterBids.Contracts.Auctions`, `sealed record` with positional
constructor, and XML doc comments naming publisher, transport queue, and full consumer list
per the `Selling/ListingPublished.cs` precedent.

### W002-9 decision inline in `BiddingOpened.cs`

The prompt required the W002-9 decision to live in the `BiddingOpened.cs` docstring rather
than a separate doc file ("decisions land where readers look"). The relevant passage:

> "W002-9 (payload completeness) — resolved M3-S1: this contract carries the full extended-
> bidding configuration (enabled flag, trigger window, extension duration, max duration cap)
> rather than requiring the Auction Closing saga to load from stream on each reaction. Saga
> is self-contained from the BiddingOpened event alone — no event-store lookup needed for
> extension logic, which simplifies replay semantics and keeps the saga's only dependency
> on prior events its own saga state."

### `IsProxy` hard-coded-false note in `BidPlaced.cs`

The prompt required a docstring note that the M3 handler always sets `IsProxy = false` and the
M4 proxy saga wires the `true` path with zero contract change. That note is present:

> "IsProxy flag is hard-coded to false by the M3 PlaceBid handler (no proxy path exists in
> M3). M4 wires the Proxy Bid Manager saga to set IsProxy=true on auto-bids. The contract
> shape is stable across M3 and M4 — field is present now to avoid contract churn."

### Payload-completeness discipline

Every contract carries every field any known future consumer will need, not just M3 consumers.
Concrete examples:

- `ListingSold.SellerId` — Settlement (M5) uses this to drive payout without a follow-up lookup
  to Selling. Not needed by M3 (Listings projection uses only `HammerPrice`, `BidCount`), but
  present per `integration-messaging.md` L2.
- `BiddingOpened.SellerId` — DCB `PlaceBid` handler (S4) uses this for the "seller cannot bid
  on own listing" check. Not an integration-consumer field strictly, but present so that
  Auctions-internal listeners reconstructing state from `BiddingOpened` alone don't need a
  side lookup.
- `BiddingOpened.MaxDuration` — Auction Closing saga (S5) needs this to enforce the extended-
  bidding cap on rescheduled closes. Inline per W002-9.

### Build + test verification

| Phase | Test count | `dotnet build` warnings | Result |
|-------|-----------|-------------------------|--------|
| Baseline | 44 | 0 | Green |
| After 9 contract stubs added | 44 | 0 | Green |
| Session close | 44 | 0 | Green |

**Test count unchanged** — the prompt's acceptance criterion ("Adding `sealed record` contract
stubs should not change test count") is met. Stubs are referenced by no handler yet.

### Structural metrics

| Metric | Before | After |
|--------|--------|-------|
| `.cs` files in `src/CritterBids.Contracts/` | 2 | 11 |
| `.cs` files in `src/CritterBids.Contracts/Auctions/` | 0 (directory did not exist) | 9 |
| Contract records in `CritterBids.Contracts.Auctions` namespace | 0 | 9 |
| Contract records across the whole Contracts assembly | 2 | 11 |

---

## S1e — PARKED-QUESTIONS ledger moves

- W002-7 moved Open → Resolved, "Resolved In: M3-S1", resolution row cross-references the new
  section in `dynamic-consistency-boundary.md`
- W002-9 moved Open → Resolved, "Resolved In: M3-S1", resolution row cross-references the
  `BiddingOpened.cs` docstring
- W002-8 — remained Open per prompt's explicit non-goal. Target column updated to "Auctions BC
  (M4)" and Notes rewritten to name the M4 Proxy Bid Manager saga dependency explicitly. No
  row deletion; the question moves with the saga to M4
- Header "Last updated" bumped from 2026-04-10 to 2026-04-16
- Open count 22 → 20; Resolved count 26 → 28

---

## Test results

| Phase | Total tests | Result |
|-------|-------------|--------|
| Baseline | 44 | Green |
| After Commit 1 (ADR 007 amendment) | 44 | Green (docs only) |
| After Commit 2 (skill updates) | 44 | Green (docs only) |
| After Commit 3 (9 contract stubs) | 44 | Green (stubs unreferenced) |
| After Commit 4 (ledger + retro) | 44 | Green (docs only) |

Test count delta across the session: **0**.

---

## Build state at session close

- `dotnet build` — 0 errors, 0 warnings
- `dotnet test` — 44 passing (1 Api + 1 Contracts + 4 Listings + 6 Participants + 32 Selling)
- Contract records under `src/CritterBids.Contracts/Auctions/`: **9**
- Contract records under `src/CritterBids.Contracts/Selling/`: 1 (unchanged)
- Contract records at `src/CritterBids.Contracts/` root: 1 (unchanged — `SellerRegistrationCompleted`)
- `.cs` files modified in `src/` beyond the new `Auctions/` directory: **0**
- ADRs modified: 1 (`007-uuid-strategy.md`); ADRs created: 0 (per prompt scope)
- Skill files modified: 2 (`dynamic-consistency-boundary.md`, `adding-bc-module.md`)
- Handlers, modules, projections, or tests modified: **0** — docs-and-contracts-only session, as scoped

---

## Key learnings

1. **Deferral with a named trigger is a first-class decision outcome.** Gate 4 closed not with
   a v7-accepted or engine-default answer but with a dated, trigger-bound deferral. The
   alternative — guessing a v7 strategy while Gate 1 is unconfirmed — would have been worse
   than deferral, because it might have compiled-but-silently-not-worked. Future sessions
   hitting decisions where key input is missing should default to "defer with a named
   blocker + trigger" rather than picking to unblock flow. The M3-S4 prompt draft is the
   forcing function; if Gate 4 is still open then, the default-path amendment lands in S4.

2. **Decisions go where their readers are.** Three decisions in this session landed in three
   different locations: ADR 007 for Gate 4 (architectural), `dynamic-consistency-boundary.md`
   for W002-7 (implementation pattern), and `BiddingOpened.cs` XML docstring for W002-9 (the
   type the decision governs). The prompt's "do not duplicate the decisions across locations;
   cross-reference if needed" rule produces less rot than a single "decisions.md" file would.
   Future sessions should apply the same test: which file is the reader of this decision most
   likely to open first? Put the decision there.

3. **Vocabulary lock before implementation is a real discipline, not overhead.** Nine
   contract stubs with no handlers look like ceremony. The M2 retrospective's "three rapid
   ADR pivots" warning (referenced in the S1 prompt) is exactly what this session exists to
   prevent — had `BiddingOpened` lacked `MaxDuration` at M3-S2, the saga author at S5 would
   have either (a) discovered the gap mid-saga-authoring and pivoted, or (b) shipped a
   saga that reloads config from stream. The former is three days of churn; the latter is
   the wrong W002-9 answer baked into code. Paying 1 session of vocabulary-lock cost avoids
   both.

4. **`integration-messaging.md` L2 ("full payload for all future consumers at first commit")
   continues to hold as a contract-authoring discipline.** Three of the nine contracts carry
   fields that no M3 consumer uses (`ListingSold.SellerId` for Settlement M5;
   `BiddingOpened.SellerId` for the Auctions-internal seller-cannot-bid check at S4;
   `BiddingOpened.MaxDuration` for S5 saga reschedule logic). Omitting them would save one
   field per commit and cost one contract-versioning round-trip at the consumer-wire session.
   The same call was made on `ListingPublished` at M2-S6 and has held without revision.

5. **Historical-pattern retention vs stale-documentation removal is a real distinction.** The
   `adding-bc-module.md` fix had two overlapping concerns: (a) correct the stale two-flavor
   table, (b) preserve the Polecat-registration reference for CritterStackSamples archive
   readers. The prompt drew a clean line — "Leave archived-pattern references intact …
   Update downstream prose that references Polecat as an *active* flavor." The resolution
   (historical-header + retained code block + dropped checklist item + References link
   intact) is the shape future "ADR-011-era cleanup" work should follow.

---

## Verification checklist

- [x] `docs/decisions/007-uuid-strategy.md` — new "Event Row ID Decision — Deferred" section present; status header updated; Gate 4 line annotated with deferred state
- [x] `docs/decisions/README.md` — ADR 007 row Summary column updated; Status column updated; status key extended to include ⏸
- [x] `docs/skills/dynamic-consistency-boundary.md` — `BidRejected` stream placement decision recorded with rationale under "CritterBids Usage"
- [x] `docs/workshops/PARKED-QUESTIONS.md` — W002-7 and W002-9 moved Open → Resolved with "Resolved In: M3-S1"; W002-8 target updated to "Auctions BC (M4)" with M4 rationale in Notes
- [x] `src/CritterBids.Contracts/Auctions/` — directory exists; contains exactly nine `.cs` files, one per event listed in the prompt
- [x] Each contract file uses namespace `CritterBids.Contracts.Auctions`; `sealed record`; triple-slash summary with publisher, transport queue (or "TBD" for post-M3 consumers), full consumer list
- [x] `BiddingOpened.cs` docstring explicitly records the W002-9 payload decision
- [x] `BidPlaced.cs` carries `IsProxy: bool` with a docstring note that M3 always sets it to `false`; M4 wires the proxy path with zero contract change
- [x] `docs/skills/adding-bc-module.md` — two-flavor Marten/Polecat overview table replaced with all-Marten state; historical Polecat references preserved
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] `dotnet test` — 44 passing (baseline unchanged)
- [x] This retrospective exists; records each decision's final state, the nine contract file paths, and a "what M3-S2 should know" note (below)

---

## What M3-S2 should know

**M3-S2 is the Auctions BC scaffold session.** S1 locked vocabulary and deferred Gate 4; S2
creates the `CritterBids.Auctions` project, `CritterBids.Auctions.Tests` project,
`AddAuctionsModule()` extension, Marten configuration, `Listing` aggregate empty shell,
`CritterBids.Api.csproj` project reference, and a smoke test.

Concrete items S2 should walk in with:

1. **Gate 4 remains deferred until the M3-S4 prompt is drafted.** S2 does not need to touch
   Gate 4. Stream IDs are UUID v7 (already accepted, already applied in every Marten BC). Event
   row IDs stay on the Marten engine default. If S2's smoke test needs to create a stream, it
   uses `Guid.CreateVersion7()` for the stream ID and does nothing for event row IDs.

2. **The nine contract stubs are referenced by S2 only via `typeof(BiddingOpened).Assembly`
   discovery or similar — no event type registration yet.** The S2 scaffold registers the
   aggregate type and projection lifecycle via `services.ConfigureMarten()`; event type
   registration for `BiddingOpened`, `BidPlaced`, etc. comes with their first use in S3 (the
   `ListingPublished` consumer that produces `BiddingOpened`) and S4 (the DCB handler that
   produces `BidPlaced` and friends). Registering event types before first use risks the
   "silent `AggregateStreamAsync<T>` null returns" class of error the M2 retro warned about.

3. **The `adding-bc-module.md` skill is current and clean after this session.** S2 follows
   the Marten BC registration path verbatim. If S2 discovers any further ADR-011-era staleness
   in the skill, that is an additional doc fix; but the overview table and flavor split are
   correct.

4. **`CritterBids.Auctions` is the sixth BC on the shared Marten store (per ADR 009).** Its
   schema name is `auctions`; no cross-schema references. `services.ConfigureMarten()`
   contributes Auctions types to the single primary store. See `adding-bc-module.md` for
   the canonical Marten BC module pattern.

5. **S1 explicitly did NOT touch `Program.cs`.** The `auctions-selling-events` and
   `listings-auctions-events` queues are named in the M3 milestone doc but wired in S3
   and S6 respectively. S2 adds `services.AddAuctionsModule()` to `Program.cs` and adds the
   `CritterBids.Auctions` assembly to the Wolverine `opts.Discovery.IncludeAssembly()` call,
   but does not touch routing rules.

6. **`CritterBids.Api.csproj` gains a `<ProjectReference>` to `CritterBids.Auctions.csproj`
   in S2.** Per the M2-S7 discovery documented in `adding-bc-module.md`, the `Program.cs`
   `typeof(...)` pattern requires the project reference. This is a known step, not a
   discovery.

7. **The M2 retro's "three rapid ADR pivots" warning is the thing S1's vocabulary lock is
   defending against.** S2 should not encounter ambiguity about Gate 4, `BidRejected`
   placement, or `BiddingOpened` payload shape. If it does, the correct response is a stop-
   and-flag to the session prompt author, not an ad-hoc pivot.

---

## What remains / deferred into later M3 sessions

**In scope for M3, deferred to later sessions:**

- Auctions BC project scaffold (S2)
- `ListingPublished` consumer producing `BiddingOpened` (S3)
- DCB boundary model + `PlaceBid` + `BuyNow` handlers (S4)
- Auction Closing saga (S5)
- Listings catalog auction-status fields (S6)
- Retrospective skill updates to `dynamic-consistency-boundary.md` and `wolverine-sagas.md` (S4 and S5)
- M3 milestone retrospective (S7)

**Out of scope for M3, tracked elsewhere:**

- Proxy Bid Manager saga (M4); W002-8 resolution waits for M4
- Session aggregate (M4)
- `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`, `RegisterProxyBid`,
  `ProxyBidRegistered`, `ProxyBidExhausted` contracts (M4)
- Settlement BC consuming `ListingSold` / `BuyItNowPurchased` (M5)
- Gate 4 re-evaluation if JasperFx input arrives — trigger set before M3-S4 prompt draft

**Still deferred from M2:**

- RabbitMQ routing in BC modules vs `Program.cs` (threading `WolverineOptions` into module
  methods) — deferred again in M3 per `M3-auctions-bc.md` §8
