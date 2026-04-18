# Diagnostics — CLI and Programmatic Tools

Reference for CritterBids diagnostic tooling: the JasperFx CLI commands, Wolverine's programmatic
diagnostic APIs, and database/storage/resource management. The audience is both human developers
debugging a specific problem and LLM agents working inside the project that need a fast "run this
command, paste the output" path to an answer.

Everything below is verified against the Wolverine source tree at `C:\Code\JasperFx\wolverine`
as of 2026-04-18. Flag names, sub-command names, and output structure are taken from source, not
from ai-skills — see §11 for the discrepancies that motivated source-first verification.

> **Status: ✅ Complete — source-verified 2026-04-18.** Authored to replace the fragmentary
> coverage in `wolverine-message-handlers.md` §11 and `critter-stack-testing-patterns.md` §20
> with a single reference that also covers schema management, storage reset, environment checks,
> and programmatic diagnostics. The CLI command surface is stable; extend when JasperFx adds
> new sub-commands.

---

## 1. Scope

Load this skill when:
- A handler isn't behaving as expected and you want to inspect the generated adapter code
- Integration tests assert on messages that never arrive and you suspect a routing rule is missing
- The test suite fails with schema errors ("relation does not exist")
- A deploy is coming up and you want pre-flight verification of DB + broker connectivity
- A Hetzner node crashed and you need to release persisted node ownership
- You want a structured export of the running app's configuration for drift comparison

Out of scope — see other skills:
- Continuous monitoring (traces, metrics, Grafana) → `observability.md`
- Handler authoring patterns and anti-patterns → `wolverine-message-handlers.md`
- Test fixture construction and scenario patterns → `critter-stack-testing-patterns.md`

---

## 2. Prerequisite — `RunJasperFxCommands`

Nothing in this document works without this line at the end of `Program.cs`:

```csharp
return await app.RunJasperFxCommands(args);
```

**CritterBids status:** verified present at `src/CritterBids.Api/Program.cs:96`. If that line is
removed or replaced with `app.Run()`, every CLI command below returns with "No commands found"
and the AI agent looking at the failure has no signal that the prerequisite is missing — it
just looks like the commands don't exist. Keep this line intact.

All CLI commands are invoked with the project targeted explicitly:

```bash
dotnet run --project src/CritterBids.Api -- <command>
```

The `--` separator is optional on .NET 9+. CritterBids is on .NET 10 so you can write
`dotnet run --project src/CritterBids.Api describe` if you prefer. All examples below include
the `--` for compatibility with older shells and for copy-paste clarity.

---

## 3. The Five Commands for Quick Diagnosis

Memorise these five. In practice they answer ~80% of "something is wrong" questions.

### 3.1 `describe` — the "what is going on" catchall

```bash
dotnet run --project src/CritterBids.Api -- describe
```

Single highest-value command. Prints every piece of the running Wolverine configuration:

1. **Wolverine Options** — service name, assembly, extensions
2. **Handler Graph** — every discovered handler with its message type
3. **Message Routing** — table with columns: .NET Type, Message Type Alias, Destination, Content Type
4. **Sending Endpoints** — all outbound endpoints
5. **Listeners** — every inbound listening endpoint with its durability / queue settings
6. **Error Handling** — global failure rules and per-message-chain failure policies

Source: `C:\Code\JasperFx\wolverine\src\Wolverine\WolverineSystemPart.cs:WriteToConsole()`.

The Error Handling section is the canonical way to inspect retry / circuit-breaker configuration.
There is no separate `describe-resiliency` sub-command (see §11).

**When to reach for it:** almost always the first step when anything is wrong and you don't know
where the problem is. The output is large but grep-friendly — pipe it to a file and search.

### 3.2 `wolverine-diagnostics describe-routing` — routing table

Two forms:

```bash
# Full routing topology for every known message type
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-routing --all

# Routing for a specific type (accepts full name, short name, or alias)
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-routing "CritterBids.Contracts.Selling.ListingPublished"
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-routing ListingPublished
```

The argument is positional — no flag. If you supply neither the argument nor `--all`, the command
errors with a usage hint.

Source: `C:\Code\JasperFx\wolverine\src\Wolverine\Diagnostics\WolverineDiagnosticsCommand.cs:335`.

