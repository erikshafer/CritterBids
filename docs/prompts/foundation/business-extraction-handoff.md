# Foundation Prompt — CritterBids Business Architecture Extraction

**Type:** Foundation (multi-phase orchestration prompt)
**Target artifact tree:** `docs/extraction/`
**Suggested prompt home:** `docs/prompts/foundation/business-extraction-handoff.md`
**Date authored:** 2026-05-28
**Runner:** GitHub Copilot (single long agentic session; phases gated and committed in sequence)
**Produces:** A descriptive corpus (per-BC dossiers, cross-BC process traces, ubiquitous-language glossary, synthesis), one evaluative artifact (lessons learned), and one factual register (gaps and drift).

---

## Framing

CritterBids is a mid-flight reference architecture: five of its eight bounded contexts are implemented in `src/`, three exist only as vision-document prose, and the project is about to be rebuilt around a new specification methodology. Before that rebuild begins, this session extracts what CritterBids *actually is today* into a self-contained corpus under `docs/extraction/`, so the rebuild has an honest, source-grounded record of the business it is re-expressing rather than a pile of aspirational docs.

This is the same kind of extraction CritterSupply ran, adapted for a project that is small and incomplete. Two adaptations matter most: **everything is tagged by implementation maturity** (so no reader mistakes vision-doc prose for working code), and **judgment is quarantined** into a single lessons-learned document so the rest of the corpus stays strictly factual.

The rebuild methodology is a **downstream consumer of this corpus and is out of scope here.** This session does not design, reference, or anticipate it. It describes what exists.

---

## Goal

Produce, under `docs/extraction/`, a complete and source-cited descriptive record of CritterBids' bounded contexts, business processes, and domain vocabulary; a factual register of every gap and drift between intent and reality; and a single lessons-learned document mined from the project's retrospectives and ADR history — with every bounded context and process tagged by implementation maturity throughout.

---

## Prime directives

These hold for every phase. They are the guardrails; the phase plan is the work.

1. **Code is the source of truth. Docs describe intent; code shows reality.** When a vision doc, ADR, `CLAUDE.md`, or `.github/copilot-instructions.md` disagrees with `src/` or `tests/`, the code wins, and the divergence is recorded as an entry in the gap-and-drift register. Never silently reconcile the two.

2. **Two registers, never mixed.** The dossiers, process traces, glossary, gap register, and synthesis are the **descriptive register**: they state what is, in neutral language, with no evaluation. The lessons document is the **evaluative register**: it is the only place judgment, "what was hard," or "what we learned" may appear. A sentence that evaluates does not belong in a descriptive artifact, and a sentence that merely inventories does not belong in lessons. If a passage feels like it judges, move it to lessons or cut it.

3. **Maturity is tagged everywhere.** Every BC, every process, and every notable capability carries one of the maturity tags defined below. A reader skimming any artifact must be able to tell working behavior from declared intent without cross-referencing `src/`.

4. **Every structural claim cites a source.** Cite by repository-relative path (e.g. `src/CritterBids.Auctions/AuctionClosingSaga.cs`), and where useful a type or member name. A claim about behavior cites the test that proves it. An unverifiable claim is either dropped or recorded as an open question.

5. **Describe; do not repair, design, or prescribe.** This session creates only the `docs/extraction/` tree (and updates its own README). It does not modify `src/`, does not edit or "fix" existing docs to resolve drift, does not author ADRs, does not write tests, and does not design or hint at the downstream rebuild. Found drift is a finding, not a fix.

6. **Escalate, do not guess.** When the agent hits a genuine ambiguity that the code does not resolve (an event named but never emitted, a saga path with no apparent trigger, a maturity call that is truly unclear), it records the question in that artifact's open-questions section and the corpus-level `OPEN-QUESTIONS.md`, and moves on. It never invents architecture or resolves a design decision unilaterally.

7. **The prompt contains no code, the corpus references code by name.** This prompt carries no snippets. The extraction documents may name types, events, commands, handlers, sagas, and projections, and may sketch a minimal signature or an event sequence, but they do not copy handler bodies or paste implementation blocks. Documentation *about* code names and locates it; it does not reproduce it.

8. **No marketing voice.** Factual, terse, grep-able. No "elegant," no "clean," no "robust." A one-paragraph business purpose per BC is the upper bound of prose flourish; everything else is tables, names, paths, and short declarative sentences.

