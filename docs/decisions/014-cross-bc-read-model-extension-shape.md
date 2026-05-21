# ADR 014 — Cross-BC Read-Model Extension Shape

**Status:** ✅ Accepted
**Date:** 2026-05-17 (initial) / 2026-05-20 (M4-S6 amendment — third lived application, sub-question resolved)
**Milestone:** M5-S6 — Settlement Outbound Publish Routes + Listings Catalog `Settled` Status + ADR-014 Authoring + M5 Milestone Close · amended M4-S6 — Listings Catalog Session Membership + Withdrawn Status

---

## Context

CritterBids' Listings BC owns the catalog read model (`CatalogListingView`). As successive BCs ship, their integration events extend the catalog with new fields:

- **M2-S7** (Selling source): `ListingPublished` populates the M2 base — `Id, SellerId, Title, Format, StartingBid, BuyItNow, Duration, PublishedAt`.
- **M3-S6** (Auctions source): the six auction integration events (`BiddingOpened, BidPlaced, BiddingClosed, ListingSold, ListingPassed, BuyItNowPurchased`) populate auction-status fields — `Status, ScheduledCloseAt, CurrentHighBid, CurrentHighBidderId, BidCount, HammerPrice, WinnerId, PassedReason, FinalHighestBid, ClosedAt`.
- **M5-S6** (Settlement source): `SettlementCompleted` populates the settlement-status field — `SettledAt` — and transitions `Status` from `"Sold"` to `"Settled"`.

At M3-S6 the pattern surfaced for the first time: how does the Listings BC absorb projection fields whose source events are integration contracts from foreign BCs, without violating the modular-monolith BC-isolation rule (ADR-001)?

M3-S6's resolution — recorded as **M3-D2 Path A** in the M3 milestone retrospective — was: **one view per logical entity, sibling handler classes per source BC, fields added additively**. The decision was made with one lived application (the M3-S6 `AuctionStatusHandler` extending the M2-S7 base). The M3-S7 ADR candidate review explicitly deferred body authoring of this ADR (then numbered ADR 013, renumbered to ADR 014 at the M4-S1 reservation per the ADR README's numbering convention) **to the second lived application of the pattern**, on the grounds that one example is too thin a precedent to enshrine an ADR.

The original M4-S6 planning slot expected the second application to be `SessionMembershipHandler` extending `CatalogListingView` with session-membership and withdrawn fields. M4 paused after M4-S2 (the Auctions BC completion milestone has not yet shipped its remaining slices). The M5-S6 `SettlementStatusHandler` is therefore the chronological second application by lived ground, and this slice authors the ADR body.

---

## Options Considered

### Option A — One view per logical entity; sibling handler classes per source BC; fields additive (chosen)

`CatalogListingView` is a single Marten document keyed by `ListingId`. Each contributing BC owns one sibling handler class inside `CritterBids.Listings` that consumes its events and writes its disjoint field set. The contract chain at M4-S6 close (amended from the M5-S6-close shape; the three rows below the seed handler now total four, not two, after M4-S6 split the Session-membership and Selling-withdrawn surfaces into two single-source siblings per the sub-question resolution):

| Handler | Source BC | Source events | Fields owned |
|---|---|---|---|
| `ListingPublishedHandler` | Selling (M2-S7) | `ListingPublished` | `SellerId, Title, Format, StartingBid, BuyItNow, Duration, PublishedAt` |
| `AuctionStatusHandler` | Auctions (M3-S6) | six auction events | `Status` (subset of transitions), `ScheduledCloseAt, CurrentHighBid, CurrentHighBidderId, BidCount, HammerPrice, WinnerId, PassedReason, FinalHighestBid, ClosedAt` |
| `SettlementStatusHandler` | Settlement (M5-S6) | `SettlementCompleted` | `Status` (`"Sold"` → `"Settled"` transition only), `SettledAt` |
| `AuctionsSessionHandler` | Auctions (M4-S6) | `ListingAttachedToSession`, `SessionStarted` | `SessionId, SessionStartedAt` |
| `SellingListingWithdrawnHandler` | Selling (M4-S6) | `ListingWithdrawn` | `Status` (`"Published"` / `"Open"` → `"Withdrawn"` transition only), `ClosedAt` (on the Withdrawn arrival) |

