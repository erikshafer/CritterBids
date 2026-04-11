# M1-S1: Solution Baseline

**Milestone:** M1 — Skeleton
**Slice:** S1 — Solution baseline
**Agent:** @PSA
**Estimated scope:** one PR, solution + empty projects + central package pins

## Goal

Stand up the empty CritterBids solution structure that every subsequent M1
session builds on: the solution file, the host and contracts projects, the
test project layout, and `Directory.Packages.props` with the minimal set of
package pins needed for the baseline to compile and for tests to run. No
bounded context code, no Aspire AppHost, no Docker Compose file, no
infrastructure wiring. The PR lands a solution that builds green and an
empty test project that runs green.

## Context to load

- `docs/milestones/M1-skeleton.md` — milestone scope; this session implements
  the first row of the session breakdown and nothing beyond it
- `CLAUDE.md` — solution layout rules, modular monolith rules, Do Not list
- `docs/prompts/README.md` — the ten rules this prompt obeys
- `docs/skills/csharp-coding-standards.md` — C#/.NET baseline, target
  framework, nullable, language version, file-scoped namespaces, `sealed
  record`, `IReadOnlyList<T>`

## In scope

- Update `.slnx` to include the new projects listed below.
- `src/CritterBids.Api` — empty ASP.NET Core host project. Default
  `Program.cs` that builds and runs; no endpoints, no Wolverine, no Marten,
  no auth wiring yet. This is the skeleton host; later sessions wire it up.
- `src/CritterBids.Contracts` — empty class library. No types yet. Exists so
  that the modular monolith rule ("BCs depend on Contracts only") has a
  target from day one.
- `tests/CritterBids.Api.Tests` — empty xUnit test project referencing
  `src/CritterBids.Api`. Contains one trivial passing smoke test so the test
  runner is exercised.
- `tests/CritterBids.Contracts.Tests` — empty xUnit test project referencing
  `src/CritterBids.Contracts`. Contains one trivial passing smoke test.
- `Directory.Packages.props` — central package management, pins listed
  below. No transport packages, no database drivers, no Critter Stack
  packages. Those arrive with the BC slices.
- Verify `dotnet build` and `dotnet test` both succeed from a clean clone.

## Test project structure — Layout 2

CritterBids uses **Layout 2**: one test project per production project,
named `{ProductionProject}.Tests`, living under `tests/` with the same
relative folder shape as `src/`. This session establishes the layout with
the two test projects above. Every future production project added to the
solution is expected to add its `.Tests` sibling in the same PR.

## Directory.Packages.props pins — minimal

Pin only what this PR actually needs to compile and test. No speculative
additions.

- **C# / .NET BCL** — whatever the chosen target framework requires beyond
  the implicit SDK references (typically nothing; list explicitly only if
  the SDK does not provide it).
- **xUnit** — test framework, runner, and analyzers, pinned together at
  compatible versions. Applied to test projects only.
- **Shouldly** — assertion library. Applied to test projects only.

No Microsoft.NET.Test.Sdk omissions; pin it alongside xUnit. No FluentAssertions,
no Moq, no AutoFixture, no Testcontainers — those arrive when a session
actually needs them.

## Explicitly out of scope

- **No Aspire.** No `CritterBids.AppHost`, no `Aspire.Hosting.*` packages,
  no service defaults project. The Aspire-vs-Compose decision is tracked in
  the M1 milestone doc and will be resolved in a later M1 session, not here.
- **No Docker Compose file.** No `docker-compose.yml`, no `.env` template.
- **No bounded context projects.** No `CritterBids.Participants`, no module
  registration extension, no `AddParticipantsModule()`, nothing.
- **No M1-D2 work.** M1-D2 has been removed from the milestone doc; do not
  resurrect it.
- **No Wolverine, no Marten, no Polecat, no auth wiring.** Not even
  `builder.Host.UseWolverine()` as a placeholder. The host is empty.
- **No integration events, no domain events, no shared abstractions** in
  `CritterBids.Contracts`. The project exists; it is empty.
- **No CI workflow changes.** If `.github/workflows/` needs a nudge to pick
  up new projects, that is a separate PR.

## Conventions to pin or follow

- Target framework, nullable, language version, file-scoped namespaces, and
  implicit usings come from `docs/skills/csharp-coding-standards.md`. If
  `Directory.Build.props` already sets these solution-wide, do not
  re-declare them in individual `.csproj` files.
- Central package management via `Directory.Packages.props` is mandatory —
  no `Version=` attributes on `<PackageReference>` in any `.csproj`.
- Test project layout follows Layout 2 as described above. This is the
  decision this session pins for the rest of the project.

## Acceptance criteria

- [ ] `.slnx` lists the four new projects.
- [ ] `src/CritterBids.Api/CritterBids.Api.csproj` exists, builds, and the
      host runs (`dotnet run`) without error.
- [ ] `src/CritterBids.Contracts/CritterBids.Contracts.csproj` exists and
      builds. The project contains no types.
- [ ] `tests/CritterBids.Api.Tests/` exists with one passing smoke test.
- [ ] `tests/CritterBids.Contracts.Tests/` exists with one passing smoke
      test.
- [ ] `Directory.Packages.props` contains pins for xUnit (framework +
      runner + analyzers + Test.Sdk) and Shouldly, and nothing else beyond
      what the BCL requires.
- [ ] No `Version=` attribute appears on any `<PackageReference>` in any
      `.csproj`.
- [ ] `dotnet build` succeeds from a clean clone with no warnings from the
      new projects.
- [ ] `dotnet test` runs and reports two passing tests, zero failing.
- [ ] No files created under `src/` or `tests/` beyond the four project
      folders described above.
- [ ] No files created at the repo root beyond updates to `.slnx` and
      `Directory.Packages.props`.

## Open questions

- Target framework choice is assumed to be whatever `csharp-coding-standards.md`
  specifies. If the skill file is silent, flag and stop — do not pick a TFM
  unilaterally.
- If `Directory.Build.props` or `Directory.Packages.props` already exists
  with content that conflicts with this prompt, flag the conflict and stop
  before editing.
