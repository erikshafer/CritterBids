---
name: diagnostics
description: "CritterBids diagnostics: source-verified Wolverine/JasperFx CLI invocation, known ai-skills flag corrections, schema/storage/resource commands, and programmatic APIs. Use when debugging handlers, routing, or DB drift."
cluster: observability
tags: [diagnostics, wolverine, marten, cli, codegen]
---

# Diagnostics — CLI and Programmatic Tools

> CritterBids diagnostic commands and source-verified corrections.
> Generic Wolverine observability mechanics live in ai-skills `wolverine-observability-*`; **this skill documents only the CritterBids-specific invocation form, corrections, and posture.**

## When to apply this skill

Use this skill when:

- A handler, endpoint, route, retry policy, or generated adapter behaves unexpectedly.
- `tracked.Sent.MessagesOf<T>()` returns zero and route config is suspect.
- Test/CI fails with schema drift or missing tables.
- Pre-deploy needs DB/broker health checks or config drift capture.
- A crashed node leaves persisted Wolverine message ownership behind.

Do NOT use this skill for: continuous monitoring/OTEL dashboards, generic handler authoring, or test fixture construction.

## Read upstream first

Generic mechanics are covered upstream. Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of diagnostics:

1. `wolverine-observability-command-line-diagnostics` — Wolverine CLI surface, routing/codegen/schema commands.
2. `wolverine-observability-code-generation` — generated code inspection, static/dynamic codegen, `codegen write`.

This skill picks up at the CritterBids command form and two source-verified corrections.

## CritterBids command shape

`Program.cs` must end with:

```csharp
return await app.RunJasperFxCommands(args);
```

Run every command from repo root through the API host:

```bash
dotnet run --project src/CritterBids.Api -- <command>
```

CritterBids is on .NET 10, so `--` is optional, but keep it in docs and prompts for copy-paste clarity.

## Fast diagnosis commands

| Need | Command |
|---|---|
| Full Wolverine config, handler graph, routing, listeners, error handling | `dotnet run --project src/CritterBids.Api -- describe` |
| All routes | `dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-routing --all` |
| One route by full or short type name | `dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-routing ListingPublished` |
| Generated handler/message adapter | `dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --handler SubmitListing` |
| Generated HTTP endpoint adapter | `dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --route "POST /api/listings"` |
| DB/broker/env health | `dotnet run --project src/CritterBids.Api -- check-env` |
| Schema drift gate | `dotnet run --project src/CritterBids.Api -- db-assert` |
| Apply schema locally | `dotnet run --project src/CritterBids.Api -- db-apply` |
| Dump expected DDL | `dotnet run --project src/CritterBids.Api -- db-dump schema.sql` |
| Export config for drift diff | `dotnet run --project src/CritterBids.Api -- capabilities wolverine.json` |
| Clear persisted messages | `dotnet run --project src/CritterBids.Api -- storage clear` |
| Rebuild message storage | `dotnet run --project src/CritterBids.Api -- storage rebuild` |
| Release crashed-node ownership | `dotnet run --project src/CritterBids.Api -- storage release` |
| Provision/drop resources | `resources setup` / `resources teardown` through same `dotnet run` prefix |

`db-assert` and `db-apply` cover Marten plus Wolverine message storage. CritterBids is all-Marten/PostgreSQL per ADR 011; stale Polecat wording from older docs does not apply.

## Symptom map

| Symptom | First command | Read output for |
|---|---|---|
| Handler missing | `describe` | Handler Graph; then `opts.DescribeHandlerMatch(typeof(...))` if absent |
| `IDocumentSession` not injectable | `codegen-preview --handler <T>` | session variable resolution; `IntegrateWithWolverine()` |
| `[WriteAggregate]` / `[ReadAggregate]` not loading | `codegen-preview --route "VERB /path"` | aggregate ID binding |
| `tracked.Sent.MessagesOf<T>()` is empty | `describe-routing "<Type>"` | missing `opts.PublishMessage<T>()` route |
| Retry/circuit breaker confusion | `describe` | Error Handling section |
| Relation/table missing | `db-assert` | outstanding DDL; use `db-apply` locally |
| Pre-deploy smoke | `check-env` | DB/broker connectivity |
| Config drift | `capabilities file.json` | diff across env artifacts |

## Source-verified ai-skills corrections

These corrections are verified against Wolverine source as of 2026-04-18 and must not be regressed.

### `codegen-preview --message` is wrong

Use `--handler` or `-h`:

```bash
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --handler SubmitListing
```

The flag accepts full message type, short class name, or handler class name. There is no `--message` alias.

### `describe-resiliency` does not exist

Use `describe` and read the **Error Handling** section:

```bash
dotnet run --project src/CritterBids.Api -- describe
```

No `describe-resiliency` sub-command exists in Wolverine/Marten/Polecat source at the verification point.

## Programmatic APIs

Use these inside tests, startup diagnostics, or ad-hoc admin code when shell commands are not available:

```csharp
Console.WriteLine(opts.DescribeHandlerMatch(typeof(SubmitListingHandler)));

var bus = host.Services.GetRequiredService<IMessageBus>();
var envelopes = bus.PreviewSubscriptions(new ListingPublished(listingId, publishedAt));

await host.SetupResources();
await host.TeardownResources();
await host.ResetResourceState();

var store = host.Services.GetRequiredService<IMessageStore>();
await store.Admin.RebuildAsync();
await store.Admin.ClearAllAsync();
```

Test fixtures should prefer `Host.CleanAllMartenDataAsync()` / `Host.ResetAllMartenDataAsync()` from `critter-stack-testing-patterns` unless intentionally managing all resources.

## Common pitfalls

- **Removing `RunJasperFxCommands(args)`.** CLI commands disappear and debugging signal drops to zero.
- **Using upstream's stale `--message` flag.** It fails; use `--handler`.
- **Looking for `describe-resiliency`.** It does not exist; use `describe` -> Error Handling.
- **Running commands against wrong project.** Always target `src/CritterBids.Api`, not a BC library.
- **Treating `storage clear` as test cleanup.** Use fixture cleanup APIs in tests; reserve storage commands for shell/admin use.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-observability-command-line-diagnostics`, `wolverine-observability-code-generation`.

**Prerequisites:**

- `wolverine-message-handlers` — handler shape and outbox route footguns.
- `marten-event-sourcing` — single-store host wiring and transaction policy.

**Downstream:**

- `critter-stack-testing-patterns` — fixture-level debugging and `tracked.Sent`/`NoRoutes` interpretation.
- `aspire` — local orchestration context and dashboard complement.

**External:**

- [`CLAUDE.md`](../../../CLAUDE.md) § Canonical Bootstrap Sequence.
- Wolverine CLI/diagnostics docs; source corrections tracked in `docs/jasperfx-open-questions.md`.
