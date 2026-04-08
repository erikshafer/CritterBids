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
| Marten event sourcing | `marten-event-sourcing.md` | ✅ Complete | Extracted from CritterSupply |
| Marten projections (EF Core) | `marten-projections.md` | ✅ Complete | New — authored for CritterBids |
| Polecat event sourcing | `polecat-event-sourcing.md` | 🟡 Placeholder — fill in during first Polecat BC | New |
| Dynamic Consistency Boundary | `dynamic-consistency-boundary.md` | ✅ Complete | Extracted from CritterSupply |
| Integration messaging | `integration-messaging.md` | ✅ Complete | Extracted from CritterSupply |
| SignalR real-time | `wolverine-signalr.md` | ✅ Complete | Extracted from CritterSupply |
| Testing patterns | `critter-stack-testing-patterns.md` | ✅ Complete | Extracted from CritterSupply |
| Event Modeling workshop | `event-modeling/SKILL.md` | ✅ Complete | Shared |
| C# coding standards | `csharp-coding-standards.md` | 🔴 Not yet written | Extract from CritterSupply |
| Adding a BC module | `adding-bc-module.md` | 🔴 Not yet written | New — modular monolith specific |
| React frontend | `react-frontend.md` | 🔴 Not yet written | New |
| Domain event conventions | `domain-event-conventions.md` | 🔴 Not yet written | New |

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
| DCB boundary model | `dynamic-consistency-boundary.md` | `marten-event-sourcing.md` |
| Integration event (cross-BC) | `integration-messaging.md` | `domain-event-conventions.md` |
| SignalR hub + real-time push | `wolverine-signalr.md` | — |
| New BC module registration | `adding-bc-module.md` | — |

### Testing

| Task | Primary Skill | Secondary Skill |
|---|---|---|
| Integration test (Alba + Testcontainers) | `critter-stack-testing-patterns.md` | — |
| Unit test (pure handler logic) | `critter-stack-testing-patterns.md` | — |
| Saga test | `wolverine-sagas.md` | `critter-stack-testing-patterns.md` |
| EF Core projection test | `marten-projections.md` | `critter-stack-testing-patterns.md` |
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

**Extract from CritterSupply (domain-agnostic content exists):**

- `csharp-coding-standards.md` — records, immutability, `IReadOnlyList<T>`, `with` expressions, value object patterns, sealed types. Source: `docs/skills/modern-csharp-coding-standards.md`.

**Write fresh for CritterBids (no direct CritterSupply equivalent):**

- `adding-bc-module.md` — modular monolith module registration, `AddXyzModule()` extension method pattern, Contracts project conventions, `Program.cs` wiring. CritterBids-specific.
- `react-frontend.md` — React + TypeScript conventions, SignalR hook patterns, bid feed state management, ops dashboard patterns. CritterBids-specific.
- `domain-event-conventions.md` — past-tense naming, no "Event" suffix, aggregate ID as first property, `DateTimeOffset` timestamps, `IReadOnlyList<T>` for collections, CritterBids vocabulary reference. CritterBids-specific.

**Fill in from first implementation:**

- `polecat-event-sourcing.md` — placeholder exists with known patterns and an implementation checklist. Complete during the first Polecat BC (likely Participants).

---

## Relationship to CritterSupply Skills

The skills marked "Extract from CritterSupply" have direct equivalents in CritterSupply's `docs/skills/` directory. The extraction process:

1. Keep all domain-agnostic content verbatim or near-verbatim
2. Replace CritterSupply BC names and examples with CritterBids equivalents
3. Strip milestone markers, retrospective references, and `src/` file paths
4. Keep every anti-pattern and lesson learned — these transfer wholesale

If a skill is a placeholder or not yet written, the CritterSupply equivalent is the authoritative reference until the CritterBids version exists.