Cross-BC events flow over RabbitMQ queues; the Listings BC's handler discovery routes each event to its sibling. Each handler follows the **tolerant-upsert** primitive per [`marten-projections.md §"Handler-Driven Projections — Tolerant Upsert"`](../skills/marten-projections.md): `LoadAsync` by `ListingId`; if absent, construct a minimal row; mutate via record `with`; `session.Store`. The `Status` field carries the workflow-position vocabulary across sources via per-handler **status-preservation guards** mirroring the M5-S3 `PendingSettlementHandler` discipline — only legal transitions advance the row; arrival states that do not match the guard no-op.

UI queries against the catalog remain single-document reads. No cross-document join is paid by the read path.

### Option B — One view per source BC; UI-side join

`CatalogListingViewCore` (M2 base), `CatalogListingViewAuctionStatus` (M3 fields), `CatalogListingViewSettlement` (M5 fields). Each new BC ships its own view; the frontend joins on `ListingId`.

Rejected. Three concrete costs:
- **Read-path complexity.** Every UI query crosses 2-3 document types; join semantics vary per query (the catalog list page needs auction-status + settlement-status, the listing detail needs M2 base + auction-status + settlement-status — different join shapes).
- **API-layer coupling to BC topology.** The frontend learns the BC partitioning; adding a new contributing BC requires both a new view AND a frontend join update. The Path A shape contains the topology behind the read model.
- **Single-document atomicity for status transitions is lost.** A status transition that should be atomic from the consumer's perspective (`"Sold"` → `"Settled"`) becomes a two-document write (advance the auction-status row's marker + insert the settlement row) with no transactional boundary.

### Option C — Native Marten `MultiStreamProjection` composed inline

A single Marten projection driven by event streams from multiple BCs. The projection subscribes to streams owned by Selling, Auctions, and Settlement and produces the catalog view directly.

Rejected at M3-S6. The cross-BC event flow is **not Marten-managed** — events traverse RabbitMQ + Wolverine handler chains, not direct stream subscriptions. A `MultiStreamProjection` subscribed to remote-BC streams would require the Listings store to read the producer BCs' event streams directly, violating the modular-monolith BC-isolation rule (ADR-001). Path A wins by elimination (cross-BC isolation forbids C) and by simplicity (one view + N handler classes vs N views + frontend join).

### Sub-question — Multi-source siblings (resolved at M4-S6, Sub-Option A)

A future contributing BC may legitimately need to consume events from two or more source BCs in one sibling handler. The original M4-S6 planning slot's `SessionMembershipHandler` was framed this way: it would consume `SessionCreated, ListingAttachedToSession, SessionStarted` from Auctions plus `ListingWithdrawn` from Selling. Two framings were considered:

- **Sub-Option A — one sibling class per source BC** (chosen at M4-S6). Multi-source handlers split into one class per source. The M4-S6 lived shape ships `AuctionsSessionHandler` (Auctions-sourced — two events: `ListingAttachedToSession`, `SessionStarted`) and `SellingListingWithdrawnHandler` (Selling-sourced — one event: `ListingWithdrawn`) as two single-source siblings, symmetric with the M3-S6 + M5-S6 precedent. `SessionCreated` is intentionally not handled (no per-listing catalog field consequence).
- **Sub-Option B — one sibling class per logical feature** (rejected at M4-S6). A single `SessionMembershipHandler` would have consumed from both Auctions and Selling. Rejected because:
  - The "source BC" axis was already load-bearing for handler discovery, queue subscription, and BC-isolation reasoning under M3-S6 + M5-S6. Sub-Option B would have introduced two organizing principles (logical-feature for multi-source; source-BC for single-source) where the codebase previously had one.
  - The Wolverine discovery interaction with a single static class consuming from two queues was unverified in CritterBids until M4-S6 — Sub-Option B would have been the first lived test of that composition, blocked the slice on a discovery-ambiguity risk it did not need to take, and forced a halt-and-consult per OQ6 if discovery failed.
  - The two lived feature groups M4-S6 surfaced — session membership and listing withdrawal — turned out not to be a single logical feature in any meaningful sense. Sessions are an Auctions-internal lifecycle the catalog reflects; withdrawal is a Selling-side terminal that affects every BC differently. Bundling them in one class on the "M4-S6 feature scope" axis would have masked that distinction.

**Resolution: Sub-Option A.** The shape constraint binding all current and future sibling handlers strengthens to: **one handler class per source BC, single-source per sibling.** A future BC that needs multi-source coordination either splits into per-source siblings (preferred), or amends this ADR with new evidence justifying Sub-Option B for that specific application.

