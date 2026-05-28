# ADR 020: Spec-Delta Closure Loop

**Status:** Accepted
**Date:** 2026-05-28

---

## Context

ADR 016 named the authority relationship between specs and code (spec-anchored: narratives and workshops are the architectural reference; code is authoritative for runtime behavior; drift is caught at retrospective time). It did not, however, name the *mechanism* by which a given session's spec amendment gets proposed, ratified, and recorded. The foundation refresh's Phase 2 findings discipline carried that load for one phase. Beyond Phase 2, the discipline reverts to ambient retrospective practice without a named cadence.

Two sibling Critter Stack projects have since converged on per-session spec-delta cadences. CritterMart adopted the full OpenSpec CLI: a peer `openspec/` directory, per-change folders carrying `proposal.md` + delta `specs/<cap>/spec.md` + `design.md` + `tasks.md`, `openspec validate --strict` in CI, and an archive ritual that syncs delta specs into accumulated capability specs (CritterMart ADR 010 + ADR 011). CritterCab declined the framework but kept the discipline: each prompt declares a `## Spec delta`, each retro confirms `Spec delta — landed?`, and the narrative's `## Document History` records the amendment. CritterCab's discipline is encoded across three READMEs with no new tooling.

CritterBids enters this decision having shipped five milestones (M1 through M5) under spec-anchored development plus the foundation refresh's findings audits. M6 (Obligations, Relay, Operations) is the next greenfield work. The narrative layer exists (five narratives + four findings ledgers). Prompts and retros number 53 and 48 respectively. What is missing is the per-session loop that closes the spec-anchored stance: the explicit statement of what the spec gains, the explicit confirmation that it landed, the explicit entry in the spec's own history.

The choice this ADR resolves is: *given that CritterBids is spec-anchored (ADR 016), what is the minimum per-session cadence that keeps the anchor honest without introducing tooling?*

## Options Considered

### Option 1: Continue ambient retrospective practice

Each retrospective already includes a "findings against narrative" section (per the retrospectives README). Drift is caught when a retro author notices it. No prompt-level spec-delta declaration is required; no narrative-level versioning is required.

This is the discipline CritterBids has used through M1 to M5. It works when the session author is also the retrospective author and the cycle time is short. It produces durable retros and durable findings ledgers (Phase 2's twelve findings, Phase 2.5's resolution prompts). It does not, however, produce a *spec-shaped* per-session record. A retrospective tells you what was learned that session; it does not tell you, at a glance, what the canonical spec gained. The narrative layer drifts silently between findings audits.

Continued ambient practice was sufficient through the foundation refresh because the refresh itself was a multi-phase audit. Past M5 closure, the audit cadence ends and ambient practice operates without a periodic forcing function. The narrative ↔ code relationship would re-drift until the next deliberate audit.

### Option 2: Adopt the OpenSpec CLI (CritterMart pattern)

Install the `openspec` CLI as a project dependency. Create a peer `openspec/` directory with `specs/<capability>/spec.md` per BC and `changes/<slug>/` for active proposals. Each change carries four artifacts: `proposal.md` (Why / What Changes / Capabilities / Impact), delta `specs/<capability>/spec.md` (ADDED/MODIFIED/REMOVED requirements with SHALL + GWT scenarios), `design.md` (per-change technical decisions, distinct from cross-cutting ADRs), `tasks.md` (live implementation checklist). Run `openspec validate <change> --strict` in CI. Archive completed changes to `openspec/changes/archive/YYYY-MM-DD-<slug>/`, syncing delta specs into the capability spec.

The appeal is real and CritterMart-proven: machine-validated specs, an accumulated capability spec that grows truthfully from archived changes, and a CLI-mediated authoring loop that prevents the four artifacts from drifting against each other. For greenfield BCs, the cost of authoring four artifacts up front is paid back by the validator catching spec-shape errors before code is written.

The costs are also real and CritterBids-specific. The CLI introduces a Node.js dependency (CritterBids is otherwise pure .NET + Aspire; no `package.json` exists in the repo). The peer `openspec/` directory creates a fork in the documentation hierarchy that CLAUDE.md and every contributor must learn to navigate alongside `docs/narratives/`, `docs/workshops/`, and `docs/decisions/`. The validator's value depends on contributors authoring delta specs in SHALL + GWT form; CritterBids' narratives are journey-prose, not requirement-list. Either the narratives become the OpenSpec proposal source (a format shift) or the team maintains specs in two formats (the parallel-source failure mode ADR 018 explicitly rejected for Reqnroll, for the same reason).

