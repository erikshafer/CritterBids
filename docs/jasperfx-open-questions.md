# JasperFx Open Questions

A living document of open questions, capability gaps, and clarification requests for the JasperFx team (Jeremy Miller, Adam Dymitruk, and others) encountered while evolving CritterBids' skill library and architecture against the JasperFx `ai-skills` repo and the Wolverine / Marten / Polecat source trees.

Meant to be shared with JasperFx directly or raised in conversations with the team. Each entry aims to stand on its own \u2014 enough context that Jeremy (or whoever picks it up) can engage without needing to re-read the prompting conversation.

---

## How to use this doc

- **Adding an entry.** Include source date, context (what triggered the question), the specific question or gap, the evidence you found, and any tentative workaround CritterBids is using in the meantime. Keep entries self-contained \u2014 assume the reader may land in the middle of the file.
- **Resolving an entry.** Move the entry to the "Resolved" section at the bottom with resolution date, source of resolution (Slack DM, blog post, GitHub issue, source commit, in-person conversation), and the final answer. Preserve the original question text \u2014 don't compress away the context.
- **Tagging.** Use inline tags like `[Polecat]`, `[Wolverine]`, `[Marten]`, `[Docs]`, `[Testing]`, `[Tooling]`, `[Meta]` at the start of each entry title for fast scanning.
- **When in doubt, file an entry.** Better to have an over-documented nit than a lost observation.

---

## Open Questions

### 1. [Polecat][Wolverine] Handler routing attribute for ancillary Polecat stores

**Raised:** 2026-04-17
**Context:** While rewriting CritterBids' archived `marten-named-stores.md` skill into `critter-stack-ancillary-stores.md` against the updated JasperFx ai-skills `marten/advanced/ancillary-stores.md`, I wanted to document the Polecat-equivalent story for Critter Stack scope. The Marten side is clean and well-documented. The Polecat side has a gap.

**What exists on the Marten side (verified in source):**

- `Wolverine.Marten.MartenStoreAttribute` \u2014 `[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]`, takes `Type StoreType`, and `Modify(IChain, ...)` sets `chain.AncillaryStoreType = StoreType` and inserts `AncillaryOutboxFactoryFrame(StoreType)` at the head of the middleware chain.
- Practical consequence: a handler tagged `[MartenStore(typeof(IOrderStore))]` gets `IDocumentSession` injected from the ancillary store, `AutoApplyTransactions` fires against that store, `[Entity]` loads from that store, `[WriteAggregate]` / `[ReadAggregate]` resolve against that store, and `IMartenOp` / `IStorageAction<T>` return types route to that store's session \u2014 all via the same code-generation path used for the primary store, just redirected.

**What I found for Polecat (verified in source):**

- `Wolverine.Polecat.AncillaryWolverineOptionsPolecatExtensions` (requires Polecat 1.1+) provides the store-registration surface in full parity:
  - `AddPolecatStore<T>()` registration
  - `IConfigurePolecat<T>` configuration hook
  - `PolecatStoreExpression<T>` builder
  - `.IntegrateWithWolverine<T>()` on the expression
  - `.SubscribeToEvents<T>()`, `.SubscribeToEventsWithServices<T, TSubscription>()`, `.ProcessEventsWithWolverineHandlersInStrictOrder<T>()`, `.PublishEventsToWolverine<T>()`
- `Wolverine.Polecat.Publishing.OutboxedSessionFactoryGeneric` also explicitly requires Polecat 1.1+ with `AddPolecatStore<T>`.
- **No `PolecatStoreAttribute` file exists** in `Wolverine.Polecat`. Search scope: `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Polecat\**\*.cs` \u2014 the only attributes present are `AggregateHandlerAttribute`, `BoundaryModelAttribute`, `ConsistentAggregateAttribute`, `ConsistentAggregateHandlerAttribute`, `IdentityAttribute`, and `WriteAggregateAttribute`. No store-routing attribute.

**The question(s):**

1. **Is the absence of `[PolecatStore]` intentional, or not-yet-implemented?** If intentional, what is the intended Polecat ancillary-store handler-routing pattern?
2. **If the intended pattern is "inject the typed store directly and call `LightweightSession()` manually,"** that pattern functions but appears to lose the benefits of Wolverine's code-generated handler middleware against that store: no `AutoApplyTransactions`, no `[Entity]`, no `[WriteAggregate]` / `[ReadAggregate]`, no `IStorageAction<T>` / `IMartenOp`-equivalent returns. Is that the accepted trade-off for Polecat ancillary stores today, and does it factor into the Wolverine 6 / Polecat 2.x roadmap?
3. **Would `[MartenStore(typeof(IPolecatDocStore))]` work by accident?** I strongly suspect no, because `MartenStoreAttribute.Modify()` inserts `AncillaryOutboxFactoryFrame` which is specifically in `Wolverine.Marten.Codegen` and presumably Marten-aware. But I have not tested it and have not found a documented statement either way.

**Tentative CritterBids position while unresolved:**

The new `critter-stack-ancillary-stores.md` skill has a **"Polecat Ancillary Stores \u2014 API Parity"** section that documents the registration-side parity and explicitly flags the handler-routing gap as an open question. The skill's capability matrix marks several Polecat rows as \u26a0\ufe0f unverified, with the note: *"Verify the current Polecat ancillary handler story with the JasperFx team before committing to an architecture that depends on attribute-driven routing for Polecat stores."* This keeps CritterBids honest and keeps future readers from assuming parity exists where it may not.

**Possible doc-side action regardless of the answer:**

The ai-skills `polecat/polecat-setup-and-decision-guide.md` frames Marten and Polecat as near-identical: *"The API surface is intentionally identical. `IDocumentSession`, `IQuerySession`, `FetchForWriting`, `[WriteAggregate]`, projections, and subscriptions all work the same way. Code moves between Polecat and Marten with minimal changes."* That's true for the primary-store path. For the **ancillary-store** path, the handler-routing attribute is (today) a specific asymmetry worth calling out in the setup guide, even if the intent is to close that gap later.

---

## Resolved Questions

*(empty)*

---

## References

- `docs/skills/critter-stack-ancillary-stores.md` \u2014 CritterBids' current ancillary-store reference doc (the questions above are flagged inline in this file's capability matrix and Polecat section)
- `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\MartenStoreAttribute.cs` \u2014 Marten attribute source (verified 2026-04-17)
- `C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Polecat\AncillaryWolverineOptionsPolecatExtensions.cs` \u2014 Polecat ancillary-store extension source (verified 2026-04-17)
- JasperFx ai-skills: `marten/advanced/ancillary-stores.md`, `polecat/polecat-setup-and-decision-guide.md`
