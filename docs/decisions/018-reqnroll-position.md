# ADR 018: Reqnroll Position

**Status:** Accepted
**Date:** 2026-04-27

---

## Context

Workshop scenarios in CritterBids are prose-formatted Given/When/Then blocks in markdown. They live in companion files (`001-scenarios.md` through `004-scenarios.md`) alongside the workshops they belong to. Implementation prompts cite them by name: M3-S5b's prompt references `002-scenarios.md` §3 rows 3.5 through 3.11; M3-S4's prompt references W002 scenarios 1.5 through 1.15. The linkage between workshop scenarios and tests is by convention - prompts say which scenarios apply, retrospectives confirm they were exercised, and reviewers verify the linkage during PR review.

A reasonable question is whether to mechanize that linkage with executable specifications. Reqnroll (the .NET-native open-source fork of the now-deprecated SpecFlow) is the natural candidate. Reqnroll converts `.feature` files into generated test fixtures that execute step bindings in C#. The appeal is mechanical: a workshop scenario that updates causes the corresponding test to update; a step binding that breaks fails the build.

The question is more acute in 2026 than it would have been in 2024. SpecFlow was deprecated in 2024; Reqnroll emerged as its open-source successor and the .NET community is still evaluating Reqnroll as the long-term home for executable BDD. Adopting it as part of the foundation refresh would commit CritterBids to a tooling layer whose long-term maintenance posture is itself still solidifying.

CritterBids enters this decision having shipped four milestones under the convention-based discipline. Phase 2's narrative session produced 12 findings; 5 of them landed in the `workshop-update` lane (workshop scenarios stale relative to lived code). Phase 2.5 closed Finding 011, where scenario 1.11's test assertion had been authored by reading the implementation rather than the workshop scenario - a failure mode adjacent to what mechanization is meant to address. Both data points inform the question.

## Options Considered

### Option 1: Adopt Reqnroll

Workshop scenarios are exported (or directly authored) as `.feature` files. Reqnroll generates test fixtures from them; step bindings in C# implement the Given/When/Then steps. The CI pipeline runs the generated tests. A scenario that updates causes a regenerated test; a step binding that breaks fails the build.

The appeal is real. The five `workshop-update` findings from Phase 2 would likely have surfaced as build failures or as `.feature` diff PRs earlier, possibly without the narrative-session audit step.

The costs are also real and CritterBids-specific. Reqnroll requires test-infrastructure work (NuGet integration, generator config, IDE tooling), a new skill file documenting CritterBids' Reqnroll patterns, and a migration of the existing prose scenarios to `.feature` format (cumulatively 100+ scenarios across W001-W004). Reqnroll itself is in a transitional period; adopting it commits to tracking a tool whose long-term maintenance posture is still in flux. The Critter Stack's testing patterns (Alba + Testcontainers + xUnit + Shouldly) work well without Reqnroll; introducing Reqnroll adds a parallel test-execution path with its own runner and reporting.

The Critter Stack does not produce reference projects that demonstrate Reqnroll integration. CritterBids would be authoring those patterns from scratch, making the project's test infrastructure a first-of-its-kind reference for a feature it does not need for its core mission as an event-sourcing reference architecture.

### Option 2: Workshop scenarios authored as `.feature` files directly

The workshop is the `.feature` file; no parallel prose-scenario document. Workshops gain Reqnroll dependency but lose the natural-language editorial flexibility of markdown. Phase introduction prose, condensed phase summaries, and parked-question tables would either move to a parallel markdown file or sit awkwardly inside `.feature` comment blocks.

This option has the same costs as Option 1 plus a workshop-format change. The workshop format is a deliberate artifact (W001-W004 exercise it across journey-grain and BC-grain workshops, with three Phases of condensed prose plus a Phase 5 scenarios block). Forcing the workshop into a `.feature`-shaped container loses fidelity for marginal mechanical benefit.

### Option 3: Parallel-source

Workshop scenarios live as both prose markdown and `.feature` files, kept in sync by discipline. The `.feature` files generate executable tests; the markdown scenarios remain the human-readable canonical source.

Parallel-source has all the costs of Option 1 plus a new failure mode: the two sources can drift from each other. Mechanical sync (a `.feature` generator from markdown, or a markdown generator from `.feature`) defeats the point of either format being canonical. Hand-maintained sync requires reviewer discipline equal to the convention-based linkage being mechanized away.

