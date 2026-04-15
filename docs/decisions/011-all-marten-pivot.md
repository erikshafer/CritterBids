# ADR 011 — All-Marten Pivot: Migrate Participants, Settlement, and Operations to PostgreSQL

**Status:** Accepted
**Date:** 2026-04-15
**Supersedes:** ADR 003 — Polecat BCs (Operations, Settlement, Participants)
**Resolves:** ADR 010 — Wolverine Dual-Store Resolution (blocker closed; scenario no longer exists)

---

## Context

### The production blocker

ADR 010 documented an `InvalidWolverineStorageConfigurationException` that fires on every
production startup when both PostgreSQL and SQL Server connection strings are present:

```
There must be exactly one message store tagged as the 'main' store.
Found multiples:
  wolverinedb://sqlserver/127.0.0.1/master/wolverine,
  wolverinedb://postgresql/127.0.0.1/postgres/wolverine
```

Root cause: `AddMarten().IntegrateWithWolverine()` and `AddPolecat().IntegrateWithWolverine()` both
unconditionally register as the Wolverine "main" message store. Wolverine requires exactly one.
Polecat 5.30.0 exposes no ancillary-store API equivalent to Marten's, and the EF Core ancillary
pattern confirmed by Jeremy Miller's 2025 blog post does not extend to Polecat without modifying
BC module files. ADR 010 deferred resolution pending JasperFx input.

The project is not waiting for a JasperFx fix.

### The north star evidence

A full analysis of the CritterStackSamples reference repository (`C:\Code\JasperFx\critter-stack-samples-analysis.md`)
examined 12 sample projects covering every significant Critter Stack use case. The findings are
unambiguous:

- Every single sample — without exception — uses one `AddMarten(...).IntegrateWithWolverine().UseLightweightSessions()` chain.
- Zero samples use a mixed PostgreSQL + SQL Server backend in a single process.
- Every idiomatic Critter Stack pattern — `IDocumentSession` injection, `AutoApplyTransactions()`,
  `[Entity]` declarative loading, `[AggregateHandler]`, `Validate`/`ValidateAsync` railway
  programming, `CleanAllMartenDataAsync()` in tests — depends on the primary store being Marten.
- The modular monolith samples (four of them) all share a single `AddMarten()` in `Program.cs`
  with each module contributing its document types and projections via `ConfigureMarten()`.

### The project's purpose

CritterBids is both a conference demo vehicle and a reference architecture for idiomatic Critter Stack
development. Its value as a showcase depends on every BC demonstrating the same uniform, idiomatic
patterns. A codebase where three of eight BCs cannot use `IDocumentSession`, `[Entity]`, or
`CleanAllMartenDataAsync()` cannot fulfil that purpose.

---

## Options Considered

### Option A — All-Marten: migrate Participants, Settlement, Operations to Marten/PostgreSQL

Drop the Polecat/SQL Server storage layer. All eight BCs use Marten and PostgreSQL. Each BC
contributes via `services.ConfigureMarten()` inside its `AddXyzModule()`. `Program.cs` contains
exactly one `AddMarten().IntegrateWithWolverine().UseLightweightSessions()` chain.

**Evaluation:** The dual-store conflict ceases to exist. The CritterStackSamples north star applies
uniformly to every BC. Every idiomatic Critter Stack pattern works out of the box for every BC
without exception. The modular monolith structure is fully preserved — ADR 001 is unaffected.
Participants BC has six passing integration tests that define the acceptance criteria for a dedicated
migration session.

**The motivations that drove ADR 003 (BI tooling access, SQL Server compliance, finance team access)
are deferred to a future milestone.** CritterBids is not yet at the milestone where those
capabilities are needed, and none of the reference samples demonstrate multi-engine patterns. If
demonstrating Polecat alongside Marten is valuable for a future milestone or as a stretch goal, the
migration can be reversed — Wolverine's message-based BC communication means storage engine changes
are registration-level concerns, not business logic refactors (as ADR 003 itself noted in its
Stretch Goal section).

**Decision: chosen.**

---

### Option B — Microservices split: separate deployable hosts by storage backend

Split CritterBids into two hosts: one for Marten BCs, one for Polecat BCs. Each host has exactly
one store and no dual-store conflict.

**Evaluation:** This option partially supersedes ADR 001 (Modular Monolith). The modular monolith
architecture was chosen specifically to avoid the operational overhead of microservices — service
discovery, network latency, distributed tracing, container orchestration — for a conference demo on
a single Hetzner VPS. Splitting into two hosts reintroduces that overhead.

More critically: zero of the CritterStackSamples microservices samples (`EcommerceMicroservices`)
use a mixed Marten + Polecat backend. The microservices sample also uses Marten exclusively. Option B
would introduce a split that no reference sample demonstrates and that the project's operational
constraints explicitly ruled out in ADR 001.