The deeper structural cost: OpenSpec assumes each change accumulates onto a single capability spec via archive-time sync. CritterBids has five milestones of lived code with no archived OpenSpec history. Adopting the CLI mid-project for M6 forward would either (a) require backfilling fictional OpenSpec changes for M1 through M5 (the false-provenance failure mode ADR 016 explicitly rejects), or (b) leave the capability spec empty until M6's first slice archives, with the existing narratives carrying the historical record. Option (b) is the only honest path; it leaves the OpenSpec layer asymmetric across BCs (M1–M5 BCs have no OpenSpec history; M6 BCs do), which is its own form of drift.

### Option 3: Adopt the spec-delta closure loop (CritterCab pattern, NDD-informed)

Encode a four-step cadence in the existing artifacts. No new directory, no new tooling, no format shift on the narratives or workshops.

```
Step 1 — Prompt declares the spec delta
    Each prompt under docs/prompts/ adds a `## Spec delta` section.
    Two to four lines in spec-shaped terms naming what the canonical
    spec gains when the session ships (new Moment, new slice
    coverage, amended GWT, new forward-constraint, new ADR).

Step 2 — Session executes
    The session does the work the prompt defines. No new mechanics.

Step 3 — Retro confirms the spec delta landed
    Each retrospective adds a `## Spec delta — landed?` paragraph.
    One paragraph confirming whether the planned delta landed.
    Names any divergence; cites the spec amendments that followed.

Step 4 — Spec records its own amendment
    The narrative (or workshop, or ADR) gains a row in its
    ## Document History naming the prompt slug that produced the
    amendment and the substance of the amendment.
```

The four steps form a closed loop: the prompt is the proposal, the session is the execution, the retro is the ratification, the spec history is the audit trail. Each step is single-paragraph or single-row; no step requires new infrastructure. The cadence is enforceable by reviewer checklist (a prompt without `## Spec delta` is rejected at PR review; a retro without `Spec delta — landed?` is rejected at PR review).

The discipline is borrowed in *pattern* from OpenSpec's change-proposal mechanism. It is not a framework, not a tool, not a format. The narratives stay journey-prose; the workshops stay event-model markdown; the ADRs stay narrative ADRs. What is new is that every session ships with an explicit pre/post statement about the spec, and every spec carries its own changelog.

The discipline is also a no-regret precursor to Option 2. If a future BC's complexity justifies the OpenSpec CLI (M6 Obligations is one candidate; its multi-step obligation lifecycle has more SHALL surface than M5 Settlement did), the prompt-level spec deltas already authored translate naturally into OpenSpec change proposals. The decision to adopt OpenSpec for a specific BC becomes a per-BC ADR rather than a project-wide ceremony shift.

## Decision

**Option 3.** CritterBids adopts the spec-delta closure loop as the per-session cadence that operationalizes ADR 016.

The four steps are mandatory for all implementation prompts and retros from this ADR forward. The mechanics:

1. **Every prompt under `docs/prompts/implementations/` and `docs/prompts/narratives/` carries a `## Spec delta` section.** Two to four lines in spec-shaped terms. The section is added to the prompt template in `docs/prompts/AUTHORING.md`.

2. **Every retrospective under `docs/retrospectives/` carries a `## Spec delta — landed?` paragraph.** One paragraph confirming the delta landed or naming the divergence. The section is added to the retrospective template in `docs/retrospectives/README.md`.

3. **Every narrative under `docs/narratives/` carries a `## Document History` section** that gains a row per session that touches the narrative. The row names the triggering prompt slug and the substance of the amendment. The convention is added to `docs/narratives/README.md`.

4. **Workshops and ADRs that are amended by a session gain a `## Document History` row by the same convention.** Workshops already use a `Document History` section per the workshop format. ADRs are append-only and do not require document history; an amending session creates a new ADR (per ADR-016 supersession discipline) rather than editing an accepted ADR in place.

The loop is **lightweight and additive**. No new directories, no new tools, no format shifts. The cost is one section per prompt, one paragraph per retro, one row per spec-touching session.

**This ADR does not adopt the OpenSpec CLI.** That decision is deferred per-BC and named below as a future supersession trigger. The spec-delta closure loop is the prerequisite discipline; OpenSpec adoption (if it happens) consumes spec deltas the loop already produces.

**This ADR does not require backfill.** The five existing narratives (001 through 005) and the four findings ledgers (001 through 004) are not retroactively required to add Document History rows for sessions that predate this ADR. The Document History section is required *going forward*: the next session that touches a narrative adds its row and any subsequent session does likewise.

## Consequences

### The spec-delta cadence operationalizes ADR 016 without changing its stance

ADR 016 declared spec-anchored development. ADR 020 names the mechanism. The narratives remain the architectural reference; the code remains authoritative for runtime behavior. What changes is that each session ships with an explicit pre/post about the spec, and each spec carries its own changelog. Drift is no longer caught only at retrospective time as ambient practice; it is named at prompt time, executed against at session time, ratified at retro time, and recorded at spec time.

### Prompt and retro templates gain one section each

