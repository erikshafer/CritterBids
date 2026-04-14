# CritterBids Skills Index

Skills are implementation pattern documents. Load the relevant skill file **before** starting any implementation task. They encode hard-won patterns and prevent rediscovering known solutions.

## How to Use Skills

1. Identify your task from the table below
2. Load the skill file(s) into your context
3. Follow the patterns — don't improvise unless you have a specific reason to deviate
4. If you deviate, document why in a comment or ADR

Skills are living documents. When a new pattern is established or an existing one is refined during implementation, update the relevant skill file.

---

## Skill Status

| Skill | File | Status | Source |
|---|---|---|---|
| Wolverine message handlers | `wolverine-message-handlers.md` | ✅ Complete | Extracted from CritterSupply |
| Wolverine sagas | `wolverine-sagas.md` | ✅ Complete | Extracted from CritterSupply |
| Marten event sourcing | `marten-event-sourcing.md` | ✅ Complete | Extracted from CritterSupply + updated M2 (named stores, perf settings) |
| Marten projections (EF Core) | `marten-projections.md` | ✅ Complete | New — authored for CritterBids |
| Marten querying | `marten-querying.md` | ✅ Complete | Authored from Marten docs + Jeremy Miller's blog |
| Polecat event sourcing | `polecat-event-sourcing.md` | ✅ Complete | Filled in from M1 Participants BC |
| .NET Aspire orchestration | `aspire.md` | ✅ Complete | Authored from M1 (S1–S4) experience |
| Dynamic Consistency Boundary | `dynamic-consistency-boundary.md` | ✅ Complete | Extracted from CritterSupply |
| Integration messaging | `integration-messaging.md` | ✅ Complete | Extracted from CritterSupply + updated M2 (Aspire RabbitMQ, Separated mode) |
| SignalR real-time | `wolverine-signalr.md` | ✅ Complete | Extracted from CritterSupply |
| Testing patterns | `critter-stack-testing-patterns.md` | ✅ Complete | Extracted from CritterSupply + updated M2 (named store fixtures, Marten cleanup API) |
| C# coding standards | `csharp-coding-standards.md` | ✅ Complete | Extracted from CritterSupply |
| Event Modeling workshop | `event-modeling/SKILL.md` | ✅ Complete | Shared |
| Adding a BC module | `adding-bc-module.md` | ✅ Complete | New — authored M2 pre-S2 from ADR 0002 + JasperFx ai-skills |
| React frontend | `react-frontend.md` | 🔴 Not yet written | New |
| Domain event conventions | `domain-event-conventions.md` | 🔴 Not yet written | New — write in M2-S7 |

**Status key:**
- ✅ Complete and ready to use
- 🟡 Placeholder — useful stub exists, fill in during first real use
- 🔴 Not yet written — create when first needed

---

## Skills by Task

### Implementation

| Task | Primary Skill | Secondary Skill |
|---|---|---|
| Wolverine command handler | `wolverine-message-handlers.md` | `csharp-coding-standards.md` |
| Wolverine HTTP endpoint | `wolverine-message-handlers.md` | — |
| Saga (multi-step workflow) | `wolverine-sagas.md` | `integration-messaging.md` |
| Scheduled messages / timeouts | `wolverine-sagas.md` | — |
| Event-sourced aggregate (Marten) | `marten-event-sourcing.md` | `csharp-coding-standards.md` |
| Event-sourced aggregate (Polecat) | `polecat-event-sourcing.md` | `marten-event-sourcing.md` |
| Marten native projection | `marten-event-sourcing.md` | — |
| EF Core projection (Marten or Polecat) | `marten-projections.md` | `polecat-event-sourcing.md` |
| Read model query (LINQ / compiled / batched) | `marten-querying.md` | `csharp-coding-standards.md` |
| JSON streaming to HTTP response | `marten-querying.md` | `wolverine-message-handlers.md` |
| Raw SQL / advanced SQL query | `marten-querying.md` | — |
| DCB boundary model | `dynamic-consistency-boundary.md` | `marten-event-sourcing.md` |
| Integration event (cross-BC) | `integration-messaging.md` | `domain-event-conventions.md` |
| SignalR hub + real-time push | `wolverine-signalr.md` | — |
| New BC module registration | `adding-bc-module.md` | `marten-event-sourcing.md` |

### Testing

| Task | Primary Skill | Secondary Skill |
|---|---|---|
| Integration test (Alba + Testcontainers) | `critter-stack-testing-patterns.md` | — |
| Unit test (pure handler logic) | `critter-stack-testing-patterns.md` | — |
| Marten BC test fixture | `critter-stack-testing-patterns.md` | `adding-bc-module.md` |
| Polecat BC test fixture | `critter-stack-testing-patterns.md` | — |
| Saga test | `wolverine-sagas.md` | `critter-stack-testing-patterns.md` |
| EF Core projection test | `marten-projections.md` | `critter-stack-testing-patterns.md` |
| Compiled query correctness | `marten-querying.md` | `critter-stack-testing-patterns.md` |
| SignalR integration test | `wolverine-signalr.md` | `critter-stack-testing-patterns.md` |

### Frontend

| Task | Primary Skill | Secondary Skill |
|---|---|---|
| React component | `react-frontend.md` | — |
| SignalR client connection | `react-frontend.md` | `wolverine-signalr.md` |
| Real-time bid feed | `react-frontend.md` | `wolverine-signalr.md` |

### Design & Architecture

| Task | Skill |
|---|---|
| Event Modeling workshop | `event-modeling/SKILL.md` |
| Naming domain events | `domain-event-conventions.md` |
| Personas for workshop | `../personas/README.md` |

---

## Writing New Skills

When writing a 🔴 skill for the first time:

1. Implement the feature first — let the code reveal the real patterns
2. Document what you learned, including what didn't work
3. Follow the density principle: every line earns its place
4. Reference related skills at the bottom
5. Update this README status from 🔴 to ✅

### Skills Still Needed

**Write fresh for CritterBids (no direct CritterSupply equivalent):**

- `react-frontend.md` — React + TypeScript conventions, SignalR hook patterns, bid feed state management, ops dashboard patterns. CritterBids-specific.
- `domain-event-conventions.md` — past-tense naming, no "Event" suffix, aggregate ID as first property, `DateTimeOffset` timestamps, `IReadOnlyList<T>` for collections, CritterBids vocabulary reference. Write in M2-S7 when first domain events for Selling BC are authored.

---

## Relationship to CritterSupply Skills and JasperFx AI Skills

Skills marked "Extracted from CritterSupply" have direct equivalents in CritterSupply's `docs/skills/` directory. The extraction process:

1. Keep all domain-agnostic content verbatim or near-verbatim
2. Replace CritterSupply BC names and examples with CritterBids equivalents
3. Strip milestone markers, retrospective references, and `src/` file paths
4. Keep every anti-pattern and lesson learned — these transfer wholesale

CritterBids also maintains a gap analysis againstthe public JasperFx AI skills repo at `docs/skills/jasper-fx-ai-skills-gap-analysis.md`. Consult it when the canonical Critter Stack patterns have changed or when a skill seems incomplete. The gap analysis was last reviewed 2026-04-14 and drove the pre-S2 skills pass that updated `marten-event-sourcing.md`, `critter-stack-testing-patterns.md`, `integration-messaging.md`, and authored `adding-bc-module.md`.
