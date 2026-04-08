# CritterBids Personas

Personas are tool-agnostic agent definitions used during Event Modeling workshops, architectural reviews, and AI-assisted development sessions. They live in this repository so any LLM, tool, or developer can reference them without depending on a specific platform configuration.

## How to Use Personas

Each persona is a self-contained markdown document. To activate a persona in any LLM tool:

1. Load the relevant persona document(s) as context
2. Declare which personas are active at the start of the session
3. Label each persona's contribution clearly when working in multi-persona mode (e.g. `[ARCHITECT]`, `[QA]`)

Personas may agree, disagree, and build on each other. Productive tension is the goal — not consensus for its own sake.

## The Roster

| Persona | File | Role |
|---|---|---|
| `@Facilitator` | `facilitator.md` | Runs workshops, keeps sessions on track, synthesizes output |
| `@DomainExpert` | `domain-expert.md` | Auction domain knowledge, eBay conventions, business accuracy |
| `@Architect` | `architect.md` | Critter Stack, modular monolith, BC boundaries, conventions |
| `@BackendDeveloper` | `backend-developer.md` | C#, Wolverine, Marten, Polecat, messaging, implementation feasibility |
| `@FrontendDeveloper` | `frontend-developer.md` | React, TypeScript, SignalR, SPA patterns, real-time UX |
| `@QA` | `qa.md` | Edge cases, compensation paths, failure modes, acceptance criteria |
| `@ProductOwner` | `product-owner.md` | Scope, milestones, demo-first constraints, MVP guardian |
| `@UX` | `ux.md` | Participant and seller experience, read model advocacy, demo legibility |

## When to Use Which Personas

Not every session needs all eight. Use judgment:

- **Event Modeling — Brain Dump:** `@Facilitator`, `@DomainExpert`
- **Event Modeling — Storytelling:** all personas
- **Event Modeling — Storyboarding:** `@FrontendDeveloper`, `@UX`, `@BackendDeveloper`
- **Event Modeling — Slicing:** `@Facilitator`, `@ProductOwner`, `@BackendDeveloper`
- **Event Modeling — Scenarios:** `@BackendDeveloper`, `@DomainExpert`, `@QA`
- **Architecture review:** `@Architect`, `@BackendDeveloper`, `@ProductOwner`
- **Frontend design:** `@FrontendDeveloper`, `@UX`

## Relationship to Skills

Personas define *who is in the room*. Skills define *what they know*.
Load the relevant skill documents alongside persona documents for best results.
See `docs/skills/README.md` for the skills index.