9. **CritterBids describes only CritterBids.** Do not pull CritterSupply or CritterCab into the analysis. The one exception already living in `docs/vision/bounded-contexts.md` (the CritterSupply-analogue table) may be acknowledged as existing context but is not expanded, mirrored, or used as a framing device.

---

## Maturity taxonomy

Apply exactly these tags. Verify each against `src/` and `tests/` directly — do not trust this prompt's or any doc's classification.

| Tag | Definition |
|---|---|
| **Implemented** | A `src/CritterBids.<BC>` project exists with aggregates/handlers and is registered in the API host; behavior is exercised by tests. |
| **Partial** | The project exists and runs, but a capability the vision docs attribute to it is absent or stubbed (e.g. a declared saga path not built, a declared event never emitted). |
| **Scaffolded** | A project or folder exists but holds little more than registration and stubs; minimal real behavior. |
| **Planned-only** | Described in vision docs with no corresponding `src/` project. As of authoring this prompt, the candidates are Obligations, Relay, and Operations — **verify against `src/` before tagging**, since the project is mid-flight and may have moved. |

Capability-level tags within an otherwise-Implemented BC are encouraged (e.g. a BC may be Implemented overall while its Proxy Bid saga is Partial). When a tag is a close call, state the evidence for the call in one line rather than asserting it bare.

---

## Orientation reading

This is a foundation prompt, so the seven-item context rule in `docs/prompts/AUTHORING.md` is relaxed in favor of a per-phase reading discipline. Read the **core set** once at Phase 0, then the **per-phase additions** as each phase opens. Do not attempt to hold the entire repo in context at once.

**Core set (Phase 0):**
- `CLAUDE.md` — routing, conventions, documentation hierarchy. Treat as authoritative for *intent*, subordinate to code for *reality*.
- `docs/vision/bounded-contexts.md` — the BC map, ownership, design decisions, integration topology. Primary descriptive source; verify every claim against code.
- `docs/vision/domain-events.md` — canonical event vocabulary.
- `docs/decisions/README.md` — the ADR status ledger and the superseded chains. The lessons phase leans heavily on this.
- `docs/personas/README.md` and the persona files in `docs/personas/` — the roster and invocation convention for this session.
- `docs/prompts/AUTHORING.md` and `docs/retrospectives/README.md` — the house conventions this corpus and its commits should respect.
- `.github/copilot-instructions.md` — read as a **drift candidate, not as authority.** It is known to lag the ADR record; that lag is itself extraction material.
- The `src/` tree and the `tests/` tree — list them, and note which BCs have projects and which do not.

**Per-phase additions** are named in each phase below.

---

## Personas

Load CritterBids' own roster from `docs/personas/` and follow the invocation convention in `docs/personas/README.md`: declare which personas are active at the start of each phase, and label contributions (`[ARCHITECT]`, `[DOMAIN EXPERT]`, `[QA]`, `[PRODUCT OWNER]`, etc.). Productive tension is the point; do not flatten disagreement into false consensus. `@Facilitator` runs the session, keeps phases on track, and synthesizes.

Default per-phase casting (adjust with judgment):

- **Phase 0 (scaffold + inventory):** `@Facilitator`, `@Architect`, `@ProductOwner`.
- **Phase 1 (dossiers):** `@Architect` and `@BackendDeveloper` lead structure (aggregates, handlers, sagas, projections, integration contracts, storage); `@DomainExpert` writes each BC's business purpose; `@QA` derives behavioral facts from tests.
- **Phase 2 (process traces):** `@DomainExpert` and `@Architect` co-lead; `@QA` supplies end-to-end evidence from tests; `@ProductOwner` keeps the framing business-legible.
- **Phase 3 (glossary):** `@DomainExpert` leads; `@Architect` verifies each term's usage (or violation) in code.
- **Phase 4 (gap and drift register):** `@Architect` and `@QA`.
- **Phase 5 (lessons):** `@Architect`, `@ProductOwner`, `@DomainExpert`; `@Facilitator` enforces that lessons are generalizable, not restated item detail.
- **Phase 6 (synthesis):** `@Architect` and `@ProductOwner` co-author; `@DomainExpert` confirms domain accuracy.

`@FrontendDeveloper` and `@UX` stay benched: the SPA surface is research-only and offers little to extract. Pull them in only if a process trace genuinely turns on a read-model or real-time-feed concern.

---

## Phase plan

Phases run in order. Each ends at a **gate**: a short self-check against that phase's acceptance criteria, an update to `docs/extraction/README.md`, and a commit. A failed gate is escalated, not worked around. Do not start a phase before the prior phase's gate passes.

