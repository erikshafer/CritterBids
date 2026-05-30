---
name: build-fix
description: Iteratively fix a broken CritterBids build until it compiles green. Use when `dotnet build` fails, after a large refactor or rename, after a NuGet/Wolverine/Marten version bump, or when the user says "fix the build", "build is broken", or "make it compile". Runs a bounded loop: build, parse and categorize errors, apply the smallest correct fix, rebuild, repeat. Knows the CritterBids-specific false alarms — Wolverine compile-time codegen and Marten compiled types — that produce errors that look like your bug but aren't.
license: MIT
compatibility: Requires the .NET 10 SDK (dotnet build).
metadata:
  author: CritterBids
  version: "0.1"
  status: draft-pending-accepted
  source: "Adapted from the /build-fix command in codewithmukesh/dotnet-claude-kit (MIT)"
---

# Build-Fix — CritterBids

> Adapted from the MIT-licensed `/build-fix` command in codewithmukesh/dotnet-claude-kit. Same bounded autonomous loop; the categorization table and the "don't fix this, it's not your bug" section are CritterBids-specific.

## Loop

1. Run `dotnet build` on the solution (or the failing project). Capture full output.
2. Parse the diagnostics. Group by error code and by file.
3. **Categorize before fixing** (see table). Fix root causes, not symptoms.
4. Apply the smallest change that addresses a category. Prefer one category per iteration so cause and effect stay legible.
5. Rebuild. If green, stop. If errors decreased, continue. If errors *increased* or repeated unchanged, stop and report — don't thrash.
6. **Iteration cap: 5.** If not green by then, summarize what's left and what you tried; hand back to the human.

## Categorize first

| Symptom | Likely cause | Fix |
|---|---|---|
| `CS0246`/`CS0234` missing type or namespace | Missing `using`, missing project reference, or renamed type | Add using / reference; update the rename |
| Nullable warnings as errors (`CS8602`, `CS8618`) | `TreatWarningsAsErrors` + nullable context | Fix the nullability properly (guard, `required`, init) — do **not** sprinkle `!` to silence |
| `CS0535`/`CS0534` interface/abstract not implemented | Decider or handler signature drifted | Align to the current contract; check the BC's canonical aggregate shape |
| Analyzer error from a JasperFx/Wolverine analyzer | Handler/saga shape violates a Wolverine rule | Read the analyzer message — it usually names the exact convention |
| Errors in `Internal/Generated/**` or codegen files | **Stale Wolverine codegen** (see below) | Don't edit generated files — regenerate or delete them |
| `CS0117`/wrong overload on a Marten call | Marten API moved between versions | Check the installed Marten version against Context7 docs (`/jasperfx/marten`) |

## Don't fix this — it's not your bug

These produce errors that *look* like code you broke but are actually stale or generated state:

- **Wolverine compile-time codegen.** If errors point at `Internal/Generated/` or pre-generated handler/saga types, the generated code is stale relative to your handlers. Delete the generated output (or switch `TypeLoadMode` to dynamic for the loop) and rebuild — Wolverine regenerates. Never hand-edit generated files to make them compile.
- **Marten compiled query / compiled type artifacts.** Same principle — regenerate, don't patch.
- **`bin`/`obj` staleness after a branch swap or version bump.** If errors are incoherent (types that clearly exist reported missing), do a clean first: `dotnet clean` then delete stray `bin`/`obj`, then rebuild. Burning an iteration on a clean is cheaper than chasing a ghost.

## Hard rules

- **Never** make the build pass by deleting or commenting out tests, suppressing diagnostics wholesale (`#pragma warning disable` over a file, `<NoWarn>` blanket), or weakening a contract just to satisfy the compiler. A green build that hid the real error is worse than a red one.
- Keep fixes inside the bounded contexts they belong to (Participants, Selling, Listings, Auctions, Settlement, Obligations, Relay, Contracts, Api). A build error is not license to reach across BC boundaries.
- Match existing conventions — primary constructors, records, `required`, the Decider aggregate shape — rather than introducing a new style mid-fix.

## On green

State what was broken and what fixed it, in one or two lines per category. If the build is part of a session that ends in a commit, use a conventional-commit message (`fix:` or `build:` as appropriate). Don't auto-commit unless the human asked.
