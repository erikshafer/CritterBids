# M2-S1: Marten BC Isolation ADR

**Milestone:** M2 — Listings Pipeline
**Slice:** S1 — Marten BC isolation decision
**Agent:** @PSA
**Estimated scope:** one PR, one new ADR

## Goal

Resolve how multiple Marten bounded contexts coexist in the same process — the fundamental
architectural question that governs every Marten BC added in M2 and beyond. Selling and Listings
are the first two Marten BCs; they arrive in the same session series and share a PostgreSQL
server. Without a committed isolation model, S2 cannot scaffold the Selling BC module.

This session produces the decision as an ADR, confirms UUID v7 as the stream ID convention for
Marten BCs, and sketches the resulting module registration pattern for reference in S2+. **No
code, no `.csproj` changes, no packages added, no infrastructure wiring of any kind** — this is
a documentation-only PR.

## Context to load

- `docs/milestones/M2-listings-pipeline.md` — the authoritative M2 milestone doc; §5 Infrastructure
  describes the working assumption this ADR confirms or corrects; §8 lists M2-D1 as the open question
  this session resolves
- `CLAUDE.md` — BC module quick-reference table, storage assignments per BC
- `docs/vision/bounded-contexts.md` — BC storage summary table (which BCs are Marten, which are
  Polecat); integration topology
- `docs/decisions/003-polecat-bcs.md` — precedent for BC-level storage isolation; Polecat's per-BC
  scheme is the target analogue for Marten
- `docs/decisions/006-infrastructure-orchestration.md` — format reference; ADR section structure,
  tone, and length to match
- `docs/decisions/0001-uuid-strategy.md` — related decision (Proposed); stream ID strategy this ADR
  must confirm for Marten BCs specifically
- `docs/skills/marten-event-sourcing.md` — Marten patterns relevant to BC module setup
- `docs/prompts/README.md` — the ten rules this prompt obeys

Additionally, **use Context7 to verify the Marten named-stores API before drafting the ADR**:

- Library ID: Marten → `/jasperfx/marten`
- Queries to research: how `AddMartenStore<T>()` works for multiple stores in one process; whether
  `AddMarten()` called twice from separate modules conflicts in DI; how `IntegrateWithWolverine()`
  behaves on named vs. default stores; what `ApplyAllDatabaseChangesOnStartup()` does on a named store

The ADR's Options section must be grounded in verified Marten API behaviour, not assumptions.

## In scope

### `docs/decisions/0002-marten-bc-isolation.md` — new ADR

Author the ADR with status **Accepted**. The point of this session is to commit to one pattern, not
to propose further research.

The ADR must cover at minimum:

**Context** — Why this decision is needed now. Two Marten BCs (Selling, Listings) arrive in M2.
CritterBids is a modular monolith: all BCs run in one process. Each BC module registers itself via
`AddXyzModule()` on `IServiceCollection`. No BC project references another BC project. The isolation
goal is the same as Polecat BCs (ADR 003): each BC owns its own schema; no cross-BC schema access.
Reference the Polecat precedent explicitly.

**Options considered** — Evaluate the two realistic choices through the lens of CritterBids' actual
constraints (modular monolith, module-per-BC registration, no BC cross-reference, schema isolation
requirement). Do not frame this as a generic Marten multi-tenancy discussion. Specifically:

- **Option A — Shared `AddMarten()` with per-BC schema.** A single default Marten store registered
  somewhere central (e.g. in `Program.cs`), with BCs contributing their schema names, event stream
  registrations, and projections to it. Evaluate: does this break module encapsulation? Does it
  require `Program.cs` to know BC internals? Can `AddXyzModule()` still own its own Marten
  configuration, or must it delegate to a shared registration?

