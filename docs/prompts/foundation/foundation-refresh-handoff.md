# Hand-off Prompt — CritterBids Foundation Refresh

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-25 |
| **Author of record** | Erik Shafer (with prior-session AI collaborator analysis) |
| **Target project** | CritterBids — `C:\Code\CritterBids` |
| **Source project for methodology lifts** | CritterCab — `C:\Code\CritterCab` |
| **Workflow position** | Multi-phase methodology refresh. Phase 1 is the agent's first actionable scope. |
| **Total scope** | 4 phases (with a contingent Phase 2.5), each with its own session(s) |

---

## 0. Read this section first

You are picking up a multi-phase **methodology refresh** for **CritterBids**, an open-source auction-platform Critter Stack showcase project. The work was scoped in a prior multi-turn analysis session by reading two sibling projects side-by-side: **CritterBids** (lived; four milestones of code, four event-modeling workshops, mature skills library) and **CritterCab** (newer; zero code yet, but more thoroughly developed methodology layers including a piloted narrative-driven layer). The refresh **lifts methodology primitives from CritterCab into CritterBids** while preserving everything CritterBids has already done well.

Three things that will trip you up if you're not careful:

1. **Both projects exist on disk and you can read both directly.** CritterBids is the **target**. CritterCab is the **source of patterns**. Do not write methodology files into CritterCab. Do not refactor CritterBids code as part of this work; refactor opportunities surface during Phase 2 and are routed to follow-up slice prompts via the findings discipline (Phase 2.5).
2. **CritterBids has working code and a queued M3-S6 prompt.** It is not a greenfield project. Methodology added must coexist with what's already lived.
3. **Methodology lifts are adapted, not copy-pasted.** CritterCab's domain is ride-sharing (riders, drivers, dispatch). CritterBids' domain is auctions (bidders, sellers, auctioneers, listings, sessions). Names and examples must be adapted; structure and conventions are what carry across.

This document is comprehensive on purpose. You should be able to execute **Phase 1** from this document alone. Resume Phases 2–4 from this document after each prior phase closes.

---

## 1. Project context

### 1.1 What CritterBids is

CritterBids is an open-source eBay-modeled auction platform built on the Critter Stack (Wolverine, Marten, Polecat, Weasel, Alba). Modular monolith, eight bounded contexts (Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay, Operations). Two auction formats: **Timed** (eBay-style, days-long) and **Flash** (session-based, short, the conference-demo vehicle).

Canonical orientation files:
- `C:\Code\CritterBids\README.md`
- `C:\Code\CritterBids\CLAUDE.md`
- `C:\Code\CritterBids\docs\vision\overview.md`
- `C:\Code\CritterBids\docs\vision\bounded-contexts.md`
- `C:\Code\CritterBids\docs\vision\domain-events.md`
- `C:\Code\CritterBids\docs\vision\live-queries-and-streaming.md`
- `C:\Code\CritterBids\docs\milestones\MVP.md`

### 1.2 What CritterBids has today

**Strong:**
- 8 bounded contexts mapped (`docs/vision/bounded-contexts.md`)
- Domain event vocabulary with internal/integration distinction (`docs/vision/domain-events.md`)
- Event Modeling skill codifying Adam Dymitruk's five-phase workshop (`docs/skills/event-modeling/SKILL.md`)
- 8 personas as workshop voices (`docs/personas/`)
- 4 workshops complete (`docs/workshops/001` through `004`) with paired scenarios files
- Cross-workshop parked questions with stable IDs (`docs/workshops/PARKED-QUESTIONS.md`)
- 13 accepted ADRs plus a reserved ADR-014 and conditionally-reserved ADR-015
- 22-file skills library with status ledger (`docs/skills/README.md`)
- 3 substantial research docs (`docs/research/auction-ux-research.md`, `frontend-stack-research.md`, `grpc-opportunities-research.md`)
- Slice-driven milestones with paired prompts and retrospectives (`docs/prompts/`, `docs/retrospectives/`)
- Disciplined prompt → execute → retro loop (10 rules each, codified in `prompts/README.md` and `retrospectives/README.md`)
- JasperFx upstream-feedback ledger (`docs/jasperfx-open-questions.md`)

**Confirmed gaps (which this refresh addresses):**
- No narrative-driven (NDD-informed) journey-spec layer between workshops and prompts
- No project-level glossary (per-doc glossaries exist in research docs only)
- No top-level catalogs for commands or read-models (events have one, in `domain-events.md`)
- No spec-anchored development contract naming the authority relationship between specs and code
- No named design-phase workflow sequence
- No `docs/rules/` directory for AI-optimized constraint encodings
- Prompts directory is flat — does not subdivide by target artifact type
- No methodology log for cross-cutting observations
- No "guardrail" vs. "convention" distinction
- No demo-script runbook beyond prose mentions
- Reqnroll / executable-spec position not chosen
- No timed-listing user-journey workshop (W001 covers Flash only)
- Cast and Setting blocks missing from W001's journey workshop
- Per-BC Ubiquitous Language sections missing from W002–W004
- Workshop slices have no `status:` frontmatter for explicit handoff
- No slice-grain learnings file separate from session retros
- Klefter and Bruun event-modeling patterns used in code but not named in skills

### 1.3 What CritterCab has

CritterCab is a sister Critter Stack showcase (ride-sharing domain, gRPC focus) currently in pure design phase — no runnable code yet. It has been used as a methodology pilot ground. Specifically, it has produced and committed:

- `docs/decisions/003-spec-anchored-development.md` — names the authority relationship between specs and code
- `docs/decisions/004-design-phase-workflow-sequence.md` — names the staged sequence: Context Mapping → Domain Storytelling → Event Modeling → Narratives → Prompts → Implementation + Retrospective
- `docs/rules/structural-constraints.md` — AI-optimized rule encoding distilled from ADRs, organized by source ADR
- `docs/narratives/README.md` — operational manual for an NDD-informed narrative format with bounded frontmatter, prose-paragraph Moment bodies, two explicit guardrails, a seven-tag deferral discipline, and bidirectional referencing
- `docs/narratives/001-rider-books-a-ride.md` — first piloted narrative (status: accepted, v0.1)
- `docs/prompts/` subdivided by artifact type (`workshops/`, `narratives/`, `decisions/`, `implementations/`, `skills/`)
- `docs/research/methodology-log.md` — append-only journal of cross-cutting methodology observations, with an entry-criteria gate and time-boxed pilot disposition
- `docs/research/sdd-event-model-to-code.md` — Martin Dilger's SDD distilled (status fields, Ralph Loop, learnings file)

