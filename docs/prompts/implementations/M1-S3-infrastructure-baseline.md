# M1-S3: Infrastructure Baseline

**Milestone:** M1 — Skeleton
**Slice:** S3 — Infrastructure baseline
**Agent:** @PSA
**Estimated scope:** one PR, one new project + two files modified in `src/` + package pins + two doc fixes

## Goal

Create `src/CritterBids.AppHost`, wire PostgreSQL, SQL Server, and RabbitMQ
as Aspire resources with connection strings injected into the API host, and
configure Wolverine (RabbitMQ transport) and Polecat (SQL Server) at the host
level in `CritterBids.Api`. End state: `dotnet run --project
src/CritterBids.AppHost` boots all four services — Postgres, SQL Server,
RabbitMQ, and the API — with no hardcoded connection strings in
`appsettings.Development.json`. The two existing smoke tests still pass.

This session also closes two carry-over documentation items that were deferred
from prior sessions: the stale docker-compose fallback text in `CLAUDE.md`
(flagged in M1-S2 retro and ADR 006 Consequences) and the stale tier-first
test layout diagram in `docs/milestones/M1-skeleton.md` §4 (flagged in M1-S1
retro, still unassigned).

**No bounded context code. No stream registration. No endpoints. No test
changes beyond confirming the existing two still pass.**

## Context to load

- `docs/milestones/M1-skeleton.md` — milestone scope, §5 defines the Aspire
  resource list and Polecat/Wolverine host-level config requirements
- `docs/decisions/006-infrastructure-orchestration.md` — **primary
  implementation specification for this session.** The ADR's Consequences
  section defines what resources to provision and what is explicitly out of
  scope for this ADR. Treat it as authoritative. Note: `docs/skills/aspire.md`
  does not yet exist; the ADR Consequences section is the substitute.
- `CLAUDE.md` — conventions, tools table, Quick Start section (contains stale
  docker-compose text to remove in this session)
- `docs/retrospectives/M1-S1-solution-baseline.md` — flags orphaned version
  properties in `Directory.Build.props` (`WolverineVersion`, `MartenVersion`,
  etc.) that need to be confirmed current before restoring pins
- `docs/retrospectives/M1-S2-infrastructure-orchestration-adr.md` — confirms
  the CLAUDE.md stale text and flags the milestone doc §4 layout issue
- `docs/prompts/README.md` — the ten rules this prompt obeys
- `docs/skills/csharp-coding-standards.md` — C#/.NET baseline conventions
- `docs/skills/polecat-event-sourcing.md` — Polecat host-level configuration
  patterns (status 🟡; fill in gaps as you go and note any missing content
  for the S7 retro session)
- `docs/skills/wolverine-message-handlers.md` — Wolverine configuration
  conventions including transport setup

## In scope

### New project: `src/CritterBids.AppHost`

- `CritterBids.AppHost.csproj` with Aspire AppHost SDK reference.
- `Program.cs` provisions four resources:
  - **PostgreSQL** — for Marten BCs arriving in M2+. Not consumed in M1;
    wired now so M2 requires no infrastructure changes.
  - **SQL Server** — for Polecat BCs (Participants in M1, Settlement and
    Operations later).
  - **RabbitMQ** — Wolverine transport. No active subscribers in M1.
  - **`CritterBids.Api`** — references all three infrastructure resources
    via Aspire connection string injection.
- Add the project to `.slnx`.
- Add the project to `docs/milestones/M1-skeleton.md` §4 solution layout
  diagram (see doc fixes below).

### Modified project: `src/CritterBids.Api`

- `Program.cs` updated to:
  - Configure Wolverine with the RabbitMQ transport, connection string
    resolved from the Aspire-injected environment variable.
  - Configure Polecat for SQL Server, connection string resolved from the
    Aspire-injected environment variable. **Host-level only** — no stream
    registration, no aggregate configuration. That is M1-S4's job.
  - `opts.Policies.AutoApplyTransactions()` applied in the Polecat
    configuration per §6 of the milestone doc.
