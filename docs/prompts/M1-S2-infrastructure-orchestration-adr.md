# M1-S2: Infrastructure Orchestration ADR

**Milestone:** M1 — Skeleton
**Slice:** S2 — Infrastructure orchestration decision
**Agent:** @PSA
**Estimated scope:** one PR, one new ADR + one edit to `docs/milestones/M1-skeleton.md` §5

## Goal

Resolve the Aspire-vs-Compose decision that M1-S2 inherited from planning. The M1 milestone doc currently leaves the orchestration path ambiguous — `CLAUDE.md` originally described Docker Compose, the planning session assumed .NET Aspire, and §5 of `M1-skeleton.md` describes both paths without committing to one. Subsequent M1 sessions cannot wire infrastructure until one path is chosen. This session produces the decision as an ADR and updates the milestone doc to reference it. **No code, no `.csproj` changes, no packages added, no infrastructure wiring of any kind** — this is a documentation-only PR that makes the next session's prompt possible.

## Context to load

- `docs/milestones/M1-skeleton.md` — authoritative for M1 scope; §5 is the section this session rewrites
- `CLAUDE.md` — current routing-layer description of the local-dev story (`docker compose up` + `dotnet run`)
- `docs/prompts/README.md` — the ten rules this prompt obeys
- `docs/prompts/M1-S1-solution-baseline.md` — prior session's prompt, for format and conflict-review language carried forward
- `docs/retrospectives/M1-S1-solution-baseline-retrospective.md` — prior session's retro, for any M1-S1 findings relevant to this session
- Existing ADRs under `docs/decisions/` if any — for format reference. Note that `0001-uuid-strategy.md` is reserved per `M1-skeleton.md` §8 M1-D3 even if not yet authored; this ADR is therefore `0002-infrastructure-orchestration.md`.

## In scope

- Author `docs/decisions/0002-infrastructure-orchestration.md` as a new ADR.
- ADR status: **Accepted** at the end of this session, not Proposed. The point of this session is to decide, not to kick the decision further down the road.
- ADR must cover, at minimum:
  - **Context** — why the decision is needed, what the two options are, what constraints CritterBids places on the choice (single-contributor project, modular monolith, conference-demo vehicle, Polecat + SQL Server as first-class dependency, Wolverine transport needed eventually, Marten arriving in a later BC).
  - **Options considered** — .NET Aspire AppHost and Docker Compose, each evaluated against the CritterBids constraints above. Do not treat this as a generic Aspire-vs-Compose comparison from the internet; the evaluation must be grounded in this project's specifics.
  - **Decision** — one path, named explicitly, with a one-sentence rationale.
  - **Consequences** — what changes in the repo as a result, what future sessions need to know, what is explicitly *not* decided by this ADR (e.g. container image choices, version pinning strategy, production deployment — none of those are M1 concerns).
- Rewrite `docs/milestones/M1-skeleton.md` §5 ("Infrastructure Baseline") to commit to the chosen path. Remove the "both paths must work at M1 exit" language. Remove the fallback description for whichever option was not chosen. Add a link to the new ADR.
- Update `docs/milestones/M1-skeleton.md` §9 session breakdown table: the row that currently says *TBD* for infrastructure implementation should be updated to reflect that this session (S2) is the ADR and that the implementation session is now S3.
- Verify internal links resolve and the milestone doc still reads coherently after the §5 rewrite.

## Explicitly out of scope

- **No code.** No `.csproj` files created or modified. No `Program.cs` edits. No package additions to `Directory.Packages.props`.
- **No new projects.** No `CritterBids.AppHost`, no service defaults project, no `docker-compose.yml` file, no `.env` template — regardless of which path the ADR chooses. Creating those artifacts is M1-S3's job; this session only produces the decision that makes M1-S3 draftable.
- **No CLAUDE.md rewrite.** `CLAUDE.md`'s current description of the local-dev story may become stale as a result of this decision. Flagging the staleness is fine; fixing it is not this session's job. Add a note to the ADR's Consequences section if relevant.
- **No other ADRs.** The UUID strategy ADR (`0001-uuid-strategy.md` per §8 M1-D3) is not this session's concern.
- **No bounded context projects, no Wolverine wiring, no Polecat wiring, no Marten wiring, no auth wiring.** None of M1-S3 onward.
- **No CI workflow changes.**

## Conventions to pin or follow

- ADR format follows whatever convention already exists in `docs/decisions/`. If no ADRs exist yet, use a minimal Michael Nygard-style template: Title, Status, Context, Decision, Consequences. Do not introduce a more elaborate format on a first-ADR basis.
- ADR numbering is zero-padded three-digit, prefixed with the number and a kebab-case slug: `0002-infrastructure-orchestration.md`.
- The ADR's Decision section is one path, named in one sentence. No equivocation, no "we'll try Aspire and fall back to Compose if it doesn't work out." If the session cannot commit to one path for any reason, that is a rule 7 escalation — flag and stop.

## Acceptance criteria

- [ ] `docs/decisions/0002-infrastructure-orchestration.md` exists.
- [ ] ADR status is **Accepted**.
- [ ] ADR Decision section names exactly one orchestration path (Aspire or Compose) in one sentence.
- [ ] ADR Context section references CritterBids-specific constraints (single contributor, modular monolith, conference demo, Polecat + SQL Server), not generic tradeoffs.
- [ ] ADR Consequences section names at least one follow-up item for M1-S3 and at least one item that is explicitly *out of scope* for the ADR.
- [ ] `docs/milestones/M1-skeleton.md` §5 is rewritten to commit to the chosen path and links to the new ADR.
- [ ] `docs/milestones/M1-skeleton.md` §5 no longer contains the phrase "both paths must work" or any fallback description for the non-chosen option.
- [ ] `docs/milestones/M1-skeleton.md` §9 session table reflects the new sequence: S2 is the ADR, S3 is the infrastructure implementation, subsequent rows shift accordingly.
- [ ] No files created or modified outside `docs/decisions/`, `docs/milestones/`, and this prompt's own retrospective (if the agent writes one).
- [ ] No `.csproj`, `.slnx`, `.props`, or source files touched.
- [ ] `dotnet build` and `dotnet test` still succeed from a clean clone (sanity check — no code changed, so this should be trivially true).

## Open questions

- Target framework choice and language version are assumed from `docs/skills/csharp-coding-standards.md`. If anything in this session requires a framework-version-specific claim (e.g. "Aspire 9 requires .NET 9"), flag and stop — do not pick a TFM unilaterally.
- If `Directory.Build.props`, `Directory.Packages.props`, or any other root configuration file has content that conflicts with this prompt's scope, flag the conflict and stop before editing. (Carried forward from M1-S1 per retrospective finding #2.)
- If `docs/decisions/` already contains an ADR numbered `0002-*`, flag and stop — do not renumber. The prompt assumes `0002` is free.
- If `CLAUDE.md`'s description of the local-dev story conflicts irreconcilably with the ADR's chosen path (beyond a note-in-Consequences level of staleness), flag and stop so the user can decide whether to fix CLAUDE.md in this PR or split it off.