### Phase 0 — Orientation and scaffold

**Additions to read:** none beyond the core set.

**Do:**
- Read the core set. List `src/` and `tests/`.
- Create the `docs/extraction/` tree: `bcs/`, `workflows/`, plus the top-level files seeded as stubs (`README.md`, `glossary.md`, `gaps-and-drift.md`, `lessons.md`, `synthesis.md`, `OPEN-QUESTIONS.md`).
- In `README.md`: state the extraction's purpose and method in a few sentences, define the maturity taxonomy inline (copy it from this prompt), and build a **status table** listing all eight BCs with their verified maturity tag and a one-line purpose, plus rows tracking the planned workflow count, glossary, gap register, lessons, and synthesis. Add navigation links. The README is updated at every subsequent gate.
- Produce the verified BC inventory: all eight BCs, each tagged Implemented / Partial / Scaffolded / Planned-only against `src/`, with the one-line evidence for any non-obvious call.

**Gate / acceptance criteria:**
1. `docs/extraction/` exists with `bcs/`, `workflows/`, and all six seeded top-level files.
2. `README.md` contains the maturity taxonomy, a status table covering all eight BCs with verified tags, and navigation links.
3. Every BC's maturity tag is justified by a cited `src/` observation (presence or absence of a project is sufficient at this stage).
4. No file under `src/` or any pre-existing doc was modified.

### Phase 1 — Per-BC dossiers

**Additions to read (per BC, as you reach it):** that BC's `src/CritterBids.<BC>/` files (note the flat, concept-named vertical-slice layout — aggregates, commands, handlers, sagas, `*Status` saga-state types, projections, module registration, identity helpers all sit as sibling files), its `tests/` project, and the most relevant ADRs.

**Do:** one dossier per BC under `docs/extraction/bcs/<bc>.md`.

For **Implemented / Partial / Scaffolded** BCs, each dossier covers: business purpose (one paragraph, `@DomainExpert`); maturity tag with evidence; aggregates and their lifecycle; the domain events the BC owns (names, no "Event" suffix per house convention); commands and their handlers; sagas and their state types, correlation keys, and terminal paths; projections and read models; any DCB / boundary-model surface; integration events in and out (cite `CritterBids.Contracts`); storage; and the test-evidenced behaviors. Every section cites paths.

For **Planned-only** BCs, the dossier is deliberately thin: business purpose from the vision doc, the maturity tag, the events and capabilities the vision doc *attributes* to it, and an explicit "declared in `docs/vision/bounded-contexts.md`; no `src/` project as of extraction" note. Do not infer behavior that no code supports; the absence is the finding.

**Gate / acceptance criteria:**
1. Eight dossiers exist under `docs/extraction/bcs/`, one per BC named in the vision doc.
2. Each dossier carries a maturity tag and at least one cited source path.
3. Implemented-BC dossiers enumerate aggregates, events, commands/handlers, sagas (with correlation and terminal paths), projections, and integration in/out, each cited.
4. Planned-only dossiers assert no behavior beyond what the vision doc declares and explicitly note the `src/` absence.
5. No evaluative language in any dossier (no "this works well", no "this should").
6. `README.md` status table updated.

### Phase 2 — Business process traces

**Additions to read:** the dossiers from Phase 1; the integration topology in the vision doc; the saga and handler files that carry each flow; the tests that exercise end-to-end paths.

**Do:** one file per cross-BC business process under `docs/extraction/workflows/<process>.md`. At minimum, trace the flows the system is built around (confirm and adjust against code):
- Seller publishes a listing through to bidding open (Selling → Auctions → Listings).
- A timed listing closing: reserve check, winner declaration, anti-snipe extension, no-sale resolution (Auctions Closing saga → outcome events → Settlement).
- Buy It Now purchase as a terminal path (Auctions → Settlement), noting its relationship to the closing flow.
- Proxy bidding (the per-bidder-per-listing saga, correlation, termination).
- Post-sale obligations and notification fan-out (Settlement → Obligations → Relay/Operations) — expected to trace into Planned-only territory; mark where the trace leaves implemented code.
- The flash-session container flow, if present in code.

Each trace: the triggering event, the ordered hops across BCs with the event/command at each hop, the outcome(s), and a **maturity tag for the process as a whole** (fully wired in code / partially wired / aspirational where it crosses into Planned-only BCs). Distinguish the mechanical close fact from the business outcome events where the code does. Cite the saga, handler, and contract files at each hop; cite tests for the paths they cover.

