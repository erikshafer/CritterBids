# ADR 014 — Cross-BC Read-Model Extension Shape

**Status:** ✅ Accepted
**Date:** 2026-05-17 (M5-S6 — second lived application of the M3-D2 Path A pattern)
**Milestone:** M5-S6 — Settlement Outbound Publish Routes + Listings Catalog `Settled` Status + ADR-014 Authoring + M5 Milestone Close

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

`CatalogListingView` is a single Marten document keyed by `ListingId`. Each contributing BC owns one sibling handler class inside `CritterBids.Listings` that consumes its events and writes its disjoint field set. The contract chain at M5-S6 close:

| Handler | Source BC | Source events | Fields owned |
|---|---|---|---|
| `ListingPublishedHandler` | Selling (M2-S7) | `ListingPublished` | `SellerId, Title, Format, StartingBid, BuyItNow, Duration, PublishedAt` |
| `AuctionStatusHandler` | Auctions (M3-S6) | six auction events | `Status` (subset of transitions), `ScheduledCloseAt, CurrentHighBid, CurrentHighBidderId, BidCount, HammerPrice, WinnerId, PassedReason, FinalHighestBid, ClosedAt` |
| `SettlementStatusHandler` | Settlement (M5-S6) | `SettlementCompleted` | `Status` (`"Sold"` → `"Settled"` transition only), `SettledAt` |

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

### Sub-question — Multi-source siblings (deferred)

A future contributing BC may legitimately need to consume events from two or more source BCs in one sibling handler. The original M4-S6 planning slot's `SessionMembershipHandler` was framed this way: it would consume `SessionCreated, ListingAttachedToSession, SessionStarted` from Auctions plus `ListingWithdrawn` from Selling. Two framings exist:

- **Sub-Option A — one sibling class per source BC.** Multi-source handlers split into one class per source (e.g., `AuctionsSessionHandler` + `SellingSessionHandler`). Symmetric with the M3-S6 + M5-S6 single-source precedent.
- **Sub-Option B — one sibling class per logical feature.** Multi-source handlers stay in one class that consumes from multiple BCs (e.g., `SessionMembershipHandler` consuming both Auctions and Selling events). Single feature, single class regardless of source.

