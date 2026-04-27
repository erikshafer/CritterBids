# Prompt 001 - Author the First NDD-informed Narrative: Bidder Wins Flash Auction

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-26 |
| **Phase** | Foundation Refresh, Phase 2 |
| **Subdirectory** | `docs/prompts/narratives/` |
| **Journey** | Bidder wins a Flash auction (happy path) |
| **Protagonist** | Single bidder (named in Cast at session start) |
| **Target artifact** | `docs/narratives/001-bidder-wins-flash-auction.md` (to be produced) |
| **Companion artifact** | `docs/narratives/001-findings.md` (to be produced) |
| **Source-of-truth dependencies** | W001 workshop and scenarios; lived M3 and M4 code |
| **Workflow position** | First exercise of CritterBids' narrative document layer; downstream of W001, upstream of any Phase 2.5 follow-up implementation prompts |

---

## Framing

This session is the first concrete authoring of CritterBids' narrative document layer. Phase 1 of the foundation refresh established the format (`docs/narratives/README.md` v0.1, lifted from CritterCab); Phase 2 produces the canonical example. The narrative authored here becomes the structural and audit-discipline reference for the four backfill narratives scoped in Phase 5 (Auctions, Settlement, Selling, Participants); conventions signed off during this session are the conventions Phase 5 inherits.

CritterBids is asymmetric to CritterCab in one structural way: lived code exists. M1 through M4-S2 have shipped, covering Participants, Selling, Auctions, Listings, and the early Settlement plumbing. The narrative is therefore not just a forward-looking spec. It is the first time the project's domain understanding meets four milestones of implementation. Any disagreement that surfaces gets routed through the **findings discipline** documented below: four lanes (`narrative-update`, `workshop-update`, `code-update`, `document-as-intentional`), with `code-update` items deferred to Phase 2.5 rather than resolved in-session.

ADR 016 (Spec-Anchored Development) governs the relationship: specs describe intent; code is authoritative for runtime; drift is caught at retrospective time.

---

## Goal

Author the first NDD-informed narrative for CritterBids, covering a single bidder's happy-path Flash auction journey from anonymous session start through settlement, and audit the lived M3 and M4 code against it, routing every disagreement through the findings discipline.

---

## Orientation files (read in order before starting)

1. `C:\Code\CritterBids\CLAUDE.md` - routing layer and global conventions.
2. `C:\Code\CritterBids\docs\narratives\README.md` - the format manual, v0.1. Pay attention to the bounded frontmatter, Guardrail 1 (prose-paragraph Moments), Guardrail 2 (frontmatter vocabulary), the seven disposition tags, and the multi-slice Moment convention.
3. `C:\Code\CritterBids\docs\workshops\001-flash-session-demo-day-journey.md` - the workshop the narrative implements. Tier 0 through Tier 6 are in scope.
4. `C:\Code\CritterBids\docs\workshops\001-scenarios.md` - Given/When/Then scenarios for the slices in scope. Reference, do not restate.
5. `C:\Code\CritterBids\docs\decisions\016-spec-anchored-development.md` - the spec-anchored discipline that governs findings routing.
6. `C:\Code\CritterBids\docs\rules\structural-constraints.md` - Layer 1 directives. Consult when a finding flirts with a guardrail.
7. `C:\Code\CritterBids\docs\prompts\foundation\foundation-refresh-handoff.md` §4 - the Phase 2 plan this prompt executes.

Lived-code reference reads are *per-Moment*, not pre-session. For each Moment, before drafting:

- Read the relevant retro under `docs/retrospectives/`. Notable retros for slices in scope: `M3-S5b-auction-closing-saga-terminal-paths-retrospective.md`, `M3-S6-listings-catalog-auction-status-retrospective.md`, the M4-S1 and M4-S2 retros. Use Glob to locate by slug if needed.
- Read the relevant code path under `src/CritterBids.<BC>/`. Wolverine handlers, Marten projections, integration events.

The orientation list is finite (seven items) per the AUTHORING ten-rules constraint. Per-Moment reads are session-time work, not pre-session loading.

---

## Working pattern

Same interactive cadence as Phase 1 and the M3-S5b session.

