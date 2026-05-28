# ADR 021: OpenSpec CLI Adoption for M6 (per-BC, Opt-In)

**Status:** Accepted
**Date:** 2026-05-28

---

## Context

ADR 016 named CritterBids' spec-anchored stance. ADR 020 operationalized it with the spec-delta closure loop (prompt declares; session executes; retro confirms; spec records). ADR 020 explicitly reserved tool adoption as a per-BC future option, naming OpenSpec CLI as the likely candidate first considered at M6.

M6 is now the next milestone. Its three BCs — Obligations, Relay, and Operations — are all greenfield: no lived code, no committed narratives covering their slices beyond forward-spec references in narratives 001 and 002. M6 Obligations specifically carries the densest SHALL surface of any remaining BC. The obligation lifecycle is a multi-phase state machine with explicit timeout cascades, ScheduledAt/CancelledAt semantics, and Wolverine-saga interactions. Its event model is dense enough that machine-validated requirement lists would catch shape errors before code is written.

OpenSpec CLI v1.3.1 (`@fission-ai/openspec`) is the candidate tool. It provides:

- A peer `openspec/` workspace directory (CLI-hardcoded; cannot be relocated).
- Per-capability spec accumulation: `openspec/specs/<capability>/spec.md` grows truthfully from archived changes.
- Per-change folders carrying four artifacts: `proposal.md` (Why/What/Capabilities/Impact), delta `specs/<capability>/spec.md` (ADDED/MODIFIED/REMOVED requirements in SHALL + GWT form), `design.md` (per-change technical decisions, distinct from cross-cutting ADRs), `tasks.md` (live implementation checklist).
- `openspec validate <change> --strict` as a CI-grade validation step.
- An archive ritual (`openspec archive`) that syncs delta specs into capability specs after a change ships.

CritterMart adopted OpenSpec project-wide (their ADR 010 + ADR 011) because they had no prior lived code asymmetry. CritterCab declined the tool entirely, encoding the spec-delta loop in markdown only. CritterBids is the asymmetric case: five lived BCs (M1–M5) with no honest OpenSpec history, plus three greenfield BCs (M6) where OpenSpec adoption is structurally clean.

A second consideration entered with the foundation refresh: this repository is **GitHub-Copilot-piloted**, not Claude-Code-piloted. The OpenSpec CLI ships first-class Copilot support (`--tools github-copilot`) which scaffolds `.github/prompts/opsx-*.prompt.md` slash-command prompts plus `.github/skills/openspec-*/SKILL.md` skill files. The Copilot path was exercised before this ADR landed; the scaffolded files coexist cleanly with CritterBids' existing `docs/skills/` library because they are differently scoped (CritterBids skills cover Critter Stack patterns; OpenSpec skills cover OpenSpec workflow mechanics).

The question this ADR resolves is: *do we adopt the OpenSpec CLI for M6, and if so, how does the OpenSpec workspace coexist with the existing narratives, workshops, ADRs, and the spec-delta closure loop?*

## Options Considered

### Option 1: Decline OpenSpec; continue with ADR 020's discipline alone

ADR 020 is sufficient on its own. CritterCab proves the spec-delta closure loop can carry a project without tooling. M6 proceeds with the same prompt/retro/narrative cadence M1–M5 used, augmented only by the ADR 020 Document History rows.

This is the lowest-risk option. It is also the option that under-uses ADR 020's structural setup: the spec-delta closure loop was explicitly designed to be the prerequisite that makes future OpenSpec adoption cheap. Declining adoption when the structural cost is paid and the per-BC asymmetry favors greenfield BCs leaves value on the table for the BC (Obligations) most likely to benefit from machine-validated SHALL lists.

The cost of declining is M6 Obligations ships without spec validation. The cost of accepting is one new directory, one new validation step, and a reviewer-discipline reconciliation between the openspec change folder and the narrative.

### Option 2: Adopt OpenSpec project-wide; backfill M1–M5

