---
name: _template
description: "CritterBids skill authoring template. NOT AN ACTIVATABLE SKILL. Copy this directory to start a new skill; see inline comments for conventions."
cluster: core
tags: [meta, template]
---

<!--
  CritterBids Skill Template
  ==========================
  Canonical starting point for every CritterBids skill. CritterBids skills follow
  the same "defer to upstream, write only what diverges" discipline as the sibling
  CritterCab and CritterMart projects.

  THE CORE RULE
  -------------
  The JasperFx ai-skills library is the source of truth for generic Marten,
  Wolverine, and Polecat mechanics. It is installed at the user level (license
  required; `npx skills add`). A CritterBids skill exists ONLY to document what is
  genuinely CritterBids-specific on top of upstream:

    - project shape decisions (sealed records, BidderId-not-paddle, schema-per-BC)
    - divergences from the upstream default, with rationale
    - posture tables that map a capability to specific CritterBids BCs
    - hard-won findings (anti-patterns discovered in this codebase)
    - in-project examples using real CritterBids BCs and types

  Do NOT re-document generic mechanics. Up-reference them instead (see § See also).

  How to use:
    1. cp -r docs/skills/_template docs/skills/<your-skill-name>
    2. Rename the directory to match the `name` frontmatter (kebab-case).
    3. Fill in frontmatter and replace placeholder content.
    4. Name the ai-skills this skill defers to in § See also -> Upstream.
    5. If the skill grows past ~500 lines, move deep-dive material into
       references/<topic>.md within the skill directory.

  Frontmatter fields
    name (required)        kebab-case; MUST match the directory name.
    description (required) what + when. Loaded at agent startup; every word counts.
                           "<topic>: <key concepts>. Use when <trigger>."
    cluster                one of: core, wolverine, marten, polecat, aspire,
                           testing, observability, infrastructure, design.
                           Pick the PRIMARY-value axis; secondary axis -> tags.
    tags                   3-6 freeform concept tags (stack + concern).
    status (optional)      one of: complete, reference, placeholder, draft.
                           Omit for complete.

  Length guideline: aim under 500 lines. Cohesion beats arbitrary caps; move
  deep-dive material into references/ when it earns its own file.
-->

# Skill Title

> One-line summary of what this skill teaches.
> Generic mechanics live in ai-skills `<skill-name>`; **this skill documents the CritterBids-specific decisions.**

## When to apply this skill

Use this skill when:

- Concrete activation trigger (task type or description).
- Another concrete activation trigger.

Do NOT use this skill when:

- Anti-trigger, with a pointer to the skill that should be used instead.

## Read upstream first

If you're new to the generic mechanics, read these ai-skills (in order) before this skill:

1. `<ai-skill-name>` — what it covers.
2. `<ai-skill-name>` — what it covers.

Those cover ~80% of the topic. This skill picks up at the CritterBids-specific decisions.

## (Core content — replace heading and structure to fit the skill)

(replace with CritterBids-specific content only)

## Common pitfalls

- **Pitfall name.** Brief description of the mistake and why it bites.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `<ai-skill-name>` — what it covers that this skill assumes.

**Prerequisites** — CritterBids skills to load first if unfamiliar:

- `<critterbids-skill>` — what it covers.

**Downstream** — natural follow-ups:

- `<critterbids-skill>` — what comes next.

**External:**

- ADR-XXX in [`docs/decisions/`](../../decisions/) — decision context.
- [`CLAUDE.md`](../../../CLAUDE.md) § Section — project-level rationale.
