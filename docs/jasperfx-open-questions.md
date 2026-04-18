# JasperFx Open Questions

A living document of open questions, capability gaps, and clarification requests for the JasperFx team (Jeremy Miller, Adam Dymitruk, and others) encountered while evolving CritterBids' skill library and architecture against the JasperFx `ai-skills` repo and the Wolverine / Marten / Polecat source trees.

Meant to be shared with JasperFx directly or raised in conversations with the team. Each entry aims to stand on its own — enough context that Jeremy (or whoever picks it up) can engage without needing to re-read the prompting conversation.

---

## How to use this doc

- **Adding an entry.** Include source date, context (what triggered the question), the specific question or gap, the evidence you found, and any tentative workaround CritterBids is using in the meantime. Keep entries self-contained — assume the reader may land in the middle of the file.
- **Resolving an entry.** Move the entry to the "Resolved" section at the bottom with resolution date, source of resolution (Slack DM, blog post, GitHub issue, source commit, in-person conversation), and the final answer. Preserve the original question text — don't compress away the context.
- **Tagging.** Use inline tags like `[Polecat]`, `[Wolverine]`, `[Marten]`, `[Docs]`, `[Testing]`, `[Tooling]`, `[Meta]` at the start of each entry title for fast scanning.
- **When in doubt, file an entry.** Better to have an over-documented nit than a lost observation.

---

## Open Questions

### 1. [Polecat][Wolverine] Handler routing attribute for ancillary Polecat stores

**Raised:** 2026-04-17
**Context:** While rewriting CritterBids' archived `marten-named-stores.md` skill into `critter-stack-ancillary-stores.md` against the updated JasperFx ai-skills `marten/advanced/ancillary-stores.md`, I wanted to document the Polecat-equivalent story for Critter Stack scope. The Marten side is clean and well-documented. The Polecat side has a gap.

**What exists on the Marten side (verified in source):**

- `Wolverine.Marten.MartenStoreAttribute` — `[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]`, takes `Type StoreType`, and `Modify(IChain, ...)` sets `chain.AncillaryStoreType = StoreType` and inserts `AncillaryOutboxFactoryFrame(StoreType)` at the head of the middleware chain.
- Practical consequence: a handler tagged `[MartenStore(typeof(IOrderStore))]` gets `IDocumentSession` injected from the ancillary store, `AutoApplyTransactions` fires against that store, `[Entity]` loads from that store, `[WriteAggregate]` / `[ReadAggregate]` resolve against that store, and `IMartenOp` / `IStorageAction<T>` return types route to that store's session — all via the same code-generation path used for the primary store, just redirected.

**What I found for Polecat (verified in source):**

- `Wolverine.Polecat.AncillaryWolverineOptionsPolecatExtensions` (requires Polecat 1.1+) provides the store-registration surface in full parity:
  - `AddPolecatStore<T>()` registration
  - `IConfigurePolecat<T>` configuration hook
  - `PolecatStoreExpression<T>` builder
  - `.IntegrateWithWolverine<T>()` on the expression
  - `.SubscribeToEvents<T>()`, `.SubscribeToEventsWithServices<T, TSubscription>()`, `.ProcessEventsWithWolverineHandlersInStrictOrder<T>()`, `.PublishEventsToWolverine<T>()`
- `Wolverine.Polecat.Publishing.OutboxedSessionFactoryGeneric` also explicitly requires Polecat 1.1+ with `AddPolecatStore<T>`.
- **No `PolecatStoreAttribute` file exists** in `Wolverine.Polecat`. Search scope: `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Polecat\**\*.cs` — the only attributes present are `AggregateHandlerAttribute`, `BoundaryModelAttribute`, `ConsistentAggregateAttribute`, `ConsistentAggregateHandlerAttribute`, `IdentityAttribute`, and `WriteAggregateAttribute`. No store-routing attribute.

**The question(s):**

