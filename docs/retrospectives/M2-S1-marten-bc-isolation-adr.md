# M2-S1: Marten BC Isolation ADR — Retrospective

**Date:** 2026-04-14
**Milestone:** M2 — Listings Pipeline
**Slice:** S1 — Marten BC isolation decision
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M2-S1-marten-bc-isolation-adr.md`

## Baseline

- Solution builds clean; 8 tests pass (M1 close state: Participants 4, Api 2, Contracts 2).
- `docs/decisions/` contained `001-modular-monolith.md` through `006-infrastructure-orchestration.md` plus `0001-uuid-strategy.md`. No `0002-*` file existed.
- `docs/milestones/M2-listings-pipeline.md` §8 M2-D1 disposition contained hedging language: "S1 may identify that named stores are needed." No ADR authored yet.
- `docs/milestones/M2-listings-pipeline.md` §9 S1 row already carried the correct prompt filename (`docs/prompts/implementations/M2-S1-marten-bc-isolation-adr.md`) from the initial milestone commit; no update was required there.
- No `docs/retrospectives/M2-*` file existed.

## Items completed

| Item | Description |
|------|-------------|
| S1a | Authored `docs/decisions/0002-marten-bc-isolation.md` as Accepted ADR |
| S1b | Updated `docs/milestones/M2-listings-pipeline.md` §8 M2-D1 disposition to reflect the ADR's decision |

## S1a: ADR 0002 — decision rationale

**Decision:** One named Marten store per BC, registered via `AddMartenStore<IBcDocumentStore>()`.

**Why named stores over shared `AddMarten()`.**
Two variants of Option A (shared store) were evaluated and both fail for CritterBids' constraints:

- *Shared `AddMarten()` with `ConfigureMarten()` contributions:* `DatabaseSchemaName` is a store-level property. One store produces one `mt_events` table in one schema. Multiple BCs contributing to the same store share that table — BC event-stream isolation is not achievable.
- *Separate `AddMarten()` calls per BC (the §5 working assumption):* `AddMarten()` registers `IDocumentStore` as a singleton. A second call produces a competing singleton registration; DI resolves one and silently discards the other. This is the working assumption from `M2-listings-pipeline.md` §5. It is broken for multi-BC use.

Option B (named stores via `AddMartenStore<T>()`) is the only path that gives each BC its own `mt_events` table while keeping configuration fully enclosed within `AddXyzModule()`.

**Context7 verification findings.**
The prompt required Marten and Wolverine API verification before drafting the Options section. Key findings:

- `AddMartenStore<T>().IntegrateWithWolverine()` chains correctly; named stores participate in the transactional outbox. This was the blocking verification — if named stores could not satisfy the `OutgoingMessages` pattern, Option B would have been a blocking finding.
- `AddMartenStore<T>().ApplyAllDatabaseChangesOnStartup()` chains correctly; schema objects apply per-store at startup.
- Wolverine handlers require the `[MartenStore(typeof(IBcDocumentStore))]` attribute to receive a session from the correct named store. Without it, Wolverine will not route to the named store. This is a non-obvious requirement that every Marten BC handler in S2+ must satisfy.
- When named stores share a PostgreSQL server, `opts.Durability.MessageStorageSchemaName` in the host Wolverine configuration routes envelope rows to a shared schema, preventing envelope table duplication.

**Default `IDocumentStore` — intentionally absent.**
No BC calls `AddMarten()`; `IDocumentStore` is not registered. This is an explicit design choice. Any component resolving `IDocumentStore` directly will fail at startup. The ADR records this as intentional rather than an oversight.

**§5 working assumption correction.**
The `M2-listings-pipeline.md` §5 code example shows `AddMarten()` per BC with `DatabaseSchemaName` set. This pattern is incorrect for a multi-BC process and is corrected by the ADR. S2 must use `AddMartenStore<ISellingDocumentStore>()`. The builder chain shape (`.ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()`) is unchanged; only the registration method and BC marker interface are new.

## S1b: Milestone doc update

Updated §8 M2-D1 from the pre-ADR hedging language ("S1 may identify that named stores are needed") to a clear disposition reflecting the ADR's outcome: named stores required, working assumption corrected, with a pointer to `docs/decisions/0002-marten-bc-isolation.md`.

§9 S1 row required no update — the prompt filename was already correct from the initial milestone commit.

## API surface explored

| Question | Finding |
|---|---|
| `AddMartenStore<T>().IntegrateWithWolverine()` works? | Yes — confirmed via Context7 |
| Transactional outbox works on named stores? | Yes — outbox available across all configured Marten stores |
| `ApplyAllDatabaseChangesOnStartup()` on named store? | Yes — chains identically to `AddMarten()` |
| Wolverine handler injection for named stores? | Requires `[MartenStore(typeof(IBcDocumentStore))]` attribute explicitly |
| Two `AddMarten()` calls in one process? | DI conflict — second call registers competing singleton, first BC's config silently lost |
| Default `IDocumentStore` when no `AddMarten()` called? | Not registered; intentionally absent in CritterBids |
| Shared envelope tables across named stores? | `opts.Durability.MessageStorageSchemaName` in host Wolverine config routes all stores to shared schema |

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Session open (baseline) | 8 | Pass |
| Session close | 8 | Pass — no code changed |

## Build state at session close

- Errors: 0
- Warnings: 0 (no code changed)
- `.csproj` files created or modified: 0
- Source files created or modified: 0
- `docs/` files created: 2 (`0002-marten-bc-isolation.md`, this retrospective)
- `docs/` files modified: 1 (`M2-listings-pipeline.md` §8 M2-D1 row)

## Key learnings

1. **Two `AddMarten()` calls in one process is a silent DI conflict.** The second call registers a competing `IDocumentStore` singleton; the container resolves one and discards the other. This would have caused M2 to silently lose one BC's entire Marten configuration with no error at startup. The ADR session exists precisely to catch this class of issue before code is written.
2. **Named store handlers require `[MartenStore]` explicitly.** Wolverine does not infer the named store from the handler's parameter type. Every Marten BC handler in S2+ must carry this attribute or sessions will not route correctly.
3. **The `OutgoingMessages` outbox pattern gates the isolation model choice.** Option B would have been a blocking finding — and the session would have escalated rather than guessed — if `IntegrateWithWolverine()` had not worked on named stores. Verifying integration compatibility before committing to an isolation model is the right order of operations.
4. **ADR numbering inconsistency is unresolved.** The repo has two schemes: three-digit (`001`–`006`) and four-digit (`0001`–`0002`). This ADR uses four-digit to match `0001-uuid-strategy.md`. The inconsistency should be addressed in M2-S7's skills pass — either rename `001`–`006` to four-digit, or adopt a `docs/decisions/README.md` index that absorbs the visual noise.

## Verification checklist

- [x] `docs/decisions/0002-marten-bc-isolation.md` exists
- [x] ADR status is **Accepted**
- [x] ADR Decision section names exactly one isolation pattern in one sentence
- [x] ADR Context section references the CritterBids modular monolith constraint and ADR 003 (Polecat BCs) as the isolation precedent
- [x] ADR Options section covers both Option A and Option B with CritterBids-specific evaluation (not generic Marten multi-store discussion). Evaluation grounded in verified Marten API behaviour (Context7 research completed before drafting)
- [x] ADR Consequences section describes the resulting module registration pattern in prose
- [x] ADR Consequences section confirms UUID v7 for Marten BC stream IDs and cross-references `0001-uuid-strategy.md`
- [x] ADR Consequences section addresses test fixture connection string override for named stores
- [x] ADR Consequences section names at least one item explicitly out of scope
- [x] `docs/milestones/M2-listings-pipeline.md` §8 M2-D1 disposition updated to reflect the working assumption was corrected (named stores required)
- [x] `docs/milestones/M2-listings-pipeline.md` §9 S1 row already correct — no update required
- [x] No files created or modified outside `docs/decisions/0002-marten-bc-isolation.md`, `docs/milestones/M2-listings-pipeline.md`, and this retrospective
- [x] No `.csproj`, `.slnx`, `.props`, or source files touched
- [x] `dotnet build` and `dotnet test` still pass — no code changed, trivially true

## What remains / next session should verify

- **S2 must use `AddMartenStore<ISellingDocumentStore>()`**, not `AddMarten()`. The §5 working assumption code example is now superseded by the ADR; S2's scaffold must implement the corrected pattern.
- **`[MartenStore(typeof(ISellingDocumentStore))]` attribute on all Selling BC handlers.** S2 or S3 must verify this is present on every handler that injects a Marten session.
- **Host-level `MessageStorageSchemaName` configuration.** The shared envelope schema setting belongs in `Program.cs` Wolverine configuration. S2 or S3 must add it when the first named store is wired in.
- **ADR numbering inconsistency** (`001`–`006` vs `0001`–`0002`) flagged for M2-S7 skills pass.
- **`0001-uuid-strategy.md` stays Proposed** through M2. Promotion gates re-evaluated at M3 (Auctions BC — the high-write motivation for UUID v7 insert locality).