- No endpoints added. No auth wiring. No `AddParticipantsModule()` call.

### `Directory.Packages.props` — add required pins

Add pins for all packages this session actually references. At minimum:

- Aspire AppHost SDK and hosting extension packages
- Wolverine core + Wolverine.RabbitMQ
- JasperFx / Polecat packages required for host-level SQL Server configuration
- Any `Microsoft.Extensions.*` packages not already pinned

Before adding, check `Directory.Build.props` for existing `WolverineVersion`,
`MartenVersion`, `AlbaVersion`, `SwashbuckleAspNetCoreVersion` properties
(flagged in M1-S1 retro as orphaned). Verify those version values are still
current before restoring them in `Directory.Packages.props`. If a version
appears stale, flag it in the open-questions section of the retro — do not
silently use a stale pin.

No `Version=` attribute on any `<PackageReference>`.

### Doc fix 1: `CLAUDE.md` — remove stale docker-compose text

Per ADR 006 Consequences and the M1-S2 retro, CLAUDE.md Quick Start contains
this stale paragraph:

> A `docker-compose.yml` is maintained as a fallback for contributors who
> don't want to run Aspire; if present, `docker compose up -d && dotnet run
> --project src/CritterBids.Api` is the equivalent path.

Remove this paragraph. Also remove the `Docker Compose (fallback)` row from
the "Preferred Tools & Stack" table. After the edit, the Quick Start should
describe only the Aspire path.

### Doc fix 2: `docs/milestones/M1-skeleton.md` §4 — layout diagram

Two edits to §4:

1. Add `CritterBids.AppHost` to the `src/` block of the solution layout
   diagram — it now exists.
2. Correct the test project names in the `tests/` block to reflect the
   Layout 2 naming convention pinned in M1-S1 (`{ProductionProject}.Tests`),
   if the diagram currently shows tier-first names. (Flagged in M1-S1 retro
   as unresolved.)

## Explicitly out of scope

- **No `CritterBids.Participants` project.** No BC scaffold, no module
  registration, no `AddParticipantsModule()` call. That is M1-S4.
- **No Polecat stream registration.** No `Participant` aggregate, no event
  types, no stream setup in the Polecat configuration. Host-level init only.
- **No Marten configuration.** Marten arrives with the Selling BC in M2.
- **No endpoints.** No HTTP routes, no controllers, no minimal API handlers.
- **No auth wiring.** The M1 `[AllowAnonymous]` override is established in
  M1-S4 when the first endpoint lands.
- **M1-D4 (Polecat namespace GUID).** Assigned to M1-S4; do not generate or
  pin the UUID v5 constant in this session.
- **No `docker-compose.yml`.** ADR 006 dropped this path entirely.
- **No E2E test project** (`CritterBids.E2E.Tests`) — deferred per §3.
- **ADR discoverability gap** (no `docs/decisions/README.md` index, no CLAUDE.md
  routing to decisions) — assigned to M1-S7.
- **`docs/skills/aspire.md`** — does not exist; deferred to M1-S7 where it
  will be authored retrospectively.
- **`docs/decisions/0001-uuid-strategy.md`** (UUID strategy ADR) — assigned
  to M1-S5.
- **No CI workflow changes.**
- **No SignalR, no frontend, no real-time wiring** — all out of M1 scope per
  §3 of the milestone doc.

## Conventions to pin or follow

- Connection strings flow exclusively from Aspire resource references. Do
  not add any connection string values to `appsettings.Development.json`.
  An `appsettings.Development.json` may exist or may not — either is fine,
  but it must not contain connection strings that duplicate or shadow what
  Aspire injects.
- `opts.Policies.AutoApplyTransactions()` is required in the Polecat host
  configuration per M1-skeleton.md §6. This session is the first to write
  Polecat config; establish it correctly here.
- Container image choices (SQL Server tag, RabbitMQ management vs. standard,
  Postgres version) are not decided by ADR 006 — that is explicitly deferred
  to this session. Make pragmatic choices, document them in the retro, and
  add brief inline comments in `AppHost/Program.cs` noting the chosen tags.
- `sealed record` for any new types; `IReadOnlyList<T>` for collections. No
  new types are expected in this session, but the rule applies if any arise.
- Central package management: no `Version=` on any `<PackageReference>`.
- `InternalsVisibleTo` for `CritterBids.AppHost` is not required — the AppHost
  has no production types that tests need to access.

## Acceptance criteria

- [ ] `src/CritterBids.AppHost/CritterBids.AppHost.csproj` exists and is
      listed in `.slnx`.
- [ ] `AppHost/Program.cs` provisions PostgreSQL, SQL Server, RabbitMQ, and
      `CritterBids.Api` with resource references wiring connection strings.
- [ ] `CritterBids.Api/Program.cs` configures Wolverine with RabbitMQ
      transport; connection string resolved from environment, not hardcoded.
- [ ] `CritterBids.Api/Program.cs` configures Polecat for SQL Server at
      host level with `opts.Policies.AutoApplyTransactions()`; connection
      string resolved from environment, not hardcoded.
- [ ] `dotnet run --project src/CritterBids.AppHost` boots all four services
      without error (API + Postgres + SQL Server + RabbitMQ).
- [ ] `dotnet build` succeeds with zero errors and zero warnings from new or
      modified projects.
- [ ] `dotnet test` reports 2 passing tests, zero failing — no existing tests
      broken.
- [ ] `Directory.Packages.props` contains all new package pins; no `Version=`
      attribute on any `<PackageReference>` anywhere in the solution.
- [ ] No connection string values appear in `appsettings.Development.json`
      (or the file is absent).
- [ ] `CLAUDE.md` Quick Start no longer contains the docker-compose fallback
      paragraph.
- [ ] `CLAUDE.md` "Preferred Tools & Stack" table no longer lists Docker
      Compose.
- [ ] `docs/milestones/M1-skeleton.md` §4 solution layout includes
      `CritterBids.AppHost` and reflects Layout 2 test project naming.
- [ ] No files created or modified outside `src/CritterBids.AppHost/`,
      `src/CritterBids.Api/`, `Directory.Packages.props`, `CLAUDE.md`,
      `docs/milestones/M1-skeleton.md`, and this session's retrospective.
- [ ] No BC projects, no stream registration, no endpoints introduced.

## Open questions

- **Aspire package versions.** Confirm the current stable release of the
  Aspire workload and hosting packages at session time. `Directory.Build.props`
  may already define an `AspireVersion` property from earlier exploratory
  work — check before adding pins, and use whatever is present if it is
  current.
- **Orphaned version properties in `Directory.Build.props`.** `WolverineVersion`,
  `MartenVersion`, `AlbaVersion`, `SwashbuckleAspNetCoreVersion` were left
  over from M1-S1's strip. Before restoring pins that use these properties,
  verify the version values are still current. If any appear stale, flag in
  the retro — do not silently use an outdated pin.
- **Container image tags.** ADR 006 explicitly defers SQL Server tag,
  RabbitMQ variant (management vs. standard), and Postgres version to this
  session. Make a pragmatic choice, add a brief inline comment in
  `AppHost/Program.cs`, and record the chosen tags in the retro. If the
  choice requires more than a minute of consideration, flag and stop.
- **Polecat host-level migration strategy.** If Polecat requires explicit
  schema creation or migration calls beyond connection string injection (e.g.
  a call to `ApplyAllDatabaseChangesOnStartup()` or similar), flag and stop
  — do not choose a migration strategy unilaterally. This is an architectural
  decision that belongs as an open item in the milestone doc.
- **Wolverine host configuration surface area.** If wiring RabbitMQ transport
  requires decisions about exchange topology, durable inbox/outbox, or
  persistence store for the outbox — decisions not covered by
  `wolverine-message-handlers.md` — flag and stop per rule 7.
- If any root configuration file (`Directory.Build.props`, `.slnx`, etc.)
  conflicts with this prompt's scope, flag and stop before editing. (Carried
  forward from M1-S1 retro finding #2.)
