# Foundation Refresh Phase 1: Retrospective

**Date:** 2026-04-26
**Phase:** Phase 1 (Naming and porting) of the foundation refresh
**Prompt:** `docs/prompts/foundation/foundation-refresh-handoff.md` §3
**Branch:** `foundation-refresh/p1-01-adr-016-spec-anchored`

## What landed

Six items, each as its own commit on the Phase 1 branch. The branch is 6 commits ahead of `origin/main` at retrospective time.

| Item | Commit | Files | Description |
|---|---|---|---|
| 1: ADR 016 (Spec-Anchored Development) | `9f518b1` | 3 changed | Lifts CritterCab ADR 003. New ADR plus README ledger row plus first commit of foundation-refresh-handoff prompt itself (was untracked). |
| 2: ADR 017 (Design-Phase Workflow Sequence) | `0473d1e` | 2 changed | Lifts CritterCab ADR 004. New ADR plus README ledger row. Per-BC opt-in for Steps 1-2 (Context Mapping, Domain Storytelling) for future BCs. |
| 3: docs/narratives/ + README | `e8fef96` | 1 new | Lifts CritterCab narratives README v0.1. Bounded frontmatter v1, Guardrail 1 (prose-paragraph Moments), Guardrail 2 (frontmatter vocabulary), seven disposition tags, single-named-protagonist voice. Index empty; Phase 2 authors first narrative. |
| 4: docs/rules/ + structural-constraints.md | `8562b7f` | 2 new | Layer 1 rules distilled from ADRs 001, 002, 007, 008, 009, 010, 011, 012, 013, 016, 017 plus skill files. Guardrail-vs-convention distinction codified. |
| 5: prompts/ subdivision + AUTHORING split | `29b4b0e` | 61 changed | 28 file moves into `implementations/` and `foundation/` subdirs. Cross-reference bulk update across retros, milestones, and post-move prompts. README rewritten to lean index-shape; ten rules + template moved to `AUTHORING.md`. |
| 6: research/methodology-log.md intro | `13ca2a2` | 1 new | Lifts CritterCab methodology-log v0.1 intro. Time-box adapted to "close of Phase 2 or after third entry, whichever comes first." Entries empty until Phase 2 close. |

## Items folded in beyond the original six

**Pre-Item 1: §4.5 staleness fix on the foundation-refresh-handoff prompt itself.** Caught at session start: the prompt's §4.5 said "M3-S6 is queued, not built," but the M3-S6 retrospective already existed in `docs/retrospectives/`. M4-S1 and M4-S2 had also shipped between the prompt's authoring (2026-04-25) and execution. Folded into Item 1's commit with a paragraph note dated 2026-04-25 at execution start.

**During Item 5: AUTHORING.md as a sibling to README.** The original §3.5 directive was "rewrite README to mirror CritterCab's lean index-shape." The pre-existing CritterBids README carried valuable content (the ten rules of prompt authoring, the implementation-prompt template skeleton) that a strict lean rewrite would have lost. Resolved by extracting that content to a new `docs/prompts/AUTHORING.md` sibling and pointing the lean README at it. User-approved mid-execution.

**During Item 4: self-referential em-dash exception.** The rule prohibiting em dashes in committed prose had to describe the character it was prohibiting. Resolved by referring to the em dash by Unicode codepoint (`U+2014`) instead of displaying the literal glyph. Small but principled.

## Phase 2 narrative target

**Confirmed default:** Flash demo bidder happy-path against W001 (`docs/workshops/001-flash-session-demo-day-journey.md` + `docs/workshops/001-scenarios.md`), single-bidder perspective.

**Adjustment from §4.5 staleness fix:** M3-S6, M4-S1, and M4-S2 shipped between the foundation-refresh prompt's authoring and Phase 1's execution. The narrative now audits lived M3-S6 / M4-S1 / M4-S2 code alongside M3-S5b, rather than being authored before M3-S6 runs. The findings discipline (four lanes: `narrative-update` / `workshop-update` / `code-update` / `document-as-intentional`) applies to all of M3 and the shipped M4 work.

