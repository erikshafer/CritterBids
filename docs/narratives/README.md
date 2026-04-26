# CritterBids Narratives

Narratives are the journey-scoped domain specs for CritterBids. Each narrative captures a user journey as a sequence of moments through time, told from a single user's perspective: context, interaction, and system response at each moment. Narratives sit between Event Modeling workshop output and implementation prompt documents.

This README is the operational manual for authoring narratives. For the rationale behind the narrative layer and CritterBids' NDD-informed framing, see [`docs/decisions/016-spec-anchored-development.md`](../decisions/016-spec-anchored-development.md). For the staged sequence narratives sit within, see [`docs/decisions/017-design-phase-workflow-sequence.md`](../decisions/017-design-phase-workflow-sequence.md). The first authored narrative will become the canonical example and is referenced from the Index table at the bottom of this README.

## Where narratives sit in the document layering

```
Workshop (event model + slices, ubiquitous language)
        │
        ▼
Narrative (journey-scoped, user-perspective spec)
        │
        ▼
Prompt document (implementation build order)
        │
        ▼
Code (implementation)
```

Workshops produce point-in-time event-model artifacts. Narratives thread multiple workshop slices into one user's coherent experience. Implementation prompt documents reference narratives; prompts close after their session, narratives persist.

Per ADR 016's spec-anchored discipline, narratives outlive the prompts and code that satisfy them. They are the most stable artifact in the chain alongside the workshop event models.

## Format

CritterBids uses the **NDD-informed structured-markdown format** lifted from CritterCab's narratives README v0.1. No NDK or Auto-platform dependency. Format dialect locked at the v0.1 of this README; subsequent format changes are tracked in this file's Document History.

### File naming

Numeric prefix + slug, mirroring workshops:

- `001-bidder-wins-flash-auction.md` *(planned: foundation-refresh Phase 2 first narrative)*
- `002-seller-publishes-timed-listing.md` *(planned)*

The numeric prefix sorts narratives chronologically by *authoring*, not by journey ordering.

### Frontmatter: v1 bounded schema