**Naming convention pinned by the resolution.** Sub-Option A handler classes carry the source BC in the prefix: `Auctions*Handler`, `Selling*Handler`, `Settlement*Handler`. The suffix describes the feature scope. Existing single-source siblings (`AuctionStatusHandler`, `ListingPublishedHandler`, `SettlementStatusHandler`) are grandfathered — no rename is on the table — but new sibling classes follow the source-prefixed convention. Both M4-S6 classes (`AuctionsSessionHandler`, `SellingListingWithdrawnHandler`) ship with the new naming.

### Out of scope — moving the read model to a different storage substrate

A future architectural direction may move read models off Marten/PostgreSQL onto a search-optimized store (Elasticsearch, OpenSearch, or similar). That decision would be a separate ADR. The Path A shape is independent of the storage substrate — sibling handler classes work as well against an Elasticsearch document as against a Marten document. Path A's commitment is the *handler topology*, not the persistence engine.

---

## Decision

**Option A.** The Listings BC's catalog read model is extended via the **one-view-per-logical-entity, sibling-handler-class-per-source-BC, additive-fields** shape. Three lived applications at M4-S6 close: the M3-S6 `AuctionStatusHandler` (first), the M5-S6 `SettlementStatusHandler` (second), and the M4-S6 pair `AuctionsSessionHandler` + `SellingListingWithdrawnHandler` (third — split per Sub-Option A). Future contributing BCs follow the same shape.

The multi-source-sibling sub-question is **resolved at M4-S6 to Sub-Option A** (one handler class per source BC, single-source per sibling). See §"Sub-question" above for the rationale and the naming convention pinned by the resolution.

The shape constraints binding all current and future sibling handlers:

1. **One handler class per source BC, single-source per sibling.** Strengthened at M4-S6 from "no multi-source handlers ship until the sub-question resolves" to the unconditional rule. A future BC that needs multi-source coordination either splits into per-source siblings, or amends this ADR.
2. **Tolerant-upsert per handler.** `LoadAsync` by document id; construct a minimal row if absent; mutate via record `with`; `session.Store`. No throw on missing-row.
3. **Disjoint field sets per handler.** Each handler owns the fields its source events populate. The `Status` field is shared across handlers but each handler owns one or more **status transitions** — never the full vocabulary. Status vocabulary at M4-S6 close: `"Published"`, `"Open"`, `"Closed"`, `"Sold"`, `"Passed"`, `"Settled"`, `"Withdrawn"` (seven values).
4. **Status-preservation guards.** Per the M5-S3 `PendingSettlementHandler` precedent: a handler that would advance the row only does so when the current status matches its expected pre-state; arrival states outside the expected pre-state no-op without throwing. This guards against cross-queue race conditions where events arrive out of order. M4-S6 adds two lived applications: `AuctionStatusHandler.Handle(BiddingOpened)` no-ops when `view.Status == "Withdrawn"` (OQ3 Path α terminal-state pin); `SellingListingWithdrawnHandler.Handle(ListingWithdrawn)` no-ops on any pre-state outside `{ "Published", "Open" }`.
5. **Seed-handler load-and-preserve.** The handler that seeds the row from the M2-base contract (`ListingPublishedHandler`) must load-and-preserve any downstream-handler state on re-delivery. The M5-S6 amendment to `ListingPublishedHandler` brought it into compliance with this rule, mirroring the M5-S3 `PendingSettlementHandler.Handle(ListingPublished)` shape. **M4-S6 verification surfaced a gap and amended:** the M5-S6 amendment uses an explicit named-field allow-list, so new fields added at M4-S6 (`SessionId`, `SessionStartedAt`) required their own preservation lines. Future M*-S* milestones adding fields to `CatalogListingView` must extend the preservation block in the seed handler — the named-field discipline is now part of the rule.

### Revisit trigger

This ADR is reopened when **the Path A shape produces friction that Path B or C would have prevented** during a future BC's read-model extension. Examples that would justify revisit:

- A contributing BC whose source events do not key naturally on the existing view's primary key — forcing either a key-translation layer or a separate view (Path B becomes correct in this case).
- A read-path query pattern that cannot be satisfied by a single-document read — forcing either an external join layer or the search-substrate direction (separate ADR).
- A status-vocabulary conflict where two contributing BCs claim the same status transition with different semantics — forcing either explicit conflict-resolution rules or a per-BC status field (workshop-update territory).

The default response if the trigger fires: amend this ADR with the new evidence and consider whether Path B or a search-substrate move is warranted. Migration to Path B would require concrete cost evidence; Path A's "single document, single read" property is load-bearing for the API surface CritterBids has shipped.

---

## Consequences

