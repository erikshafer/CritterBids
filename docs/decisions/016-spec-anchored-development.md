# ADR 016: Spec-Anchored Development

**Status:** Accepted
**Date:** 2026-04-25

---

## Context

CritterBids commits to a structured design methodology. An Event Modeling workshop produces a blueprint per bounded context. NDD-informed narratives in `docs/narratives/` (introduced by this foundation refresh) distill those blueprints into journey-scoped specifications. Prompt documents in `docs/prompts/` drive implementation sessions from those narratives. Skill files in `docs/skills/` carry the patterns that any given session draws on. The methodology itself is not in question. What requires a decision is the *authority relationship* between the specifications the methodology produces and the code that implements them.

That question matters more in 2026 than it would have in 2024. Spec-Driven Development matured from a framing into a product category over the prior eighteen months. Tools like Amazon Kiro, Xolvio's Auto platform, and prooph board's code-generation pipeline implement what the SDD literature calls *spec-as-source*: the specification is the authoritative artifact, and code is derived from it. A change to the spec causes the agent to regenerate or patch the code. The spec and the code are never out of sync because the code is always downstream. This is a different contract than the one BDD, TDD, or Event Modeling have historically offered.

The choice of authority relationship has structural consequences. It determines what tooling is required, what the update cycle looks like when implementation reveals a modeling gap, and how contributors (human or AI) should behave when spec and code disagree.

CritterBids enters this decision having already shipped four milestones (Participants, Selling, Auctions, partial Listings) under an implicit *spec-free* posture. Workshops were authored, prompts were written, code was implemented, retrospectives closed each session. Drift between workshop intent and shipped behavior was caught at retrospective time and corrected when noticed, but no persistent narrative artifact sat between the workshops and the code to make that drift mechanically visible. The foundation refresh introduces that artifact; this ADR names the relationship the artifact has with the code.

## Options Considered

### Option A: Spec-free

Implementation proceeds directly from Event Modeling workshops, prompt documents, and skill files. No persistent narrative layer sits between workshop artifacts and code. Workshops are produced once, consulted during development, and allowed to drift as the system evolves. Drift is caught, if at all, by individual reviewers noticing it.

This is the approach CritterBids has taken through M1 to M4. It is low-overhead and does not require maintaining a parallel specification layer. For a short-horizon project where the original contributors remain engaged and the scope is stable, it works.

For CritterBids specifically, the project's purpose as both a conference demo vehicle and a reference architecture for idiomatic Critter Stack development requires that contributors arriving cold can derive system intent without re-running the original sessions. The retrospectives carry session-specific learnings, but they are not specifications: a retrospective for M3-S5b explains what was decided that session, not what the system as a whole is supposed to do. Continued spec-free operation would let workshop ↔ code drift accumulate silently across additional milestones.

### Option B: Spec-as-source

The Event Model and narratives become authoritative. Code is generated or regenerated from them by an agent. When the spec changes, the agent patches the implementation. Spec and code are kept in sync by the toolchain, not by discipline.

This is the model commercial SDD platforms implement. The appeal is real: the gap between "design is done" and "implementation is done" narrows, and the gap between "spec was updated" and "code reflects the update" disappears. For a team producing many slices at fast cadence, the productivity upside is substantial.

The costs are also real and specific to CritterBids' situation. Spec-as-source requires a platform (commercial product or purpose-built agent pipeline) that understands the specification format, can interpret it, and can generate and patch Critter Stack code from it. No such platform exists for the Critter Stack today. Building one to support CritterBids' development would make the toolchain a project deliverable, distorting the project's actual purpose. Commercial platforms impose a format and an ecosystem dependency that CritterBids' open-source reference-architecture mission does not justify: contributors should not need to license a commercial tool to understand or extend the project.

There is a second, CritterBids-specific cost. Spec-as-source assumes a clean-slate authoring posture or a fully-converted existing codebase. CritterBids has four milestones of lived code that no spec authored today can claim to have generated. Adopting spec-as-source would either (a) make the spec a fiction that pretends to have produced code it did not, or (b) require regenerating the existing code to match a new spec, which is a refactor at a scale this foundation refresh explicitly excludes.

### Option C: Spec-anchored

The Event Model and NDD-informed narratives are the architectural reference. They describe intent, domain behavior, and the reasoning behind design choices. Code is authoritative for runtime behavior: the code does what it does, regardless of what a narrative says. Drift between spec and code is detected at retrospective time, not by automated tooling. When drift is detected, spec or code is updated (whichever is wrong) and committed in the same PR as the retrospective.

