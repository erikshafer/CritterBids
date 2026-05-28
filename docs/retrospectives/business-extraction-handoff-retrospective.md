# Business Extraction Handoff — Retrospective

**Date:** 2026-05-19
**Prompt:** `docs/prompts/foundation/business-extraction-handoff.md`
**Agent:** Copilot CLI (Claude Opus 4.7, interactive mode)
**Branch:** `erikshafer/business-extraction`
**Phases:** 0 → 6 (sequential, gated, one commit per phase)

This retro records what the business-extraction prompt produced when run end-to-end. It does not anchor to a milestone slice; it anchors to the prompt's six-phase structure.

---

## Baseline

- `docs/extraction/` did not exist at session start.
- ADR ledger sat at 019; 48 retro files existed; eight BCs declared in `docs/vision/bounded-contexts.md`.
- `src/` held five `CritterBids.<BC>` projects: Participants, Selling, Auctions, Listings, Settlement. No projects for Obligations, Relay, Operations.
- The prompt's prime directives (code-wins, descriptive-vs-evaluative split, maturity-tag-everywhere, cite-every-claim, never-edit-`src/`-or-pre-existing-docs) were re-read at every phase boundary.

## Items completed

| Phase | Deliverable | Commit |
|------|---|---|
| 0 | Scaffold (`README.md`, `OPEN-QUESTIONS.md`, `bcs/` + `workflows/` directories with stubs), src inventory, drift candidates list. | `c79e6b6` (scaffold) |
| 1 | Eight BC dossiers under `bcs/`: Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay, Operations. | `ddfad7d` |
| 2 | Six cross-BC workflow traces under `workflows/`: publish-to-bidding-open, timed-listing-close, buy-it-now, proxy-bidding, post-sale-obligations, flash-session. OQ-P2-01 raised. | `84e0fbb` |
| 3 | `glossary.md` — ~60 terms. | `9dce22d` |
| 4 | `gaps-and-drift.md` — 13 doc-vs-code, 14 declared-but-not-built, 9 declared-but-not-wired entries. | `c4a1bab` |
| 5 | `lessons.md` — 14 lessons + "what we'd weigh differently" section. The only evaluative artifact. | `852f697` |
| 6 | `synthesis.md` — cold-readable 15-minute overview; final README pass; OPEN-QUESTIONS closed. | `bf8a185` |

Total: seven commits on `erikshafer/business-extraction`, all `--no-verify`, all without `Co-Authored-By` trailer per `CLAUDE.md:190`.

## What worked

1. **Sequential gated phases stopped scope creep.** Each phase ended with a commit and a README status-table update. The temptation to author lessons-style observations during dossier-writing (Phase 1) was real; the gate discipline routed those thoughts into Phase 5 instead, where they belonged.

2. **Maturity tagging was load-bearing.** Tagging each BC and each workflow step (Implemented / Partial / Scaffolded / Planned-only) gave the corpus a consistent skim-surface. A reader can see, in any artifact, what's real vs declared without cross-referencing `src/`. This is the single highest-value convention from the prompt.

3. **Citing source paths in every structural claim** kept the corpus auditable. Behavioral claims cited tests; structural claims cited source files. When Phase 6 walked the synthesis against its references, almost every claim resolved to a specific file path or section ID without ambiguity.

4. **The OPEN-QUESTIONS register is small.** Only OQ-P2-01 (`IsProxy` plumbing on saga-emitted `PlaceBid`) couldn't be resolved from code alone. Everything else was either reconcilable to source or properly classified as drift. The small register size suggests the prompt's instruction to "escalate ambiguity, don't infer" worked.

5. **Phase 4 (drift register) made Phase 5 (lessons) easier.** Lessons could cite drift-register entries by ID rather than re-arguing the divergence in prose. The two registers compose cleanly.

6. **The descriptive/evaluative split held.** No reviewer-style "this should be refactored" leaked into the dossiers, glossary, or synthesis. Lessons stays evaluative; everything else stays descriptive. The prompt's hard rule paid off.

## What was hard

1. **The vision doc's present-tense framing of unbuilt BCs** required a specific authorial discipline in the Obligations / Relay / Operations dossiers. The temptation was to repeat the vision doc's confident prose; instead each dossier had to lead with a "Status: Planned-only" header and frame every subsequent statement as a vision-doc citation, not a description of behavior. This took longer per dossier than the lived BCs did.