**The sub-question is deferred** to a third lived application that legitimately requires multi-source coordination. Both M3-S6 and M5-S6 are single-source siblings; resolving the multi-source framing on single-source evidence is premature. When the third application lands (the natural candidate remains M4-S6's `SessionMembershipHandler`), that slice picks A or B; this ADR records the framework and the deferral so the question's status is explicit rather than implicit.

### Out of scope — moving the read model to a different storage substrate

A future architectural direction may move read models off Marten/PostgreSQL onto a search-optimized store (Elasticsearch, OpenSearch, or similar). That decision would be a separate ADR. The Path A shape is independent of the storage substrate — sibling handler classes work as well against an Elasticsearch document as against a Marten document. Path A's commitment is the *handler topology*, not the persistence engine.

---

## Decision

**Option A.** The Listings BC's catalog read model is extended via the **one-view-per-logical-entity, sibling-handler-class-per-source-BC, additive-fields** shape. The M5-S6 `SettlementStatusHandler` is the second lived application of the shape; the M3-S6 `AuctionStatusHandler` is the first. Future contributing BCs follow the same shape.

The multi-source-sibling sub-question is deferred to a third lived application that legitimately requires multi-source coordination — most likely M4-S6's `SessionMembershipHandler` if and when M4 resumes. This ADR records the framework (Sub-Option A vs Sub-Option B) and acknowledges that current evidence does not justify pre-emptively choosing.

The shape constraints binding all current and future sibling handlers:

1. **One handler class per source BC.** No multi-source handlers ship until the sub-question resolves.
2. **Tolerant-upsert per handler.** `LoadAsync` by document id; construct a minimal row if absent; mutate via record `with`; `session.Store`. No throw on missing-row.
3. **Disjoint field sets per handler.** Each handler owns the fields its source events populate. The `Status` field is shared across handlers but each handler owns one or more **status transitions** — never the full vocabulary.
4. **Status-preservation guards.** Per the M5-S3 `PendingSettlementHandler` precedent: a handler that would advance the row only does so when the current status matches its expected pre-state; arrival states outside the expected pre-state no-op without throwing. This guards against cross-queue race conditions where events arrive out of order.
5. **Seed-handler load-and-preserve.** The handler that seeds the row from the M2-base contract (`ListingPublishedHandler`) must load-and-preserve any downstream-handler state on re-delivery. The M5-S6 amendment to `ListingPublishedHandler` (commit `b61995a`) brings it into compliance with this rule, mirroring the M5-S3 `PendingSettlementHandler.Handle(ListingPublished)` shape.

### Revisit trigger

This ADR is reopened when **the Path A shape produces friction that Path B or C would have prevented** during a future BC's read-model extension. Examples that would justify revisit:

- A contributing BC whose source events do not key naturally on the existing view's primary key — forcing either a key-translation layer or a separate view (Path B becomes correct in this case).
- A read-path query pattern that cannot be satisfied by a single-document read — forcing either an external join layer or the search-substrate direction (separate ADR).
- A status-vocabulary conflict where two contributing BCs claim the same status transition with different semantics — forcing either explicit conflict-resolution rules or a per-BC status field (workshop-update territory).

The default response if the trigger fires: amend this ADR with the new evidence and consider whether Path B or a search-substrate move is warranted. Migration to Path B would require concrete cost evidence; Path A's "single document, single read" property is load-bearing for the API surface CritterBids has shipped.

---

## Consequences

- **The pattern will apply again.** Future BCs whose state intersects the catalog follow Path A. The Listings catalog is the projected fact; remote BCs' read-model extensions are sibling handlers, not new documents.
- **`marten-projections.md §"View Extension Across Milestones"` is the authoritative pattern reference.** The M5-S6 amendment adds `SettlementStatusHandler` as the second lived example. The skill file's diagram (currently marking `SettlementStatusHandler` as "M4 planned") is updated to reflect M5-S6 reality.
- **Multi-source siblings remain an open sub-question.** When M4-S6 (or a later equivalent) ships, that slice picks Sub-Option A or B with its own lived evidence and amends this ADR.
- **The seed handler (`ListingPublishedHandler`) carries the load-and-preserve discipline forward.** The M5-S6 amendment to that handler is part of this ADR's evidence — without it, the M5-S6 `SettlementStatusHandler`'s tolerant-upsert posture would create a structurally-rare race where a `SettlementCompleted`-seeded minimal row could be overwritten back to `Status = "Published"` if `ListingPublished` arrived later.
- **Status transitions across handlers compose by string matching, not enum hierarchy.** The `Status` field is a string per the M2-S7 OQ2 Path A precedent (symmetry with `Format`). Each handler's preservation guard is a string equality check. Future contributing BCs adding new statuses must coordinate the vocabulary at workshop-update time; the read model does not enforce a finite enum at the storage layer.
- **The M3-S6 + M5-S6 evidence is exclusively single-source.** ADR-014's body does not pre-resolve the multi-source sub-question on this evidence. If a future maintainer reads the ADR and finds the multi-source question already answered, that resolution lives in a later slice's amendment to this ADR, not in this initial body.

---

## References

- [ADR 001 — Modular Monolith Architecture](001-modular-monolith.md) — the BC-isolation rule Path A honors via sibling handlers inside the consuming BC
- [ADR 009 — Shared Primary Marten Store](009-shared-marten-store.md) — the storage substrate enabling sibling handlers to share a single `CatalogListingView` document
- [M3 milestone retrospective](../retrospectives/M3-auctions-bc-retrospective.md) — §"M3-D2 Path Rationale" + §"ADR Candidate Review" (the original deferral)
- [M5-S6 retrospective](../retrospectives/M5-S6-settlement-outbound-publish-routes-listings-catalog-extension-adr-014-retrospective.md) — second lived application, the evidence section of this ADR
- [`marten-projections.md §"View Extension Across Milestones"`](../skills/marten-projections.md) — pattern reference (amended at M5-S6 to add the second example)
- `src/CritterBids.Listings/SettlementStatusHandler.cs` (M5-S6) — second sibling handler implementation
- `src/CritterBids.Listings/AuctionStatusHandler.cs` (M3-S6) — first sibling handler implementation
- `src/CritterBids.Listings/ListingPublishedHandler.cs` (M2-S7, amended M5-S6) — seed handler with the load-and-preserve discipline