Spec-anchored is distinct from spec-first in one critical way: the specifications stay current. A spec-first document is written before coding begins and then abandoned; it is a snapshot of intent at one moment, accurate at that moment, stale afterward. A spec-anchored document is a living artifact maintained in lockstep with the code, via disciplined retrospective review, for the life of the project.

This approach requires no external platform beyond what CritterBids already uses: git, markdown, Wolverine, Marten, and a consistent retrospective habit. It is also the approach that makes the project's documentation actually useful to contributors, because the narratives reflect the current state of the system rather than the state it was in when the session that produced them ran.

For CritterBids, Option C also has a property the other two options lack: it accommodates lived code without lying about its provenance. The narratives authored from this foundation refresh forward describe the system as it is, with the same authority the workshops and skill files already have. Where the narratives expose drift between the model and the implementation, the findings discipline routes the drift to whichever artifact is wrong.

## Decision

**Option C.** CritterBids uses spec-anchored development. The Event Model produced by workshops and the NDD-informed narratives in `docs/narratives/` are the architectural reference. Code in `src/` is authoritative for runtime behavior. When the two disagree after a session, the retrospective for that session identifies the disagreement, and the correct artifact is updated in the same PR that closes the session.

The sync mechanism is explicit retrospective review, not automated derivation. Every retrospective closes with the question: *did this slice's implementation teach us anything that should update the workshop or the narrative?* If yes, that update is part of the session's PR, not a follow-up.

CritterBids does not adopt spec-as-source commercial platforms. This is a deliberate non-adoption: the project's open-source reference-architecture purpose requires that contributors be able to understand, run, and extend the project without licensing external tooling.

## Consequences

### The narrative layer joins workshops and skills as a first-class deliverable

Files under `docs/narratives/` carry the same maintenance expectations as code in `src/`. A narrative that has drifted from the implementation is a defect, caught at retrospective time. The narrative directory is created in the foundation refresh's Phase 1 (Item 3); the first narrative is authored in Phase 2.

### AI-assisted sessions load the relevant narrative before generating implementation

When generated code diverges from what a narrative specifies, the divergence is surfaced in the retrospective and resolved: either by correcting the code or by updating the narrative to reflect what was learned. The retrospective is where "the model was wrong" becomes "the model is updated" rather than "the model is ignored."

### Retrospective discipline is mandatory

A session that closes without a retrospective leaves the spec ↔ code relationship unaudited. The retrospective is part of the session's definition of done; the PR that contains the implementation contains the retrospective. CritterBids has practiced this from M1; this ADR codifies the *purpose* of the practice rather than instituting a new one.

### This ADR applies to lived code retroactively

CritterBids shipped four milestones (Participants, Selling, Auctions, partial Listings) before this ADR existed. The narrative layer did not exist when M1 to M4 were authored. The first narrative session (foundation refresh Phase 2) authors a narrative against lived code and surfaces drift between the workshop layer, the narrative layer (newly authored), and the implementation.

Each drift item routes via the **findings discipline** into one of four lanes:

- **`narrative-update`**: code and workshop are right; narrative renders what is actually true. Resolved in the narrative session's PR.
- **`workshop-update`**: workshop is stale (event renamed, payload grew, slice intent shifted). Resolved in the narrative session's PR by editing the workshop directly.
- **`code-update`**: code is wrong relative to domain understanding. Becomes a follow-up implementation prompt under `docs/prompts/implementations/`. Not resolved in the narrative session's PR.
- **`document-as-intentional`**: code and workshop are both right; the apparent disagreement is two valid expressions. Document the relationship and move on.

This ADR's authority does not retroactively void code that predates it. It makes the future relationship between specs and code explicit and provides the routing for the inevitable initial drift. Phase 2.5 of the foundation refresh absorbs `code-update` findings as standard implementation slices with their own prompts and retrospectives.

### Future supersession trigger

Adopting a spec-as-source workflow in the future would supersede this ADR. The trigger for that reconsideration would be the emergence of a Critter-Stack-aware code-generation platform with permissive licensing and operational maturity sufficient to host a reference-architecture project. At that point, the balance of costs shifts.

## References

- `docs/narratives/README.md` (created in foundation refresh Phase 1, Item 3): operational manual for the narrative format
- `docs/prompts/foundation/foundation-refresh-handoff.md` (relocated in Phase 1, Item 5): the multi-phase plan introducing this ADR
- `docs/decisions/017-design-phase-workflow-sequence.md` (paired with this ADR in Phase 1, Item 2): the staged sequence specs and code travel through
- CritterCab ADR-003 (source for this ADR's structure, adapted for the auction domain)
