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

The next unreserved ADR number is **`017-<slug>.md`**. ADR 014 is reserved for M4-S6 authorship
(Cross-BC read-model extension shape — draft body lands alongside the second lived Path A
application). ADR 015 is reserved for conditional authorship at M4-S7 or earlier if M4-S1
resolved M4-D4 to the cross-BC read option (Cross-BC read access from handlers). Check this
index before creating one to confirm the next available number.

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
| [007](007-uuid-strategy.md) | UUID Strategy for Stream IDs and Event Row IDs | ✅/⏸ Stream IDs: Accepted; Event Row IDs: Re-Deferred (M4-S1) | 2026-04-13 / 2026-04-16 / 2026-04-20 | UUID v7 for all Marten BC stream IDs (Accepted). Event row ID strategy re-deferred at M4-S1 second re-evaluation — Gate 1 (Marten 8 row-ID seam) still unconfirmed, Gate 4 (JasperFx input) still pending; M3 shipped on engine default without incident; new trigger is M5-S1 (Settlement BC foundation decisions), named owner Erik for the JasperFx follow-up nudge. Default remains engine-assigned row IDs. Gate 2 closed per ADR 011. |
| [008](008-marten-bc-isolation.md) | Marten BC Isolation: Named Stores per BC | ~~Superseded by 009~~ | 2026-04-14 | Named stores via `AddMartenStore<T>()` — superseded when ancillary store API was found to omit critical Wolverine registrations |
| [009](009-shared-marten-store.md) | Shared Primary Marten Store | ✅ Accepted | 2026-04-14 | Single primary `IDocumentStore` in `Program.cs`; each Marten BC contributes its types via `services.ConfigureMarten()` inside `AddXyzModule()` |
| [010](010-wolverine-dual-store-resolution.md) | Wolverine Dual-Store Resolution | ~~Resolved by ADR 011~~ | 2026-04-15 | Both `AddMarten().IntegrateWithWolverine()` and `AddPolecat().IntegrateWithWolverine()` claim "main" store; Polecat has no ancillary-store API; production Aspire start blocked — resolved by removing the dual-store scenario (ADR 011) |
| [011](011-all-marten-pivot.md) | All-Marten Pivot | ✅ Accepted | 2026-04-15 | Migrate Participants, Settlement, Operations from Polecat/SQL Server to Marten/PostgreSQL; eliminates dual-store conflict; all 8 BCs use uniform bootstrap pattern; supersedes ADR 003 |
| [012](012-frontend-spa-vite.md) | Frontend: Vite SPA, Not a Meta-Framework | ✅ Accepted | 2026-04-19 | React frontends ship as static Vite SPAs; no Next.js, Remix framework mode, or TanStack Start; backend owns all contracts; extends ADR 004 |
| [013](013-frontend-core-stack.md) | Frontend Core Stack | 🟡 Proposed | 2026-04-19 | TypeScript strict, Zod, TanStack Query, Tailwind v4 + shadcn/ui, react-hook-form, `@microsoft/signalr`, Vitest + Playwright, PWA from day one; routing and auth client pattern deferred |
| 014 | Cross-BC Read-Model Extension Shape | 🔒 Reserved for M4-S6 authorship | — | Reserved in M4-S1. ADR body authored at M4-S6 alongside the second lived application of the M3-D2 Path A pattern (Listings `CatalogListingView` extended with session-membership and withdrawn fields). Do not draft before M4-S6 — the lived second application is the evidence. |
| [016](016-spec-anchored-development.md) | Spec-Anchored Development | ✅ Accepted | 2026-04-25 | Event Model + narratives in `docs/narratives/` are the architectural reference; code is authoritative for runtime behavior; drift caught at retrospective time. Applies to lived M1 to M4 code retroactively via the Phase 2 findings discipline (foundation refresh). |

**Status key:** ✅ Accepted · 🟡 Proposed (acceptance gates open) · ⏸ Deferred (trigger set) · 🔒 Reserved (number held; body authored later) · ~~Superseded~~

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