- **Cast and Setting first.** Propose protagonist name, supporting actors with onstage/offstage status, and Setting (time, place, policy posture, inherited conditions including the listings already published and attached to a session). Sign-off before any Moment.
- **Moment-by-Moment thereafter.** For each Moment:
  1. Read the implementing slice(s) from W001 and the matching scenarios from `001-scenarios.md`.
  2. Read the lived code path that satisfies the slice.
  3. Draft the Moment in the README's Guardrail-1 shape: prose paragraphs labeled `Context.`, `Interaction.`, `Response.`, and optionally `Why this matters to the bidder.`.
  4. Identify findings as the draft is written. Surface them per the Findings discipline below before sign-off.
  5. Sign-off, commit.
- **Multi-slice Moments grow in paragraphs, not labels** (README §"Multi-slice Moments"). The session-start cascade Moment is the canonical example: one Moment, multiple slices, a `Response.` block that grows in paragraphs.
- **Per-Moment "deliberately not included" subsection.** Each Moment closes with a short list of what was consciously omitted, tagged with one of the seven disposition tags. They consolidate into `## Deferred from this narrative` at session close.

Do not batch the whole narrative into one output. Bullets are not allowed inside a Moment body (Guardrail 1). Frontmatter keys are bounded by README v1 (Guardrail 2).

---

## Voice and perspective

**Single-named-protagonist plus omniscient narrator** is locked by `docs/narratives/README.md` v0.1.

The protagonist is a single bidder, named in Cast at session start. Other actors (seller, auctioneer or operator, system automations, downstream BCs) appear in Cast with onstage or offstage status and are observed by the protagonist or the narrator. Multi-perspective and parallel approaches are explicitly out of scope for this narrative.

The narrator is omniscient about the system. It can name events, projections, and downstream effects the bidder does not perceive. It dramatises only what the bidder actually experiences. This is what permits Moments where the system does most of the work (the session-start cascade, the close timer firing, the settlement saga) while keeping the journey voice intact.

---

## Findings discipline (new in CritterBids)

Authoring this narrative against lived code surfaces discrepancies between the narrative, the workshop, and the implementation. Each one is captured in a parallel `docs/narratives/001-findings.md` file with a routing decision:

| Lane | Meaning | Resolved in this PR? |
|---|---|---|
| `narrative-update` | Code and workshop are right; the narrative renders what is actually true. | Yes. Narrative edited. |
| `workshop-update` | Workshop is stale (event renamed, payload grew, slice intent shifted). | Yes. Workshop edited. |
| `code-update` | Code is wrong relative to domain understanding. | **No.** A stub follow-up implementation prompt is created under `docs/prompts/implementations/`. Resolved in Phase 2.5. |
| `document-as-intentional` | Code and workshop are both right; the apparent disagreement is two valid expressions of the domain. | Yes. Relationship documented. |

**Code refactors do not happen in this session.** The narrative session writes the narrative, classifies findings, and routes them. Phase 2.5 absorbs `code-update` items via the standard slice-then-retro flow.

### Findings file shape

Per foundation-refresh handoff §4.4:

```
### Finding NNN - <one-line title>

**Routing:** narrative-update | workshop-update | code-update | document-as-intentional

**Surfaced at:** Moment X | per-Moment proposal | session close

**Discrepancy.** What disagrees with what. Cite the workshop slice, the
code file or commit, and the narrative Moment that surfaced it.

**Resolution.** What was done in this PR (for narrative-update,
workshop-update, document-as-intentional). For code-update: the path to the
stub follow-up prompt under docs/prompts/implementations/.
```

### Heads-up sources of likely findings

Do not pre-decide outcomes. Be ready when these come up:

1. **`Handle(CloseAuction)` reads `SellerId` via `AggregateStreamAsync<Listing>`** rather than capturing it on saga state at start. A close-related Moment may force the question: should the saga carry `SellerId` from start, or is on-demand load the right shape?
2. **`BuyItNowPurchased` is a terminal outcome with no preceding `BiddingClosed`**. Buy-It-Now is currently out of scope (see "Out of scope" below). If it is pulled in opportunistically, the narrative must render this faithfully.
3. **`ListingPublished` exists as both a domain event and an integration contract** in different namespaces. Each Moment that mentions it must render the right one.
4. **W001 was authored before lived code.** Some scenarios in `001-scenarios.md` will be stale (renamed events, payload drift, intermediate saga states added). Those route to `workshop-update`.