- **Option B — Named stores via `AddMartenStore<T>()`.** Each BC defines a BC-specific store
  interface (e.g. `ISellingDocumentStore : IDocumentStore`) and registers it via
  `AddMartenStore<ISellingDocumentStore>()` inside `AddSellingModule()`. The BC's handlers inject
  the typed store. Evaluate: does this work with `IntegrateWithWolverine()`? Does
  `ApplyAllDatabaseChangesOnStartup()` chain correctly? What does the injection point look like in
  Wolverine handler signatures? Does Testcontainers override work at the named-store level?

Verify option correctness against the Marten API via Context7 before writing this section.

**Decision** — One option, named in one sentence, with a one-sentence rationale. No equivocation.

**Consequences** — At minimum:
- The module registration pattern that follows from the decision (a prose description — no C# code
  blocks, per rule 8)
- What S2 (Selling BC scaffold) must do differently than the working assumption in
  `M2-listings-pipeline.md` §5 if the ADR corrects that assumption, or a confirmation that the
  working assumption holds
- How test fixtures override connection strings for named stores (relevant for S2 and S6 test
  infrastructure)
- Whether the default `IDocumentStore` is available or consumed by one BC's module (if Option B is
  chosen — the first-consumer question)
- At least one item explicitly out of scope for this ADR (e.g. Marten async daemon configuration,
  EF Core projection setup — those are later sessions)

### UUID v7 for Marten BCs — confirm in ADR Consequences

The Consequences section confirms that Marten BC stream IDs use UUID v7 (`Guid.CreateVersion7()`)
per the convention pinned in `M2-listings-pipeline.md` §6. The ADR does not re-litigate the v5 vs
v7 question (that is `0001-uuid-strategy.md`'s scope); it simply records that Marten BCs use v7,
consistent with the Proposed ADR. Include a cross-reference to `0001-uuid-strategy.md`.

### `docs/milestones/M2-listings-pipeline.md` — minor updates

- Update §8 M2-D1 from "Resolved in S1 (ADR 0002). Working assumption..." to reflect whether the
  working assumption in §5 was confirmed or corrected.
- Update §9 S1 row from the prompt filename placeholder to `docs/prompts/implementations/M2-S1-marten-bc-isolation-adr.md`
  (this file). The milestone doc was written before the prompt file existed; the row needs the
  actual filename.

## Explicitly out of scope

- **No code.** No `.csproj` files created or modified. No `Program.cs` edits. No package additions.
- **No new BC projects.** `CritterBids.Selling` and `CritterBids.Listings` are M2-S2 and M2-S6.
- **No existing ADR edits** beyond what is referenced above. `0001-uuid-strategy.md` stays Proposed;
  its promotion gates are M3 concerns.
- **No CLAUDE.md updates.** The `AutoApplyTransactions` wording cleanup is M2-S7's job.
- **No other milestone doc edits** beyond the two line-items in §8 and §9 of
  `M2-listings-pipeline.md` described above.
- **No module pattern code.** The ADR describes the pattern in prose. S2 writes the first
  instance of actual code.
- **No Testcontainers or test fixture code.** The ADR's Consequences section describes how the
  override works; S2's test fixture implements it.
- **No EF Core projections, no async daemon config, no Marten multi-tenancy** (distinct from
  multi-store — CritterBids does not use Marten multi-tenancy).
- **No CI workflow changes.**

## Conventions to pin or follow

- ADR format matches `006-infrastructure-orchestration.md`: same section headings, same tone, same
  approximate length. Read it before drafting.
- ADR status is **Accepted** at session close. Not Proposed.
- ADR Decision section contains exactly one option in one sentence. No "we will try X and revisit."
- **ADR numbering:** Use `0002-marten-bc-isolation.md` — four-digit prefix, matching
  `0001-uuid-strategy.md`. See Open Questions for the numbering inconsistency note.
- No C# code blocks anywhere in the ADR (rule 8 — code is what sessions produce, not what they
  start with; a prose module-pattern description is sufficient and forward-looking).

## Acceptance criteria

