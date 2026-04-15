# CritterBids ADR Index

Architectural Decision Records (ADRs) capture significant decisions, the options considered, and the rationale for the chosen path. They are the canonical answer to "why does the system work this way?"

---

## Naming Convention

Files use a **zero-padded three-digit prefix**: `001`, `002`, ..., `009`, `010`, ..., `099`, `100`.

```
docs/decisions/007-uuid-strategy.md      ✅ correct
docs/decisions/0007-uuid-strategy.md     ❌ four-digit — do not use
docs/decisions/7-uuid-strategy.md        ❌ no padding — do not use
```

The next ADR is **`012-<slug>.md`**. Check this index before creating one to confirm the next available number.

---

## ADR Status Ledger

| # | Title | Status | Date | Summary |
|---|---|---|---|---|
| [001](001-modular-monolith.md) | Modular Monolith Architecture | ✅ Accepted | 2026-04 | Single deployable unit; BCs as isolated .NET class libraries; no BC-to-BC project references |
| [002](002-rabbitmq-first.md) | RabbitMQ as Initial Message Transport | ✅ Accepted | 2026-04 | RabbitMQ for MVP; transport-agnostic by design; planned swap to Azure Service Bus as a conference demo milestone |
| [003](003-polecat-bcs.md) | Polecat BCs — Operations, Settlement, Participants | ✅ Accepted | 2026-04 | SQL Server via Polecat for the three enterprise-adjacent BCs; all others use PostgreSQL via Marten |
| [004](004-react-frontend.md) | React for Frontend Applications | ✅ Accepted | 2026-04 | React + TypeScript for both SPAs; demonstrates that .NET backends pair naturally with non-Microsoft frontends |
| [005](005-contract-versioning.md) | Integration Event Contract Versioning Policy | ✅ Accepted | 2026-04 | Additive-only changes first; upcasting for breaking changes; versioned type names as last resort |
| [006](006-infrastructure-orchestration.md) | Infrastructure Orchestration | ✅ Accepted | 2026-04 | .NET Aspire AppHost is the single local-dev orchestration path; no `docker-compose.yml` deliverable |
| [007](007-uuid-strategy.md) | UUID Strategy for Stream IDs and Event Row IDs | 🟡 Proposed | 2026-04-13 | UUID v5 where a natural business key exists, v7 otherwise; event row ID strategy pending acceptance gates (Marten 8 / Polecat 2 capability check + JasperFx team input at M3) |
| [008](008-marten-bc-isolation.md) | Marten BC Isolation: Named Stores per BC | ~~Superseded by 009~~ | 2026-04-14 | Named stores via `AddMartenStore<T>()` — superseded when ancillary store API was found to omit critical Wolverine registrations |
| [009](009-shared-marten-store.md) | Shared Primary Marten Store | ✅ Accepted | 2026-04-14 | Single primary `IDocumentStore` in `Program.cs`; each Marten BC contributes its types via `services.ConfigureMarten()` inside `AddXyzModule()` |
| [010](010-wolverine-dual-store-resolution.md) | Wolverine Dual-Store Resolution | ~~Resolved by ADR 011~~ | 2026-04-15 | Both `AddMarten().IntegrateWithWolverine()` and `AddPolecat().IntegrateWithWolverine()` claim "main" store; Polecat has no ancillary-store API; production Aspire start blocked — resolved by removing the dual-store scenario (ADR 011) |
| [011](011-all-marten-pivot.md) | All-Marten Pivot | ✅ Accepted | 2026-04-15 | Migrate Participants, Settlement, Operations from Polecat/SQL Server to Marten/PostgreSQL; eliminates dual-store conflict; all 8 BCs use uniform bootstrap pattern; supersedes ADR 003 |

**Status key:** ✅ Accepted · 🟡 Proposed (acceptance gates open) · ~~Superseded~~

---

## Superseded Chain

```
ADR 008 (Named Marten Stores)
  └─ superseded by ADR 009 (Shared Primary Marten Store)

ADR 003 (Polecat BCs)
  └─ superseded by ADR 011 (All-Marten Pivot)

ADR 010 (Wolverine Dual-Store Resolution)
  └─ resolved/superseded by ADR 011 (scenario eliminated)
```

---

## When to Write an ADR

Write one when a decision is:

- **Hard to reverse** — storage engine choice, event schema policy, transport strategy
- **Cross-cutting** — affects multiple BCs, agents, or contributors who can't infer the rationale from the code alone
- **Made after evaluating alternatives** — if you considered two or more options and chose one, the reasoning is worth preserving
- **Likely to surprise someone** — if a future agent would reasonably question the approach without context

**Do not write an ADR for:**

- Implementation details confined to a single BC (comment or skill instead)
- Naming conventions and code style (those belong in `docs/skills/csharp-coding-standards.md`)
- Decisions that are obvious from the chosen stack (e.g. "we use `sealed record` for commands")

---

## References

- `CLAUDE.md` — documentation hierarchy and routing
- `docs/skills/README.md` — skill index (implementation patterns)
