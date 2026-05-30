# CritterBids Skills Index

Skills are implementation pattern documents. Load the relevant skill **before** starting any
implementation task. They encode hard-won, CritterBids-specific patterns and prevent rediscovering
known solutions.

## How to use skills

1. Identify your task from the tables below.
2. Load the skill's `SKILL.md` into your context.
3. Read the skill's **Read upstream first** list and load those ai-skills too when you need the
   generic mechanics.
4. Follow the patterns — don't improvise unless you have a specific reason to deviate; if you deviate,
   record why in a comment or ADR.

Skills are living documents. Refine the relevant `SKILL.md` whenever a pattern is established or changes.

---

## Defer to upstream — write only what diverges

CritterBids is a JasperFx member project, so the maintainer-authored **ai-skills** library is installed
at the user level (license required; `npx skills add`). ai-skills is the **source of truth for generic
Marten, Wolverine, and Polecat mechanics.**

A CritterBids skill therefore exists only to capture what is genuinely **CritterBids-specific**: project
shape decisions, divergences from the upstream default, posture tables that map a capability to specific
BCs, and hard-won anti-patterns discovered in this codebase. Generic mechanics are **up-referenced**, not
duplicated — every skill carries a `## See also` → **Upstream (ai-skills)** block naming the upstream
skills it defers to. This mirrors the sibling CritterCab and CritterMart projects.

**Format:** every skill is a directory `<name>/SKILL.md` with YAML frontmatter
(`name`, `description`, `cluster`, `tags`, optional `status`). Copy [`_template/`](./_template/SKILL.md) to
start a new or migrated skill. The flat-file era ended when the 18 skills below were leaned and migrated;
each lost 70–90% of its bulk by deferring generic content upstream while keeping every CritterBids finding.

---

## Skill status

| Skill | Cluster | Status | Notes |
|---|---|---|---|
| [`wolverine-message-handlers`](./wolverine-message-handlers/SKILL.md) | wolverine | ✅ Lean | Handler shape + AP findings (routing-rule, lambda-factory, InvokeAsync) |
| [`wolverine-sagas`](./wolverine-sagas/SKILL.md) | wolverine | ✅ Lean | `MarkCompleted` on all terminal states, doc-saga vs ES aggregate, `ScheduleAsync` |
| [`integration-messaging`](./integration-messaging/SKILL.md) | wolverine | ✅ Lean | Per-BC queue/durability posture, Settlement/Relay breakers, `DeliverWithin` rule |
| [`wolverine-signalr`](./wolverine-signalr/SKILL.md) | wolverine | ✅ Lean | FQN CloudEvents `type` + client `.split(".").pop()`, group-change posture |
| [`marten-event-sourcing`](./marten-event-sourcing/SKILL.md) | marten | ✅ Lean | UUID v7 identity, single-`AddMarten` wiring, `AutoApplyTransactions`, lessons L1–L9 |
| [`marten-projections`](./marten-projections/SKILL.md) | marten | ✅ Lean | Combined native + EF Core, tolerant upsert, seeded caches |
| [`marten-querying`](./marten-querying/SKILL.md) | marten | ✅ Lean | Schema-per-BC, streaming JSON, compiled-query posture |
| [`dynamic-consistency-boundary`](./dynamic-consistency-boundary/SKILL.md) | marten | ✅ Lean | Superior to upstream: tagging gotchas, checklist, `BidRejected` placement |
| [`projection-side-effects-for-broadcast-live-views`](./projection-side-effects-for-broadcast-live-views/SKILL.md) | marten | ✅ Lean | Broadcast/live-view, derived events, rebuild safety |
| [`critter-stack-ancillary-stores`](./critter-stack-ancillary-stores/SKILL.md) | marten | 📚 Reference | Not used (ADR 009 shared store); "if ancillary stores return" framing |
| [`polecat-event-sourcing`](./polecat-event-sourcing/SKILL.md) | polecat | 📚 Reference | Not used (ADR 011 all-Marten); kept for post-feature-complete + siblings |
| [`critter-stack-testing-patterns`](./critter-stack-testing-patterns/SKILL.md) | testing | ✅ Lean | 5 CritterBids-only patterns incl. `IWolverineExtension` cross-BC isolation |
| [`diagnostics`](./diagnostics/SKILL.md) | observability | ✅ Lean | Source-verified CLI; corrects two ai-skills errors |
| [`observability`](./observability/SKILL.md) | observability | 🟡 Placeholder | OTEL/Prometheus/Grafana scaffold; extend on first Hetzner deploy |
| [`adding-bc-module`](./adding-bc-module/SKILL.md) | infrastructure | ✅ Lean | Modular-monolith BC registration procedure |
| [`csharp-coding-standards`](./csharp-coding-standards/SKILL.md) | core | ✅ Lean | `sealed record`, `IReadOnlyList<T>`, no "Event" suffix, `BidderId` not paddle |
| [`domain-event-conventions`](./domain-event-conventions/SKILL.md) | design | ✅ Lean | Event vocabulary + payload rules |
| [`aspire`](./aspire/SKILL.md) | aspire | ✅ Lean | AppHost provisions Postgres + RabbitMQ; profiles, dashboard, labels |
| [`event-modeling`](./event-modeling/SKILL.md) | design | ✅ Complete | Workshop facilitation (shared) |
| `react-frontend` | frontend | 🔴 Not written | React + TS, SignalR hooks, bid-feed state — author on first use |

