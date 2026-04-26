# ADR 017: Design-Phase Workflow Sequence

**Status:** Accepted
**Date:** 2026-04-26

---

## Context

CritterBids has shipped four milestones across four bounded contexts (Participants, Selling, Auctions, partial Listings). The remaining four BCs (Settlement, Obligations, Relay, Operations) are mapped in `docs/vision/bounded-contexts.md` but unbuilt. The project also has subsequent feature work pending inside the shipped BCs (Listings catalog extensions, additional Selling commands, Auctions saga refinements). Design techniques the project has committed to include Event Modeling as the primary design tool (codified in `docs/skills/event-modeling/SKILL.md`), DDD strategic design, and the NDD-informed narrative layer introduced by ADR 016.

What this ADR decides is the *sequence* in which those techniques apply for the remaining BCs and for substantial feature work in shipped BCs. The four lived BCs went through an implicit, ad-hoc ordering: workshops were authored (Workshop 001 for Flash session journey, 002-004 for Auctions/Settlement/Selling deep dives), prompts were written, code was implemented, retrospectives closed each session. Two design steps that this ADR codifies as preliminaries (Context Mapping and Domain Storytelling) were not performed before the lived BCs' workshops. That is a known cost; this ADR makes it visible rather than hiding it.

Without a defined sequence going forward, two failure modes are likely to repeat. The first is the one the lived BCs have partially demonstrated: linguistic disagreements and boundary ambiguities surface inside the Event Modeling workshop where rework is expensive, rather than before it where rework is cheap. The second is the converse: the design phase for a new BC expands without convergence, the workshop is repeated, and the BC never reaches code because the model is never "done enough."

A defined sequence addresses both by establishing what happens in what order, what each step's output is, and when a step is finished enough to move to the next. It also makes the feedback path explicit: the sequence is not a waterfall, and the right mechanism for updating upstream artifacts is the retrospective (per ADR 016), not a new design session.

## Options Considered

### Option A: Continue the lived ad-hoc ordering

Future BCs follow the same implicit ordering the lived BCs did. A workshop is authored when the BC's turn comes; prompts and code follow; retrospectives close each session; Context Mapping and Domain Storytelling remain absent.

This is the path of lowest immediate friction. It requires no new methodology adoption and matches the rhythm contributors already know. The cost is what the lived BCs have already demonstrated: linguistic disagreements about words like "Listing" (a Selling draft, a Listings catalog row, an Auctions bidding stream) and "Bidder" (a Participants identity, an Auctions actor) are surfaced inside the Event Modeling workshop or, worse, after implementation begins. Naming inconsistencies surface in retrospective and get patched piecemeal across PRs rather than resolved upfront.

For the four shipped BCs, this cost has been absorbed and the system works. For the four remaining BCs, repeating the same ordering means repeating the same cost. Settlement in particular has cross-cutting linguistic overlap with Selling (sale price vs hammer price vs settlement amount) and Participants (seller vs payee) that would benefit from being surfaced before workshop authoring rather than during it.

### Option B: Event Modeling only, without preliminary strategic design

The Event Modeling workshop remains the first structured activity for each new BC. Context Mapping and Domain Storytelling are omitted. The workshop's swim-lane step (Apply Conway's Law) and brain-dump step surface bounded-context concerns and language choices as side effects.

Event Modeling is capable of surfacing these concerns: the swim-lane step is where module boundaries are reaffirmed, and the brain dump forces vocabulary decisions. The limitation is that discovering linguistic disagreements during an Event Modeling session is expensive. A naming dispute that surfaces in Step 2 (plot formulation) when "settlement" turns out to mean different things to the seller-payee flow, the buyer-charge flow, and the Operations reconciliation flow stalls the workshop and requires re-work on stickies that have already been placed.

