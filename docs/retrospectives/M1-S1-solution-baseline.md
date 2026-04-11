# M1-S1 Retrospective: Solution Baseline

**Milestone:** M1 — Skeleton
**Session:** S1 — Solution baseline
**Prompt:** `docs/prompts/M1-S1-solution-baseline.md`
**Branch:** `m1-s1-solution-baseline`
**Date:** 2026-04-11

---

## What was delivered

Stood up the empty CritterBids solution structure: four projects, central package management, and a green build + test run.

### Projects created

| Project | Path | Purpose |
|---|---|---|
| CritterBids.Api | `src/CritterBids.Api/` | Empty ASP.NET Core host — `Program.cs` with `WebApplication` builder, no middleware or endpoints |
| CritterBids.Contracts | `src/CritterBids.Contracts/` | Empty class library — exists as the future cross-BC integration target |
| CritterBids.Api.Tests | `tests/CritterBids.Api.Tests/` | xUnit + Shouldly, one smoke test, references Api project |
| CritterBids.Contracts.Tests | `tests/CritterBids.Contracts.Tests/` | xUnit + Shouldly, one smoke test, references Contracts project |

### Root file changes

| File | Change |
|---|---|
| `CritterBids.slnx` | Updated from empty `<Solution />` to include all four projects in `/src/` and `/tests/` solution folders |
| `Directory.Packages.props` | Stripped to minimal pins: Microsoft.NET.Test.Sdk 18.3.0, xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Shouldly 4.3.0 |
| `Directory.Build.props` | Added `ImplicitUsings` enable; replaced `InternalsVisibleTo` entries with Layout 2 naming |
| `CLAUDE.md` | Added "Do Not include Co-Authored-By trailer" rule |

---

## Decisions made during session

### 1. Directory.Packages.props — stripped to minimal (Conflict A)

The existing file contained pins for Marten, Wolverine, Swashbuckle, OpenTelemetry, Testcontainers, Alba, NSubstitute, and coverlet. The prompt's acceptance criteria required "xUnit + Shouldly and nothing else beyond what the BCL requires." Decision: strip to minimal. Packages removed will be re-added by the sessions that actually need them.

### 2. InternalsVisibleTo — replaced with Layout 2 naming (Conflict A)

The existing `Directory.Build.props` had `InternalsVisibleTo` entries for `$(AssemblyName).IntegrationTests`, `$(AssemblyName).Api.IntegrationTests`, and `$(AssemblyName).UnitTests`. These matched a tier-first test layout, not the Layout 2 (`{ProductionProject}.Tests`) pattern pinned by this session. Decision: replace all three entries with a single `$(AssemblyName).Tests`.

### 3. ImplicitUsings enabled solution-wide

Not called out in the prompt, but required for the build to succeed. `Microsoft.NET.Sdk.Web` needs implicit usings enabled to resolve `WebApplication` without explicit `using` directives. Added `<ImplicitUsings>enable</ImplicitUsings>` in `Directory.Build.props` since it was already the home for solution-wide compiler settings.

### 4. Co-Authored-By trailer removed from commit convention

Added to the "Do Not" section of `CLAUDE.md` per user instruction during session.

---

## Acceptance criteria status

- [x] `.slnx` lists the four new projects
- [x] `src/CritterBids.Api/CritterBids.Api.csproj` exists, builds, host runs without error
- [x] `src/CritterBids.Contracts/CritterBids.Contracts.csproj` exists and builds; no types
- [x] `tests/CritterBids.Api.Tests/` exists with one passing smoke test
- [x] `tests/CritterBids.Contracts.Tests/` exists with one passing smoke test
- [x] `Directory.Packages.props` contains pins for xUnit + Test.Sdk + Shouldly only
- [x] No `Version=` attribute on any `<PackageReference>` in any `.csproj`
- [x] `dotnet build` succeeds with zero warnings from new projects
- [x] `dotnet test` reports two passing tests, zero failing
- [x] No files created under `src/` or `tests/` beyond the four project folders
- [x] No files created at repo root beyond updates to `.slnx` and `Directory.Packages.props`

**Note:** `Directory.Build.props` and `CLAUDE.md` were also modified at the root. The `Directory.Build.props` change was necessary for the build to succeed (ImplicitUsings) and to align InternalsVisibleTo with Layout 2. The `CLAUDE.md` change was a user-directed convention addition.

---

## What went well

- **Conflict detection worked.** The prompt's "flag and stop" instruction caught two real mismatches before any files were created. Both were resolved cleanly with user input.
- **Minimal scope held.** No Aspire, no Wolverine, no BCs, no Docker Compose — exactly as scoped.
- **Build and test green on first pass** after the ImplicitUsings fix.

---

## What to watch

- **Layout 2 vs. M1 milestone doc mismatch.** The M1 milestone doc (`docs/milestones/M1-skeleton.md` §4) describes a tier-first test layout (`CritterBids.UnitTests/`, `CritterBids.IntegrationTests/`) while this session pins Layout 2 (`{Project}.Tests`). The prompt was authoritative and Layout 2 was implemented. The milestone doc's §4 layout diagram is now stale and should be updated to reflect the actual convention.
- **Removed packages.** Sessions 2+ will need to re-add packages (Wolverine, Marten/Polecat, Testcontainers, Alba, etc.) to `Directory.Packages.props` as they enter scope. The versions from the original file are preserved in git history if needed for reference.
- **Directory.Build.props version properties.** `WolverineVersion`, `MartenVersion`, `AlbaVersion`, and `SwashbuckleAspNetCoreVersion` properties remain in `Directory.Build.props` but are currently unused (no project references them). They're harmless but orphaned until later sessions consume them.