**Status key:** ✅ Lean = migrated and up-referencing ai-skills · 📚 Reference = documents an unused
capability · 🟡 Placeholder = stub, fill on first real use · 🔴 Not written = create when first needed.

---

## Skills by task

Paths resolve to `<name>/SKILL.md`. The **Secondary** column is supporting context.

### Implementation

| Task | Primary | Secondary |
|---|---|---|
| Wolverine command handler | `wolverine-message-handlers` | `csharp-coding-standards` |
| Wolverine HTTP endpoint | `wolverine-message-handlers` | — |
| Saga (multi-step workflow) | `wolverine-sagas` | `integration-messaging` |
| Scheduled messages / timeouts | `wolverine-sagas` | — |
| Event-sourced aggregate (Marten) | `marten-event-sourcing` | `csharp-coding-standards` |
| Marten native projection | `marten-projections` | `marten-event-sourcing` |
| EF Core projection (Marten) | `marten-projections` | `marten-event-sourcing` |
| Read model query (LINQ / compiled / batched) | `marten-querying` | `csharp-coding-standards` |
| JSON streaming to HTTP response | `marten-querying` | `wolverine-message-handlers` |
| DCB boundary model | `dynamic-consistency-boundary` | `marten-event-sourcing` |
| Integration event (cross-BC) | `integration-messaging` | `domain-event-conventions` |
| SignalR hub + real-time push | `wolverine-signalr` | — |
| Broadcast live view via projection side effect | `projection-side-effects-for-broadcast-live-views` | `wolverine-signalr` |
| Derived domain event from projection state | `projection-side-effects-for-broadcast-live-views` | `marten-event-sourcing` |
| New BC module registration | `adding-bc-module` | `marten-event-sourcing` |

### Operations

| Task | Primary | Secondary |
|---|---|---|
| Wiring OpenTelemetry traces and metrics | `observability` | `aspire` |
| Choosing which messages to tag with `[Audit]` | `observability` | `wolverine-message-handlers` |
| Suppressing telemetry on health checks / keep-alives | `observability` | `wolverine-message-handlers` |
| Inspecting full app configuration | `diagnostics` | — |
| Debugging "why is my handler not firing?" | `diagnostics` | `wolverine-message-handlers` |
| Debugging "why is `tracked.Sent.MessagesOf<T>()` zero?" | `diagnostics` | `wolverine-message-handlers` |
| Inspecting retry / circuit-breaker policies | `diagnostics` | — |
| Schema drift check for CI / pre-deploy verification | `diagnostics` | `aspire` |

### Testing

| Task | Primary | Secondary |
|---|---|---|
| Integration test (Alba + Testcontainers) | `critter-stack-testing-patterns` | — |
| Unit test (pure handler logic) | `critter-stack-testing-patterns` | — |
| Marten BC test fixture | `critter-stack-testing-patterns` | `adding-bc-module` |
| **Cross-BC handler isolation** | **`critter-stack-testing-patterns`** | — |
| Saga test | `wolverine-sagas` | `critter-stack-testing-patterns` |
| Projection test | `marten-projections` | `critter-stack-testing-patterns` |
| SignalR integration test | `wolverine-signalr` | `critter-stack-testing-patterns` |
| Projection side-effect integration test | `projection-side-effects-for-broadcast-live-views` | `critter-stack-testing-patterns` |

### Frontend

| Task | Primary | Secondary |
|---|---|---|
| React component | `react-frontend` | — |
| SignalR client connection / real-time bid feed | `react-frontend` | `wolverine-signalr` |

### Design & architecture

| Task | Skill |
|---|---|
| Event Modeling workshop | `event-modeling` |
| Naming domain events | `domain-event-conventions` |
| Personas for workshop | [`../personas/README.md`](../personas/README.md) |
| Reactive / live-view architecture | [`../vision/live-queries-and-streaming.md`](../vision/live-queries-and-streaming.md) |

---

## Writing a new skill

1. Copy [`_template/`](./_template/SKILL.md) to `docs/skills/<name>/` and rename.
2. Implement the feature first — let the code reveal the real patterns.
3. Write only what diverges from upstream; up-reference the ai-skills that cover the generic mechanics.
4. Keep `SKILL.md` under ~500 lines; push deep-dives into `references/`.
5. Add the skill to the tables above.

---

## Relationship to ai-skills and the gap analysis

The map of what is CritterBids-specific (KEEP) versus absorbed-generic (up-reference) lives in
[`jasper-fx-ai-skills-gap-analysis.md`](./jasper-fx-ai-skills-gap-analysis.md): **Section 1** maps each
ai-skills file to CritterBids coverage; **Section 2** enumerates the intentional divergences each skill
preserves. Consult it before leaning a skill or when a skill seems incomplete.

The skills originated as extractions from the sibling CritterSupply project and were progressively
cross-referenced against ai-skills. That history is in git; this index tracks current state only.
