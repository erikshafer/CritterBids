# OpenSpec Workspace

This directory is the OpenSpec CLI's workspace for CritterBids. It is created and populated by the [`openspec`](https://github.com/Fission-AI/OpenSpec) CLI (v1.3.1) and is governed by [ADR 021: OpenSpec CLI Adoption for M6 (per-BC, Opt-In)](../docs/decisions/021-openspec-cli-for-m6.md).

The workspace is **peer to `docs/`** at the repo root. The CLI hardcodes the directory name; it cannot be relocated.

## When this workspace is in scope

OpenSpec is adopted **per-BC, opt-in, starting with M6 Obligations**. Per ADR 021:

- M1–M5 BCs (Participants, Selling, Auctions, Listings, Settlement) **do not** use OpenSpec. Their specs live in `docs/narratives/` and `docs/workshops/` under ADR 020's spec-delta closure loop.
- M6 Obligations adopts OpenSpec at design-phase opening.
- M6 Relay and M6 Operations evaluate independently at their own opening sessions.
- Future BCs (none planned beyond M6) evaluate at their own openings.

If you are working a non-adopting BC, you can ignore this directory entirely.

## Installing the CLI

The OpenSpec CLI is a **contributor-installed tool**, not a repo dependency. No `package.json` is committed to the repo. To install:

```powershell
npm install -g @fission-ai/openspec
```

The version pinned at ADR 021's authoring is **v1.3.1**. Confirm with:

```powershell
openspec --version
```

If your version drifts significantly from v1.3.1, check ADR 021's "Trigger for revisit" section before bumping.

## Driving OpenSpec from GitHub Copilot

OpenSpec init was run with `--tools github-copilot`. Four Copilot slash commands are available:

| Slash command | Action |
|---|---|
| `/opsx:propose` | Scaffold a new change folder with all four artifacts (proposal, delta spec, design, tasks). |
| `/opsx:apply` | Implement a change's tasks list; check off tasks as they ship. |
| `/opsx:archive` | Move a completed change to `changes/archive/YYYY-MM-DD-<slug>/` and sync delta specs into the capability spec. |
| `/opsx:explore` | Free-form exploration mode; read-only with respect to code. |

The slash-command prompts live at `.github/prompts/opsx-*.prompt.md` and the backing skills live at `.github/skills/openspec-*/SKILL.md`. **Do not edit these files** — they are OpenSpec-CLI-regenerated and edits will be overwritten by `openspec update`.

## Workspace layout

```
openspec/
├── README.md           ← this file
├── specs/              ← accumulated capability specs, one per adopting BC
│   └── <capability>/
│       └── spec.md     ← grows from archived changes; empty until first archive
└── changes/            ← active change proposals (each adopted-BC slice gets one)
    ├── <slug>/         ← four artifacts per change
    │   ├── .openspec.yaml
    │   ├── proposal.md
    │   ├── design.md
    │   ├── tasks.md
    │   └── specs/
    │       └── <capability>/
    │           └── spec.md   ← delta: ADDED/MODIFIED/REMOVED requirements
    └── archive/
        └── YYYY-MM-DD-<slug>/   ← completed changes; preserved verbatim
```

## Capability-name ledger

Per ADR 021, capability names are kebab-case and one-per-BC. Names are proposed in this ADR and confirmed at the adopting BC's opening session.

| BC | Capability name | Status | Confirmed at |
|---|---|---|---|
| Obligations | `obligation-lifecycle` | proposed | M6 Obligations opening (TBD) |
| Relay | `bid-relay` | proposed (working) | M6 Relay opening (TBD) |
| Operations | `operator-dashboards` | proposed (working) | M6 Operations opening (TBD) |

Update this table when each BC's opening session lands.

## Per-BC adoption ledger

| BC | Adoption decision | Decided at |
|---|---|---|
| Obligations | ✅ adopt (per ADR 021) | M6 design-opening pending |
| Relay | ⏸ evaluate at opening | — |
| Operations | ⏸ evaluate at opening | — |

Three outcomes are valid per ADR 021 §3: **adopt** (Obligations path), **decline** (CritterCab path; proceed under ADR 020 alone), **defer** (proceed under ADR 020; revisit at the BC's first complex change). Record the decision in the BC's opening prompt and update this table.

## Reconciliation with ADR 020

For OpenSpec-adopting BCs, the spec-delta closure loop's four steps gain explicit physical homes:

| Closure-loop step | Non-adopting BC | OpenSpec-adopting BC |
|---|---|---|
| 1. Prompt declares spec delta | `## Spec delta` section: full prose | `## Spec delta` section: short pointer to `openspec/changes/<slug>/proposal.md` + delta spec |
| 2. Session executes | unchanged | unchanged |
| 3. Retro confirms | `## Spec delta — landed?` paragraph | `## Spec delta — landed?` paragraph: confirms OpenSpec archive + narrative Document History row both landed |
| 4. Spec records amendment | Narrative `## Document History` row | Narrative `## Document History` row **plus** OpenSpec capability-spec sync at archive time |

The OpenSpec change folder is **authoritative for the SHALL-form requirement**. The narrative is **authoritative for the journey prose**. Where they conflict at retrospective time, the conflict is a finding (per ADR 016's four lanes: `narrative-update`, `workshop-update`, `code-update`, `document-as-intentional`) and resolves in the same PR.

## CI validation

CI validation is **deferred** per ADR 021 §10. Run `openspec validate <change> --strict` locally as part of the slice author's flow. CI enforcement is reopened after the first M6 OpenSpec slice ships, on the same reasoning ADR 020 deferred CI enforcement: reviewer discipline first, automated enforcement after the discipline is proven and failure modes are observed.

## Document history

- **v0.1** (2026-05-28): Authored alongside ADR 021. `openspec init --tools github-copilot` scaffolded the workspace and the `.github/prompts/` + `.github/skills/` Copilot integration. Capability-name and adoption ledgers populated with M6 BCs in proposed/pending state.