Author OpenSpec capability specs for the five lived BCs based on shipped code and existing narratives. Establish `openspec/specs/participants/spec.md`, `openspec/specs/selling/spec.md`, etc., with retroactive requirement lists.

This option was considered and rejected in ADR 020 for the same reason ADR 016 rejects spec-as-source: backfilling specs that pretend to have generated existing code creates a false provenance. The OpenSpec capability spec is *the accumulated history of all archived changes*. A capability spec authored today without any archived changes underneath it lies about its provenance: it reads as if a sequence of changes produced it, when in fact a session pieced it together from the implementation.

Rejected on the same grounds ADR 016 rejected spec-as-source for lived code.

### Option 3: Adopt OpenSpec per-BC, opt-in starting with M6 Obligations

Each forthcoming BC's design-opening session evaluates OpenSpec adoption for that BC alone. M6 Obligations adopts at the session that opens its design phase (Context Mapping → Domain Storytelling → Event Modeling → first slice). M6 Relay and M6 Operations evaluate independently at their own opening sessions; neither is bound by Obligations' decision.

Adopted BCs author OpenSpec change folders per the CLI workflow. Their capability specs grow truthfully from their own archived changes. The asymmetry across BCs is honest: M1–M5 BCs have no OpenSpec history because they predate the tooling; M6-adopting BCs accumulate from their first slice forward.

The spec-delta closure loop (ADR 020) is unchanged for non-adopting BCs. For OpenSpec-adopting BCs, the loop gains a second physical home: the prompt's `## Spec delta` section still declares the amendment in narrative-prose terms, and the OpenSpec change folder's `proposal.md` + delta spec carry the same amendment in OpenSpec SHALL form. The narrative's Document History row still records the human-readable amendment after the session ships.

The cost is reviewer-discipline reconciliation: in OpenSpec BCs, two physical artifacts cover the same amendment in two forms. The discipline rule named below resolves the friction: the OpenSpec change folder is authoritative for the SHALL-form requirement; the narrative is authoritative for the journey prose; the prompt's `## Spec delta` section points at both with one line each.

## Decision

**Option 3.** CritterBids adopts the OpenSpec CLI per-BC, opt-in, starting with M6 Obligations.

The mechanics:

1. **M6 Obligations adopts OpenSpec at design-phase opening.** The first M6 Obligations prompt — Context Mapping or first slice scaffold, whichever opens the BC — authors the BC's OpenSpec capability spec name (kebab-case, named after the BC's primary capability domain; `obligation-lifecycle` is the working name and is confirmed or revised at that session). The first slice authors the first OpenSpec change folder.

2. **M6 Relay and M6 Operations evaluate independently.** Each opens its own design phase with an explicit OpenSpec-adoption decision recorded in the opening prompt's metadata. Three outcomes are valid: adopt (Obligations path), decline (CritterCab path), or defer (proceed with ADR 020 alone, revisit at the BC's first complex change). The decision sits in the opening prompt; no ADR amendment is required for individual BC adoption choices.

3. **M1–M5 BCs are not retroactively adopted.** Participants, Selling, Auctions, Listings, and Settlement continue under ADR 020's discipline alone. No `openspec/specs/<m1-m5-bc>/` directory is authored. The asymmetry is intentional and honest: OpenSpec adoption requires greenfield authoring, not retroactive declaration.

4. **The OpenSpec workspace sits at `openspec/`, peer to `docs/`.** The CLI hardcodes this location and cannot be relocated. The empty `openspec/changes/` and `openspec/specs/` directories ship with this ADR (carrying `.gitkeep` files); they fill as M6 work lands.

5. **The Copilot slash commands ship at `.github/prompts/opsx-*.prompt.md`.** Four commands: `/opsx:propose`, `/opsx:apply`, `/opsx:archive`, `/opsx:explore`. They are OpenSpec-managed; do not edit them. The user invokes them in the Copilot CLI when working an OpenSpec-adopting BC's slice.

