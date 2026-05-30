---
name: polecat-event-sourcing
description: "Polecat reference for CritterBids: SQL Server-specific event-sourcing differences, DCB API deltas, and M1-era gotchas. Use only when evaluating or reviving Polecat after the ADR 011 all-Marten pivot."
cluster: polecat
tags: [polecat, event-sourcing, sql-server, reference, dcb]
status: reference
---

# Event Sourcing with Polecat

> 📚 Reference — not currently used; all BCs are Marten per ADR 011.
> Generic Polecat mechanics live in ai-skills `polecat-setup-and-decision-guide` and `polecat-cross-stream-operations`;
> **this skill documents only the CritterBids-specific reference posture and Polecat findings preserved from M1.**

CritterBids pivoted entirely to PostgreSQL/Marten in ADR 011. This file is retained only for Polecat-based sibling projects, historical M1 Participants work, and a possible post-feature-complete return to Polecat.

## When to apply this skill

Use this skill when:

- Evaluating whether a future CritterBids BC should reintroduce Polecat/SQL Server.
- Porting a current Marten aggregate pattern to Polecat for a sibling project.
- Diagnosing a Polecat-specific API, SQL Server, or DCB difference captured during M1.

Do NOT use this skill for current CritterBids event sourcing. Use `marten-event-sourcing` first; all eight BCs are Marten today.

## Read upstream first

Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of Polecat setup and cross-stream mechanics:

1. `polecat-setup-and-decision-guide` — packages, host wiring, SQL Server store setup, and when Polecat fits.
2. `polecat-cross-stream-operations` — Polecat DCB, tag queries, and cross-stream consistency operations.

Then read `marten-event-sourcing` for CritterBids' active aggregate and stream-identity conventions. This skill picks up only at the archived CritterBids/Polecat deltas.

## CritterBids posture

| Decision point | Current CritterBids answer |
|---|---|
| Active storage platform | Marten/PostgreSQL for every BC (ADR 011) |
| Polecat status | Reference only; no BC currently targets SQL Server |
| Historical Polecat BCs | Participants, Settlement, Operations were planned/early Polecat candidates before ADR 011 |
| If Polecat returns | Start from `marten-event-sourcing`, then apply the SQL Server and API deltas below |

The old "SQL Server for Operations/Settlement/Participants" rationale is superseded for CritterBids, but still useful for sibling projects that need Power BI/SQL Server tooling, financial SQL Server affinity, or staff-managed operational data in SQL Server.

## Polecat-specific deltas worth preserving

| Area | Polecat note retained from CritterBids work |
|---|---|
| Runtime model | Source generators, not Marten runtime code generation |
| Serialization | System.Text.Json only; no Newtonsoft.Json option |
| Connection | `opts.ConnectionString = connectionString` was confirmed in M1; SQL Server connection string format applies |
| Schema management | `AutoCreate.CreateOrUpdate`; Polecat does not have `AutoCreate.All` |
| Schema isolation | Always set `opts.DatabaseSchemaName` explicitly; defaults to `dbo` |
| Startup migration | `.ApplyAllDatabaseChangesOnStartup()` chains on the builder expression, not inside the options lambda |
| Wolverine integration | `.IntegrateWithWolverine()` registers SQL Server inbox/outbox tables; message storage schema is separate from the BC schema |
| Async projections | Register `ProjectionLifecycle.Async`; Polecat has no Marten-style `.AddAsyncDaemon(DaemonMode.Solo)` chain |
| HTTP aliases | `WolverineFx.Http.Polecat` provides `[Aggregate]` and `[Document]` aliases |
| Tables | Polecat tables use `pc_` prefixes (`pc_events`, `pc_streams`, `pc_event_progression`, `pc_doc_*`) |
| SQL Server JSON | SQL Server 2025 native `json` is the default; set `opts.UseNativeJsonType = false` for older servers |
| Collation | SQL Server defaults case-insensitive; prefer GUID aggregate IDs and normalize any string lookup keys |

## M1 findings to keep

### Single-event return: do not wrap in `new Events(evt)`

When a `[WriteAggregate]` handler appends exactly one domain event, return that event directly as a tuple member. `Events` is only for multiple events.

```csharp
public static (IResult, SellerRegistered, OutgoingMessages) Handle(
    RegisterAsSeller cmd,
    [WriteAggregate] Participant participant)
{
    var evt = new SellerRegistered(participant.Id, DateTimeOffset.UtcNow);
    var outgoing = new OutgoingMessages();
    outgoing.Add(new SellerRegistrationCompleted(participant.Id, evt.CompletedAt));
    return (Results.Ok(), evt, outgoing);
}
```

### `OnMissing.Simple404` fires before `Before()`

If the stream is missing, Wolverine returns 404 before calling `Before()`. A `Before()` method that runs has a loaded aggregate; declare the parameter non-nullable and reserve `ProblemDetails` for stream-exists precondition failures.

### Cleanup extension location

M1 fixture work confirmed `CleanAllPolecatDataAsync()` was called from the host service provider. Use Polecat reset helpers when async projections enter the picture.

## DCB deltas

Polecat has DCB support, but APIs differ slightly from Marten:

| Operation | Marten | Polecat |
|---|---|---|
| Attribute namespace | `Wolverine.Marten` | `Wolverine.Polecat` |
| Tag event | `wrapped.AddTag(...)` | `wrapped.WithTag(...)` |
| Fetch for writing | stream-based `FetchForWriting` | `FetchForWritingByTags<T>(query)` |
| Concurrency exception | Marten event-stream exception types | `DcbConcurrencyException` |

For the full CritterBids DCB posture, use `dynamic-consistency-boundary`. This file only records the Polecat namespace/API differences.

## Common pitfalls

- **Treating this as current CritterBids guidance.** It is reference-only; current BC work uses Marten.
- **Assuming Marten bootstrap chains copy exactly.** Polecat has different schema-management and async-daemon surfaces.
- **Ignoring SQL Server collation.** Case-insensitive string comparisons can change lookup and unique-index behavior.
- **Using StronglyTypedId wrappers blindly.** A known codegen gap existed for `[Entity]`/saga identifiers; verify before adopting.
- **Assuming DCB APIs are identical.** The concept transfers, but tag and exception APIs differ.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `polecat-setup-and-decision-guide` — Polecat packages, host setup, and SQL Server decision guidance.
- `polecat-cross-stream-operations` — Polecat DCB and cross-stream consistency.

**Prerequisites:**

- `marten-event-sourcing` — active CritterBids aggregate workflow and stream-identity posture.
- `dynamic-consistency-boundary` — CritterBids DCB decisions and gotchas.

**Downstream:**

- `critter-stack-testing-patterns` — test cleanup and race-condition patterns to adapt if Polecat returns.

**External:**

- ADR 003, ADR 011 in [`docs/decisions/`](../../decisions/) — superseded Polecat split and all-Marten pivot.
- [`CLAUDE.md`](../../../CLAUDE.md) § BC Module Quick Reference.