---

## Cross-reference discipline

- Each Moment cites its slice or slices via `**Implements:** slice X.Y[, slice X.Z, ...].`
- Domain event names render in code-style backticks: `ParticipantSessionStarted`, `BidPlaced`, `ExtendedBiddingTriggered`, `BiddingClosed`, `ListingSold`, `SettlementInitiated`, `SettlementCompleted`. Plain text for ordinary nouns from the Ubiquitous Language: Listing, Bid, Bidder Session, Reserve, Hammer Price, Flash Session, Extended Bidding, Credit Ceiling.
- Do not restate the Given/When/Then content from `001-scenarios.md`. Reference the slice number; the workshop is the test specification, the narrative is the journey.
- Workshop W001 carries no `Narratives:` back-reference today. Phase 3 Item 2 of the foundation refresh does the broad backfill. **This session adds back-references only on the slices the narrative directly implements.**

---

## What the narrative does NOT carry

- **No code or pseudocode.**
- **No implementation choices.** Transport (RabbitMQ, Wolverine integration events, SignalR), projection mechanism, aggregate shape, and library primitives belong to skill files.
- **No architectural decisions.** Flag any that surface during authoring as ADR candidates and capture them in the narrative's Deferred section. Do not resolve in-narrative.
- **No GWT test specifications.** Reference `001-scenarios.md` slice numbers; do not restate.
- **No UX or UI design.** Narrate at the bidder-experience grain ("the lot board ticks forward to..."); do not design the screens.

---

## In scope (proposed Moment list)

| Moment | Slice(s) from W001 | Bidder experience |
|---|---|---|
| 1 | 0.2 | Bidder scans the QR code, lands on the demo, anonymous session starts. Display name and credit ceiling are assigned. |
| 2 | 1.3, 1.4 | Bidder browses the catalog, opens a listing detail. |
| 3 | 2.3 (multi-slice cascade) | The session starts. `BiddingOpened` cascades across all attached listings. Bidder watches the lot board come alive. |
| 4 | 3.1 | Bidder places a bid. `BidPlaced` is committed and the lot board updates. |
| 5 | 4.1, 4.3 | Bidder receives a `BidPlaced` push from BiddingHub for the listing they bid on. After a competitor outbids them, they receive a targeted `Outbid` push. |
| 6 | 3.1 (return) and 5.1 | Bidder re-bids in the trigger window, triggering `ExtendedBiddingTriggered` and reclaiming the high-bid position. *(Sign-off question at session start: include or defer this Moment.)* |
| 7 | 3.3 | Scheduled close timer fires. `BiddingClosed` and `ListingSold` commit. Bidder sees they won at the hammer price. |
| 8 | 6.1 | Settlement saga runs. Bidder is charged, fee calculated, seller payout issued, `SettlementCompleted` emitted. Bidder sees the final confirmation. |

Settlement (Moment 8) is included because the bidder's journey arc closes when they are charged and confirmed, not when the gavel falls.

---

## Out of scope for this session

- **Tier 7 obligations.** Post-settlement obligations (shipping reminders, tracking provision, delivery confirmation) belong in a follow-up narrative. Disposition: `separate-narrative`.
- **Buy-It-Now.** Slice 5.3 is a structurally distinct flow with no preceding `BiddingClosed`; warrants its own narrative. Disposition: `separate-narrative`.
- **Failure paths as narrative branches.** `BidRejected`, `ListingPassed`, settlement payment failure. Each is a separate-narrative or alternate-path-failure deferral per the seven-tag discipline.
- **Seller-perspective Moments.** Seller registration (slice 0.3), listing draft (1.1), listing publish (1.2), and attach-to-session (2.2) are setup for the bidder's experience. They live in Setting (already-true at journey start), not as Moments. Disposition: `separate-narrative` for any future seller-perspective work.
- **Auctioneer or operator-perspective Moments.** Session creation (slice 2.1) and start (2.3) are visible to the bidder only as the lot board changing state. The operator's experience is `separate-narrative`.
- **Code refactoring.** `code-update` findings become stub follow-up prompts. The actual refactors run in Phase 2.5.
- **W001 broad backfill.** Only the directly-implemented slices get a `Narratives:` back-reference here. Phase 3 Item 2 covers the rest.
- **Narrative #2 candidate selection.** Not authored in this session. The Phase 2 retrospective may name candidates for narrative #2.
- **Methodology format changes.** The narratives README v0.1 dialect is locked. Format issues that surface go to the narrative-internal retro, not into mid-session edits of the README.