The new agent should read at least the following from CritterCab before starting:
- `C:\Code\CritterCab\CLAUDE.md`
- `C:\Code\CritterCab\docs\vision\README.md`
- `C:\Code\CritterCab\docs\decisions\003-spec-anchored-development.md`
- `C:\Code\CritterCab\docs\decisions\004-design-phase-workflow-sequence.md`
- `C:\Code\CritterCab\docs\rules\README.md`
- `C:\Code\CritterCab\docs\rules\structural-constraints.md`
- `C:\Code\CritterCab\docs\narratives\README.md`
- `C:\Code\CritterCab\docs\narratives\001-rider-books-a-ride.md`
- `C:\Code\CritterCab\docs\prompts\README.md`
- `C:\Code\CritterCab\docs\prompts\narratives\001-rider-books-a-ride.md`
- `C:\Code\CritterCab\docs\research\methodology-log.md`

Skim, do not memorize. You will reference these by absolute path during execution.

### 1.4 Why this refresh is happening now

Erik (the maintainer) wants CritterBids' methodology foundation to support:
- Spec-Driven Development (SDD) and Narrative-Driven Development (NDD) explicitly
- Established user stories / use-cases (narratives) that can be referenced
- Specific slices pinned and discussed before implementation
- Identification (or commitment) on whether Reqnroll-style executable specs are part of the answer
- Cross-checking lived code against the deeper domain understanding the narrative layer exposes — i.e., **rework opportunities are an expected output, not a side effect**

The refresh also formally reorganizes the docs surface so the narrative-shaped artifacts have a real home and the agent-loaded rule artifacts are crisply separated from prose-shaped ADRs.

---

## 2. The four-phase plan (overview)

| Phase | Name | Produces | Gate before next phase |
|---|---|---|---|
| **1** | Naming and porting | New ADRs (Spec-Anchored Dev, Design-Phase Workflow), `docs/narratives/` directory + README, `docs/rules/` directory + first rule file, subdivided `docs/prompts/`, `docs/research/methodology-log.md` (intro only) | All Phase 1 artifacts committed; no implementation code touched |
| **2** | First narrative session (proving ground) | First narrative for CritterBids (the Flash demo journey, against W001), the narrative's retrospective, and a `docs/narratives/001-findings.md` triage list | Narrative is `status: accepted`; findings list classified into 4 routing lanes |
| **2.5** | Rework slices (contingent on Phase 2 findings) | One new implementation prompt under `docs/prompts/implementations/` per `code-update`-routed finding; M3-S6 reviewed/re-prompted if needed | All `code-update` findings closed via standard slice → retro flow |
| **3** | Convention rollouts | Status frontmatter on workshop slices, retroactive `Narratives:` cross-references in W001, Cast/Setting blocks added to W001, per-BC Ubiquitous Language sections in W002–W004, named Klefter/Bruun patterns in event-modeling skill | All workshops carry the new conventions; bidirectional references close the workshop ↔ narrative graph |
| **4** | Open questions resolved by ADR | ADR for Reqnroll position, ADR for glossary strategy, ADR for learnings-file scope | Each open question either has an ADR or is consciously parked with a trigger |

The phases are linear with one exception: **Phase 2.5 is contingent.** If Phase 2 produces zero `code-update` findings, Phase 2.5 is empty and Phase 3 starts immediately. If it produces several, those slices run before Phase 3.

---

## 3. Phase 1 — Naming and porting

**Scope:** Produce the methodology infrastructure. No code, no methodology change to existing artifacts, no narrative authoring.

**Agent persona:** Architect (CritterBids `docs/personas/architect.md`) is the natural fit. Domain Expert and Product Owner stay on standby.

**Gate to close Phase 1:** All of items 1–5 below committed in their own PR (or one PR per item — author decides). Phase 2's first session does not start until Phase 1 is committed.

### 3.1 Item 1 — Lift Spec-Anchored Development ADR

**Source:** `C:\Code\CritterCab\docs\decisions\003-spec-anchored-development.md`

**Target:** `C:\Code\CritterBids\docs\decisions\016-spec-anchored-development.md`

**Adaptation:** The CritterCab ADR references its own narratives directory and ride-sharing context. Adapt the prose so it references CritterBids (auction domain, eight BCs, four lived milestones). Preserve the three-options structure (spec-free / spec-as-source / spec-anchored), the decision (Option C), and the consequences. Add one CritterBids-specific consequence: **the ADR applies to lived code retroactively** — the first narrative session will surface drift that Phase 2.5 absorbs as rework.

**Verify before authoring:** ADR-014 and ADR-015 are reserved per project memory (014 for M4-S6 cross-BC read-model extension shape; 015 conditional on M4-D4). The next safely-available ADR number is **016**. Confirm by reading `C:\Code\CritterBids\docs\decisions\README.md` and checking the "Reserved" entries.

**Update:** `C:\Code\CritterBids\docs\decisions\README.md` — add the new ADR row.

### 3.2 Item 2 — Lift Design-Phase Workflow Sequence ADR

**Source:** `C:\Code\CritterCab\docs\decisions\004-design-phase-workflow-sequence.md`

**Target:** `C:\Code\CritterBids\docs\decisions\017-design-phase-workflow-sequence.md`

**Adaptation:** The CritterCab ADR is written for a project with no code yet. CritterBids has lived four milestones. Adapt the framing: the staged sequence (Context Mapping → Domain Storytelling → Event Modeling → Narratives → Prompts → Implementation + Retrospective) is committed **going forward**. Steps 1–3 already happened in spirit for the existing four BCs (Participants, Selling, Auctions, Listings) without explicit Context Mapping or Domain Storytelling — that's a known cost. Future BCs (Settlement, Obligations, Relay, Operations) can run the full sequence; existing BCs absorb retroactive Cast/Setting/Ubiquitous-Language additions in Phase 3.

The ADR should also acknowledge that **Domain Storytelling (Stefan Hofer) and Context Mapping** are not currently CritterBids practices but become available as steps 1 and 2 for future BC work. Whether they are run for Settlement/Obligations/Relay/Operations is a per-BC decision deferred to those workshops.

**Update:** `C:\Code\CritterBids\docs\decisions\README.md` — add the new ADR row.

### 3.3 Item 3 — Create `docs/narratives/` directory and README

**Source for README:** `C:\Code\CritterCab\docs\narratives\README.md`

**Target:** `C:\Code\CritterBids\docs\narratives\README.md`