2. **The storage-saga arc (ADR 008 → 009 → 010 → 011)** required reading four ADRs and a session retro to reconstruct. The retro for M2-S3 — which actually performed the API audit that confirmed Polecat had no ancillary-store overload — was the highest-density single source. Lesson #1 leans heavily on that one retro.

3. **Selling between "this is a recurring pattern" (lessons #3, #5, #7) and "this is a one-off implementation detail" (dossier facts).** The rule used: if the pattern shows up in two or more BCs with the same shape, it's a lesson; if it shows up in one BC and is incidental, it stays in the dossier. The threshold worked but required a re-read pass during Phase 5.

4. **Determining whether the OPEN-QUESTIONS register should accumulate or stay small.** The interpretation chosen: the register is for items that **cannot** be resolved from code, not for items that surface during analysis. Most drift items surfaced during analysis but could be properly classified into `gaps-and-drift.md`, so they belong there, not in OPEN-QUESTIONS.

## What would be weighed differently

- **The Phase 0 inventory could have been deeper.** A few facts cited in Phase 1 dossiers (e.g., the number of saga handlers in Auctions) would have been faster to surface during a single Phase 0 sweep than discovering them per-dossier.

- **The README status table should track per-phase commits, not just status.** Linking each row to its commit would make the corpus more navigable from the README alone. (Recorded in this retro instead of patched into the README because the gate is closed.)

- **Lesson #14 ("what we'd weigh differently")** could be split out as its own short artifact rather than living inside `lessons.md`. The lessons file is now ~24KB; the meta-reflection bullets are buried at the end. Future similar extractions could lead with that section.

## Findings against narrative

This session did not anchor to a workshop narrative. The driving prompt is a foundation prompt (`docs/prompts/foundation/`), not a milestone implementation slice. No narrative findings to record.

The extraction itself surfaces drift between vision docs and code (`gaps-and-drift.md` Class 1), but that's the deliverable, not a finding against a narrative.

## Verification checklist

Mirrors the handoff prompt's acceptance criteria:

- [x] `docs/extraction/` directory exists with eight artifacts (README, OPEN-QUESTIONS, glossary, gaps-and-drift, lessons, synthesis, bcs/, workflows/).
- [x] All 8 BCs have dossiers; each carries a maturity tag.
- [x] All 6 enumerated workflows have traces; each step carries a maturity tag.
- [x] Glossary covers vision-doc terms + house-naming-rule terms.
- [x] Drift register has three classes (doc-vs-code, declared-but-not-built, declared-but-not-wired).
- [x] Lessons is the only evaluative artifact; covers the storage-decision arc as a gate criterion.
- [x] Synthesis is cold-readable and introduces no new facts.
- [x] Every claim cites a source path; behavioral claims cite the proving test.
- [x] No `src/` modification, no pre-existing-doc modification.
- [x] All commits use `extraction(phase N): <summary>` format, `--no-verify`, no `Co-Authored-By` trailer.

## What remains / next session should verify

**In scope, deferred to a follow-up:**

- None. The handoff prompt's scope closed at Phase 6. The corpus is delivered.

**Out of scope, tracked elsewhere:**

- Remediation of any drift item in `gaps-and-drift.md`. Drift is recorded as a fact; addressing it is a separate concern not in this extraction's scope.
- Backporting present-tense unbuilt-BC sections of `docs/vision/bounded-contexts.md` to "Status: Planned" framing. This would change pre-existing docs, which the prompt prohibits. Lesson #9 records the observation for whatever process handles vision-doc maintenance.
- A follow-up extraction in 3-6 months would benefit from a "diff since previous extraction" pass — what new BCs landed, what drift closed, what new ADRs reshape the lessons. Out of scope here.

**Next session should verify (if any):**

- If a Phase-N+1 prompt is authored from this corpus (e.g., a successor methodology or rebuild kickoff), it should treat this extraction as input-only — the prompt forbids referencing rebuild methodology from inside this corpus.
- The two unbuilt BCs that are closest to "next built" (Obligations and Relay) have stubs in place via Settlement's outbound queue routes (`relay-settlement-events`, `operations-settlement-events`). Whichever BC ships first will produce drift in the gaps register; the relevant entries (D2.01, D2.02) are pre-tagged to ease that next pass.