For CritterBids, the modular monolith structure (ADR 001) means the boundary decisions are already partly settled: the eight BCs are named in `docs/vision/bounded-contexts.md`. So the full strategic-design value of Context Mapping is reduced compared to a project that is also choosing its module decomposition. What remains valuable is the *relationship* mapping (upstream/downstream, anti-corruption layers, conformist relationships), which is not redundant with the BC list. Domain Storytelling's vocabulary-stabilization value is not reduced by the BC list being settled, because the disagreements are within and across the listed BCs.

### Option C: Staged sequence with feedback loop

Design activities are ordered to minimize rework. Each step's output is the input to the next. The sequence is linear for the initial pass per BC; retrospectives provide the feedback mechanism for updating any upstream artifact when implementation reveals a gap.

The sequence:

1. **Context Mapping.** Name the upstream/downstream relationships between this BC and the others. Identify anti-corruption layers, published languages, and conformist relationships. For Settlement, this means clarifying which Selling and Auctions events Settlement consumes and what Participants vocabulary it adopts vs translates. Output: a context-map artifact in `docs/workshops/` that informs the BC's Event Modeling swim lanes.
2. **Domain Storytelling.** Surface language boundaries by telling domain stories with actors, work objects, and activities. Where the same word means different things to different actors, Domain Storytelling surfaces the disagreement without requiring a fully populated Event Model. For Settlement, the actors are seller, buyer, payment processor, accounting system; the work objects are the listing, the bid, the settlement record; the activity disagreement to surface is what "settled" means at each step. Output: stable, BC-specific vocabulary feeding into the Event Modeling workshop's Ubiquitous Language section.
3. **Event Modeling.** The primary design tool. A multi-session workshop producing events, commands, views, swim lanes, slices, and GWT scenarios per `docs/skills/event-modeling/SKILL.md`. Swim lanes are drawn with the benefit of Context Mapping's relationship clarity and Domain Storytelling's stable vocabulary. Output: a workshop artifact in `docs/workshops/` and a slice backlog.
4. **NDD-Informed Narratives.** Journey-scoped domain specs authored from the Event Modeling slice output. Each narrative spans multiple slices and captures a single protagonist's perspective across the full journey. Captured in `docs/narratives/`. Output: durable, journey-scoped specifications per ADR 016 and `docs/narratives/README.md`.
5. **Prompt Authoring.** Task-scoped build orders referencing narratives, workshops, and skill files. One slice per prompt, one prompt per session. Output: a prompt document in `docs/prompts/implementations/` that drives a specific implementation session.
6. **Implementation and Retrospective.** Code produced in session, closed with a retrospective. The retrospective asks whether the slice's implementation revealed anything that should update the Event Model, the narrative, the workshop, or the context map. If yes, those updates are part of the same PR (per ADR 016). Output: committed code in `src/`, a retrospective in `docs/retrospectives/`, and any upstream artifact updates the session surfaced.

## Decision

**Option C.** CritterBids' design-phase work follows the staged sequence above for future bounded contexts and for substantial feature work in shipped bounded contexts.

The sequence is the *initial* order of operations per BC, not a waterfall. Steps 1 to 3 run once before the first line of new code in a BC. Steps 4 to 6 iterate: each slice is a loop through narrative → prompt → implementation → retrospective. Retrospectives are the feedback mechanism (per ADR 016) and may update any upstream artifact (a narrative, an Event Model entry, a Domain Storytelling artifact, or even a context map boundary) when the implementation reveals something the design did not anticipate.

**For the four shipped BCs (Participants, Selling, Auctions, Listings), Steps 1 and 2 are not retroactively performed.** That cost is absorbed. Phase 3 of the foundation refresh adds retroactive Cast/Setting/Ubiquitous-Language sections to the existing workshops as a partial substitute, but this ADR does not require new Context Mapping or Domain Storytelling sessions for the lived BCs. The workshops authored before this ADR are the substantive design artifacts for those BCs.

