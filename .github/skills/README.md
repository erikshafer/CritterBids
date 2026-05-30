# Agent Skills — CritterBids

This directory contains executable agent skills — SKILL.md files that Copilot CLI and other agents load to implement CritterBids-specific development workflows.

## Skill Index

| Skill | Description | Purpose |
|---|---|---|
| **build-fix** | Iteratively fix a broken CritterBids build | Use when `dotnet build` fails; provides categorization of error types and knowledge of CritterBids-specific false alarms (Wolverine codegen, Marten compiled types) |
| **docker** | Containerize CritterBids for local dev and Hetzner VPS deploy | Use when writing/fixing Dockerfile, docker-compose.yml, or deciding rebuild vs restart vs reload |
| **testcontainers-dotnet** | Write reliable integration tests using Testcontainers for PostgreSQL | Use when writing integration tests for handlers, sagas, projections, or event-sourced aggregates |
| **openspec-apply-change** | Implement tasks from an OpenSpec change | Use with M6 Obligations BC and adopting BCs only; see `openspec/README.md` for adoption ledger |
| **openspec-archive-change** | Archive a completed change in the OpenSpec workflow | Use with M6 adopting BCs only |
| **openspec-explore** | Enter explore mode for thinking through design and investigation | Use with M6 adopting BCs only |
| **openspec-propose** | Propose a new OpenSpec change with artifacts generated in one step | Use with M6 adopting BCs only |

## Prose Documentation

For narrative, examples, and deep-dive patterns, see `docs/skills/README.md`. That directory contains:
- Multi-page implementation guides (Marten event sourcing, Wolverine handlers, sagas, testing)
- Anti-patterns and lessons learned
- Architecture decision notes
- Cross-references to each other and to these executable skills

## Relationship to docs/skills

- **This directory (`.github/skills/`):** Executable, agent-loadable, task-focused
- **`docs/skills/`:** Narrative, human-readable, pattern-focused

The two are complementary. Load a skill from `.github/skills/` to execute a task; read the corresponding pattern guide from `docs/skills/` to understand the *why* behind it.

## Which Scope to Use

- **CritterBids-specific:** `build-fix`, `docker`, `testcontainers-dotnet` (this repo only)
- **OpenSpec workflow:** `openspec-*` (M6 adopting BCs)

## See Also

- `docs/skills/README.md` — Pattern library and skill index
- `CLAUDE.md` — Development guidelines, conventions, and architecture overview
- `openspec/README.md` — OpenSpec workspace adoption ledger and capability specs
