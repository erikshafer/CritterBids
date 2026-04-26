# M1-S1: Solution Baseline — Retrospective

**Date:** 2026-04-11
**Milestone:** M1 — Skeleton
**Slice:** S1 — Solution baseline
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M1-S1-solution-baseline.md`

## Baseline

- Solution file (`CritterBids.slnx`) existed but was empty: `<Solution />`.
- `src/` and `tests/` directories existed but were empty.
- `Directory.Build.props` present with `net10.0`, `LangVersion 14`, `Nullable enable`, version properties for Wolverine/Marten/Alba/Swashbuckle, and three `InternalsVisibleTo` entries targeting a tier-first test layout.
- `Directory.Packages.props` present with `ManagePackageVersionsCentrally` enabled and 16 package pins spanning Marten, Wolverine, OpenTelemetry, Testcontainers, Alba, NSubstitute, and xUnit.
- Zero projects, zero tests, zero build output.

## Items completed

| Item | Description |
|------|-------------|
| S1a | `.slnx` updated with four projects in `/src/` and `/tests/` solution folders |
| S1b | `src/CritterBids.Api` — empty ASP.NET Core host with minimal `Program.cs` |
| S1c | `src/CritterBids.Contracts` — empty class library, no types |
| S1d | `tests/CritterBids.Api.Tests` — xUnit + Shouldly smoke test |
| S1e | `tests/CritterBids.Contracts.Tests` — xUnit + Shouldly smoke test |
| S1f | `Directory.Packages.props` stripped to minimal pins |
| S1g | `Directory.Build.props` aligned to Layout 2 (`InternalsVisibleTo`) and `ImplicitUsings` enabled |
| S1h | `CLAUDE.md` updated — Co-Authored-By trailer added to "Do Not" list |

## S1f: Directory.Packages.props — stripped to minimal

**Why this approach.** The prompt's acceptance criteria required "xUnit + Shouldly and nothing else beyond what the BCL requires." The existing file had 16 pins including Marten, Wolverine, OpenTelemetry, Testcontainers, Alba, NSubstitute, and coverlet — all speculative for a session that delivers zero BC code. Two resolution options were presented to the user (strip vs. leave in place); user chose strip.

**Structure after:**

```xml
<Project>
    <PropertyGroup>
        <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    </PropertyGroup>
    <ItemGroup>
        <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
        <PackageVersion Include="xunit" Version="2.9.3" />
        <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" ... />
        <PackageVersion Include="Shouldly" Version="4.3.0" />
    </ItemGroup>
</Project>
```

| Metric | Before | After |
|--------|--------|-------|
| Package pins | 16 | 4 |
| Non-test pins | 9 | 0 |

Removed packages will be re-added by sessions that need them. Versions are preserved in git history (`a228a98`).

## S1g: Directory.Build.props — Layout 2 alignment and ImplicitUsings

**Discovery / resolution.** Initial build failed with `CS0103: The name 'WebApplication' does not exist in the current context`. Root cause: `Microsoft.NET.Sdk.Web` relies on implicit usings to import `Microsoft.AspNetCore.Builder`, but `ImplicitUsings` was not enabled anywhere in the solution. Added `<ImplicitUsings>enable</ImplicitUsings>` to `Directory.Build.props` since it already owns solution-wide compiler settings. Build succeeded on next attempt.

**Why this approach (InternalsVisibleTo).** The existing entries (`$(AssemblyName).IntegrationTests`, `$(AssemblyName).UnitTests`) targeted a tier-first test layout. This session pins Layout 2 (`{ProductionProject}.Tests`), so the entries were replaced with a single `$(AssemblyName).Tests`. Two resolution options were presented; user chose replace.

| Metric | Before | After |
|--------|--------|-------|
| `InternalsVisibleTo` entries | 3 | 1 |
| `ImplicitUsings` | absent | `enable` |

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| After S1d (Api.Tests) | 1 | Pass |
| After S1e (Contracts.Tests) | 1 | Pass |
| **Final** | **2** | **All pass** |

## Build state at session close

- Errors: 0
- Warnings: 0
- Projects in solution: 4 (2 src, 2 tests)
- `Version=` on `<PackageReference>`: 0 occurrences (verified via grep)
- Package pins in `Directory.Packages.props`: 4
- Types in `CritterBids.Contracts`: 0
- Endpoints in `CritterBids.Api`: 0
- Wolverine/Marten/Polecat references: 0

## Key learnings

1. **`ImplicitUsings` must be explicitly enabled.** The existing `Directory.Build.props` set `LangVersion`, `TargetFramework`, and `Nullable` but omitted `ImplicitUsings`. `Microsoft.NET.Sdk.Web` does not make `WebApplication` available without it. This is a one-time fix — all future projects inherit it.
2. **Pre-existing root files need conflict review before session execution.** Both `Directory.Packages.props` and `Directory.Build.props` carried forward from earlier exploratory work and conflicted with the prompt's scope constraints. The prompt's "flag and stop" instruction caught this cleanly. Future prompts for greenfield sessions should include the same guard.
3. **Layout 2 naming diverges from the M1 milestone doc.** The milestone doc §4 diagrams a tier-first layout (`CritterBids.UnitTests/`, `CritterBids.IntegrationTests/`). This session's prompt was authoritative and pinned Layout 2. The milestone doc is now stale on this point.

## Verification checklist

- [x] `.slnx` lists the four new projects
- [x] `src/CritterBids.Api/CritterBids.Api.csproj` exists, builds, and the host runs without error
- [x] `src/CritterBids.Contracts/CritterBids.Contracts.csproj` exists and builds; no types
- [x] `tests/CritterBids.Api.Tests/` exists with one passing smoke test
- [x] `tests/CritterBids.Contracts.Tests/` exists with one passing smoke test
- [x] `Directory.Packages.props` contains pins for xUnit (framework + runner + Test.Sdk) and Shouldly only
- [x] No `Version=` attribute on any `<PackageReference>` in any `.csproj`
- [x] `dotnet build` succeeds with zero warnings from new projects
- [x] `dotnet test` reports two passing tests, zero failing
- [x] No files created under `src/` or `tests/` beyond the four project folders
- [x] No files created at repo root beyond updates to `.slnx` and `Directory.Packages.props` (note: `Directory.Build.props` and `CLAUDE.md` were also modified — see S1g and S1h)

## What remains / next session should verify

- **M1 milestone doc §4 layout diagram is stale.** Shows tier-first test layout; actual convention is Layout 2. Should be updated to reflect reality. In scope for the milestone, not assigned to a specific session.
- **Orphaned version properties in `Directory.Build.props`.** `WolverineVersion`, `MartenVersion`, `AlbaVersion`, `SwashbuckleAspNetCoreVersion` remain defined but are consumed by no project. Harmless until later sessions re-add their packages — at which point confirm the pinned versions are still current.
- **Removed packages need re-addition.** Sessions adding Wolverine, Marten/Polecat, Testcontainers, Alba, etc. must add their pins to `Directory.Packages.props`. The prior versions are in git history at `a228a98` for reference.
- **No Aspire, no Compose, no BCs.** All explicitly out of scope per prompt. Infrastructure baseline is the next session's job.
