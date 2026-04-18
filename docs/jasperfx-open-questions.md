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

### 3. [Docs][Wolverine] ai-skills `command-line-diagnostics.md` contains two source-unverified claims

**Raised:** 2026-04-18
**Context:** Consolidating CritterBids' scattered CLI-diagnostics coverage into a new `docs/skills/diagnostics.md` against `ai-skills/wolverine/observability/command-line-diagnostics.md`. Source verification against the Wolverine repo turned up two errors in the ai-skills doc. Both had also propagated into CritterBids' own pre-2026-04-18 docs (`wolverine-message-handlers.md` §11, `critter-stack-testing-patterns.md` §20) because those sections were authored against the ai-skills doc rather than against source.

**Claim A — `codegen-preview --message <Type>` is a valid flag.**

ai-skills text:

> ```bash
> # Preview by handler type (full namespace-qualified name)
> dotnet run -- wolverine-diagnostics codegen-preview --handler "MyApp.Orders.CreateOrderHandler"
> ```

The ai-skills doc itself uses `--handler`, but the *decision guidance* table at the bottom of the same file shows:

> | "Inspect generated code for one handler" | `dotnet run wolverine-diagnostics codegen-preview --handler "MyApp.MyHandler"` |

...which is consistent with `--handler`. The issue is that **CritterBids' ai-skills-derived docs — and, I suspect, other downstream readers — drifted to `--message` instead.** Verifying against source to settle it:

- `C:\Code\JasperFx\wolverine\src\Wolverine\Diagnostics\WolverineDiagnosticsCommand.cs:27`
    ```csharp
    [FlagAlias("handler", 'h')]
    [Description(
        "For codegen-preview: preview generated code for a specific message handler. " +
        "Accepts a fully-qualified message type name (e.g. MyApp.Orders.CreateOrder), " +
        "a short class name (e.g. CreateOrder), or a handler class name (e.g. CreateOrderHandler).")]
    public string HandlerFlag { get; set; } = string.Empty;
    ```

Only one alias is declared: `handler` / `h`. There is no `--message` alias. `--handler` accepts message type names as input, which is what makes the alias confusingly named but technically unambiguous.

**Claim B — `describe-resiliency` exists as a `wolverine-diagnostics` sub-command.**

The ai-skills doc documents:

> ```bash
> # Show all resiliency policies for a named endpoint
> dotnet run -- wolverine-diagnostics describe-resiliency orders-queue
>
> # Show policies that apply to a specific message type
> dotnet run -- wolverine-diagnostics describe-resiliency --message "MyApp.Commands.ProcessPayment"
>
> # Show all endpoints and their resiliency configuration
> dotnet run -- wolverine-diagnostics describe-resiliency --all
> ```

with extensive example output.

**Source contradicts this directly.** The same `WolverineDiagnosticsCommand.cs` file defines the sub-command dispatcher:

```csharp
case "codegen-preview":
    return await RunCodegenPreviewAsync(input);

case "describe-routing":
    return await RunDescribeRoutingAsync(input);

default:
    AnsiConsole.MarkupLine(
        $"[red]Unknown sub-command '{input.Action}'. Valid sub-commands: codegen-preview, describe-routing[/]");
    return false;
```

Only `codegen-preview` and `describe-routing` are implemented. Searching `C:\Code\JasperFx\wolverine`, `\marten`, and `\polecat` for `describe-resiliency` returns zero hits. The ai-skills example output appears to be describing a hypothetical future command, not an existing one.

The canonical path for the capability the ai-skills doc attributes to `describe-resiliency` is the **Error Handling** section of `dotnet run describe`, emitted by `WolverineSystemPart.WriteErrorHandling()` at `C:\Code\JasperFx\wolverine\src\Wolverine\WolverineSystemPart.cs:180`. That section includes global failure rules plus per-message-chain policy trees — functionally equivalent to what the ai-skills example output depicts.

**The question(s):**

1. **Should the ai-skills `command-line-diagnostics.md` file be updated?** The `describe-resiliency` claim is the larger issue — a reader who copies the command gets "Unknown sub-command" and has no obvious recovery path. Either (a) ship the sub-command to match the doc, (b) remove the documentation of it, or (c) reframe it as forward-looking / roadmap material.
2. **Is `describe-resiliency` on the Wolverine roadmap?** If it is, CritterBids would benefit from the dedicated view (the `describe` Error Handling section mixes all error policies into one tree and is harder to scan for a specific endpoint or message type).
3. **Should `codegen-preview` add an explicit `--message` alias?** Readers conflating the input type with the flag name is a repeated pattern in CritterBids' own drifted docs. A second alias (`message` as an alternate for `handler`) would make the doc-drift harmless. Alternatively, the flag could be renamed `--target` or `--for` to disambiguate.

**Tentative CritterBids position while unresolved:**

- `docs/skills/diagnostics.md` uses `--handler` and substitutes `dotnet run describe` (Error Handling section) for the retry-policy inspection use case. A dedicated §11 in that file documents both discrepancies inline so future cross-references don't re-introduce the confusion.
- `wolverine-message-handlers.md` §11 and `critter-stack-testing-patterns.md` §20 were corrected in the 2026-04-18 consolidation: `--message` replaced with `--handler` (8 occurrences total), `describe-resiliency` sub-command references replaced with `dotnet run describe` plus a one-line callout explaining the prior error.

**Severity:** Medium. Downstream impact on LLM agents is real — an agent reading ai-skills and trying to run `describe-resiliency` gets a runtime error and may propose nonexistent troubleshooting paths. The `--message` vs `--handler` discrepancy is milder because Wolverine's error message is specific enough to recover from.

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
- `C:\Code\JasperFx\wolverine\src\Wolverine\Diagnostics\WolverineDiagnosticsCommand.cs` — diagnostics CLI source, shows only `codegen-preview` and `describe-routing` sub-commands (verified 2026-04-18)
- `C:\Code\JasperFx\wolverine\src\Wolverine\WolverineSystemPart.cs` — `describe` command output including the Error Handling section (verified 2026-04-18)
- JasperFx ai-skills: `marten/advanced/ancillary-stores.md`, `marten/advanced/dynamic-consistency-boundary.md`, `marten/advanced/cross-stream-operations.md`, `marten/aggregate-handler-workflow.md`, `polecat/polecat-setup-and-decision-guide.md`, `wolverine/observability/command-line-diagnostics.md`
