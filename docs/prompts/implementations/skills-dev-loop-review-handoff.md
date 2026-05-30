# Handoff — Dev-Loop Skills Review

**For:** the next session that reviews the three draft agent skills.
**Status:** drafts placed, **not committed**, **not yet corrected** for the items below.
**Companion to:** `.github/skills/{docker,build-fix,testcontainers-dotnet}/SKILL.md` (v0.1, `draft-pending-review`).

This is not an implement-a-slice prompt. It's a pre-review checklist: things to be aware of, plus decisions to put to the user with options before accepting the skills.

---

## 1. What landed last session

- Three SKILL.md files at `.github/skills/docker/`, `.github/skills/build-fix/`, `.github/skills/testcontainers-dotnet/`.
- Each is an MIT-licensed derivative (sources in frontmatter `source:`): `docker` and `build-fix` from codewithmukesh/dotnet-claude-kit; `testcontainers-dotnet` from testcontainers/claude-skills.
- Frontmatter matches the existing `openspec-*` house style (`license`, `compatibility`, `metadata`), marked `version: "0.1"`, `status: draft-pending-review`.
- Nothing committed. No `.github/skills/README.md` index added (left per instruction).

---

## 2. Must-verify / correct before accepting

These were drafted partly from stale assumptions. Each is contradicted by the current repo (grounded in `Directory.Packages.props` + `src/` + `tests/`). Fix before the skills are trusted.

1. **Deployable host is wrong.** The `docker` skill's Dockerfile and compose target `CritterBids.Auctions`. The actual web host is **`CritterBids.Api`** (modular monolith), with **`CritterBids.AppHost`** for orchestration. Retarget the Dockerfile/compose to `CritterBids.Api`.
2. **Aspire IS in use.** The `docker` skill claims "CritterBids does not use Aspire — Compose-first." Repo has `CritterBids.AppHost` + `Aspire.Hosting.AppHost/PostgreSQL/RabbitMQ` (13.3.5). Reframe: **Aspire = local orchestration; Docker/Compose = the Hetzner deploy artifact.** The rebuild-vs-restart and branch-swap guidance should be rewritten against the AppHost as the local runner (the dashboard already gives per-resource log streaming, which covers the original "verify the log line" use case).
3. **RabbitMQ is missing from the local picture.** Messaging is Wolverine over RabbitMQ (`WolverineFx.RabbitMQ` 5.39.3). The compose sample only has Postgres. Add a `rabbitmq` service for the deploy compose (Aspire supplies it locally).
4. **Polecat / SQL Server are still present.** `Polecat` 3.2.1, `WolverineFx.Polecat`, and `Testcontainers.MsSql` are all referenced. Memory's "ADR 011 all-Marten, Polecat eliminated" is **contradicted** by the current package set — there appears to be a dual event-store (Marten/Postgres + Polecat/SQL Server). Both skills currently assume Postgres-only. See decision **D2**.
5. **Test stack specifics.** Repo uses **xUnit 2.9.3 (v2, not v3)**, **Shouldly** for assertions, and **Alba** 8.5.2 as the HTTP integration harness. The `testcontainers-dotnet` skill uses `Assert.Equal` and a raw `DocumentStore` — switch assertions to Shouldly and reference Alba host fixtures.
6. **Existing fixtures already exist — don't fork them.** Each `tests/<BC>.Tests/` has a `Fixtures/` directory (e.g., `tests/CritterBids.Auctions.Tests/Fixtures/`). The skill introduces a fresh `PostgresFixture`. **Read the existing fixture first** and align the skill to document the real pattern rather than a parallel one.
7. **BC list in `build-fix` is off.** Actual BCs in `src/`: Participants, Selling, Listings, Auctions, Settlement, Obligations, **Relay** (+ Api, Contracts, AppHost). The draft lists "Operations" (not present). Correct the list.
8. **Analyzer/nullable categories rarely fire.** `Directory.Build.props` sets `RunAnalyzersDuringBuild=false` and does **not** treat warnings as errors. The `build-fix` "nullable-as-errors" and "analyzer error" categories will rarely trigger on a normal build — keep them but de-emphasize; note `NoWarn` includes `NU1507`.
9. **.NET target — confirmed correct.** `net10.0`, `LangVersion 14`. The `sdk:10.0` / `aspnet:10.0` image tags are fine. No change needed.

---

## 3. Decisions to surface to the user (with options)

Put each of these to Erik as a choice with a recommendation — don't decide unilaterally.

- **D1 — Skill home vs the prose library.** `docs/skills/` is already a substantial prose skill library (`critter-stack-testing-patterns.md`, `observability.md`, `diagnostics.md`, `aspire.md`, `marten-*.md`) with its own `README.md` index. The new `.github/skills` SKILL.md files overlap it.
  - (a) Two layers on purpose: prose library in `docs/skills` (human reading) + executable SKILL.md in `.github/skills` (agent loading), cross-linked. *(recommended)*
  - (b) Consolidate: fold new content into the existing `docs/skills` docs, drop the `.github/skills` copies.
  - (c) Promote the generic ones (`build-fix`, `testcontainers-dotnet`) to the CritterCab shared library; keep `docker` CritterBids-local.
- **D2 — Dual event-store scope.** Resolve against the actual current ADR (`docs/decisions/`) first.
  - (a) Both skills cover both stores (add SQL Server container + Polecat notes).
  - (b) Scope the new skills to Marten/Postgres; lean on the existing `docs/skills/polecat-event-sourcing.md` for the SQL Server side. *(likely, pending ADR check)*
  - (c) Confirm Polecat is being phased out and scope to Marten with a deprecation note.
- **D3 — Testcontainers isolation convention.** Standard for deterministic UUIDNext v5 stream IDs in a shared container: (a) per-test database/schema, (b) Marten tenancy, (c) randomized seed. **Whatever the existing `Fixtures/` already does wins** — document that rather than imposing a new one.
- **D4 — `build-fix` delivery form.** (a) Keep as SKILL.md only. (b) Also add a Claude Code `/build-fix` slash command mirroring the upstream command form.
- **D5 — Commit / PR grouping** (one-session-one-PR). (a) Single PR "add dev-loop agent skills." (b) Split test-infra / deploy-infra / dev-loop.
- **D6 — `.github/skills` index README.** None exists there yet. (a) Add a thin `.github/skills/README.md` indexing the seven skills (build-fix, docker, testcontainers-dotnet + four openspec). (b) Leave it index-less and reference from `docs/skills/README.md` or `CLAUDE.md`.

---

## 4. Read these first next session

- `.github/skills/{docker,build-fix,testcontainers-dotnet}/SKILL.md` — the drafts.
- `tests/CritterBids.Auctions.Tests/Fixtures/` — the real container/host fixture (settles items 5, 6, D3).
- `docs/skills/critter-stack-testing-patterns.md`, `observability.md`, `aspire.md`, `polecat-event-sourcing.md` — overlap surface (D1, D2).
- `Directory.Packages.props` — the stack of record.
- `docs/decisions/` — the Marten/Polecat ADR, to settle D2.

---

## 5. Not done (so this session doesn't assume otherwise)

- Drafts are uncorrected for §2; treat their CritterBids-specific claims as provisional.
- Nothing committed; no `.github/skills` README; no reconciliation with `docs/skills` yet.