**Adaptation:**
- Replace ride-sharing actor examples (rider, driver, operator) with auction-domain actors (**bidder, seller, auctioneer, operator**).
- Replace ride-sharing journey examples (rider books a ride, driver accepts and starts trip) with auction-domain examples (bidder wins a Flash auction, seller publishes a timed listing, bidder is outbid in extended bidding).
- Preserve the **bounded frontmatter schema (v1)** verbatim: `slug, status, journey, perspective, scope, bounded_contexts, boundaries_touched, slices_implemented, canonical_id`. This is Guardrail 2 — adding a key requires revising the README first.
- Preserve the **prose-paragraph Moment body structure** verbatim: `Context.` / `Interaction.` / `Response.` / optional `Why this matters to <protagonist>.` This is Guardrail 1 — bullets are not allowed inside a Moment body.
- Preserve the **seven disposition tags** for cumulative deferral: `defer`, `post-MVP`, `separate-narrative`, `separate-workshop`, `implementation-detail`, `alternate-path-failure`, `UX-or-UI-detail`.
- Preserve the **bidirectional referencing convention** (workshops *will* cite narratives via a `Narratives:` line — backfill happens in Phase 3).
- Preserve the **single-named-protagonist + omniscient-narrator** voice convention.
- Preserve the **"When a new narrative is warranted" rubric** (different protagonist / different terminal outcome / different BC set / structurally distinct flow).

The Index table at the bottom of the README starts empty (no narratives written yet). Phase 2 will populate it.

### 3.4 Item 4 — Create `docs/rules/` directory and first rule file

**Source for README:** `C:\Code\CritterCab\docs\rules\README.md`

**Source for first rule file:** `C:\Code\CritterCab\docs\rules\structural-constraints.md`

**Target:**
- `C:\Code\CritterBids\docs\rules\README.md`
- `C:\Code\CritterBids\docs\rules\structural-constraints.md`

**Adaptation:** The CritterCab structural constraints draw from ADRs 002, 003, 005, 006, and 009 — all distributed-services / gRPC-shaped. CritterBids' equivalent constraints come from a different ADR set:

| Topic | Source ADR(s) in CritterBids |
|---|---|
| Modular monolith boundary discipline | ADR-001, ADR-008 (Marten BC isolation), ADR-011 (all-Marten pivot) |
| Transport posture (RabbitMQ first; integration-events-only via OutgoingMessages) | ADR-002 |
| Marten event store ownership and shared store | ADR-008, ADR-009, ADR-011 |
| Wolverine dual-store resolution | ADR-010 (resolved by 011) |
| UUID strategy | ADR-007 |
| Frontend SPA posture | ADR-012, ADR-013 |
| Spec-anchored development | New ADR-016 (Phase 1 Item 1) |

Author `structural-constraints.md` as **directive sentences grouped by source ADR**, mirroring CritterCab's shape. Keep imperative voice. Examples of the right register: "Never publish a domain event from `CritterBids.Contracts`. Domain events live in their owning BC namespace." / "Always declare `[WriteAggregate]` on aggregate-targeting handlers." / "Never use `IMessageBus.PublishAsync` inside a saga handler body; cascade via `OutgoingMessages` return."

