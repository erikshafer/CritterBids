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
| Wolverine message handlers | `wolverine-message-handlers.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| Wolverine sagas | `wolverine-sagas.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| Marten event sourcing | `marten-event-sourcing.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| Marten projections | `marten-projections.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| Polecat event sourcing | `polecat-event-sourcing.md` | 🔴 Not yet written | New |
| Dynamic Consistency Boundary | `dynamic-consistency-boundary.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| Integration messaging | `integration-messaging.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| SignalR real-time | `wolverine-signalr.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| Adding a BC module | `adding-bc-module.md` | 🔴 Not yet written | New — modular monolith specific |
| Testing patterns | `testing-patterns.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| React frontend | `react-frontend.md` | 🔴 Not yet written | New |
| C# coding standards | `csharp-coding-standards.md` | 🟡 Stub — extract from CritterSupply | CritterSupply |
| Event Modeling workshop | `event-modeling/SKILL.md` | ✅ Complete | Shared |
| Domain event conventions | `domain-event-conventions.md` | 🔴 Not yet written | New |

**Status key:**
- ✅ Complete and ready to use
- 🟡 Stub — placeholder exists, content needs extraction or authoring
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
| Event-sourced aggregate (Polecat) | `polecat-event-sourcing.md` | `csharp-coding-standards.md` |
| Marten projection | `marten-projections.md` | — |
| DCB boundary model | `dynamic-consistency-boundary.md` | `marten-event-sourcing.md` |
| Integration event (cross-BC) | `integration-messaging.md` | `domain-event-conventions.md` |
| SignalR hub + real-time push | `wolverine-signalr.md` | — |
| New BC module registration | `adding-bc-module.md` | — |

### Testing

| Task | Skill |
|---|---|
| Integration test (Alba + Testcontainers) | `testing-patterns.md` |
| Unit test (pure handler logic) | `testing-patterns.md` |
| Saga test | `wolverine-sagas.md` + `testing-patterns.md` |

### Frontend

| Task | Skill |
|---|---|
| React component | `react-frontend.md` |
| SignalR client connection | `react-frontend.md` |
| Real-time bid feed | `react-frontend.md` + `wolverine-signalr.md` |

### Design & Architecture

| Task | Skill |
|---|---|
| Event Modeling workshop | `event-modeling/SKILL.md` |
| Naming domain events | `domain-event-conventions.md` |
| Personas for workshop | `../personas/README.md` |

---

## Relationship to CritterSupply Skills

Many skill files in CritterBids are extracted and adapted from CritterSupply's `docs/skills/` directory. Domain-agnostic Critter Stack patterns transfer directly. CritterSupply-specific examples are replaced with CritterBids equivalents during extraction.

If a skill file is a stub, check CritterSupply's equivalent first — the pattern is almost certainly documented there.