**For the four remaining BCs (Settlement, Obligations, Relay, Operations), whether Steps 1 and 2 run is a per-BC decision deferred to that BC's design-phase opening session.** The ADR makes Context Mapping and Domain Storytelling *available* as named steps; whether each is exercised per BC depends on the BC's specific linguistic and boundary risk profile, judged at the time. Settlement is the strongest candidate (cross-BC vocabulary overlap with Selling, Auctions, Participants); Relay is the weakest (its purpose is technical signal-relay rather than domain-rich, and its vocabulary is largely inherited from the BCs whose events it relays).

## Consequences

### Context Mapping and Domain Storytelling are named steps, not universal prerequisites

For shipped BCs, neither step is required retroactively. For new BCs, both are named and available; whether each runs is decided at the BC's design opening. The ADR's role is to make them legible as options rather than to mandate them universally. Where a BC declines either step, the decision and rationale belong in that BC's first workshop artifact.

### The narrative layer is now a sequenced step, not an optional addition

Prior to ADR 016, narratives did not exist. The introduction of `docs/narratives/` makes Step 4 a real artifact rather than a placeholder. For lived BCs, narratives are authored opportunistically rather than on a Step 4 schedule (the foundation refresh's Phase 2 authors the first narrative against M3 lived code as a proving ground). For new BCs, Step 4 produces narratives before prompts are written for the slice they implement.

### Each step's output is a committed artifact

A step is finished enough to move forward when its artifact is committed, not when the discussion feels complete. Context maps and Domain Storytelling outputs land in `docs/workshops/`. Event Models land in `docs/workshops/`. Narratives land in `docs/narratives/`. Prompts land in `docs/prompts/implementations/` (per Phase 1 Item 5's subdivision). Retrospectives land in `docs/retrospectives/`.

### The modular monolith decomposition (ADR 001) is upstream, not downstream

For projects where module decomposition is itself a design output, the equivalent service-topology decision is *downstream* of Event Modeling's swim-lane step. For CritterBids, the modular monolith decomposition predates this ADR (ADR 001). The eight BCs are already named. This ADR does not revisit module decomposition; it sequences the design work *within* an already-decomposed system. Should a future BC's Event Modeling workshop reveal that the original BC boundaries are wrong (a slice clearly belongs in a different BC than its workshop assigned it), that finding routes through the standard ADR 016 retrospective path.

### Retrospectives take on a sequence-feedback role in addition to their existing roles

Per ADR 016, retrospectives already audit spec ↔ code drift. This ADR adds: retrospectives also audit *sequence* drift. If a slice's implementation reveals that Step 4's narrative misjudged a boundary, the retrospective updates the narrative; if it reveals the workshop's vocabulary was stale, the retrospective updates the workshop. The PR closes the loop in the same commit.

### Future supersession trigger

Adopting a different design-phase methodology (for example, fully replacing Event Modeling with DDD tactical patterns as the primary design tool, or adopting a spec-as-source workflow that subsumes Steps 4-6) would supersede this ADR. The trigger for that reconsideration would be either evidence from lived BCs that the staged sequence is producing artifacts that don't earn their cost, or the emergence of tooling that changes the cost-benefit analysis (the same trigger ADR 016 names for its own supersession).

## References

- ADR 016 (Spec-Anchored Development): names the authority relationship between specs and code; this ADR sequences the work that produces those specs
- ADR 001 (Modular Monolith Architecture): names the eight BCs whose design work this sequence governs
- `docs/skills/event-modeling/SKILL.md`: the operational manual for Step 3
- `docs/narratives/README.md` (created in foundation refresh Phase 1, Item 3): the operational manual for Step 4
- `docs/prompts/README.md` (rewritten in foundation refresh Phase 1, Item 5): the operational manual for Step 5
- `docs/retrospectives/README.md`: the operational manual for Step 6
- CritterCab ADR-004 (source for this ADR's structure, adapted for CritterBids' modular monolith and lived-code situation)