- **The pattern will apply again.** Future BCs whose state intersects the catalog follow Path A. The Listings catalog is the projected fact; remote BCs' read-model extensions are sibling handlers, not new documents.
- **`marten-projections.md §"View Extension Across Milestones"` is the authoritative pattern reference.** The M5-S6 amendment added `SettlementStatusHandler` as the second lived example; the M4-S6 amendment adds `AuctionsSessionHandler` + `SellingListingWithdrawnHandler` as the third (split per source per the sub-question resolution). The skill file's §"Status-Preservation Guards" subsection (M5-S6) gains the M4-S6 Withdrawn-preservation example.
- **Multi-source siblings are resolved to Sub-Option A.** A future BC that legitimately needs multi-source coordination either splits into per-source siblings (the established pattern), or amends this ADR with new evidence justifying Sub-Option B for that specific application. The expectation going forward is "per-source sibling unless proven otherwise."
- **The seed handler (`ListingPublishedHandler`) carries the load-and-preserve discipline forward, with a named-field allow-list.** The M5-S6 amendment uses explicit per-field preservation lines, not implicit `with` semantics. M4-S6's verification surfaced this and extended the block with `SessionId` and `SessionStartedAt` preservation lines. Every future M*-S* milestone adding a field to `CatalogListingView` extends the seed handler the same way.
- **Status transitions across handlers compose by string matching, not enum hierarchy.** The `Status` field is a string per the M2-S7 OQ2 Path A precedent (symmetry with `Format`). Each handler's preservation guard is a string equality check. M4-S6 grew the vocabulary to seven values by adding `"Withdrawn"`. Future contributing BCs adding new statuses coordinate the vocabulary at workshop-update time; the read model does not enforce a finite enum at the storage layer.
- **Cross-BC composition is now an observed terminal-state pin.** The M4-S5 retro's three candidate downstream paths for "`BiddingOpened` arriving at a withdrawn listing" collapse to one observed lived path at M4-S6: the catalog handler preserves `"Withdrawn"` (Path 3 from that retro). The M4 milestone doc §3 stance ("Defensive pre-filtering at `StartSession` time is post-MVP hardening") is now backed by `BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus` in the lived test suite — load-bearing on observed behaviour, not assumption.
- **The M3-S6 + M5-S6 single-source evidence is no longer the only ground.** M4-S6 ships two new single-source siblings (Sub-Option A), so the ADR's lived evidence now spans four single-source handlers (counting the seed `ListingPublishedHandler`): one Selling-source seed + one Auctions-source + one Settlement-source + one Auctions-source + one Selling-source.

---

## References

- [ADR 001 — Modular Monolith Architecture](001-modular-monolith.md) — the BC-isolation rule Path A honors via sibling handlers inside the consuming BC
- [ADR 009 — Shared Primary Marten Store](009-shared-marten-store.md) — the storage substrate enabling sibling handlers to share a single `CatalogListingView` document
- [M3 milestone retrospective](../retrospectives/M3-auctions-bc-retrospective.md) — §"M3-D2 Path Rationale" + §"ADR Candidate Review" (the original deferral)
- [M5-S6 retrospective](../retrospectives/M5-S6-settlement-outbound-publish-routes-listings-catalog-extension-adr-014-retrospective.md) — second lived application, the evidence section of this ADR's initial body
- [M4-S6 retrospective](../retrospectives/M4-S6-listings-catalog-session-and-withdrawn-retrospective.md) — third lived application, sub-question resolution to Sub-Option A
- [`marten-projections.md §"View Extension Across Milestones"`](../skills/marten-projections.md) — pattern reference (amended at M5-S6 to add the second example; amended at M4-S6 to add the third)
- `src/CritterBids.Listings/AuctionsSessionHandler.cs` (M4-S6) — third sibling handler implementation (Auctions-source session membership)
- `src/CritterBids.Listings/SellingListingWithdrawnHandler.cs` (M4-S6) — fourth sibling handler implementation (Selling-source Withdrawn terminal); paired with `AuctionsSessionHandler` under Sub-Option A
- `src/CritterBids.Listings/SettlementStatusHandler.cs` (M5-S6) — second sibling handler implementation
- `src/CritterBids.Listings/AuctionStatusHandler.cs` (M3-S6, amended M4-S6 for Withdrawn-preservation guard) — first sibling handler implementation
- `src/CritterBids.Listings/ListingPublishedHandler.cs` (M2-S7, amended M5-S6 and M4-S6) — seed handler with the load-and-preserve discipline + named-field allow-list extended at M4-S6 for the new session-membership fields