**Gate / acceptance criteria:**
1. Each identified cross-BC process has a trace file.
2. Each trace names the triggering event, the per-hop event/command, and the outcome(s), with citations.
3. Each trace carries a process-level maturity tag, and the exact hop where a trace crosses from implemented into planned-only code is marked.
4. Traces are descriptive only.
5. `README.md` status table updated with the workflow count.

### Phase 3 — Ubiquitous-language glossary

**Additions to read:** `.github/copilot-instructions.md` vocabulary section, `docs/vision/domain-events.md`, the dossiers, and the code where terms surface.

**Do:** `docs/extraction/glossary.md`. One entry per domain term. Each entry: the term, its definition in the auction domain, and where it is honored in code (cited) — and, where applicable, where code or docs drift from it (cross-referenced to the gap register, Phase 4). Seed from the known vocabulary (Listing, Sale / Flash Session, Starting Bid, Reserve, Hammer Price, Final Value Fee, Extended Bidding, BidderId, ListingSold, ListingPassed) and the events list, then sweep the code for terms the docs missed. Record house naming rules as glossary facts where they are linguistic (the "paddle" prohibition; `BidderId` as the participant identifier; the "no Event suffix" event-naming rule; the deliberate `BiddingClosed` vs. `ListingSold`/`ListingPassed` distinction).

**Gate / acceptance criteria:**
1. `glossary.md` exists, alphabetized or sensibly grouped.
2. Each term has a definition and at least one cited code or doc location, or is flagged as declared-but-unused.
3. Linguistic conventions are captured as entries.
4. Drift between a term's intended and actual usage is cross-referenced to the gap register, not evaluated in place.

### Phase 4 — Gap and drift register

**Additions to read:** `.github/copilot-instructions.md` in full against `CLAUDE.md` and the ADR ledger; the dossiers and traces; `CritterBids.Contracts`.

**Do:** `docs/extraction/gaps-and-drift.md`, organized by the three classes below. Each entry is a small table or fixed-shape block: **the claim, its source (cited), the reality, its source (cited), the classification.** Strictly factual — drift is an observed fact, not a judgment.