If any of the above gets pulled in opportunistically, surface the scope expansion explicitly before doing the work, and reflect it in the retrospective.

---

## Deliverable plan

Per foundation-refresh handoff §4.6, Phase 2 closes with these committed:

1. **Narrative file** at `docs/narratives/001-bidder-wins-flash-auction.md`. Frontmatter v1, prose-paragraph Moments, single-named-protagonist voice. `status: accepted` at session close.
2. **Findings file** at `docs/narratives/001-findings.md`. Numbered findings per the schema in §"Findings discipline". An empty findings file is acceptable if (unlikely but possible) no findings surface.
3. **Stub follow-up prompts** at `docs/prompts/implementations/<slug>.md`, one per `code-update` finding. Each stub names the finding number, the affected code path, and the proposed slice scope. Phase 2.5 fleshes them out into full implementation prompts.
4. **Narratives README Index update** in `docs/narratives/README.md`. Row 001 added with status, journey, scope, slices.
5. **W001 cross-references.** `Narratives: [001-bidder-wins-flash-auction]` added to the W001 slices the narrative directly implements (slices 0.2, 1.3, 1.4, 2.3, 3.1, 3.3, 4.1, 4.3, 5.1 if Moment 6 is included, 6.1).
6. **Methodology-log Entry 001** at `docs/research/methodology-log.md`, OR a conscious skip note in the phase retro if no genuinely cross-cutting observation surfaces. The entry-criteria gate from the file applies: silence is fine.
7. **Narrative-internal retrospective** appended in the narrative file after `## Deferred from this narrative`, mirroring CritterCab's `001-rider-books-a-ride.md` shape.
8. **Phase 2 retrospective** at `docs/retrospectives/foundation-refresh-phase-2-retrospective.md`, mirroring the Phase 1 retrospective's structure.

---

## Acceptance criteria

- [ ] `docs/narratives/001-bidder-wins-flash-auction.md` exists. Frontmatter conforms to v1 vocabulary. `status: accepted`.
- [ ] Every Moment cites its W001 slice via `Implements:`.
- [ ] No bulleted lists appear inside any Moment body (Guardrail 1).
- [ ] No frontmatter keys outside the v1 vocabulary (Guardrail 2).
- [ ] Each Moment has a `Context.`, `Interaction.`, `Response.` body. `Why this matters to the bidder.` is present where it adds meaning, absent otherwise.
- [ ] `## Deferred from this narrative` exists. Items are bucketed by the seven disposition tags.
- [ ] `docs/narratives/001-findings.md` exists. Every finding has `Routing:`, `Surfaced at:`, `Discrepancy.`, `Resolution.`.
- [ ] One stub prompt under `docs/prompts/implementations/` per `code-update` finding.
- [ ] `docs/narratives/README.md` Index table contains row 001.
- [ ] Each W001 slice the narrative directly implements carries a `Narratives: [001-bidder-wins-flash-auction]` line.
- [ ] `docs/research/methodology-log.md` has Entry 001, OR the phase retro names the conscious skip with rationale.
- [ ] Narrative-internal retro appended in the narrative file after `## Deferred from this narrative`.
- [ ] `docs/retrospectives/foundation-refresh-phase-2-retrospective.md` exists. Mirrors Phase 1's structure.
- [ ] No file under `src/` or `tests/` was edited in this session.
- [ ] No em dashes in any committed prose (project-wide style preference).

---

## Open questions to flag (not decide)

These are session-start decisions; surface them and ask the user before locking Cast and Setting.