## Open questions surfaced during Phase 1

None warrant their own ADR before Phase 2 starts. All surfaced questions were either resolved in this phase or deferred with an explicit trigger:

- **Layer 2 (per-BC ubiquitous language) and Layer 3 (code conventions) of the rules.** Deferred per the rules README. Layer 2 lands during Phase 3 Item 4 (per-BC UL sections in workshops W002-W004). Layer 3 is a future session.
- **Whether a `docs/research/README.md` should exist.** Author's call per §3.6; deferred (four self-describing files don't need an index). Easy to add later if it earns its keep.
- **Whether `auctioneer` belongs in the narratives v1 protagonist-role vocabulary.** Included for future-proofing; revisit if no auctioneer narrative materializes.
- **Whether the "guardrail vs convention" distinction needs its own ADR.** Documented in `docs/rules/README.md`; not contested. Could become an ADR if a substantial dispute arises about whether a rule is a guardrail.
- **Empty-subdirectory convention for `docs/prompts/`.** Inherited from CritterCab. Means the README documents 6 subdirectories but only `implementations/` and `foundation/` exist initially. Slightly confusing for newcomers; not ADR-grade.

The Phase 4 questions (Reqnroll position, project-level glossary strategy, learnings-file scope, demo-script runbook, operations runbook) are explicitly Phase 4 territory per the foundation-refresh prompt §7 and do not need ADR resolution before Phase 2.

## Key learnings

1. **Lifting methodology files requires both structural preservation and domain adaptation.** Every Item 1-6 was a port from CritterCab plus auction-domain substitution. Pure copy-paste would have produced ride-sharing examples in CritterBids; pure rewrite would have lost the pattern. The recipe is "preserve structure verbatim; adapt examples to the auction domain."

2. **Lived-code adaptation is asymmetric from clean-slate adaptation.** Several items (ADR 016, ADR 017, narratives README) added explicit "lived BC" provisions that CritterCab's clean-slate originals did not need. ADR 017 in particular was forced into an asymmetric Decision (lived BCs absorb Steps 1-2 cost; future BCs opt in per-BC) that CritterCab does not face. This pattern (lift + lived-state asymmetry) likely applies to future methodology lifts.

3. **The self-referential edge case (em-dash-in-the-em-dash-rule) is the kind of thing the rules layer surfaces that the ADR layer cannot.** The convention "no em dashes in committed prose" was set at the prompt level (§13). Codifying it as a directive in `structural-constraints.md` forced the question of how to describe the prohibited character without using it. The Unicode-codepoint workaround is small but it would not have surfaced in the ADR layer, which is more about *what* to do than *how* to write the rule down.

4. **The git mv + bulk sed pattern for path subdivision works at scale, but Windows-specific quirks need attention.** Item 5 moved 28 files and updated 53 cross-references atomically. Three Windows quirks surfaced: CRLF normalization (sed -i rewrites in LF, autocrlf normalizes back on stage), perl regex backslash hex escaping (`\x5c` to bypass `\p{}` Unicode property syntax), and JSON escape collapsing in the Bash tool's argument passing. Future bulk-rename operations should plan for these.

5. **Per-PR cadence vs per-phase cadence is a real choice with real consequences.** The plan started with "one PR per item" and the user clarified to "one PR per phase" mid-Item 1. The shift means the six items live as six commits on one branch. Reviewer experience: 6 atomic commits in one PR vs 6 separate PRs. The branch's git history is now navigable per-item (bisect-friendly) without the overhead of six review cycles.

## Verification checklist (from §3.7)

- [x] All six items committed.
- [x] Items folded in beyond the original six are noted with rationale (above).
- [x] Phase 2 narrative target identified (Flash demo bidder happy-path against W001, single-bidder perspective; adjusted framing for shipped M3-S6 / M4-S1 / M4-S2).
- [x] Open questions surfaced during Phase 1 are identified above; none warrant ADR resolution before Phase 2.

## Document history

- **v0.1** (2026-04-26): Authored at Phase 1 close.