**When to reach for it:** any time `tracked.Sent.MessagesOf<T>()` returns zero unexpectedly. If the
message type isn't in the routing output, the `opts.Publish(...)` rule is missing from
`Program.cs` — that's Anti-Pattern #14 in `wolverine-message-handlers.md`.

### 3.3 `wolverine-diagnostics codegen-preview` — generated adapter code

```bash
# By handler / message — the --handler flag accepts:
#   - Fully-qualified message type name:  "CritterBids.Selling.SubmitListing"
#   - Short message class name:            "SubmitListing"
#   - Handler class name:                   "SubmitListingHandler"
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --handler SubmitListing

# By HTTP route:  "METHOD /path"
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --route "POST /api/listings"
```

Source: `WolverineDiagnosticsCommand.cs:27` — `[FlagAlias("handler", 'h')]`. The alias `-h` also
works. **There is no `--message` alias** — don't use it (the ai-skills doc and pre-2026-04-18
CritterBids docs incorrectly used `--message`).

The output shows the complete generated C# adapter class — exactly what Wolverine compiles at
startup. Use it to:

- Verify `[WriteAggregate]` is resolving the aggregate ID from the expected command property
- Confirm `[EmptyResponse]` is active on an aggregate HTTP endpoint (without it, the returned event
  becomes the HTTP body — silent failure)
- Read the actual middleware order Wolverine will invoke
- Confirm dependencies inject via direct constructor calls vs. `IServiceScopeFactory` fallback
  (service location — see `wolverine-message-handlers.md` §9)
- Verify `AutoApplyTransactions` is wrapping the handler

**When to reach for it:** whenever handler behavior doesn't match what you expect — wrong status
code, entity not loaded, event not appended, middleware not firing, session variable not resolved.

### 3.4 `check-env` — is the infrastructure wired?

```bash
dotnet run --project src/CritterBids.Api -- check-env
```

Runs every `IHostedService.CheckAsync` registered with Wolverine: database connectivity, broker
connectivity, transport configuration, IoC validity. Prints pass/fail per resource.

**When to reach for it:** before any deploy, any time you suspect a connection string or credential
is wrong, after changing `appsettings.{Env}.json`, or when a CI run fails at startup rather than
at a specific test assertion.

### 3.5 `db-assert` — schema drift check

```bash
dotnet run --project src/CritterBids.Api -- db-assert
```

Fails (non-zero exit) if the actual database schema does not match what the registered Marten /
Wolverine / Polecat stores expect. Used in CI as a gate before `dotnet publish`.

**When to reach for it:** every CI pipeline run should include this before the publish step. Locally,
run after pulling main or rebasing, to catch schema drift introduced by other contributors' merged
work before your test run hits it as a runtime error.

---

## 4. Symptom → Command Quick Reference

Ordered for LLM-agent use. If you have a specific symptom, start with the command in the second
column and work from its output.

