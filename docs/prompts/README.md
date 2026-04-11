# Session Prompts

This directory holds the structured session prompts used to drive CritterBids
implementation through GitHub Copilot custom agents (`@PSA`, `@QAE`, `@PO`,
`@UXE`, `@DOE`). One prompt corresponds to one session — a single PR's worth
of work, scoped to a specific milestone and slice.

Prompts are living documents. **The template and the rules below evolve
through M1 retrospectives** — after each M1 session lands, revisit this file
and fold in whatever the retro surfaced. M1 is where the shape of a "good
CritterBids prompt" gets discovered; don't treat anything here as frozen
until M1 is closed out.

## Naming convention

```
M{milestone}-S{slice}-{kebab-case-summary}.md
```

Examples: `M1-S1-solution-baseline.md`, `M1-S2-participants-bc-scaffold.md`.

## Template format

Every prompt file follows this structure:

```markdown
# M{n}-S{n}: {Title}

**Milestone:** M{n} — {name}
**Slice:** S{n} — {slice name}
**Agent:** @{PSA|QAE|PO|UXE|DOE}
**Estimated scope:** {one PR, ~N files}

## Goal

One paragraph. What the session produces and why it exists.

## Context to load

- `docs/milestones/M{n}-{name}.md` — the milestone doc, authoritative for scope
- `CLAUDE.md` — routing layer and global conventions
- `docs/skills/{skill}.md` — one or more skill files the session must consult
- Any workshop docs, ADRs, or prior session outputs the agent needs

## In scope

Bulleted, concrete deliverables. Files to create, projects to add, tests to write.

## Explicitly out of scope

Bulleted. What this session must not touch. Reference the milestone doc's
non-goals and narrow further to the slice.

## Conventions to pin or follow

Any convention decisions this session is responsible for encoding in code for
the first time. Point at the skill file that owns the rule.

## Acceptance criteria

Checklist the session must satisfy before opening the PR.

## Open questions

Anything the agent should flag rather than decide unilaterally.
```

## The ten rules

These are the prose rules every prompt in this directory obeys. They exist so
that prompts stay terse, reviewable, and aimed at a single PR.

1. **One prompt equals one PR.** If a prompt can't land in a single reviewable
   PR, split it. A session that produces three PRs produced the wrong artifact.

2. **Scope is named by milestone and slice, not by feature area.** The filename
   carries the coordinates; the body restates them in the header. An agent
   reading the prompt cold should know within five seconds which milestone doc
   governs the work.

3. **The milestone doc is authoritative for scope.** Prompts reference it, they
   don't duplicate it. If a prompt and the milestone doc disagree, the
   milestone doc wins and the prompt is wrong.

4. **Skill files own conventions.** A prompt names which skill files to load
   and trusts them to specify the rules. Prompts do not restate skill content
   inline — they point. If a needed convention has no skill file yet, that is
   a milestone-level decision and belongs in the milestone doc, not smuggled
   into a prompt.

5. **Explicit non-goals are mandatory.** Every prompt has an "out of scope"
   section. Silence is how scope creeps. If the answer to "should this session
   touch X" is obviously no, write it down anyway.

6. **Acceptance criteria are checkable, not aspirational.** Each line is
   something a reviewer can verify in under a minute: a file exists, a test
   passes, a package is pinned, an endpoint returns a specific status. No
   "code is clean" or "follows best practices."

7. **Open questions are escalated, not decided.** If a prompt hits a real
   design decision mid-session, the agent flags it and stops. Prompts never
   authorize an agent to guess on architecture.

8. **No code in prompts.** Prompts describe intent and constraints. They do
   not contain C# snippets, config blocks, or file contents. Code is what the
   session produces, not what it starts with.

9. **Context load is finite and listed.** A prompt enumerates the files the
   agent should read. If the list is longer than seven items the session is
   probably too big.

10. **Prompts are drafted, reviewed, and committed before the session runs.**
    Ad-hoc prompts typed into a chat window are not session prompts. They
    live in this directory, under version control, as the durable record of
    what was asked for.

## Known gaps

Until M1 retrospectives say otherwise, expect the template and the rules to
move. Propose changes by PR against this file with a short note describing
which session surfaced the gap.

See `WORKFLOW.md` in this directory for the human-facing operational loop
(how to run a session, review against acceptance criteria, and feed retros
back into the template).