### Option 4: Decline executable specs

The convention-based linkage between workshop scenarios and tests is the discipline going forward. Workshop scenarios remain prose Given/When/Then in markdown; tests cite scenarios by name and number; retrospectives verify the linkage; the narrative-vs-code audit catches workshop-scenario drift; spec-anchored development (ADR-016) routes the drift via the four finding lanes.

This is the approach CritterBids has practiced through M1-M4. Phase 2's narrative session produced 12 findings; the 5 `workshop-update` findings were caught and resolved in the narrative session's PR (W001 §"Phase 4" view inventory edit, status vocabulary sweep across seven scenario blocks, M3 milestone mapping correction). The discipline does not prevent drift; it catches drift at the narrative-session boundary.

Phase 2.5's Finding 011 is the spec-anchored discipline's strongest proof. The scenario 1.11 test assertion had been authored by reading the implementation rather than the workshop; the narrative audit caught the divergence; the fix re-anchored the test to the workshop formula. Reqnroll would not have prevented this failure mode by itself: it generates tests from scenarios, but the scenario was the canonical answer; the bug was that the test was authored against the implementation rather than the scenario. The discipline of "test assertions anchor to the scenario, not the implementation" (Phase 2.5 Key Learning 1) produces the right test with or without Reqnroll. Mechanization would have moved that discipline from human review to a generator pipeline; the discipline itself is what catches the failure mode.

## Decision

**Option 4.** CritterBids declines executable specifications at MVP. Workshop scenarios remain prose Given/When/Then in markdown. Tests cite scenarios by name and number. Retrospectives verify the linkage. Narrative-vs-code audits catch workshop-scenario drift via the four finding lanes from ADR-016. The convention-based discipline is the contract going forward.

This is a deliberate non-adoption of Reqnroll for the MVP and the foundation-refresh phases. The trigger for revisit: scenario-test drift becomes load-bearing. Specifically, if a single narrative session produces three or more `workshop-update` findings that mechanical generation would have caught earlier, or if the convention-based linkage starts producing PR rework that a `.feature` generator would have prevented, this ADR is reopened.

## Consequences

### The convention-based linkage is the contract

Implementation prompts continue to cite workshop scenarios by name and number. Retrospectives continue to verify that cited scenarios were exercised. Test assertions anchor to the workshop scenario, not the implementation - the discipline named in Phase 2.5 Key Learning 1 ("Test assertions must anchor to the spec, not the implementation") is the operating practice. A future skill file may distill that discipline once the practice surfaces a second time.

### The narrative layer is where workshop-scenario drift surfaces

Phase 2's narrative session produced 5 `workshop-update` findings. Future narrative sessions (Phase 5's four backfill narratives) are expected to surface fewer as the workshop converges on lived code. The drift-detection responsibility lives at the narrative layer; the test layer remains a verifier of the workshop scenarios it cites.

### Reqnroll adoption remains a future option

If the trigger condition fires, this ADR is revisited. Adoption at that point would cascade to (a) a test-infrastructure ADR coordinating Reqnroll with Alba/Testcontainers/xUnit, (b) a new skill file `docs/skills/reqnroll-executable-specs.md` documenting CritterBids' Reqnroll patterns, and (c) a migration session porting W001-W004 scenarios to `.feature` format. The current decision does not foreclose adoption; it defers until the convention-based discipline shows specific failure modes that mechanization would have prevented.

### CritterCab coordination

CritterCab is in a similar pre-decision posture on Reqnroll. CritterBids ADR-018 stands independently; if CritterCab authors a matching ADR, it can backreference this one as prior art. Coordination is one-way until both projects publish matching positions; no requirement for coupled revision.

## References

- `docs/decisions/016-spec-anchored-development.md`: the authority-relationship ADR that names the four finding lanes used to route scenario-test divergence
- `docs/retrospectives/foundation-refresh-phase-2-retrospective.md` §"Findings summary": the 12-finding ledger with 5 `workshop-update` resolutions
- `docs/retrospectives/foundation-refresh-phase-2-5-retrospective.md` §"Key learnings" 1 and 3: the test-anchors-to-spec discipline and misleading-comments lesson
- `docs/skills/critter-stack-testing-patterns.md`: the existing test-pattern skill file (does not currently document Reqnroll; would expand if Option 4 is reversed)
- `docs/prompts/foundation/foundation-refresh-handoff.md` §7.1: the question framing that this ADR resolves