- **Doc-vs-code drift:** where an authoritative-looking doc contradicts code or a superseding ADR. (Known starting points to verify, not assume: `.github/copilot-instructions.md` still naming Polecat/SQL Server for some BCs and a blanket UUID v5 / `[Authorize]` stance, against ADR 011's all-Marten pivot, ADR 007's UUID v7-primary strategy, and the `[AllowAnonymous]`-through-M6 stance in `CLAUDE.md`. Also reconcile the vision doc's confident present-tense prose about Planned-only BCs against their `src/` absence.)
- **Declared-but-not-built:** capabilities or whole BCs the docs describe that have no code.
- **Declared-but-not-wired:** events, commands, or integration messages that are named (in `Contracts`, vision doc, or a dossier) but have no emitter, no consumer, or no registration.

**Gate / acceptance criteria:**
1. `gaps-and-drift.md` exists with the three classes as distinct sections.
2. Every entry cites both the claim source and the reality source.
3. Every entry has a classification; none editorializes beyond classification.
4. The known `.github/copilot-instructions.md` divergences are present (or explicitly confirmed resolved, with the resolving source cited).

### Phase 5 — Lessons learned (the evaluative artifact)

**Additions to read:** every retrospective under `docs/retrospectives/` (focus on the "Key learnings" and "Findings against narrative" sections); the ADR superseded chains in `docs/decisions/README.md` and the bodies of the ADRs in those chains; the gap register from Phase 4.

**Do:** `docs/extraction/lessons.md`. This is the **only** artifact permitted to evaluate. Each lesson: a short statement of what happened, the evidence (cite the retro, ADR, or gap-register entry), and the generalizable insight. Mine especially:
- The storage saga: named Marten stores (ADR 008) → shared primary store (ADR 009) after the ancillary-store API was found to omit Wolverine registrations → the Wolverine dual-store conflict that blocked Aspire startup (ADR 010) → the all-Marten pivot that eliminated the scenario (ADR 011, superseding ADR 003). This is the richest single learning arc in the project.
- The UUID strategy convergence (ADR 007, amended across multiple dates) and any tension between the v7-primary doc stance and v5 usage in code.
- Methodology decisions: spec-anchored development (ADR 016), the design-phase workflow sequence (ADR 017), declining executable specs / Reqnroll (ADR 018), and the Saga-vs-Process-Managers-via-Handlers choice for Settlement (ADR 019).
- Recurring "Key learnings" themes across retros (handler shapes, saga terminal-path discipline, test-fixture cross-BC isolation, outbox/transaction policy placement, etc.).

Lessons may say "what was hard" and "what we'd weigh differently," framed as a lesson. Lessons **may not** prescribe the rebuild or propose a new design; "the rebuild should X" is out of bounds. Keep insights generalizable — do not restate item-level dossier detail.

**Gate / acceptance criteria:**
1. `lessons.md` exists; each lesson has statement, cited evidence, and a generalizable insight.
2. The storage-decision arc (008 → 009 → 010 → 011) is captured as a lesson.
3. No lesson prescribes the downstream rebuild or proposes new architecture.
4. No descriptive artifact (dossier, trace, glossary, gap register) absorbed any of this evaluative content.

### Phase 6 — Synthesis and close

**Additions to read:** all extraction artifacts produced in Phases 1–5.

**Do:** `docs/extraction/synthesis.md` — a standalone, cold-readable picture a newcomer can absorb without reading the dossiers. It states: what CritterBids is as a business; the eight-BC map grouped functionally, each with its maturity tag and one-line purpose; the spine processes and how they relate; the recurring structural patterns (event-sourced aggregates, saga shapes, projection-first read models, integration-via-Contracts, DCB on the contended path); and an honest one-paragraph "state of the system" that names what is real versus declared. It introduces no new facts — everything traces to an artifact it cites. Then finalize `README.md` (status table complete, all artifacts linked) and fold any straggler items into `OPEN-QUESTIONS.md`.

**Gate / acceptance criteria:**
1. `synthesis.md` exists and is internally consistent; every claim traces to a cited artifact.
2. The BC map covers all eight BCs with maturity tags.
3. A newcomer reading only the synthesis forms an accurate mental model, including of what is not yet built.
4. No section evaluates, and none references or anticipates the downstream rebuild.
5. `README.md` is finalized; `OPEN-QUESTIONS.md` reflects everything escalated across the run.

---

## Output artifact tree

```
docs/extraction/
  README.md              ← purpose, method, maturity taxonomy, status table, nav
  bcs/
    participants.md
    selling.md
    auctions.md
    listings.md
    settlement.md
    obligations.md        ← Planned-only (thin)
    relay.md              ← Planned-only (thin)
    operations.md         ← Planned-only (thin)
  workflows/
    <one file per cross-BC process>
  glossary.md
  gaps-and-drift.md
  lessons.md              ← the only evaluative artifact
  synthesis.md
  OPEN-QUESTIONS.md
```

(Confirm the BC filenames and the implemented/planned split against `src/` at Phase 0; the split above reflects the repo as understood at authoring and may have moved.)

---

## Explicitly out of scope

- Any change to `src/` or `tests/`. This is a documentation-only effort.
- Editing or "fixing" any pre-existing doc — including the stale `.github/copilot-instructions.md`. Drift is recorded in the gap register, never repaired here.
- Authoring ADRs, narratives, workshops, or skill files.
- Designing, naming, referencing, or anticipating the downstream rebuild methodology or any specification format. The corpus is methodology-neutral.
- Producing specifications of any kind. This session describes; it does not specify.
- Cross-project analysis. CritterSupply and CritterCab stay out (the existing analogue table in the vision doc may be acknowledged, not expanded).
- Inferring behavior for Planned-only BCs beyond what the vision doc declares.

---

## Working pattern

- Phases are sequential and gated. Commit at every gate with a message naming the phase and artifacts. One coherent commit per phase; do not interleave phases.
- Declare active personas at the start of each phase and label their contributions. Let them disagree; record unresolved disagreement as an open question rather than papering over it.
- When code and docs conflict: trust the code, record the conflict in the gap register, keep moving.
- When something is genuinely ambiguous: append it to the artifact's open-questions note and to `OPEN-QUESTIONS.md`, and continue. Never guess at architecture or resolve a design decision.
- Keep the descriptive and evaluative registers strictly separated at all times. If a sentence in a dossier starts to judge, it belongs in `lessons.md`.

## Hand-off

On completion, the deliverable is the populated `docs/extraction/` tree and a finalized `README.md` whose status table shows every artifact complete. `OPEN-QUESTIONS.md` is the single list of everything the extraction could not resolve from code — that list is the natural first input to whatever comes next. This prompt's session closes with a retrospective under `docs/retrospectives/` per house convention, capturing what the extraction surfaced about the project and about the extraction method itself.
```
