# Prompt Authoring Guide

This file holds the rules and template that govern *how* to author a CritterBids session prompt. The directory's [`README.md`](./README.md) is the index of what's already there; this file is the manual for adding more.

## The ten rules

These are the prose rules every prompt obeys. They exist so that prompts stay terse, reviewable, and aimed at a single PR.

1. **One prompt equals one PR.** If a prompt cannot land in a single reviewable PR, split it. A session that produces three PRs produced the wrong artifact.

2. **Scope is named by milestone and slice (or by target artifact for non-implementation prompts), not by feature area.** The filename carries the coordinates; the body restates them in the header. An agent reading the prompt cold should know within five seconds which artifact governs the work.

3. **The milestone doc is authoritative for scope.** Implementation prompts reference it; they do not duplicate it. If a prompt and the milestone doc disagree, the milestone doc wins and the prompt is wrong.

4. **Skill files own conventions.** A prompt names which skill files to load and trusts them to specify the rules. Prompts do not restate skill content inline: they point. If a needed convention has no skill file yet, that is a milestone-level decision and belongs in the milestone doc, not smuggled into a prompt.

5. **Explicit non-goals are mandatory.** Every prompt has an "out of scope" section. Silence is how scope creeps. If the answer to "should this session touch X" is obviously no, write it down anyway.

6. **Acceptance criteria are checkable, not aspirational.** Each line is something a reviewer can verify in under a minute: a file exists, a test passes, a package is pinned, an endpoint returns a specific status. No "code is clean" or "follows best practices."

7. **Open questions are escalated, not decided.** If a prompt hits a real design decision mid-session, the agent flags it and stops. Prompts never authorize an agent to guess on architecture.

8. **No code in prompts.** Prompts describe intent and constraints. They do not contain C# snippets, config blocks, or file contents. Code is what the session produces, not what it starts with.

9. **Context load is finite and listed.** A prompt enumerates the files the agent should read. If the list is longer than seven items, the session is probably too big.

10. **Prompts are drafted, reviewed, and committed before the session runs.** Ad-hoc prompts typed into a chat window are not session prompts. They live in the appropriate `docs/prompts/<subdir>/` directory, under version control, as the durable record of what was asked for.

## Implementation prompt template

For prompts in `implementations/`. Other subdirectory prompts (narratives, decisions, workshops, skills, foundation) adapt this template to their artifact's shape.

```markdown
# M{n}-S{n}: {Title}

**Milestone:** M{n} ({name})
**Slice:** S{n} ({slice name})
**Agent:** @{PSA|QAE|PO|UXE|DOE}
**Estimated scope:** {one PR, ~N files}

## Goal

One paragraph. What the session produces and why it exists.

## Context to load

- `docs/milestones/M{n}-{name}.md`: the milestone doc, authoritative for scope
- `CLAUDE.md`: routing layer and global conventions
- `docs/skills/{skill}.md`: one or more skill files the session must consult
- `docs/rules/structural-constraints.md`: Layer 1 rules (load when the session touches a constrained surface)
- Any workshop docs, narratives, ADRs, or prior session outputs the agent needs

## In scope

Bulleted, concrete deliverables. Files to create, projects to add, tests to write.

## Explicitly out of scope

Bulleted. What this session must not touch. Reference the milestone doc's non-goals and narrow further to the slice.

## Conventions to pin or follow

Any convention decisions this session is responsible for encoding in code for the first time. Point at the skill file that owns the rule.

## Acceptance criteria

Checklist the session must satisfy before opening the PR.

## Open questions

Anything the agent should flag rather than decide unilaterally.
```

## Adapting the template for non-implementation prompts

- **Narrative prompts** (`narratives/`): replace milestone/slice with `journey` and `protagonist`. Reference `docs/narratives/README.md` for the format. The "context to load" includes the workshop slices the narrative implements.
- **Decision prompts** (`decisions/`): replace milestone/slice with `ADR number` and `topic`. The "context to load" includes related ADRs and skill files. The "in scope" enumerates the options to evaluate.
- **Workshop prompts** (`workshops/`): replace milestone/slice with `workshop number` and `BC`. The "in scope" enumerates the workshop's intended sections per `docs/skills/event-modeling/SKILL.md`.
- **Skill prompts** (`skills/`): replace milestone/slice with `skill slug`. The "in scope" enumerates the patterns the skill file documents.
- **Foundation prompts** (`foundation/`): multi-phase orchestration. Diverges from the template: includes a phase plan, gates between phases, and an explicit hand-off section. The current example is `foundation-refresh-handoff.md`.

## Document history

- **v0.1** (2026-04-26): Authored as part of foundation-refresh Phase 1 Item 5. Carries forward the ten rules and template skeleton from the pre-subdivision `README.md` v0.1, expanded with subdirectory-specific adaptations.