The Participants BC seam (seller registration verification, bidder identity) is consumed in-process
by the Selling BC via `ISellerRegistrationService`. Moving Participants to a separate host turns
this in-process call into a network call — more complex to implement and test, with no corresponding
benefit at this project phase.

**Decision: rejected.**

---

### Option C — Defer: develop Marten BCs only until JasperFx resolves the ancillary API

Guard-clause the Polecat block in `Program.cs`. Participants runs in degraded mode. Resume Polecat
BC development when the ancillary API exists.

**Evaluation:** The project is not waiting for a JasperFx fix. This option was already ruled out
by project direction before this session; it is documented here so the decision record is complete.
Option C leaves the production Aspire crash unresolved indefinitely and prevents any BC from being
demoed in its full pipeline context.

**Decision: rejected.**

---

## Decision

All eight CritterBids bounded contexts use **PostgreSQL via Marten**. The Polecat/SQL Server storage
layer is removed. ADR 003 is superseded. ADR 010's blocker is resolved by eliminating the
dual-store scenario entirely.

---

## Consequences

### What changes in Program.cs

The two `AddMarten()` / `AddPolecat()` guard blocks in `Program.cs` are consolidated into a single
`AddMarten(...).IntegrateWithWolverine().UseLightweightSessions()` chain with no connection-string
guard. All eight `AddXyzModule()` calls are unconditional. `AddParticipantsModule()` is removed from
the SQL Server guard and replaced with a Marten-backed implementation in a subsequent session.

### What the Participants BC migration session must deliver

The Participants BC currently has a working Polecat implementation with six passing integration tests
(`ParticipantRegistered`, `SessionStarted`, `SellerRegistrationCompleted`, etc.). The migration
session must:

- Replace `AddPolecat()` with `services.ConfigureMarten()` inside `AddParticipantsModule()`
- Replace all `PolecatOps.StartStream()` calls with `MartenOps.StartStream()` equivalents
- Replace `IDocumentSession` (Polecat) usage with standard Marten `IDocumentSession` injection
- Migrate `ParticipantsTestFixture` from `MsSqlBuilder` + `AddParticipantsModule(connectionString)` to
  `PostgreSqlBuilder` + the standard Marten BC fixture pattern
- Confirm all six existing Participants integration tests pass against the Marten implementation

The migration does not change any handler business logic — only the storage registration and stream
start pattern change.

### Which existing tests need to change before the migration PR

No tests need to change before the migration PR. The current `SellingTestFixture` (Marten-only) and
`ParticipantsTestFixture` (Polecat-only) each provision exactly one backend and are unaffected by
this ADR. After the migration PR, `ParticipantsTestFixture` becomes a Marten BC fixture.

### The restored uniform bootstrap pattern

After the migration PR, `Program.cs` contains exactly:

- One `AddMarten(...).IntegrateWithWolverine().UseLightweightSessions()` chain
- One `UseWolverine(opts => { opts.Policies.AutoApplyTransactions(); ... })` block
- One `AddWolverineHttp()` call
- One `app.MapWolverineEndpoints()` call
- Eight unconditional `AddXyzModule()` calls

This matches the north star pattern confirmed by all twelve CritterStackSamples.

### ADRs superseded or affected

| ADR | Effect |
|---|---|
| ADR 003 — Polecat BCs | **Superseded.** Polecat BCs are migrated to Marten. |
| ADR 001 — Modular Monolith | **Unaffected.** The modular monolith structure is preserved; only the storage layer changes. |
| ADR 010 — Dual-Store Resolution | **Resolved.** The dual-store scenario no longer exists after migration. ADR 010 transitions from Proposed to superseded by this ADR. |
| ADR 009 — Shared Primary Marten Store | **Extended.** ADR 009 established the single-store pattern for Marten BCs; this ADR extends it to all eight BCs. |

### Skills affected

`docs/skills/polecat-event-sourcing.md` is archived as inactive. Its content documents valid Polecat
patterns that may be valuable for a future Polecat-showcase project; it is retained in the repository
under the archived status. `docs/skills/README.md` is updated accordingly.

### Stretch goal preserved

ADR 003's "Polecat ↔ Marten swap" stretch goal (**M-polecat-marten-swap**) is preserved as a future
milestone. The goal was to demonstrate that storage engine swaps are registration-level concerns —
this ADR is, itself, that demonstration. The swap can be reversed or extended in a future milestone
if Polecat/SQL Server showcase value becomes a project priority.

---

## References

- ADR 001 — Modular Monolith Architecture (unaffected)
- ADR 003 — Polecat BCs (superseded)
- ADR 009 — Shared Primary Marten Store (extended)
- ADR 010 — Wolverine Dual-Store Resolution (resolved)
- `C:\Code\JasperFx\critter-stack-samples-analysis.md` — north star evidence (§1, §2, §9, §11, §14)
- `docs/skills/polecat-event-sourcing.md` — archived; patterns remain valid reference material
- `src/CritterBids.Participants/` — Polecat implementation to be migrated in subsequent session