- [ ] `docs/decisions/0002-marten-bc-isolation.md` exists.
- [ ] ADR status is **Accepted**.
- [ ] ADR Decision section names exactly one isolation pattern in one sentence.
- [ ] ADR Context section references the CritterBids modular monolith constraint and ADR 003
      (Polecat BCs) as the isolation precedent.
- [ ] ADR Options section covers both Option A and Option B with CritterBids-specific evaluation
      (not generic Marten multi-store discussion). Evaluation is grounded in verified Marten API
      behaviour (Context7 research completed before drafting).
- [ ] ADR Consequences section describes the resulting module registration pattern in prose.
- [ ] ADR Consequences section confirms UUID v7 for Marten BC stream IDs and cross-references
      `0001-uuid-strategy.md`.
- [ ] ADR Consequences section addresses test fixture connection string override for named stores.
- [ ] ADR Consequences section names at least one item explicitly out of this ADR's scope.
- [ ] `docs/milestones/M2-listings-pipeline.md` §8 M2-D1 disposition updated to reflect
      whether the working assumption in §5 was confirmed or corrected by the ADR.
- [ ] `docs/milestones/M2-listings-pipeline.md` §9 S1 row updated to
      `docs/prompts/implementations/M2-S1-marten-bc-isolation-adr.md`.
- [ ] No files created or modified outside `docs/decisions/0002-marten-bc-isolation.md`,
      `docs/milestones/M2-listings-pipeline.md`, and this session's retrospective.
- [ ] No `.csproj`, `.slnx`, `.props`, or source files touched.
- [ ] `dotnet build` and `dotnet test` still pass from a clean state (no code changed, so this
      is a sanity check).

## Open questions

- **ADR numbering inconsistency.** The repo has two coexisting numbering schemes:
  three-digit (`001-modular-monolith.md` through `006-infrastructure-orchestration.md`) and
  four-digit (`0001-uuid-strategy.md`, established in M1-S5 following the milestone doc's lead
  despite M1-S2's prompt recommending three-digit). The `M2-listings-pipeline.md` milestone doc
  already names this ADR as `0002-marten-bc-isolation.md`, so use the four-digit scheme for
  consistency with `0001-uuid-strategy.md`. Flag the inconsistency in the retrospective so it can
  be addressed (a rename or a decision to stick with four-digit going forward) at M2-S7's skills
  pass.

- **Named store and `IntegrateWithWolverine()` compatibility.** If Option B (named stores) is chosen,
  verify via Context7 whether `IntegrateWithWolverine()` chains correctly on a named store builder
  (`AddMartenStore<T>().IntegrateWithWolverine()`), and whether a named store participates in
  Wolverine's transactional outbox. If named stores cannot integrate with Wolverine's outbox, that
  is a blocking finding — flag and stop rather than choosing an option that cannot satisfy the
  `OutgoingMessages` pattern CritterBids requires.

- **Default `IDocumentStore` allocation.** If Option B is chosen, confirm whether the default
  `IDocumentStore` (registered by `AddMarten()`) remains available to non-BC code (e.g. Wolverine
  itself, saga storage if a future Marten-backed saga uses the default store). If every BC uses a
  named store and nothing calls `AddMarten()`, is there a default store conflict? Document the
  answer in the ADR Consequences section.

- **`ApplyAllDatabaseChangesOnStartup()` on named stores.** Verify whether this method chains on
  the named store builder exactly as it does on the default store builder
  (`AddMartenStore<T>(...).ApplyAllDatabaseChangesOnStartup()`), and whether it runs at application
  startup when multiple named stores are registered. If behaviour differs, the Consequences section
  must describe it.

- **If Context7 research reveals a third option** that is clearly superior to both A and B for
  CritterBids' constraints, the agent should document it as Option C rather than silently adopting
  it. Raise it as an open question in the PR for review before committing to it. Rule 7: escalate
  design decisions rather than guessing.

