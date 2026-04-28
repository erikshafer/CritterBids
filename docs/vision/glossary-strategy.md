# Decision: Glossary Strategy

**Status:** Resolved as "per-BC only"
**Date:** 2026-04-27
**Source:** [`docs/prompts/foundation/foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §7.2
**Phase:** Foundation refresh Phase 4 Q2

CritterBids' workshop §"Ubiquitous Language" sections (added in foundation-refresh Phase 3 Item 4) carry per-BC vocabulary: 16 terms in W002 (Auctions), 14 in W003 (Settlement), 15 in W004 (Selling), plus W001's `## Cast` and `## Setting` blocks which carry the journey-grain vocabulary. The §"Ubiquitous Language" sections cross-reference each other via markdown anchor links for shared terms (Listing, Reserve, Hammer Price, Buy It Now, Bidder, Seller). Foundation refresh Phase 4 Q2 considered whether to add a top-level cross-BC synthesis page at `docs/vision/glossary.md`.

## Options Considered

1. **Per-BC only.** Workshop §"Ubiquitous Language" sections are the authoritative source; no top-level page.
2. **Project-level glossary as a synthesis.** `docs/vision/glossary.md` aggregates per-BC entries with cross-BC overlap notes.
3. **Project-level + per-BC.** Both, with per-BC as authoritative and project-level as derived index.

## Decision

**Option 1: Per-BC only.** No top-level glossary page is created. Workshop §"Ubiquitous Language" sections remain the canonical source for ubiquitous language; cross-BC navigation works in O(1) via the existing markdown anchor links.

The per-BC §"Ubiquitous Language" pattern (Option A from Phase 3 Item 4 sign-off: "define once per BC, cross-reference") already handles the cross-BC overlap for shared terms. A reader landing on `Listing` in W002 sees "Pre-publish lifecycle owned by Selling BC; see W004 §3" with a working link; W004's `Listing` entry has the converse pointer. The graph is bidirectional and navigable in one click.

A top-level synthesis page would duplicate content and create a maintenance ledger - when the per-BC §"Ubiquitous Language" sections update (which they will as Phase 5 backfill narratives surface `workshop-update` findings), the synthesis must update too. The synthesis adds a node to the graph without adding information that the per-BC sections do not already carry.

This decision is recorded as a decision note rather than a full ADR per `docs/decisions/README.md` §"When to Write an ADR" - the choice is not hard to reverse (a synthesis page can be introduced later), not architecturally cross-cutting, and not likely to surprise contributors who land on a per-BC §"Ubiquitous Language" and see explicit cross-references to other BCs.

## Trigger for Revisit

If shared terms grow beyond what cross-references can carry cleanly - for example, if a single term lives in three or more BCs with subtly different meanings per BC - the synthesis page becomes useful as a disambiguation hub. Currently the term `Listing` lives in two BCs (Selling, Auctions) with a clean lifecycle handoff at `ListingPublished`; `Reserve` and `Hammer Price` live in two BCs (Auctions, Settlement) with a clear authority distinction (Auctions emits the real-time UX signal; Settlement performs the binding financial comparison). Two-BC overlaps with clean handoffs scale; three-or-more-BC overlaps would not.

A second trigger: if the project introduces public-facing API documentation (Swagger / OpenAPI catalog, narrative-style API reference) that needs a project-level vocabulary index, the synthesis becomes infrastructure rather than redundancy. Until then, contributors and AI agents navigate by per-BC §"Ubiquitous Language".

## References

- `docs/workshops/002-auctions-bc-deep-dive.md` §"Ubiquitous Language": Auctions BC terms
- `docs/workshops/003-settlement-bc-deep-dive.md` §"Ubiquitous Language": Settlement BC terms
- `docs/workshops/004-selling-bc-deep-dive.md` §"Ubiquitous Language": Selling BC terms
- `docs/workshops/001-flash-session-demo-day-journey.md` §"Cast" and §"Setting": journey-grain vocabulary
- `docs/prompts/foundation/foundation-refresh-handoff.md` §7.2: the question framing this decision note resolves
- `docs/retrospectives/foundation-refresh-phase-3-retrospective.md`: the Phase 3 retro that produced the per-BC §"Ubiquitous Language" sections via Item 4