6. **The OpenSpec skill files ship at `.github/skills/openspec-*/SKILL.md`.** Four skills, one per slash command. They are OpenSpec-managed; do not edit them. They coexist with `docs/skills/` (which remains CritterBids' Critter-Stack-pattern library). The two skill libraries are differently scoped and do not duplicate content; the routing rule below names the precedence.

7. **The OpenSpec CLI is a contributor-installed tool, not a repo dependency.** No `package.json` is added to the repository. Contributors who need to drive OpenSpec install the CLI globally:

    ```powershell
    npm install -g @fission-ai/openspec
    ```

    The version pinned at this ADR's authoring is **v1.3.1**. The `openspec/README.md` carries the canonical install instructions.

8. **Capability spec naming follows BC names.** One capability per BC, named after the BC's primary capability domain in kebab-case. The mapping is established at each adopting BC's opening session and recorded in `openspec/README.md`. Initial mapping at this ADR's authoring:

    | BC | Capability name (proposed) |
    |---|---|
    | Obligations | `obligation-lifecycle` |
    | Relay | `bid-relay` *(working; confirm at opening)* |
    | Operations | `operator-dashboards` *(working; confirm at opening)* |

9. **Reconciliation with ADR 020 for OpenSpec-adopting BCs.** The spec-delta closure loop's four steps gain explicit physical homes in OpenSpec-adopting BCs:

    - **Step 1 (prompt's `## Spec delta`)**: short pointer block; not the authoritative requirement form. Example: *"Adds requirement `obligation-cancel-after-stall` to capability `obligation-lifecycle`. See `openspec/changes/M6-S2-cancel-stalled-obligation/proposal.md` for the full proposal and `…/specs/obligation-lifecycle/spec.md` for the SHALL form."*
    - **Step 2 (session executes)**: unchanged.
    - **Step 3 (retro's `## Spec delta — landed?`)**: confirms both the OpenSpec change folder is in `openspec/changes/archive/YYYY-MM-DD-<slug>/` and the narrative's Document History row landed.
    - **Step 4 (spec's `## Document History`)**: unchanged for narratives; OpenSpec capability specs maintain their own provenance via the archive sync.

    The OpenSpec change folder is authoritative for the SHALL-form requirement. The narrative is authoritative for the journey prose. Where they conflict at retrospective time, the conflict is a finding (per ADR 016's four lanes) and is resolved in the same PR.

10. **CI validation is deferred.** This ADR does not introduce `openspec validate --strict` in `.github/workflows/`. The validator runs locally as part of the slice author's flow; CI enforcement is reopened after the first M6 OpenSpec slice ships, on the same reasoning ADR 020 deferred CI enforcement of the closure loop itself: reviewer discipline first, automated enforcement after the discipline is proven and the failure modes are observed.

## Consequences

### The OpenSpec workspace coexists with the existing documentation hierarchy

`docs/narratives/`, `docs/workshops/`, `docs/decisions/`, `docs/prompts/`, `docs/retrospectives/`, `docs/skills/` are unchanged. The `openspec/` directory adds a peer layer for OpenSpec-adopting BCs. CLAUDE.md's "Quick Start" and "Documentation Hierarchy" sections gain one row each pointing at the openspec workspace; the change is additive.

### `.github/prompts/` and `.github/skills/` join the routing layer

Two new top-level directories appear under `.github/`. Both are OpenSpec-managed scaffolding. Contributors who do not work on OpenSpec-adopting BCs can ignore them. CLAUDE.md gains a brief routing rule: OpenSpec slash commands and skill files live under `.github/`; CritterBids' Critter-Stack skill library remains `docs/skills/`. The two skill libraries are differently scoped and do not duplicate content. **Do not edit `.github/skills/openspec-*/SKILL.md` or `.github/prompts/opsx-*.prompt.md`**; they are OpenSpec-CLI-regenerated artifacts and edits will be overwritten by future `openspec update`.

### The spec-delta closure loop gains a second physical home in OpenSpec-adopting BCs

ADR 020's four-step cadence is unchanged in mechanics. In OpenSpec-adopting BCs, the prompt's `## Spec delta` section becomes a short pointer to the OpenSpec change folder, and the OpenSpec change folder carries the authoritative SHALL form. The narrative's Document History row continues to record the human-readable amendment. The two forms are intentionally redundant: narratives serve human reviewers and journey-grain comprehension; OpenSpec change folders serve machine validation and capability-spec accumulation.

### Capability spec accumulation begins truthfully at the first M6 slice

`openspec/specs/obligation-lifecycle/spec.md` is empty until M6 Obligations' first slice archives. The capability spec grows from there, one archived change at a time. This is the OpenSpec mechanism that prevents false provenance: a capability spec's content is always derivable from its accumulated archive.

### No npm dependency enters the repository

`package.json`, `package-lock.json`, `node_modules/`, and `.nvmrc` remain absent from the repo. The CLI is a contributor-installed tool. CI workflow files do not install OpenSpec. The version pin (1.3.1) is documented in `openspec/README.md` and updated by contributor convention; if version drift surfaces, the trigger for revisit is named below.

### M6 Obligations is the first proving ground

The first OpenSpec change folder authored under this ADR is M6 Obligations' first slice. The retrospective for that session reports on three things specifically: (a) whether the prompt's `## Spec delta` pointer + the OpenSpec proposal felt redundant or complementary; (b) whether `openspec validate --strict` caught anything human review would have missed; (c) whether the four-artifact authoring overhead (proposal + delta spec + design + tasks) was justified by the validation payoff. Those three signals feed the M6 Relay and M6 Operations adoption decisions independently.

### Trigger for revisit

This ADR is reopened when any of the following surface:

- **Three or more reconciliation conflicts** between an OpenSpec change folder and the corresponding narrative within a single milestone. The OpenSpec / narrative split is intended to be complementary, not redundant; persistent conflict signals a structural mismatch rather than authoring friction.
- **The OpenSpec CLI's maintenance posture changes materially**: project archived, license shift, breaking-change cadence that exceeds CritterBids' tolerance for tooling churn.
- **The version pin (v1.3.1) drifts to v2.x with breaking change to the change-folder structure**, requiring per-change migration. At that point, ADR 020's discipline-without-tooling fallback becomes the contingency.
- **M6 Relay and M6 Operations both decline adoption** at their opening sessions, leaving Obligations as the sole OpenSpec BC. The asymmetry is honest but small; the ADR is revisited to confirm whether one-BC adoption justifies the workspace overhead.

## References

- `docs/decisions/016-spec-anchored-development.md`: the authority-relationship ADR (narratives are architectural reference; code is authoritative). This ADR is consistent with that stance — OpenSpec capability specs are *additional* architectural reference for adopting BCs, never authoritative over runtime code.
- `docs/decisions/020-spec-delta-closure-loop.md`: the per-session cadence ADR (prompt declares; session executes; retro confirms; spec records). This ADR reconciles the closure loop with OpenSpec change folders for adopting BCs.
- `docs/decisions/018-reqnroll-position.md`: the prior ADR that declined a different mechanization candidate (Reqnroll for executable specs). The contrast is informative: Reqnroll requires per-scenario migration of existing prose specs and a parallel test-execution path; OpenSpec requires neither (greenfield-only adoption, no parallel-source format).
- `openspec/README.md`: canonical install instructions, capability-name mapping, and per-BC adoption ledger.
- CritterMart ADR 011 "openspec CLI as Proposal Tooling, Grain-Aware Layered Integration": the project-wide adoption decision (rejected here as Option 2 for CritterBids' asymmetric lived/greenfield split).
- OpenSpec project home: <https://github.com/Fission-AI/OpenSpec>. CLI version pinned at this ADR's authoring: **v1.3.1**.