1. **Is the absence of `[PolecatStore]` intentional, or not-yet-implemented?** If intentional, what is the intended Polecat ancillary-store handler-routing pattern?
2. **If the intended pattern is "inject the typed store directly and call `LightweightSession()` manually,"** that pattern functions but appears to lose the benefits of Wolverine's code-generated handler middleware against that store: no `AutoApplyTransactions`, no `[Entity]`, no `[WriteAggregate]` / `[ReadAggregate]`, no `IStorageAction<T>` / `IMartenOp`-equivalent returns. Is that the accepted trade-off for Polecat ancillary stores today, and does it factor into the Wolverine 6 / Polecat 2.x roadmap?
3. **Would `[MartenStore(typeof(IPolecatDocStore))]` work by accident?** I strongly suspect no, because `MartenStoreAttribute.Modify()` inserts `AncillaryOutboxFactoryFrame` which is specifically in `Wolverine.Marten.Codegen` and presumably Marten-aware. But I have not tested it and have not found a documented statement either way.

**Tentative CritterBids position while unresolved:**

The new `critter-stack-ancillary-stores.md` skill has a **"Polecat Ancillary Stores — API Parity"** section that documents the registration-side parity and explicitly flags the handler-routing gap as an open question. The skill's capability matrix marks several Polecat rows as ⚠️ unverified, with the note: *"Verify the current Polecat ancillary handler story with the JasperFx team before committing to an architecture that depends on attribute-driven routing for Polecat stores."* This keeps CritterBids honest and keeps future readers from assuming parity exists where it may not.

**Possible doc-side action regardless of the answer:**

The ai-skills `polecat/polecat-setup-and-decision-guide.md` frames Marten and Polecat as near-identical: *"The API surface is intentionally identical. `IDocumentSession`, `IQuerySession`, `FetchForWriting`, `[WriteAggregate]`, projections, and subscriptions all work the same way. Code moves between Polecat and Marten with minimal changes."* That's true for the primary-store path. For the **ancillary-store** path, the handler-routing attribute is (today) a specific asymmetry worth calling out in the setup guide, even if the intent is to close that gap later.

---

### 2. [Docs][Marten] ai-skills DCB documentation claims DCB is Polecat-only, but both stores support it

**Raised:** 2026-04-18
**Context:** While preparing a Wave 4 skill refresh that touches CritterBids' DCB guidance, I consulted `ai-skills/marten/advanced/dynamic-consistency-boundary.md` to see whether anything had drifted since CritterBids' skill was authored. The ai-skills file has a direct contradiction with the actual source tree:

> **Note:** DCB is currently implemented in Polecat. The `[BoundaryModel]` attribute and `EventTagQuery` are Polecat-specific.

**What the source tree actually shows (verified 2026-04-18):**

- `BoundaryModelAttribute.cs` exists in **both** `Wolverine.Marten` and `Wolverine.Polecat`. Independent implementations, not shared via a base assembly. Paths:
  - `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\BoundaryModelAttribute.cs`
  - `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Polecat\BoundaryModelAttribute.cs`
- `EventTagQuery` lives in `JasperFx.Events` (the shared core library) — it is store-agnostic by design. Path: `C:\Code\JasperFx\jasperfx\src\JasperFx.Events\Tags\EventTagQuery.cs`
- `EventBoundary` / `IEventBoundary<T>` exist in both `Marten.Events.Dcb` and `Polecat.Events.Dcb`:
  - `C:\Code\JasperFx\marten\src\Marten\Events\Dcb\EventBoundary.cs`
  - `C:\Code\JasperFx\polecat\src\Polecat\Events\Dcb\EventBoundary.cs`
- Wolverine's own test suite has `MartenTests/Dcb/boundary_model_workflow_tests.cs` and `MartenTests/Dcb/University/BoundaryModelSubscribeStudentToCourse.cs` — the `#region sample_wolverine_dcb_boundary_model_handler` target. This is the full DCB workflow exercised against Marten, with a canonical university-enrollment scenario.
- `DcbConcurrencyException` is present in both stores with distinct namespaces:
  - Marten: `Marten.Exceptions.DcbConcurrencyException`
  - Polecat: `Polecat.Events.Dcb.DcbConcurrencyException`

