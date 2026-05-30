---
name: critter-stack-ancillary-stores
description: "Ancillary stores in CritterBids: ADR 009 reference posture and Marten/Polecat routing asymmetry. Use when evaluating AddMartenStore or multi-store BC isolation."
cluster: marten
tags: [marten, ancillary-stores, modular-monolith, reference, wolverine]
status: reference
---

# Critter Stack Ancillary Stores

> Reference guide for what CritterBids would need **if** ancillary stores were introduced.
> Generic ancillary-store mechanics live in ai-skills `marten-advanced-ancillary-stores`;
> **this skill documents only the CritterBids-specific ADR posture, migration gates, and open questions.**

Per ADR 009, CritterBids uses one primary `IDocumentStore` plus per-BC `services.ConfigureMarten()` contributions. Do not introduce `AddMartenStore<T>()` without a superseding ADR.

## When to apply this skill

Use this skill when:

- Someone proposes `AddMartenStore<T>()`, named stores, or separate physical databases per BC.
- A future BC needs independent async daemon/subscription progress beyond schema-per-BC isolation.
- Reviewing historical ADR 008 vs ADR 009 tradeoffs.
- Discussing Marten/Polecat ancillary-store parity with JasperFx.

Do NOT use this skill for normal CritterBids module registration. Use `marten-event-sourcing` and `adding-bc-module` for the active single-store pattern.

## Read upstream first

Read this ai-skill (license required; install via `npx skills add`) before this skill — it covers ~80% of the generic mechanics:

1. `marten-advanced-ancillary-stores` — `AddMartenStore<T>`, marker stores, `[MartenStore]`, per-store projections/subscriptions, Wolverine integration, and testing helpers.

This skill picks up at CritterBids' decision posture: why ancillary stores are not used today and what must be true before that changes.

## Current CritterBids posture

| Topic | CritterBids decision |
|---|---|
| Store shape | One primary Marten store in `Program.cs` |
| BC isolation | Per-BC schemas/documents/projections via `services.ConfigureMarten()` |
| Physical databases | Shared PostgreSQL database today |
| Ancillary stores | Reference only; no active `AddMartenStore<T>()` registrations |
| Required change gate | Superseding ADR over ADR 009 |
| Polecat | Not active after ADR 011; parity question preserved for future reference |

Ancillary stores would be considered only for needs the current pattern cannot satisfy: separate physical databases, independent daemon/subscription progress per module, per-tenant database topologies, or migration from a microservice split where database isolation is non-negotiable.

## If ancillary stores were introduced

CritterBids would need an ADR and an implementation plan that answers these points before code changes:

1. **Why schema-per-BC is insufficient.** Code hygiene alone is not enough; ADR 009 already gives that with less operational cost.
2. **Primary-store requirement.** Keep a primary `AddMarten(...)` so Wolverine retains `IDocumentSession`, `[Entity]`, `IStorageAction<T>`, `MartenOps`, and `AutoApplyTransactions()` idioms.
3. **Store marker naming.** One marker interface per logical store, e.g. `IAuctionsStore`, and no BC-to-BC project references.
4. **Handler routing.** Every handler targeting a named Marten store must use `[MartenStore(typeof(IXyzStore))]`; Wolverine does not infer this from document types or namespaces.
5. **Outbox/envelope schema.** Configure one shared Wolverine envelope schema at host level instead of letting every store create its own envelope tables.
6. **Projection ownership.** Decide which projections/subscriptions move with each store and how rebuild/catch-up will be operated.
7. **Fixture rewrite.** Test fixtures must repeat any store-specific schema/projection registrations they override and clean the correct typed store.

## CritterBids findings to preserve

### ADR 008 → ADR 009 lesson

ADR 008's no-primary-store version forced handlers into marker-store injection, manual sessions, and explicit `SaveChangesAsync()`. That lost the standard Critter Stack handler idioms. ADR 009 corrected course by restoring a primary store and using `ConfigureMarten()` for BC contributions.

### `[MartenStore]` is routing, not decoration

Without `[MartenStore]`, `IDocumentSession` resolves to the primary store and the handler can silently write to the wrong schema. If named stores return, require codegen verification for every new handler chain that targets one.

### Testing replacements are complete replacements

A test fixture's `AddMartenStore<T>()` override replaces production registration. Repeat schema, projection, and subscription configuration or the fixture no longer represents production behavior.

### JasperFx open question #1 — Polecat parity

`[MartenStore]` exists for Marten ancillary handlers. Source review found no `[PolecatStore]` equivalent as of the gap-analysis review. Polecat ancillary stores expose registration parity (`AddPolecatStore<T>()`, `IConfigurePolecat<T>`, `.IntegrateWithWolverine()`), but handler-routing parity remains a JasperFx open question. Do not base a CritterBids architecture on `[PolecatStore]` unless the current source proves it exists.

## Common pitfalls

- **Adding ancillary stores for code organization only.** CritterBids already gets BC organization through modules and schemas.
- **Dropping the primary store.** This recreates the ADR 008 handler-shape regression.
- **Forgetting host-level envelope schema.** Multiple stores can otherwise create fragmented Wolverine tables.
- **Omitting `[MartenStore]`.** Silent writes to the primary store are worse than a loud startup failure.
- **Assuming Polecat has `[PolecatStore]`.** Verify source or ask JasperFx first.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `marten-advanced-ancillary-stores` — full ancillary Marten store mechanics.

**Prerequisites:**

- `marten-event-sourcing` — current shared-primary-store pattern.
- `adding-bc-module` — current BC registration shape.

**Downstream:**

- `critter-stack-testing-patterns` — fixture isolation and Marten cleanup patterns.
- `polecat-event-sourcing` — reference-only Polecat deltas if SQL Server returns.

**External:**

- ADR 008, ADR 009, ADR 011 in [`docs/decisions/`](../../decisions/) — named-store history, shared-store decision, all-Marten pivot.
- [`CLAUDE.md`](../../../CLAUDE.md) § Canonical Bootstrap Sequence.