| Symptom | First command | Secondary |
|---|---|---|
| "Something is broken, I don't know where" | `describe` | — |
| Handler compiled but not found at runtime | `describe` (scan Handler Graph) | `opts.DescribeHandlerMatch(typeof(Handler))` programmatically |
| `IDocumentSession` not injectable / codegen failure | `codegen-preview --handler <T>` | `describe` (Message Routing to confirm reach) |
| `[WriteAggregate]` / `[ReadAggregate]` not loading | `codegen-preview --route "VERB /path"` | Check aggregate ID naming convention |
| `[Entity]` parameter always null | `codegen-preview --handler <T>` | Check property naming (`{EntityName}Id`) |
| HTTP endpoint returns 200 but event not appended | `codegen-preview --route "POST /path"` | Missing `[EmptyResponse]` or wrong tuple shape |
| `tracked.Sent.MessagesOf<T>()` returns 0 | `describe-routing "<Type>"` | If absent → `opts.Publish(...)` missing in `Program.cs` (AP#14) |
| Retry policy not firing | `describe` (Error Handling section) | Inspect `[RetryNow]` attrs and `opts.OnException<T>()` |
| Circuit breaker not triggering | `describe` (Error Handling section) | — |
| Test fails with "relation does not exist" | `db-assert` | `db-apply` to fix; `db-dump schema.sql` to inspect |
| CI fails at startup before any test runs | `check-env` | `describe` for config verification |
| Outbox has stale messages after crash | `storage release` | `storage clear` if recovery is acceptable |
| Test teardown left rows in DB | `storage clear` | `ResetAllMartenDataAsync()` in the fixture (preferred) |
| Deploy prep — config drift between envs | `capabilities` (JSON export) | Diff two environments' exports |
| Integration message routed to wrong transport | `describe-routing "<Type>"` | Check endpoint registration in `Program.cs` |
| Handler loaded from wrong assembly | `describe` (Handler Graph) | Check `opts.Discovery.IncludeAssembly(...)` |
| Mystery codegen exception on startup | `codegen-preview --handler <T>` (isolates the failing chain) | Consider `DisableAllWolverineMessagePersistence()` for the codegen run |

---

## 5. Programmatic Equivalents

Not every diagnostic need can be answered by running a CLI command. The following APIs let you
interrogate Wolverine from inside the running host — useful inside tests, startup logging, or ad-hoc
console apps.

### 5.1 `opts.DescribeHandlerMatch(typeof(...))` — handler discovery diagnostic

```csharp
// Inside Program.cs, after handler discovery is configured
Console.WriteLine(opts.DescribeHandlerMatch(typeof(SubmitListingHandler)));
```

Prints a structured report explaining why Wolverine did or did not discover a given handler type.
Surfaces reasons like "class is not public", "assembly not scanned", "no method matches the
handler convention", "excluded by customised discovery filter".

**When to reach for it:** a handler appears implemented correctly but doesn't fire. The report
tells you precisely what about it the discovery pass rejected — more specific than `describe`'s
Handler Graph output, which only shows *what was* discovered, not *why something wasn't*.

### 5.2 `bus.PreviewSubscriptions(message)` — programmatic routing preview

```csharp
var bus = host.Services.GetRequiredService<IMessageBus>();
var outgoing = bus.PreviewSubscriptions(new ListingPublished(listingId, DateTimeOffset.UtcNow));
foreach (var envelope in outgoing)
{
    Console.WriteLine($"→ {envelope.Destination}");
}
```

Returns the envelopes Wolverine would produce if the message were published right now — without
actually publishing. Shows every destination the routing rules would fan out to.

**When to reach for it:** inside a custom diagnostic endpoint or a test setup hook when you want
a routing sanity check that's parameterised by the actual runtime config rather than by the
`describe-routing` CLI output.

### 5.3 Programmatic resource management (tests, scripts)

```csharp
await host.SetupResources();        // Apply schema changes + create broker queues
await host.TeardownResources();     // Remove schema + drop broker queues
await host.ResetResourceState();    // Clear data in DB + broker without removing schema

// Lower-level — direct store admin
var store = host.Services.GetRequiredService<IMessageStore>();
await store.Admin.RebuildAsync();   // Rebuild schema + delete all message data
await store.Admin.ClearAllAsync();  // Delete all message data, keep schema
```

**When to reach for them:** primarily in custom test harnesses or a `Program.cs`-driven admin
script. Test fixtures in CritterBids prefer `CleanAllMartenDataAsync()` /
`ResetAllMartenDataAsync()` extension methods (see `critter-stack-testing-patterns.md` §14) — those
wrap the admin-level calls with safer semantics for per-test isolation.

### 5.4 Disabling persistence for codegen runs

When running `codegen write` in CI (no database available), guard Wolverine registration:

```csharp
// Very early in Program.cs, before AddMarten / AddWolverine
if (Environment.GetCommandLineArgs().Contains("codegen"))
{
    services.DisableAllExternalWolverineTransports();
    services.DisableAllWolverineMessagePersistence();
}
```

Without this, `dotnet run --project src/CritterBids.Api -- codegen write` in a container that has
no PostgreSQL / RabbitMQ fails at startup because Wolverine tries to connect to both.

---

## 6. Schema Management

All schema-related commands operate against every database Marten / Wolverine / Polecat have
registered. Marten's schema and Wolverine's message-store schema can live in the same PostgreSQL
database (CritterBids' posture under ADR 009) or separate databases — the commands discover
whichever is registered.

| Command | Effect |
|---|---|
| `dotnet run -- db-list` | List every registered database with its URI |
| `dotnet run -- db-assert` | Exit non-zero if schema out of date — CI gate |
| `dotnet run -- db-apply` | Apply every outstanding DDL change |
| `dotnet run -- db-dump schema.sql` | Dump the full expected DDL to a file |
| `dotnet run -- db-patch` | Generate migration + drop files (for review before applying) |

### Multi-database invocation

When stores are spread across multiple databases, target one by URI:

```bash
# List URIs first to see what's available
dotnet run --project src/CritterBids.Api -- db-list
# Example output:
#   marten://store/
#   wolverine://messages/main

# Then target a specific one
dotnet run --project src/CritterBids.Api -- db-dump -d marten://store/ marten_schema.sql
```

CritterBids' current posture (ADR 009 — shared primary store) places Marten and Wolverine's
message store in the same PostgreSQL database, so `db-apply` / `db-assert` operate uniformly
without needing the `-d` flag.

### CI integration

```yaml
# Example CI step — run before `dotnet publish`
- name: Assert schema up to date
  run: dotnet run --project src/CritterBids.Api -- db-assert
```

If `db-assert` fails, the build fails. Typical root cause: a migration was merged to main but the
CI environment's database wasn't updated. Running `db-apply` against the CI database resolves it.

---

## 7. Storage and Resource Management

Commands that manipulate message-store content, not just schema.

| Command | Effect |
|---|---|
| `dotnet run -- storage rebuild` | Rebuild schema + clear all persisted messages |
| `dotnet run -- storage clear` | Delete all persisted messages (keep schema) |
| `dotnet run -- storage release` | Release node ownership of persisted messages — post-crash recovery |
| `dotnet run -- resources setup` | Create all stateful resources (DB schema + broker queues + exchanges) |
| `dotnet run -- resources teardown` | Remove all stateful resources |

### `storage release` — post-crash on Hetzner

When a Wolverine node crashes mid-processing, its persisted message ownership may not be cleaned
up. On restart the messages look "claimed" and the new instance can't touch them. `storage release`
clears stale ownership so the new instance can resume.

```bash
# On the Hetzner host after an unclean shutdown
dotnet run --project src/CritterBids.Api -- storage release
```

Pre-production CritterBids has no persistent node issue — the concern becomes live when the
Hetzner deploy is running a long-lived node that can crash. Document once observed; this is the
command to reach for.

### `storage clear` vs. programmatic cleanup

The programmatic `host.ResetResourceState()` is usually the right choice inside test harnesses.
`storage clear` is the right choice from a shell when nobody's running tests — e.g., cleaning up
a dev environment after a messy manual test session.

---

## 8. Drift Detection — `capabilities`

```bash
dotnet run --project src/CritterBids.Api -- capabilities wolverine.json
```

Exports the full Wolverine configuration — listeners, routing rules, error policies, handler
graph — as a structured JSON file. Two environments' exports can be diffed to catch configuration
drift: dev vs. staging, staging vs. production, or pre-deploy vs. post-deploy.

CritterBids posture: capture `capabilities` output as a build artifact on every deploy. First
deploy after a meaningful change → compare against the previous deploy's artifact. Drift that
isn't explained by the diff in the git log is a red flag.

---

## 9. CritterBids Posture — When to Reach for Which

### 9.1 Local Aspire loop

Aspire runs the host; the Aspire dashboard already surfaces traces and structured logs. Use the
CLI when the dashboard doesn't answer a specific question.

| Need | Command |
|---|---|
| "Why is this handler not firing?" | `codegen-preview --handler <T>` |
| "Where does this message route?" | `describe-routing "<Type>"` |
| "Full config dump for reference" | `describe` piped to a file |
| "Is my schema current?" | `db-assert` |
| "Apply schema changes to local dev DB" | `db-apply` |
| "Reset local dev data" | `resources teardown && resources setup` |

### 9.2 CI

| Step | Command | Gate? |
|---|---|---|
| Pre-publish schema check | `db-assert` | Fail build on drift |
| Pre-publish codegen | `codegen write` | Fail build on codegen errors |
| Pre-publish config snapshot | `capabilities ./artifacts/wolverine-capabilities.json` | Non-gating, kept as artifact |

### 9.3 Pre-Hetzner-deploy

| Step | Command |
|---|---|
| Smoke check DB + broker from deploy environment | `check-env` |
| Export pre-deploy config for comparison | `capabilities previous.json` |
| Apply any outstanding migrations | `db-apply` |

### 9.4 Post-incident on Hetzner

| Incident | Command |
|---|---|
| Wolverine node crashed mid-processing | `storage release` before restart |
| Unknown state after partial deploy | `capabilities current.json` → diff vs. previous |
| Handler misbehaving in production | `describe` output captured and reviewed |

---

## 10. Programmatic + CLI Mapping

When both a CLI and a programmatic form exist, prefer the CLI in shells and test scripts, and the
programmatic form inside code.

| Capability | CLI | Programmatic |
|---|---|---|
| Full config description | `describe` | — |
| Routing for a message type | `describe-routing "<Type>"` | `bus.PreviewSubscriptions(msg)` |
| Routing for all types | `describe-routing --all` | — |
| Handler adapter code | `codegen-preview --handler T` | — |
| HTTP endpoint code | `codegen-preview --route "VERB /path"` | — |
| Why isn't handler X discovered? | — | `opts.DescribeHandlerMatch(typeof(X))` |
| Apply schema | `db-apply` | `host.SetupResources()` |
| Verify schema | `db-assert` | — |
| Clear message data | `storage clear` | `host.ResetResourceState()` or `store.Admin.ClearAllAsync()` |
| Rebuild message store | `storage rebuild` | `store.Admin.RebuildAsync()` |
| Provision all resources | `resources setup` | `host.SetupResources()` |
| Remove all resources | `resources teardown` | `host.TeardownResources()` |
| Release crashed node ownership | `storage release` | — |
| Config snapshot for diff | `capabilities file.json` | — |
| Env health check | `check-env` | — |

---

## 11. Discrepancies with ai-skills (source-verified 2026-04-18)

The JasperFx ai-skills doc at `C:\Code\JasperFx\ai-skills\wolverine\observability\command-line-diagnostics.md`
contains two errors that had also propagated into older versions of CritterBids' own skill docs
(`wolverine-message-handlers.md` §11 and `critter-stack-testing-patterns.md` §20 before 2026-04-18).
The issues are tracked in `docs/jasperfx-open-questions.md` as question #3.

**Issue A: `codegen-preview --message` is not a valid flag.**
Source: `C:\Code\JasperFx\wolverine\src\Wolverine\Diagnostics\WolverineDiagnosticsCommand.cs:27`.
The handler flag is declared as `[FlagAlias("handler", 'h')]` — only `--handler` and `-h` work.
Use `--handler`, which (per its description) accepts the message type name, short class name,
*or* handler class name.

**Issue B: `describe-resiliency` does not exist as a sub-command.**
Source: same file, `Execute` method switch statement. Only `codegen-preview` and `describe-routing`
are implemented. Grepping `C:\Code\JasperFx\wolverine`, `\marten`, and `\polecat` for
`describe-resiliency` returns zero hits. Use the **Error Handling** section of `dotnet run describe`
for the equivalent inspection — it's produced by `WolverineSystemPart.WriteErrorHandling()` as
part of the standard `describe` output.

CritterBids' pre-2026-04-18 docs were wrong about both; the edits in the 2026-04-18 diagnostics
consolidation fixed them. Flagging the ai-skills errors upstream via question #3.

---

## 12. Related Skills

- `wolverine-message-handlers.md §11` — handler-authoring debugging subset; cross-references this file
- `critter-stack-testing-patterns.md §20` — test-debugging subset; cross-references this file
- `observability.md` — continuous monitoring (OTEL, Prometheus, Grafana); the "what's happening right now" companion to this "let me investigate" file
- `aspire.md` — where local dev commands run; Aspire dashboard as the complementary runtime view
- `wolverine-message-handlers.md §(AP#14)` — the canonical example of using `describe-routing` to diagnose an integration-message zero-count assertion
- `critter-stack-testing-patterns.md §Cross-BC Handler Isolation` — when `describe-routing` output surprises you because the fixture is filtering handlers

---

## 13. External References

- Wolverine CLI guide: https://wolverine.netlify.app/guide/command-line.html
- Wolverine diagnostics guide: https://wolverine.netlify.app/guide/diagnostics.html
- Wolverine message storage management: https://wolverine.netlify.app/guide/durability/managing.html
- ai-skills source (with known errors per §11): `C:\Code\JasperFx\ai-skills\wolverine\observability\command-line-diagnostics.md`
- Wolverine diagnostics command source: `C:\Code\JasperFx\wolverine\src\Wolverine\Diagnostics\WolverineDiagnosticsCommand.cs`
- Wolverine describe output source: `C:\Code\JasperFx\wolverine\src\Wolverine\WolverineSystemPart.cs`
