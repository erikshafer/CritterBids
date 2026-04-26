# CritterBids Rules Index

Rules are AI-optimized encodings of architectural constraints. Load the relevant rule file at the start of any implementation session. They distill ADR commitments and skill-file patterns into directives an AI agent can apply without re-reading full ADR prose or full skill files.

## Files

- [`structural-constraints.md`](./structural-constraints.md): Layer 1: modular monolith boundaries, Marten event store ownership, transport posture, Wolverine handler and saga conventions, domain event conventions, UUID strategy, frontend stack posture, spec-anchored workflow, design-phase sequence, and project-wide writing-style and commit conventions. Sourced from ADRs 001, 002, 007, 008, 009, 010, 011, 012, 013, 016, 017 and the skill files they reference.

Layer 2 (ubiquitous language per bounded context) lands after Phase 3 of the foundation refresh adds per-BC Ubiquitous Language sections to workshops W002 through W004 (W001's per-BC UL is added in the same phase). Layer 3 (code conventions: naming, file shape, directory structure) is deferred to a future session.

## Guardrails vs conventions

A **guardrail** is a rule whose violation is a structural defect. Examples in CritterBids: a BC project referencing another BC project; a domain event placed in `CritterBids.Contracts`; a saga handler calling `IMessageBus.PublishAsync` instead of returning `OutgoingMessages`; an aggregate-stream handler missing `[WriteAggregate]`; a domain event type carrying an "Event" suffix. Guardrails are flagged by name in this file's directives.

A **convention** is softer: a strong default that exceptional cases may deviate from with explicit justification recorded in the slice's retrospective. Examples in CritterBids: the `sealed record` pattern for messages and read models; the `IReadOnlyList<T>` collection-property convention; the `[AllowAnonymous]` blanket through M6; the per-BC `AddXyzModule()` extension pattern. Conventions are stated as imperatives but tolerate justified deviation; guardrails do not.

When a rule is a guardrail, the directive in `structural-constraints.md` says so explicitly. When silent on the distinction, treat it as a convention until it earns guardrail status through evidence of repeated violation or hard-to-reverse damage.

## Authority relationship

Skill files in `docs/skills/` carry the operative rules in prose form with worked examples. The rules files distill them to directives and point back to the skill file for the full discussion. **Skill files remain the authority; rules files are the agent-loadable summary.** When a directive here disagrees with a skill file, the skill file wins and this file is updated.

## Document history

- **v0.1** (2026-04-26): Authored as foundation-refresh Phase 1 Item 4. Lifts CritterCab's rules README v0.1 framing and adapts the file index to CritterBids' ADR set. Adds the guardrail-vs-convention distinction explicitly.
