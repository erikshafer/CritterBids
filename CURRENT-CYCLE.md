# CritterBids — Current Cycle

## Status

| Field | Value |
|---|---|
| Milestone | M2 — Listings Pipeline |
| Session | S8 (final) |
| Date closed | 2026-04-15 |
| Status | **M2 complete** |
| Test count | 42 passing |

---

## M2 Summary

M2 delivered the first cross-BC integration pipeline in CritterBids. A registered seller can
create a draft listing, configure it, submit it for publication, and any participant can browse
published listings via the catalog API.

**BCs implemented:** Participants (migrated from Polecat to Marten), Selling, Listings
**Integration flows:** Participants→Selling (`SellerRegistrationCompleted`), Selling→Listings (`ListingPublished`)
**ADRs authored:** 008, 009, 010, 011 (All-Marten Pivot)

Full session arc: `docs/retrospectives/M2-listings-pipeline-retrospective.md`

---

## Next

M3 planning — Auctions BC. Inherits:
- 42 tests passing, all green
- Three production BCs: Participants, Selling, Listings
- Known fragility: `[WriteAggregate]` stream-ID convention for `SubmitListing` unverified via HTTP
- Skills to load: `dynamic-consistency-boundary.md`, `wolverine-sagas.md`, `marten-event-sourcing.md`