The skill files in `C:\Code\CritterBids\docs\skills\` (especially `domain-event-conventions.md`, `wolverine-message-handlers.md`, `wolverine-sagas.md`) carry the operative rules in prose form. The rules file distills them to one-line directives and points back to the skill file for the full discussion. Skill files remain the authority; the rules file is the agent-loadable summary.

The README should explicitly note the **Layer 1 / Layer 2 / Layer 3** structure CritterCab uses (Layer 1 = structural; Layer 2 = ubiquitous language per BC; Layer 3 = code conventions). Layers 2 and 3 are deferred — Layer 2 lands during Phase 3 (per-BC Ubiquitous Language sections in workshops feed it); Layer 3 is its own future session.

**Adopt the "guardrail" concept** in the README: a guardrail is a rule whose violation is a structural defect. Conventions are softer. The README should name which directives are guardrails (likely: BC isolation, never put domain events in Contracts, never share Marten stores across Wolverine handlers without explicit configuration, MUST register every event type via `AddEventType<T>()`).

### 3.5 Item 5 — Subdivide `docs/prompts/` by target artifact type

**Source:** `C:\Code\CritterCab\docs\prompts\README.md`

**Target rearrangement of:** `C:\Code\CritterBids\docs\prompts\`

**Action:**
- Create subdirectories: `workshops/`, `narratives/`, `decisions/`, `implementations/`, `skills/`.
- Move all existing `M*-S*-*.md` prompt files into `implementations/`. They are all implementation prompts.
- Move `WORKFLOW.md` to remain at the root of `prompts/` — it documents the cross-prompt workflow, not a single artifact type.
- Move `README.md` to remain at the root and **rewrite** it to document the subdirectory layout (mirror CritterCab's prompts README structure: subdirectory table, naming convention, cross-references, when to create a new prompt, format conventions inside a prompt file).
- This hand-off prompt itself (`foundation-refresh-handoff.md`) is meta — it doesn't fit any subdirectory cleanly. Either leave it at the root of `prompts/` or move it to a new `meta/` subdirectory. Author's call.

**Verify nothing breaks:** Check `C:\Code\CritterBids\docs\retrospectives\` for any cross-references to prompts by relative path. Update any references that break due to the move.

### 3.6 Item 6 — Create `docs/research/methodology-log.md` (intro only)

**Source:** `C:\Code\CritterCab\docs\research\methodology-log.md`

**Target:** `C:\Code\CritterBids\docs\research\methodology-log.md`

**Adaptation:** Port the intro section verbatim (what this file is / is not, when to write an entry, entry format). Adapt the time-box note: "Decision to keep, fold, or remove the file is revisited at narrative #2's close" becomes — for CritterBids — "Decision to keep, fold, or remove the file is revisited at the close of Phase 2 or after the third entry, whichever comes first." Adjust to taste; the principle is the file is prepared to delete itself.

**Do NOT write Entry 001 in Phase 1.** Entry 001 will be authored at the close of Phase 2's narrative session, where it will capture the cross-cutting observation about narrative authoring against lived code. See Phase 2 §4.7.

**Update:** `C:\Code\CritterBids\docs\research\` listing — if a research README exists, add the new file. Currently CritterBids has no `research/README.md` (it has three loose research files). Author's call whether to create one as part of this item or defer.

### 3.7 Phase 1 close

When all six items are committed, write a short Phase 1 close note in the prompt's eventual retrospective (file path: `C:\Code\CritterBids\docs\retrospectives\foundation-refresh-phase-1-retrospective.md` if you split retros per phase, or accumulate into one foundation-refresh retro). The close note should:
- Confirm all six items landed.
- Note any items that surfaced and were folded in beyond the original six (with rationale).
- Identify the strongest candidate target for Phase 2's narrative session (default: **the Flash demo journey, against W001, from a single bidder's perspective** — see §4 below).
- Identify any open questions that surfaced during Phase 1 that may need their own ADR before Phase 2 starts.

---

## 4. Phase 2 — First narrative session (the proving ground)

**Scope:** Author CritterBids' first NDD-informed narrative. Audit the narrative against lived code as part of the authoring. Triage all discrepancies into four routing lanes.

**Agent persona:** Mix — Facilitator leads, Domain Expert is on duty for vocabulary, Backend Developer reads code, Product Owner protects scope.

**Gate to close Phase 2:** Three artifacts committed.

### 4.1 Pick the first narrative

**Recommendation:** The **Flash demo journey, single-bidder perspective, happy path**, against `C:\Code\CritterBids\docs\workshops\001-flash-session-demo-day-journey.md` and `C:\Code\CritterBids\docs\workshops\001-scenarios.md`.

**Why:**
- W001 already exists as the workshop layer the narrative consumes.
- The Flash format is the conference-demo vehicle and the most-cited journey in CritterBids documentation (`README.md`, `vision/overview.md`, `milestones/MVP.md`, W001).
- The journey crosses multiple BCs (Auctions, Listings, Settlement, Selling) — exercises the cross-BC narrative shape.
- M3 has lived through the auction lifecycle; the narrative will hit lived code on every Moment from `BiddingOpened` through `ListingSold` / `BuyItNowPurchased` / `ListingPassed`.

**Alternative (weaker) candidates** for awareness only — author should default to the Flash bidder journey unless something has changed:
- Timed listing journey (no workshop yet — would need its own workshop session first).
- Seller-publishes-listing journey (W004 exists; less cross-BC reach).
- Bidder-is-outbid-in-extended-bidding journey (good candidate for narrative #2).

### 4.2 Use the lifted narrative-authoring prompt template

**Template source:** `C:\Code\CritterCab\docs\prompts\narratives\001-rider-books-a-ride.md`

**New target:** `C:\Code\CritterBids\docs\prompts\narratives\001-bidder-wins-flash-auction.md` (or whatever slug the author and user agree on at session start)

The template has:
- Metadata block
- Framing (why this session exists)
- Goal (one declarative sentence)
- Orientation files (read in order)
- Working pattern
- Format options (will not need at session start because format is locked from CritterCab)
- Voice and perspective
- Cross-reference discipline
- What the narrative does NOT carry
- Deliverable plan
- Out of scope
- Memory inheritance
- Starting move

Adapt the orientation-files list to CritterBids absolute paths, and add **a new section between "Voice and perspective" and "Cross-reference discipline"**:

#### New section — "Findings discipline" (added in Phase 2)

> Authoring this narrative against lived CritterBids code will surface discrepancies between the narrative, the workshop, and the implementation. Each discrepancy is captured in a parallel `docs/narratives/001-findings.md` file with a routing decision:
>
> - **`narrative-update`** — code and workshop are right; narrative renders what's actually true. Resolved in this PR.
> - **`workshop-update`** — workshop is stale (event renamed, payload grew, slice intent shifted). Resolved in this PR by editing the workshop directly.
> - **`code-update`** — code is wrong relative to domain understanding. **Becomes a follow-up implementation prompt under `docs/prompts/implementations/`. Not resolved in this PR.**
> - **`document-as-intentional`** — code and workshop are both right; the apparent disagreement is two valid expressions. Document the relationship and move on.
>
> **Code refactors do not happen in this session.** The narrative session writes the narrative, classifies findings, and routes them. Phase 2.5 absorbs `code-update` items via standard slice → retro flow.

### 4.3 The narrative file itself

**Target:** `C:\Code\CritterBids\docs\narratives\001-bidder-wins-flash-auction.md` (slug TBD at session start)

**Format conventions:** Inherit the v0.1 format from `C:\Code\CritterBids\docs\narratives\README.md` (which was authored in Phase 1 Item 3). Both guardrails apply:
- **Guardrail 1:** Prose-paragraph Moment bodies. No bullets inside a Moment body.
- **Guardrail 2:** Bounded frontmatter vocabulary. Adding a key requires revising the README first.

The narrative ships with its retrospective in the same file (mirror CritterCab's `001-rider-books-a-ride.md` shape). The `## Deferred from this narrative` section is bucketed by the seven disposition tags.

### 4.4 The findings file

**Target:** `C:\Code\CritterBids\docs\narratives\001-findings.md`

**Shape:** Numbered list of findings, each with:
```
### Finding NNN — <one-line title>

**Routing:** narrative-update | workshop-update | code-update | document-as-intentional

**Surfaced at:** Moment X | per-Moment proposal | session close

**Discrepancy.** What disagrees with what. Cite the workshop slice, the
code file or commit, and the narrative Moment that surfaced it.

**Resolution.** What was done in this PR (for narrative-update / workshop-update
/ document-as-intentional). For code-update: the path to the follow-up prompt
file under docs/prompts/implementations/.
```

The findings file is committed in the same PR as the narrative. For `code-update` findings, the follow-up prompt file is also created in that same PR (as a stub at minimum — the agent doesn't have to fully spec the follow-up slice during the narrative session, but should establish its existence so it isn't lost).

### 4.5 What's likely to surface (heads-up, not predictions)

These are places the prior analysis flagged as likely to produce findings. The agent should not pre-decide outcomes; just be ready when these come up:

