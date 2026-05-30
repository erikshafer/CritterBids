# Upstream-Contribution Candidates

CritterBids-specific findings that may be worth contributing back to the JasperFx **ai-skills** library
(or the Wolverine/Marten docs). These surfaced during the 2026 skills lean pass: each is a pattern,
gotcha, or correction that ai-skills does **not** currently cover but that is not actually
CritterBids-specific — any Critter Stack project could hit it.

This is a lightweight candidate list, not a commitment. Vet each with the maintainers (Jeremy Miller /
Babu Annamalai) before opening a PR upstream. Where a finding is a correction to existing ai-skills
content, it is also logged in [`../jasperfx-open-questions.md`](../jasperfx-open-questions.md).

| # | Finding | Source skill | Why upstream-worthy | Notes |
|---|---|---|---|---|
| 1 | `OutgoingMessages`/`PublishAsync` silently drops a message (and `tracked.Sent.MessagesOf<T>()` returns 0) when no routing rule exists for the type — `NoRoutesFor()` returns before any `ISendingAgent` runs | `wolverine-message-handlers` (AP) | Pure Wolverine behavior; bites anyone writing outbox assertions in tests | Resolution is an `opts.Publish(...)` rule in host config |
| 2 | Lambda factory registrations are opaque to Wolverine codegen and force runtime `IServiceScopeFactory` (service location) instead of a direct constructor call | `wolverine-message-handlers` (AP) | Generic IoC/codegen interaction; affects perf everywhere | `AlwaysUseServiceLocationFor<T>()` is the scoped escape hatch |
| 3 | `bus.InvokeAsync()` blocks the caller for the full handler duration — wrong tool for fire-and-forget; use a cascading return value or `PublishAsync` | `wolverine-message-handlers` (AP) | Common mental-model error; not spelled out upstream | Quick-reference table by caller intent |
| 4 | Cross-BC handler isolation in tests via `IWolverineExtension` exclusion — discovered handlers fire without their infrastructure; stub-local-queue absorbs `NoRoutesFor`-dropped messages | `critter-stack-testing-patterns` | Applies to any modular-monolith Wolverine test suite | Two distinct problems (infra-absent; dropped-message) |
| 5 | `ConfigureAppConfiguration` does not propagate to a `Program.cs` inline null-guard around primary-store registration | `critter-stack-testing-patterns` | Generic ASP.NET host-builder ordering trap | Significant for null-guarded store registration patterns |
| 6 | `AutoApplyTransactions()` does not fire on direct in-test handler invocation (only through the message pipeline) | `critter-stack-testing-patterns` | Surprises anyone unit-testing handlers directly | — |
| 7 | `ProblemDetails` returned from a **non-HTTP** message handler stops the pipeline without throwing | `critter-stack-testing-patterns` | ai-skills covers `ProblemDetails` in HTTP only | — |
| 8 | DCB is store-agnostic — both Marten and Polecat ship full `[BoundaryModel]`/`EventTagQuery` implementations | `dynamic-consistency-boundary` | **Correction:** ai-skills claims DCB is Polecat-only | JasperFx open question #2; source-verified false |
| 9 | DCB tagging gotchas: `StartStream` drops tags; `[BoundaryModel]` on a `Before()`/`Validate()` parameter triggers Wolverine codegen error CS0128; boundary state needs a `Guid Id` or test teardown throws | `dynamic-consistency-boundary` | Documented nowhere upstream; high-friction for adopters | — |
| 10 | Diagnostics CLI corrections: the codegen-preview flag is `--handler` (not `--message`); there is no `describe-resiliency` command | `diagnostics` | **Correction:** two ai-skills errors | JasperFx open question #3; source-verified |
| 11 | SignalR CloudEvents `type` is the FQN for marker-interface-routed messages (kebab-case only for `WebSocketMessage`), so JS clients must `cloudEvent.type.split(".").pop()` | `wolverine-signalr` | Resolves a genuine ambiguity in the ai-skills SignalR doc | Source-verified against `WolverineMessageNaming.cs` |
| 12 | `marten/advanced/ancillary-stores` documents `[MartenStore]` handler routing but not whether Polecat has an equivalent — `[PolecatStore]` does not exist as of last review | `critter-stack-ancillary-stores` | Asymmetry worth documenting upstream | JasperFx open question #1 |
| 13 | Mutating an aggregate fetched under `UseIdentityMapForAggregates` persists silently on the next commit — treat identity-mapped aggregates as read-only outside the projection/handler chain | `marten-event-sourcing` | Generic Marten footgun; easy to hit | Project to a DTO for ad-hoc reads |

## How to use this list

- When a finding is confirmed generic and reproducible, raise it with the maintainers (issue or Slack)
  before a PR.
- Corrections (#8, #10, #11, #12) are the highest-value, lowest-risk contributions — they fix
  already-published ai-skills content.
- If a finding is upstreamed, note the upstream location in the relevant CritterBids `SKILL.md`'s
  **See also → Upstream** block and trim the local duplication.