**The claim "DCB is currently implemented in Polecat" is straightforwardly false.** DCB is available in Marten today. Production-ready: source exists, the canonical sample lives in Marten's test tree, and CritterBids' Auctions BC is wired for it via Marten (see CritterBids' `docs/skills/dynamic-consistency-boundary.md` and ADR 011).

**The question(s):**

1. **Should the ai-skills DCB file be updated?** Reading "DCB is Polecat-specific" could push developers toward a Polecat-for-DCB architectural decision that the framework does not require. This is the kind of steer that quietly reshapes project decisions for reasons that don't survive a source check.
2. **Is there a deliberate reason for the current framing — e.g., DCB shipped in Polecat first, and the Marten port is considered preview?** If so, a "DCB on Marten: preview vs stable" note is helpful. If not, the unconditional "Polecat-only" claim should go.
3. **Documentation cross-links.** ai-skills' `aggregate-handler-workflow.md` points to "Dynamic Consistency Boundary (`[BoundaryModel]`)" for cross-aggregate consistency without flagging any store restriction — a reader following that link lands on the DCB page and reads the Polecat-only claim. The two pages contradict each other within the same ai-skills repo.

**Tentative CritterBids position while unresolved:**

CritterBids' `docs/skills/dynamic-consistency-boundary.md` documents DCB as working uniformly across Marten and Polecat with per-store concurrency-exception types (the only real difference). Our Wave 4 refresh is keeping that framing because it matches the source tree, not the ai-skills claim. If JasperFx later confirms Marten DCB is preview-only, we will add a preview-status note without re-architecting.

**Severity:** Low for CritterBids directly (our skill is already correct). Higher for anyone else consulting ai-skills as the primary reference — they will read the Polecat-only claim and either reject DCB on Marten as "not implemented" or switch to Polecat unnecessarily. Worth a quick fix-up in the ai-skills file.

---

## Resolved Questions

*(empty)*

---

## References

- `docs/skills/critter-stack-ancillary-stores.md` — CritterBids' current ancillary-store reference doc (the questions above are flagged inline in this file's capability matrix and Polecat section)
- `docs/skills/dynamic-consistency-boundary.md` — CritterBids' DCB reference, store-agnostic framing
- `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\MartenStoreAttribute.cs` — Marten attribute source (verified 2026-04-17)
- `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Polecat\AncillaryWolverineOptionsPolecatExtensions.cs` — Polecat ancillary-store extension source (verified 2026-04-17)
- `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\BoundaryModelAttribute.cs` — Marten DCB attribute source (verified 2026-04-18)
- `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Polecat\BoundaryModelAttribute.cs` — Polecat DCB attribute source (verified 2026-04-18)
- `C:\Code\JasperFx\jasperfx\src\JasperFx.Events\Tags\EventTagQuery.cs` — shared core `EventTagQuery` (verified 2026-04-18)
- `C:\Code\JasperFx\marten\src\Marten\Events\Dcb\EventBoundary.cs` — Marten boundary implementation (verified 2026-04-18)
- `C:\Code\JasperFx\polecat\src\Polecat\Events\Dcb\EventBoundary.cs` — Polecat boundary implementation (verified 2026-04-18)
- `C:\Code\JasperFx\wolverine\src\Persistence\MartenTests\Dcb\boundary_model_workflow_tests.cs` — Marten DCB workflow test coverage (verified 2026-04-18)
- JasperFx ai-skills: `marten/advanced/ancillary-stores.md`, `marten/advanced/dynamic-consistency-boundary.md`, `marten/advanced/cross-stream-operations.md`, `marten/aggregate-handler-workflow.md`, `polecat/polecat-setup-and-decision-guide.md`