- **`Handle(CloseAuction)` reads `SellerId` via `AggregateStreamAsync<Listing>`** because `StartAuctionClosingSagaHandler` doesn't capture `SellerId` on saga state. A seller-perspective narrative on close (or a bidder-perspective Moment that mentions the seller) would force the question: should saga state carry `SellerId` from the start, or is on-demand loading the right shape?
- **`BuyItNowPurchased` is a terminal outcome with no preceding `BiddingClosed`** (per memory). Outcome events cascade via OutgoingMessages bus-only and are not stream-appended. A bidder narrative for "I clicked Buy It Now" needs to render this; verify the read-side projection consequences are intentional.
- **`ListingPublished` exists as both a domain event and an integration contract** in different namespaces. The narrative should render exactly the right one at the right Moment without conflating them.
- **Workshop W001 predates lived implementation.** Some W001 scenarios will be wrong in detail (event names changed, payloads grew fields, sagas added intermediate states). Each is `workshop-update`.
- **M3-S6 is queued, not built.** The Listings catalog auction-status extension prompt at `C:\Code\CritterBids\docs\prompts\implementations\M3-S6-listings-catalog-auction-status.md` (which will move to `implementations/` in Phase 1) hasn't run. **The narrative should be authored before M3-S6 runs.** If the narrative reshapes M3-S6's scope, M3-S6 is re-prompted as the first Phase 2.5 slice.
  - **Updated 2026-04-25 at execution start:** M3-S6 shipped before this hand-off prompt began executing (its retrospective is in `docs/retrospectives/`). M4-S1 and M4-S2 also shipped since this prompt was authored. The narrative session now audits lived M3-S6 / M4-S1 / M4-S2 code alongside M3-S5b. The "re-prompt M3-S6 as first Phase 2.5 slice" path is replaced by the standard finding-routing flow against shipped code.

### 4.6 Definition of done for Phase 2

- Narrative file at `docs/narratives/001-<slug>.md` with `status: accepted` in frontmatter.
- Retrospective appended in the same narrative file.
- Findings file at `docs/narratives/001-findings.md` committed.
- For each `code-update` finding, a stub prompt at `docs/prompts/implementations/<descriptive-slug>.md`.
- `docs/narratives/README.md` Index table updated with the new narrative row.
- W001 carries a `Narratives: [001-<slug>]` cross-reference where appropriate (Phase 3 does the broader retroactive backfill, but the slices this narrative directly implements get their pointer now).
- **Methodology-log Entry 001 written** at session close — see §4.7.

### 4.7 Methodology-log Entry 001

After the narrative session closes, write the first methodology-log entry at `C:\Code\CritterBids\docs\research\methodology-log.md`.