The implementation prompt template (`docs/prompts/AUTHORING.md`) gains a `## Spec delta` section between *In scope* and *Acceptance criteria*. The retrospective template (`docs/retrospectives/README.md`) gains a `## Spec delta — landed?` section between *Findings against narrative* and *Verification checklist*. The narratives README (`docs/narratives/README.md`) names the Document History row format explicitly. These template changes ship in the same PR as this ADR.

### The narrative-versioning convention is named

Each session that touches a narrative bumps a row into the narrative's `## Document History`. The row records: the date, the triggering prompt's filename (e.g., `M6-S1-obligation-bc-scaffold`), and a one-sentence summary of the amendment (e.g., "Added Moment 4 covering obligation-stall edge case per W005 slice 4.2"). The narrative's frontmatter does not gain a `version` key; the Document History rows are the version trail.

CritterMart and CritterCab both use semver-shaped `version` fields in frontmatter (e.g., `v1.0`, `v1.1`). CritterBids declines that addition because the frontmatter is `bounded-vocabulary v1` per the narratives README guardrail #2; adding a key requires the README amendment and a corresponding migration of existing narratives. The Document History rows carry the same information without a vocabulary change.

### Workshops gain Document History rows by the same convention

Workshops already use Document History sections (per workshop format §12 / final block). The convention is the same: a session that amends a workshop slice adds a row naming the prompt and the amendment. Workshops were not amended by retros prior to this ADR (Phase 2 amendments to W001 were the foundation refresh's audit work; future amendments will be session-driven).

### ADRs continue to be append-only

An ADR's status changes only via supersession (per ADR-016 supersession discipline). A session that uncovers an ADR-shaped decision authors a new ADR rather than amending an accepted one in place. The spec-delta closure loop does not change ADR mechanics; ADR amendments do not appear in Document History rows because they appear as new ADRs in the index.

### The OpenSpec CLI remains a per-BC future option

CritterMart adopted the OpenSpec CLI project-wide. CritterBids' five lived BCs and three forthcoming BCs make project-wide adoption asymmetric (no honest OpenSpec history exists for M1 through M5). The trigger for revisiting OpenSpec adoption is per-BC: a forthcoming BC whose SHALL surface is dense enough to benefit from machine-validated requirement lists and whose authoring posture is greenfield (no lived code). M6 Obligations is the first candidate; M6 Relay and M6 Operations are subsequent candidates.

If a per-BC OpenSpec adoption ADR is authored, the spec-delta closure loop's prompt sections become the natural source for OpenSpec change proposals. The loop produces spec deltas; OpenSpec consumes spec deltas. The two are stackable.

### CritterCab is the prior art; CritterMart is the heavier variant

CritterCab encoded the spec-delta closure loop without OpenSpec. CritterMart encoded both. CritterBids adopts CritterCab's discipline and reserves CritterMart's tooling as a per-BC future option. The three projects diverge cleanly on framework adoption while sharing the underlying discipline.

### The loop is enforceable by reviewer checklist

PR review for any session-producing PR checks two boxes: the prompt has a `## Spec delta` section; the retro has a `## Spec delta — landed?` paragraph. A PR missing either is incomplete. No CI validation is introduced by this ADR; the discipline is human-enforced at review time, consistent with CritterBids' existing review practice.

### Future supersession triggers

This ADR is reopened when either:

- A per-BC OpenSpec adoption produces three or more spec-delta translation frictions (the OpenSpec change proposal does not naturally consume the prompt's `## Spec delta` section). The trigger is structural, not aesthetic; surface-level translation work is expected and not a trigger.
- The spec-delta closure loop produces three or more "delta landed, narrative not updated" findings across a single milestone, indicating that the Document History row is being skipped in practice. The remedy at that point is either CI enforcement (a script that fails the PR check if a session-touching narrative gains no Document History row) or a deeper format change.

## References

- `docs/decisions/016-spec-anchored-development.md`: the authority-relationship ADR that this ADR operationalizes
- `docs/decisions/017-design-phase-workflow-sequence.md`: the staged sequence the closure loop runs inside
- `docs/decisions/018-reqnroll-position.md`: the prior ADR that declined a mechanization candidate on similar reasoning (convention-based discipline preserved; mechanization deferred until specific failure modes surface)
- `docs/prompts/AUTHORING.md`: the prompt template that gains the `## Spec delta` section
- `docs/retrospectives/README.md`: the retro template that gains the `## Spec delta — landed?` section
- `docs/narratives/README.md`: the narrative format that names the Document History row convention
- CritterCab `docs/prompts/README.md` § "Spec delta cadence": the prior art encoding the closure loop without OpenSpec
- CritterMart ADR 010 "OpenSpec + Sibling Narrative for the SDD Pipeline" and ADR 011 "openspec CLI as Proposal Tooling, Grain-Aware Layered Integration": the heavier variant that adds the OpenSpec CLI on top of the same underlying discipline