- **Outbid-then-rebid Moment inclusion.** Moment 6 above is provisionally included. If kept, the narrative covers a competitive happy-path arc with extended bidding. If dropped, the narrative is "single bid wins" and slice 5.1 defers to a follow-up. Lean: keep it. A real Flash auction's drama lives in the trigger window.
- **Protagonist name.** The system generates display names following a `<Adjective><Animal><Number>` convention (`SwiftFerret42`, `BoldPenguin7`). Pick a name in Cast that exemplifies the convention and reads cleanly across all eight Moments.
- **Settlement Moment grain.** The settlement saga emits five-plus events from `SettlementInitiated` through `SettlementCompleted`. The bidder perceives "I was charged" and "the listing is mine" and not most of the intermediate steps. Lean: render Moment 8 as one bidder-visible beat with a multi-paragraph `Response.` block. Resist breaking it into one Moment per saga event.
- **Reserve threshold visibility.** `ReserveMet` (slice 5.2) is P1 and may not have lived implementation in M3. Consult code at Moment 7. If `ReserveMet` is not yet a runtime event, the narrative either skips it (route the absence as `defer` or `post-MVP` deferral) or routes as `code-update`. Decide at the Moment.
- **`ListingPublished` namespacing finding.** When a Moment mentions a previously-published Listing (Setting-level), a sibling question is whether the upstream `ListingPublished` is the domain event (Selling BC internal) or the integration contract (`CritterBids.Contracts`). Decide per Moment if surfacing a finding adds clarity.
- **Phase 2.5 scope shape.** Phase 2.5 runs only if `code-update` findings exist. The phase retro names the disposition explicitly: empty (Phase 3 starts immediately) or enumerated (each `code-update` finding becomes a Phase 2.5 slice).

---

## Memory inheritance

Phase 1's session memories apply unchanged:

- **Depth over brevity** when explaining tradeoffs.
- **Ubiquitous language** (auction-domain) used naturally: Listing, Bidder, Seller, Auctioneer, Reserve, Hammer Price, Buy It Now, Flash Session, Timed Auction, Bidder Session, Credit Ceiling, Extended Bidding.
- **DDD, CQRS, Event Sourcing, EDA** assumed background.
- **SDD and NDD methodology vocabulary is NOT assumed background.** When concepts like *spec-anchored development*, *narrative-driven development*, the *findings discipline*, the *seven disposition tags*, the *Cast / Setting / Moment* primitives, the *Klefter pattern*, the *Bruun pattern*, or the *Ralph Loop* come up in the session, define them briefly on first use. Point at the foundation-refresh handoff §10 glossary, ADR 016, ADR 017, and the narratives README as durable references. Do not assume Erik will recognise the terms by name.
- **Lean opinions on questions.** Propose a default with rationale rather than open-ended elicitation.
- **No em dashes in any committed prose.** Hyphens (`-`) and en dashes are fine; em dashes (`U+2014`) are out. The narrative file is committed prose; this prompt is committed prose; the retros are committed prose.
- **Punchy prose; no AI-tool references in committed text.**
- **No `git push` to `main`** without explicit authorization. Commit freely on the Phase 2 branch; push only when asked.

---

## Starting move

When the session begins:

1. Re-read this prompt and `docs/narratives/README.md` v0.1 in full.
2. Confirm with user: protagonist name, Moment 6 (outbid-then-rebid extended bidding) inclusion, settlement Moment grain. Lock these before drafting Cast.
3. Propose Cast and Setting. Sign-off. Commit.
4. Walk Moment-by-Moment per the working pattern. For each Moment: read the implementing slice and matching scenario, read the lived code, draft the Moment, surface findings, sign-off, commit.
5. At session close: classify all findings, write `code-update` stub prompts, update the narratives README Index, add W001 cross-references on directly-implemented slices, write methodology-log Entry 001 (or document the skip in the phase retro), append the narrative-internal retro, write the Phase 2 retro, flip the narrative's `status:` to `accepted`.

---

## Document history

- **v0.1** (2026-04-26): Authored as foundation-refresh Phase 2 launch artifact. Adapts CritterCab's `001-rider-books-a-ride.md` prompt template to CritterBids' auction domain and lived-code asymmetry. Format-options section dropped (format locked in Phase 1 Item 3 via `docs/narratives/README.md` v0.1). Voice locked to single-named-protagonist plus omniscient narrator. New "Findings discipline" section added between "Voice and perspective" and "Cross-reference discipline" per foundation-refresh handoff §4.2. Acceptance criteria expanded over the CritterCab template per AUTHORING.md rule 6.