**Topic:** Cross-cutting observation about authoring the first narrative against lived code (as opposed to CritterCab's clean-slate authoring). Specifically:
- What proportion of findings routed to which lane?
- What does that proportion say about how much workshop ↔ code drift accumulates in a project that runs four milestones without a narrative layer?
- What does it predict for narrative #2 (likely seller- or operator-perspective, less lived code, less drift expected)?
- Does it suggest a "narrative refresh cycle every N milestones" rule, or is the once-at-foundation-refresh enough?

Format: Trigger / Observation / Implication, per the file's entry format. Include falsification criteria — what would confirm it, what would disconfirm it.

If no genuinely cross-cutting observation surfaces (the entry-criteria gate in `methodology-log.md` is real — silence is fine), don't force one. Instead, note in the Phase 2 retrospective why no entry was warranted.

---

## 5. Phase 2.5 — Rework slices (contingent)

**Scope:** Execute each `code-update` finding from Phase 2 as a follow-up slice with its own prompt and retrospective. **Pure code work, no methodology change.**

**Agent persona:** Per-slice — typically Backend Developer + QA, matching M3-S5b shape.

**Gate to close Phase 2.5:** All `code-update` findings closed via standard slice → retro flow. M3-S6 either re-prompted (if Phase 2 reshaped its scope) or confirmed unchanged.

**Operating rules:**
- Each slice gets its own prompt under `docs/prompts/implementations/` and its own retrospective under `docs/retrospectives/`.
- Slice prompts inherit all CritterBids conventions established in `docs/prompts/README.md` (the rules of ten apply).
- Reference-doc discipline (M3-S5b convention) applies: cite Wolverine/Marten source paths or AI-skills paths for any first-use claim.
- The narrative file authored in Phase 2 is the architectural reference for these slices. If implementation diverges from the narrative during Phase 2.5, surface in the slice's retro per Spec-Anchored Development discipline (ADR-016, lifted in Phase 1 Item 1).

**Not in Phase 2.5:** New methodology, new ADRs, new narratives. If new methodology gaps surface, capture them as parking-lot items for Phase 4 (which is the dedicated methodology-question-resolution phase).

---

## 6. Phase 3 — Convention rollouts

**Scope:** Apply the conventions established in Phases 1 and 2 retroactively across CritterBids' existing artifacts. Pure docs work.

**Agent persona:** Architect or Domain Expert depending on item.

**Gate to close Phase 3:** All workshops carry the new conventions; the workshop ↔ narrative graph is navigable in both directions.

### 6.1 Item 1 — Add `status:` frontmatter to existing workshop slices

For W001, W002, W003, W004 — add `status:` per slice. Vocabulary: `design | planned | in progress | done`. Most existing CritterBids slices are `done` (they have lived code); M3-S6's slice would be `planned` until it runs in Phase 2.5; M4 slices may be `design` or `planned`.

Source convention: Martin Dilger SDD via `C:\Code\CritterCab\docs\research\sdd-event-model-to-code.md` §"Step 2 — Event Model as Source of Truth".

### 6.2 Item 2 — Backfill `Narratives:` cross-references

Workshop slices that the Phase 2 narrative implements get a `Narratives: [001-<slug>]` line. Mirror the pattern in `C:\Code\CritterCab\docs\narratives\README.md` §"Bidirectional referencing".

### 6.3 Item 3 — Add Cast and Setting blocks to W001

W001 (`docs/workshops/001-flash-session-demo-day-journey.md`) is already a journey workshop. Adding Cast and Setting blocks retroactively makes it a hybrid workshop/narrative artifact and aligns it with the narrative format authored in Phase 2.

- **Cast:** Named protagonists with onstage/offstage status. Adapted from the Phase 2 narrative's Cast.
- **Setting:** The Flash demo conditions, default policy posture (e.g., session length, bid increment defaults), the configuration values that subsequent Moments inherit without restating.

### 6.4 Item 4 — Add per-BC Ubiquitous Language sections to W002–W004

Mirror the §3 Ubiquitous Language pattern from `C:\Code\CritterCab\docs\workshops\001-dispatch-event-model.md`. Each workshop's §3 becomes the per-BC glossary. This is the project's de facto glossary collection until Phase 4 resolves whether a project-level glossary is also needed.

For each of W002 (Auctions), W003 (Settlement), W004 (Selling):
- Identify the BC-internal terms that appear in the workshop's slices.
- Define each in one line with optional "what it is *not*" notes (CritterCab pattern).
- Cross-reference any term that overlaps with another BC's vocabulary.

### 6.5 Item 5 — Name Klefter and Bruun patterns in the event-modeling skill

Source: `C:\Code\CritterCab\docs\research\agents-in-event-models.md` and `C:\Code\CritterCab\docs\research\event-modeling-workshop-guide.md`.

Target: `C:\Code\CritterBids\docs\skills\event-modeling\SKILL.md`

Add named subsections covering:
- **Klefter translation-decision events:** when a slice coordinates with an external system AND a decision is made locally, the local decision becomes a first-class event. Example in CritterBids: Auctions BC reading from Listings to make a `ReserveMet` decision is a Klefter pattern. Settlement asking Participants for credit ceiling and recording the outcome would be another.
- **Bruun temporal-automation slice pattern:** todo-list read models with asterisk-suffix names (e.g., `OffersAwaitingExpiry*` in CritterCab; in CritterBids, the auction-closing saga's scheduled `CloseAuction` could be modeled this way). Clock-rewind glyph on time-driven automation stickies.
- **Configuration-as-events** (Bruun): operator-tunable policy parameters as events on a singleton stream rather than a settings table. CritterBids' `DispatchPolicy`-equivalent for auctions (default bid increment, extended-bidding window, max-duration cap) could adopt this if the project chooses.

These are pattern names, not commitments to refactor existing CritterBids code. Naming them makes the model legible.

### 6.6 Phase 3 close

Phase 3's retrospective can be a single short note. The interesting per-item lessons (especially anything that surfaced during the Cast/Setting backfill or the Ubiquitous Language extraction) feed potential methodology-log entries. Apply the entry-criteria gate from `methodology-log.md` — silence is fine.

---

## 7. Phase 4 — Open questions resolved by ADR

**Scope:** Each open methodology question becomes either an accepted ADR or a consciously parked item with a named trigger.

**Agent persona:** Architect leads, Product Owner protects scope.

**Gate to close Phase 4:** Each question below has a disposition (Accepted ADR / Proposed ADR / Parked with trigger).

### 7.1 Question 1 — Reqnroll position

**Frame:** Workshop scenarios in CritterBids are prose-formatted markdown Given/When/Then blocks. They are referenced by name from prompts and retros (the M3-S5b prompt cites `002-scenarios.md` §3 rows 3.5–3.11) but the linkage is by convention, not by mechanical executable specs.

**Options to evaluate:**
1. **Adopt Reqnroll** — workshop scenarios get exported as `.feature` files; tests are generated from them.
2. **Workshop scenarios authored as `.feature` files directly** — the workshop is the feature file (single source).
3. **Parallel-source** — `.feature` files alongside prose scenarios, with a discipline for keeping them in sync.
4. **Decline executable specs** — narrative + skill + retrospective discipline carries the linkage; Reqnroll deferred.

**Output:** ADR-018 (or next available) with the chosen position. CritterCab is in the same place on this question; the ADR could be co-published or coordinated.

### 7.2 Question 2 — Project-level glossary strategy

**Frame:** Per-BC glossaries land in workshops in Phase 3 §6.4. Per-research-doc glossaries already exist. There is no top-level cross-BC glossary.

**Options:**
1. **Per-BC only** — accept that the workshop §3 sections are the authoritative source; no top-level page.
2. **Project-level glossary as a synthesis** — `docs/vision/glossary.md` aggregates per-BC entries with cross-BC overlap notes.
3. **Project-level + per-BC** — both, with per-BC as authoritative and project-level as derived index.

**Output:** ADR or a short decision note in `docs/vision/`.

### 7.3 Question 3 — Learnings file scope

**Frame:** CritterCab's research has explored Dilger SDD's slice-grain learnings file (`C:\Code\CritterCab\docs\research\sdd-event-model-to-code.md`). CritterBids has session-grain retros only.

**Options:**
1. **Skip** — retros are sufficient.
2. **Per-BC learnings file** — `docs/skills/<bc>/learnings.md` per BC, slice-grain, persists across sessions, stable rules migrate into skill files over time.
3. **Project-wide learnings file** — single `docs/learnings.md`.

**Output:** ADR or decision note.

### 7.4 Question 4 — Demo-script runbook

**Frame:** The Flash demo scenario is described in four places (`README.md`, `vision/overview.md`, `milestones/MVP.md`, W001) but never as a runnable presenter runbook.

**Options:**
1. **Author as a narrative** — covered by Phase 2's first narrative, with a presenter-instruction overlay in a sibling file.
2. **Author as a separate runbook** — `docs/demo/flash-session-runbook.md` with stage directions.
3. **Defer** — until a real conference talk demands it.

**Output:** Decision recorded.

### 7.5 Question 5 — Operations runbook / SRE-style docs

**Frame:** Deployment and operations are referenced in ADRs but not as runbooks. Hetzner VPS topology, health checks, alerting, incident playbooks are all undocumented.

**Output:** Probably parked with a trigger ("when first production-leaning deployment is scheduled"). Not blocking the methodology refresh.

### 7.6 Phase 4 close

Phase 4 is the natural endpoint of the foundation refresh. The retrospective should answer:
- Are there other open methodology questions that surfaced during Phases 1–3 that didn't make it into Phase 4? Capture them.
- Is the methodology log carrying its weight? Apply the time-box review.
- What's the next non-methodology slice (i.e., back to product work)?

---

## 8. Working pattern (cross-phase)

These rules apply across all phases.

1. **One PR per Phase 1 item, or one PR per phase — author's call.** Either model is fine. Phase 2 must be its own PR because the narrative session has a single deliverable shape.
2. **Reference-doc discipline (M3-S5b convention) applies across all phases.** Any first-use claim about Wolverine/Marten/Polecat/Alba behavior cites a source from `C:\Code\JasperFx\ai-skills\`, a pristine source repo (`C:\Code\JasperFx\wolverine\`, `\marten\`, `\polecat\`, `\alba\`), `CritterStackSamples`, or Context7 (`/jasperfx/wolverine`, `/jasperfx/marten`).
3. **Interactive cadence.** Don't batch large outputs. Mirror the M3-S5b prompt-driven discipline: propose, sign-off, commit. The narrative session in Phase 2 specifically uses Moment-by-Moment sign-off.
4. **Retrospectives are not optional.** Each phase has a retrospective. Phase 2's retro is appended to the narrative file (CritterCab convention). Phases 1, 2.5, 3, 4 retros live in `docs/retrospectives/` per existing CritterBids convention.
5. **Surface scope expansion before doing it.** If a Phase 1 item turns up a Phase 3-shaped sub-task, surface it as a parking-lot item rather than absorbing silently.
6. **No code refactoring in Phases 1, 3, or 4.** Code refactoring lives in Phase 2.5 only, gated by the findings discipline.

---

## 9. What this work is NOT

Explicit non-goals to prevent scope drift:

- **Not a redesign of any BC.** Methodology refresh, not domain redesign. If a finding routes to `code-update`, it goes through standard slice flow with its own scope.
- **Not a Reqnroll adoption.** Reqnroll is a Phase 4 question; it is not pre-decided.
- **Not a port of CritterCab files verbatim.** Adaptation to CritterBids' domain (auctions) is required throughout. Names, examples, ADR numbers all change.
- **Not a workshop redo.** W001–W004 are not re-run. They get conventions added (Phase 3) and may have stale scenarios corrected (Phase 2 `workshop-update` findings), but the modeling work itself stands.
- **Not a CritterCab edit.** Do not write files into `C:\Code\CritterCab\`. CritterCab is read-only from this work's perspective.
- **Not a rewrite of `CLAUDE.md` or `README.md`.** Both are updated to reflect new directories and ADRs as a side effect of Phase 1, but their structure is preserved.
- **Not a Domain Storytelling or Context Mapping exercise for existing BCs.** ADR-017 names these as available steps for *future* BCs only. Existing BCs absorb retroactive Cast/Setting/Ubiquitous-Language additions in Phase 3 instead.

---

## 10. Methodology glossary (terms a fresh agent might not know)

| Term | Meaning |
|---|---|
| **NDD** | Narrative-Driven Development. Sam Hatoum at Xolvio. Synthesizes BDD, EventStorming, Specification by Example, DDD, User Story Mapping into structured narratives. CritterCab and CritterBids are NDD-**informed** (no commercial Auto/Kiro platform dependency). |
| **SDD** | Spec-Driven Development. Broader category. Martin Dilger's variant introduced status fields (`design | planned | in progress | done`), the Ralph Loop (context-clearing per slice), and the learnings file. |
| **Spec-anchored** | Distinct from spec-as-source (commercial SDD platforms) and spec-first (write once, abandon). Specs describe intent; code is authoritative for runtime; drift is caught at retrospective time. CritterCab ADR-003, CritterBids ADR-016 (Phase 1 Item 1). |
| **Klefter pattern** | When a slice coordinates with an external system AND a decision is made locally, the local decision becomes a first-class event. Source: `C:\Code\CritterCab\docs\research\agents-in-event-models.md`. |
| **Bruun pattern** | Todo-list read models (asterisk-suffix names like `OffersAwaitingExpiry*`); clock-rewind glyphs on time-driven automations; configuration-as-events for operator-tunable policy. Source: same file. |
| **Guardrail** | Rule whose violation is a structural defect. Distinct from a convention (which is softer). Established in CritterCab `docs/narratives/README.md`. |
| **Methodology log** | Append-only journal of cross-cutting methodology observations that span artifact layers or sessions. Restrictive entry criteria; silence is fine. CritterCab `docs/research/methodology-log.md`. |
| **Cast / Setting / Moment** | Narrative format primitives. Cast = actors with onstage/offstage status. Setting = inherited conditions and policy posture. Moment = one beat in the journey, structured as Context / Interaction / Response / optional Why-this-matters. |
| **Bidirectional referencing** | Narratives cite workshop slices via `Implements:`. Workshops eventually cite narratives via `Navrratives:`. Backfill happens in Phase 3 for CritterBids. |
| **Findings discipline** | The Phase 2 routing of discrepancies between narrative, workshop, and code into one of four lanes: `narrative-update` / `workshop-update` / `code-update` / `document-as-intentional`. |
| **Ralph Loop** | Dilger's agent operating cycle: find planned slices → implement → test → record learnings → clear context → repeat. CritterBids may or may not adopt this in Phase 4. |

---

## 11. Reference file inventory

### CritterBids (target — read these)

**Core orientation:**
- `C:\Code\CritterBids\README.md`
- `C:\Code\CritterBids\CLAUDE.md`
- `C:\Code\CritterBids\docs\vision\overview.md`
- `C:\Code\CritterBids\docs\vision\bounded-contexts.md`
- `C:\Code\CritterBids\docs\vision\domain-events.md`
- `C:\Code\CritterBids\docs\vision\live-queries-and-streaming.md`
- `C:\Code\CritterBids\docs\milestones\MVP.md`

**Workshops (Phase 2 input):**
- `C:\Code\CritterBids\docs\workshops\001-flash-session-demo-day-journey.md` — primary Phase 2 input
- `C:\Code\CritterBids\docs\workshops\001-scenarios.md`
- `C:\Code\CritterBids\docs\workshops\002-auctions-bc-deep-dive.md`
- `C:\Code\CritterBids\docs\workshops\002-scenarios.md`
- `C:\Code\CritterBids\docs\workshops\003-settlement-bc-deep-dive.md`
- `C:\Code\CritterBids\docs\workshops\003-scenarios.md`
- `C:\Code\CritterBids\docs\workshops\004-selling-bc-deep-dive.md`
- `C:\Code\CritterBids\docs\workshops\004-scenarios.md`
- `C:\Code\CritterBids\docs\workshops\PARKED-QUESTIONS.md`

**ADRs (existing 001–013; 014 reserved; 015 conditional; new ADRs land at 016+):**
- `C:\Code\CritterBids\docs\decisions\README.md`
- `C:\Code\CritterBids\docs\decisions\001-modular-monolith.md` through `013-frontend-core-stack.md`

**Skills (Phase 1 Item 4 distillation source):**
- `C:\Code\CritterBids\docs\skills\README.md`
- `C:\Code\CritterBids\docs\skills\domain-event-conventions.md`
- `C:\Code\CritterBids\docs\skills\wolverine-message-handlers.md`
- `C:\Code\CritterBids\docs\skills\wolverine-sagas.md`
- `C:\Code\CritterBids\docs\skills\event-modeling\SKILL.md` — Phase 3 Item 5 target

**Personas:**
- `C:\Code\CritterBids\docs\personas\README.md`
- `C:\Code\CritterBids\docs\personas\architect.md`
- `C:\Code\CritterBids\docs\personas\domain-expert.md`
- `C:\Code\CritterBids\docs\personas\backend-developer.md`
- `C:\Code\CritterBids\docs\personas\product-owner.md`

**Existing prompt and retro examples (the CritterBids style baseline):**
- `C:\Code\CritterBids\docs\prompts\README.md`
- `C:\Code\CritterBids\docs\prompts\WORKFLOW.md`
- `C:\Code\CritterBids\docs\prompts\implementations\M3-S5b-auction-closing-saga-terminal-paths.md` — gold-standard recent prompt
- `C:\Code\CritterBids\docs\retrospectives\M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` — gold-standard recent retro

**Queued prompt (not yet executed; review in Phase 2):**
- `C:\Code\CritterBids\docs\prompts\implementations\M3-S6-listings-catalog-auction-status.md` (will move to `implementations/` in Phase 1)

**Upstream feedback ledger:**
- `C:\Code\CritterBids\docs\jasperfx-open-questions.md`

### CritterCab (source of patterns — read these)

**Core orientation:**
- `C:\Code\CritterCab\README.md`
- `C:\Code\CritterCab\CLAUDE.md`
- `C:\Code\CritterCab\docs\vision\README.md`

**ADRs to lift:**
- `C:\Code\CritterCab\docs\decisions\003-spec-anchored-development.md` → CritterBids ADR-016
- `C:\Code\CritterCab\docs\decisions\004-design-phase-workflow-sequence.md` → CritterBids ADR-017

**Rules (lift to CritterBids `docs/rules/`):**
- `C:\Code\CritterCab\docs\rules\README.md`
- `C:\Code\CritterCab\docs\rules\structural-constraints.md`

**Narratives (lift README, study the example):**
- `C:\Code\CritterCab\docs\narratives\README.md` — operational manual to port
- `C:\Code\CritterCab\docs\narratives\001-rider-books-a-ride.md` — reference example, shape only

**Prompts subdivision:**
- `C:\Code\CritterCab\docs\prompts\README.md` — port the subdivision conventions
- `C:\Code\CritterCab\docs\prompts\narratives\001-rider-books-a-ride.md` — Phase 2 prompt template

**Methodology log:**
- `C:\Code\CritterCab\docs\research\methodology-log.md` — port the intro

**Research informing the methodology:**
- `C:\Code\CritterCab\docs\research\sdd-event-model-to-code.md` — Dilger SDD distilled
- `C:\Code\CritterCab\docs\research\event-modeling-workshop-guide.md` — methodology reference
- `C:\Code\CritterCab\docs\research\agents-in-event-models.md` — Klefter and Bruun patterns

### Critter Stack reference (for any first-use claim about framework behavior)

- `C:\Code\JasperFx\ai-skills\` — upstream skill files
- `C:\Code\JasperFx\wolverine\` — pristine Wolverine source
- `C:\Code\JasperFx\marten\` — pristine Marten source
- `C:\Code\JasperFx\polecat\` — pristine Polecat source
- `C:\Code\JasperFx\alba\` — pristine Alba source
- `C:\Code\JasperFx\CritterStackSamples\` — canonical examples
- Context7 IDs: `/jasperfx/wolverine`, `/jasperfx/marten`, `/jasperfx/polecat`

---

## 12. Starting move

When Phase 1 begins:

1. Read this document fully.
2. Read the CritterBids orientation files in §11.
3. Read the CritterCab source files in §11 (skim narratives README; read both ADRs in full; read rules files in full).
4. Confirm with the user:
   - The next available ADR number (default: 016, but verify by reading `C:\Code\CritterBids\docs\decisions\README.md` and noting any newly-added entries).
   - Whether Phase 1 closes as one PR or six PRs (one per item).
   - Where the hand-off prompt itself should live after the prompts subdivision in Item 5 (root of `prompts/` or a new `meta/` subdirectory).
5. Begin Phase 1 Item 1 (lift Spec-Anchored Development ADR). Do not start Item 2 until Item 1 is committed.

When Phase 2 begins (after Phase 1 closes):

1. Re-read this document's §4 fully.
2. Verify the narrative target with the user (default: Flash demo bidder happy-path against W001).
3. Author the narrative-authoring prompt at `docs/prompts/narratives/001-<slug>.md` adapting from `C:\Code\CritterCab\docs\prompts\narratives\001-rider-books-a-ride.md`.
4. Run the narrative session interactively, Moment by Moment, with sign-off cadence.
5. Capture findings in `docs/narratives/001-findings.md` as they surface.
6. Write the retrospective and methodology-log Entry 001 (or skip the entry consciously) at session close.

When Phase 2.5 begins (contingent on Phase 2 findings):

1. Read each `code-update` finding stub.
2. For each, author a full implementation prompt under `docs/prompts/implementations/`.
3. Execute slices one at a time, retros included.
4. Re-prompt M3-S6 if Phase 2 reshaped its scope; otherwise execute as queued.

When Phase 3 begins:

1. Read this document's §6 fully.
2. Items can run in any order. Item 1 (status frontmatter) and Item 4 (per-BC Ubiquitous Language) are most mechanical; Item 3 (Cast/Setting backfill on W001) is more interpretive.
3. Each item gets a small retro note. Methodology-log entries warranted only if cross-cutting observations surface.

When Phase 4 begins:

1. Read this document's §7 fully.
2. Each question gets either an ADR or a parked-with-trigger disposition.
3. Coordinate with CritterCab on Question 1 (Reqnroll) — both projects are in the same place on this.
4. Phase 4's close is the foundation refresh's close. Final retrospective should answer "what comes next" — typically a return to product work.

---

## 13. Memory / preferences inheritance

These preferences (from CritterBids' lived sessions) apply to this work:

- **Depth over brevity** when explaining tradeoffs.
- **Ubiquitous language** (auction-domain) used naturally — Listing, Bidder, Seller, Auctioneer, Reserve, Hammer Price, Buy It Now, Flash Session, Timed Auction.
- **DDD / CQRS / Event Sourcing / EDA assumed background.**
- **Lean opinions on questions** — when asking, propose a default with rationale rather than open-ended elicitation.
- **No em dashes in any committed prose.** Hyphens (regular `-`) and en dashes are fine; em dashes (`—`) are out (this is a writing-style preference; the prompt itself is exempt because it is a session artifact rather than committed prose, but the artifacts produced by the sessions adhere).
- **Punchy prose; no AI-tool references in committed text.**
- **CQRS introduced contextually**, not front-loaded.

---

## 14. Open question to confirm at session start

The hand-off prompt assumed:
- ADRs 016 and 017 are the next available numbers.
- The Flash demo bidder happy-path narrative is the right Phase 2 target.
- Phase 1 produces six items as listed.
- No CritterBids-specific methodology gap was missed during the prior analysis.

Confirm or correct each before starting.

---

## Document History

- **v0.1** (2026-04-25): Authored as the hand-off artifact closing the prior comparison-and-planning session. Captures the four-phase plan with contingent Phase 2.5, the findings-discipline routing, the methodology-log primitive, the bounded narrative format, and the rules/guardrails distinction. Ready for a fresh agent to pick up at Phase 1.