YAML frontmatter has a **bounded vocabulary** (guardrail #2). The keys below are the entire v1 set; adding a key requires revising this README first.

```yaml
---
slug: <numeric-slug>                      # e.g., 001-bidder-wins-flash-auction
status: draft | accepted | superseded
journey: <protagonist-role>               # bidder, seller, auctioneer, operator
perspective: single-bidder | single-seller | single-auctioneer | single-operator | <named>
scope: happy-path | <named-failure> | <named-edge-case>
bounded_contexts: [<BC>, ...]             # primary BCs the journey lives in
boundaries_touched: [<BC>, ...]           # other BCs whose state the journey crosses
slices_implemented: [<slice-num>, ...]    # workshop slice numbers covered
canonical_id: <field-name>                # the ID that carries the journey across BCs
---
```

### Body structure

Top-level sections, in order:

1. **Title heading and intro paragraph.** What this narrative is; what's in scope; what isn't.
2. **`## Cast`**: bulleted list of actors. Single named protagonist; other actors named with onstage/offstage status.
3. **`## Setting`**: paragraphs establishing time, place, policy posture, and inherited conditions that subsequent Moments reference without re-stating.
4. **`## Moment N: <one-line beat>`**: one section per Moment, in journey order.
5. **`## Deferred from this narrative`**: cumulative aggregation of per-Moment deferred items, bucketed by disposition.
6. **`## Retrospective`**: session retrospective in the workshop §12 shape.
7. **`## Document History`**: version log.

### Moment body structure

Each Moment is composed of **prose paragraphs labeled by phase** (guardrail #1), not bulleted fields:

```
## Moment N: <one-line beat>

**Implements:** slice X.Y[, slice X.Z…].

**Context.** What is true going in. Prior events, view contents the
protagonist sees, policy posture inherited from Setting.

**Interaction.** What happens this beat. May be a user action (protagonist
taps, types, submits) or a system trigger (an automation reacts to a prior
event, an external boundary returns a value).

**Response.** What the system does in response: events emitted, views
updated, state visible to the protagonist on screen. May span multiple
paragraphs when the Moment covers multiple workshop slices (see
"Multi-slice Moments" below).

**Why this matters to the <protagonist>.** *(optional)* Used only when this
Moment encodes a protagonist-visible invariant or constraint worth
surfacing. Skip when the Moment is self-explanatory.
```

**Bullets are not allowed inside a Moment body.** Bulleted fields turn the narrative into a JSON document with extra steps and break the journey voice.

### Multi-slice Moments

When the protagonist experiences multiple workshop slices as a single beat, **the Moment body grows in paragraphs, not in section labels.** The `Response.` block becomes multiple paragraphs under one label; new labels are not introduced. The `Implements:` line cites both slices.

## Voice and perspective

Single-named-protagonist by default. The protagonist is named in Cast, observed throughout, and is the only actor whose experience is dramatized.

The narrator is **omniscient about the system**: it can name facts the protagonist doesn't perceive (events committed, projections updated, downstream BCs notified) but governs *what is dramatized as user experience* by what the protagonist actually perceives. This is what permits Moments where the system does most of the work (automation-driven slices) while keeping the journey voice intact.

Multi-perspective (named POV switches) and parallel (two-column / two-narrative-pair) approaches are deliberate deviations from the default. Use only when single-perspective genuinely fails to render the journey faithfully. Document the deviation in the narrative's intro paragraph and update this README if it becomes a recurring pattern.

## Slice citations

Every Moment cites the workshop slice(s) it implements via the `Implements:` line. Workshop slice numbers are stable; lean on them.

**Do not restate the workshop's Given/When/Then scenarios.** The workshop is the test specification; the narrative is the journey. Restating GWT in narrative form duplicates the workshop and pulls the narrative into the wrong artifact layer.

### Bidirectional referencing (forward-looking)

Workshop slices may cite the narratives that implement them via a `Narratives:` cross-reference line, mirroring the slice-citation discipline in reverse. All four CritterBids workshops (W001 through W004) were authored before any narrative existed; their slices do not yet carry narrative back-references. Phase 3 of the foundation refresh adds retroactive `Narratives:` cross-references where appropriate. Workshops authored after the first narrative ships should adopt the convention from authoring time.

## Notation conventions

- **Code-style backticks** for domain event names and named projection/view names: `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`, `CatalogListingView`, `AuctionsAwaitingClose*`.
- **Plain text** for ordinary domain nouns from the workshop's Ubiquitous Language: Listing, Bid, Bidder Session, Reserve, Hammer Price, Buy It Now, Flash Session, Timed Auction, Extended Bidding.
- The `*` suffix on todo-list projections (Bruun convention) is preserved in narratives. Phase 3 Item 5 of the foundation refresh names this pattern in the Event Modeling skill.

Domain language uses the workshop's Ubiquitous Language. Drift into generic software vocabulary is a smell.

## What narratives carry, and don't

**Carry:** domain meaning, user-perspective story, journey arc, moment-level state transitions, system-fact-as-observed-by-protagonist.

**Do not carry:**

- Code or pseudocode.
- Implementation choices: transport (RabbitMQ, Wolverine integration events, SignalR), projection mechanism, aggregate shape, library primitives. Those belong to skill files.
- Architectural decisions: flag any that surface during authoring as ADR candidates; do not resolve in-narrative.
- GWT test specifications: reference workshop slices by number; do not restate.
- UX / UI design: note app behavior at the bidder-experience grain ("status banner ticks forward to…"); don't design the screens.

## Per-Moment and cumulative deferral discipline

Every Moment carries (in its proposal phase) a *"Things deliberately not included"* subsection that names what was consciously omitted with a disposition tag. At session close, those omissions consolidate into a **`## Deferred from this narrative`** section, bucketed by disposition. The section mirrors the workshop convention of `§10 Parking Lot` and `§11 ADR Candidates` at the narrative layer: it is a project-level backlog feeder, not a transparency footnote.

### Disposition tags (v1)

| Tag | Meaning |
|---|---|
| `defer` | Will revisit; trigger not yet known. |
| `post-MVP` | Beyond v1 scope; flagged for later release. |
| `separate-narrative` | Belongs to a different journey. |
| `separate-workshop` | Belongs to a BC not yet event-modeled. |
| `implementation-detail` | Skill file or ADR territory. |
| `alternate-path-failure` | A failure mode of the same journey; warrants its own narrative. |
| `UX-or-UI-detail` | App design; belongs to design artifacts. |

## When a new narrative is warranted

A new narrative is warranted when:

- The protagonist is different (bidder vs. seller vs. auctioneer vs. operator).
- The journey's terminal outcome is different (won vs. outbid vs. passed vs. withdrawn vs. Buy-It-Now-purchased).
- The journey crosses a *new* set of BCs that prior narratives don't cover.
- The journey exercises a structurally distinct flow (e.g., timed auctions vs. Flash sessions, lot/bundle auctions, multi-bidder extended-bidding flows, post-auction settlement journeys).

A new narrative is *not* warranted for:

- A different policy posture along the same journey (Setting absorbs this).
- A different specific failure mode of the same Moment (per-Moment deferral / cumulative section captures this).
- A different concrete protagonist with the same role and journey shape (named protagonist in Cast carries variability).

When in doubt, prefer extending an existing narrative's Setting or per-Moment alternate-path subsection over forking a new narrative.

## Retrospective convention

Every narrative ships its retrospective in the same file, appended after the `## Deferred from this narrative` section. The retrospective shape mirrors the workshop §12 convention with narrative-flavored content. The first authored narrative establishes the canonical retrospective structure for subsequent narratives to mirror.

## Index

| # | Status | Journey | Scope | Slices |
|---|---|---|---|---|
| *(empty: Phase 2 of the foundation refresh authors narrative 001)* |  |  |  |  |

## Document history

- **v0.1** (2026-04-26): Authored as foundation-refresh Phase 1 Item 3. Lifts CritterCab's narratives README v0.1 dialect: bounded frontmatter v1, prose-paragraph Moment body (Guardrail 1), single-named-protagonist + omniscient-narrator voice, seven disposition tags, forward-looking bidirectional referencing. Adapted for CritterBids' auction domain (bidder / seller / auctioneer / operator protagonist roles; auction-domain event and Ubiquitous-Language examples). Index empty; Phase 2 authors first narrative.
