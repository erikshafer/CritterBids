# M8-S7: End-to-End + Housekeeping

**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — slice plan §7, row M8-S7 (the milestone close)
**Slice:** S7 of M8 — the final slice; lands against the finished feed posture S6b delivered
**Narrative:** `docs/narratives/001-bidder-wins-flash-auction.md` (the bid-war spine the e2e dramatizes; Moments 1–7). The housekeeping half anchors to the milestone doc, not a narrative.
**Agent:** Claude Code
**Estimated scope:** one PR; Playwright e2e harness + `ci.yml` frontend-job extension + doc refreshes + two retros (~15 files). **No backend code change. No `CritterBids.Contracts` change.**

---

## Baseline this slice inherits

- S6b closed the ops feed: 22-value push vocabulary, polling stopgap deleted, topology invariant in CI. Backend 307 tests green; frontend 72 Vitest green (`@critterbids/ops` 47, `@critterbids/bidder` 25).
- Commit `7b31147` (post-S6b) made both SPAs Aspire-orchestrated children: a single `dotnet run --project src/CritterBids.AppHost` starts Postgres, RabbitMQ, the API host, the bidder dev server (pinned 5173), and the ops dev server (pinned 5174). The e2e drives against this.
- The CI `frontend` job exists (`.github/workflows/ci.yml`) but covers **`@critterbids/bidder` only** — the ops workspace is not built or tested in CI.
- Playwright is **not installed** in `client/` — ADR 013 pins it as the e2e tool, but the S6b smoke used an ad-hoc `playwright-core` script. The harness is greenfield.

## Goal

Close M8. Ship the milestone's last owed artifact — a Playwright multi-context e2e in which two anonymous bidders fight a live bid war against a running host (the ADR 013 Playwright use case and the M8 exit criterion) — extend the CI frontend job to the full workspace, refresh the documentation surface that still describes M8 as planned or in-flight (milestone doc, `STATUS.md`, `bounded-contexts.md`, `CLAUDE.md`), record the `client/shared/` extraction decision, apply the S6b skill-correction carry-forward, and write both the S7 retro and the M8 milestone retrospective with the exit criteria checked off honestly.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M8-frontend-spas.md` | **Authoritative for scope.** §1 exit criteria (the checklist this slice closes), §2 "CI: frontend job", §3 non-goals, §7 row M8-S7. |
| `docs/retrospectives/M8-S6b-ops-feed-completion-dispute-control-retrospective.md` | Session-close baseline; §"What remains" names this slice's items; the smoke-harness lessons (hub-indicator-before-seeding, per-run unique seed titles) and the Node-WS skill correction. |
| `docs/narratives/001-bidder-wins-flash-auction.md` | The journey the e2e automates: session start → catalog → bid → outbid → extended bidding → gavel-fall → sold. |
| `.claude/skills/frontend-slice-discipline/SKILL.md` + `.claude/skills/signalr/SKILL.md` | Working rules (live-host verification, browser-fallback playbook) — and the slice-discipline smoke playbook step 3 is the **edit target** for the carry-forward. |
| `.github/workflows/ci.yml` | The lived CI surface: path filters, the bidder-only `frontend` job to extend, the `CI` aggregator contract. |
| `src/CritterBids.AppHost/Program.cs` + `client/package.json` | The launch story the e2e leans on (SPA children, ports, `CRITTERBIDS_API_URL`) and the npm-workspaces layout the harness must join. |
| `docs/STATUS.md`, `docs/vision/bounded-contexts.md`, `CLAUDE.md` (frontend + ops sections) | The refresh targets — read for what is stale, not for scope. |

## In scope

**The e2e (the slice's headline):**

1. **Playwright harness in `client/`.** Install and configure Playwright as ADR 013 pins it. Working assumption for its home: a dedicated workspace member (e.g. `client/e2e/`) so e2e dependencies stay out of both app builds — record the placement choice and reasoning in the retro; if it amounts to more than adding a workspace member (e.g. it would reshape ADR 025's layout story), flag before proceeding (Open question 1).
2. **The two-bidder bid-war test.** Multi-context: two isolated browser contexts, each minting its own anonymous session against the live Aspire-orchestrated host. One listing (seeded per run with a unique title — the S6b lesson), alternating bids, and the assertions that make it a *war*: bidder B sees bidder A's bid arrive live (push, no reload), bidder A sees the outbid state after B overbids, and the listing reaches sold with the winner rendered correctly in both contexts. Assert each context's hub connection **before** any bid is placed (the other S6b lesson — a dead connection and a missing push are otherwise indistinguishable). Include the extended-bidding beat only if it can be made timing-stable (Open question 3).
3. **A documented run story.** The e2e's prerequisites (running AppHost) and invocation land as a workspace script plus a short README note in its home directory — a contributor must be able to run it from a clean checkout without reading the retro.

**CI:**

4. **Extend the `frontend` job to the whole workspace** — `@critterbids/ops` build (tsc strict + vite) and Vitest join the existing bidder steps (or a matrix, if that reads cleaner). The path filter and aggregator contract stay as they are.
5. **Decide and record whether the Playwright e2e joins CI.** Lean: defer — the e2e needs the full Aspire stack (Postgres, RabbitMQ, API, two dev servers), and standing that up in Actions is its own piece of infrastructure work. A recorded deferral with a carry-forward is an acceptable close; building bespoke CI infrastructure is not in this slice's budget (Open question 2).

**Housekeeping:**

6. **Doc refreshes.** `docs/milestones/M8-frontend-spas.md` status line and §2 staleness (the ops app is no longer "planned"); the §1 exit-criteria checkboxes ticked with honest annotations (the "281 backend tests" baseline is now 307 via the three sanctioned exceptions — say so rather than silently editing); `CLAUDE.md` frontend section (ops row no longer *(planned, M8-S5)*; `client/shared/` line updated per item 7); `docs/vision/bounded-contexts.md` frontend/SPA framing; `docs/STATUS.md` regenerated to the M8-close posture; test-baseline counts updated wherever recorded (307 backend / 72+ frontend Vitest).
7. **`client/shared/` extraction decision — decision only.** Evaluate the actually-duplicated surface between the two apps (Zod wire schemas, the SignalR provider pattern) and record extract-now vs defer-to-M9 with reasoning (in the retro, plus the `CLAUDE.md` line). No extraction implementation in this slice regardless of the answer — if the evaluation says extract, that is a recorded follow-up, not S7 work.
8. **Skill correction carry-forward.** `.claude/skills/frontend-slice-discipline/SKILL.md` smoke-playbook step 3: the "Node reproduces browser credential transport faithfully" claim holds only when the `ws` package is not resolvable; from inside `client/` the signalr client picks it up and sends an Authorization header. Add the pin-token-in-URL form as the faithful Node reproduction (per the S6b retro Finding 2).

**Close:** the M8-S7 retro **and** the M8 milestone retrospective (the ladder's final deliverable), the latter measuring the milestone against its §1 exit criteria and recording what M8 defers outward (seller console → M9, `client/shared/`, Playwright-in-CI if deferred).

## Explicitly out of scope

- **Any backend change.** M8's three sanctioned exceptions (S3a, S3c, S6b) are spent; there is no fourth. If the e2e exposes a backend defect, stop and escalate — do not fix it inside this slice.
- **`client/shared/` extraction implementation** — item 7 produces a decision record only.
- **Playwright CI infrastructure** beyond the recorded decision (item 5) — no bespoke Aspire-in-Actions plumbing.
- **New SPA surfaces or journeys.** No seller console (M9), no buyer "report a problem" form, no `Refund`/`Closed` ops controls, no notification-history expansion.
- **The backend CI matrix** — complete per PR #77; untouched.
- **Visual polish, accessibility audit, PWA scope expansion, wire-value renames** (`BidPlacedOperations` et al. stay as-is) — all standing non-goals.

## Conventions to pin or follow

- The e2e follows the S6b smoke-harness lessons now being promoted from playbook to automated test: hub connection asserted before traffic, per-run unique seed titles, full teardown after.
- The e2e asserts through the UI (what a bidder sees), not by reaching into hub internals — pushes are signals and the rendered re-query result is the observable (ADR 026 spirit, applied to test assertions).
- Test placement and dependency hygiene per the npm-workspaces layout (ADR 025): e2e dependencies must not leak into either app's production build or type-check.

## Spec delta

- `docs/narratives/001-bidder-wins-flash-auction.md` gains a Document History row: the bid-war spine (Moments 1–7) is now validated end-to-end by an automated two-bidder Playwright test, not only by manual smoke.
- `docs/milestones/M8-frontend-spas.md` §1 exit criteria are checked off and the milestone closes; the M8 milestone retrospective becomes the canonical record of what M8 shipped and what it defers (seller console, `client/shared/`, any Playwright-in-CI deferral).
- `.claude/skills/frontend-slice-discipline` smoke playbook step 3 is corrected per S6b Finding 2 (Node WS credential transport).

## Acceptance criteria

- [ ] Playwright e2e exists at its recorded home in `client/`, with its placement decision and run instructions written down (workspace script + README note).
- [ ] The e2e passes locally against the Aspire-orchestrated host: two contexts, two anonymous sessions, live cross-context bid visibility, an outbid observed, the listing sold with the winner correct in both contexts — and the run is recorded in the retro.
- [ ] The e2e asserts hub connection before the first bid and seeds with a per-run unique title (greppable in the test source).
- [ ] `.github/workflows/ci.yml` frontend job builds and tests **both** `@critterbids/bidder` and `@critterbids/ops`; the aggregator contract is unchanged.
- [ ] The Playwright-in-CI decision is recorded (retro + a comment at the frontend job if deferred).
- [ ] `CLAUDE.md` no longer marks the ops app *(planned)*; the M8 milestone doc status/§2 reflect shipped reality; §1 exit-criteria boxes are ticked with the 307-test baseline annotation; `STATUS.md` and `bounded-contexts.md` refreshed.
- [ ] The `client/shared/` decision is recorded with reasoning (retro + `CLAUDE.md` line); no extraction code in the diff.
- [ ] `.claude/skills/frontend-slice-discipline/SKILL.md` carries the corrected Node-WS note.
- [ ] `dotnet build` + `dotnet test` green (307/307, untouched); `npm test` green in both apps; both SPA builds exit 0; the new e2e workspace member does not enter either app's production type-check.
- [ ] `docs/retrospectives/M8-S7-end-to-end-housekeeping-retrospective.md` authored **and** the M8 milestone retrospective authored.

## Open questions

1. **E2e harness placement.** If giving Playwright a home requires more than adding a workspace member under `client/` (e.g. it would amend ADR 025's layout decision rather than instantiate it), flag and stop rather than reshaping the workspace.
2. **Playwright in CI.** If a cheap, reliable path exists (the runner already has Docker; the question is stability and runtime), it may be proposed — but do not build it speculatively. Default is a recorded deferral; anything beyond a small, obviously-stable job addition is escalated.
3. **The extended-bidding beat.** Include it in the e2e only if it can be asserted without timing flakiness (the trigger window is real wall-clock time). If it cannot, the bid-war core is the criterion and the uncovered beat is recorded in the retro — do not ship a flaky test to tick a box.
4. **Backend defects surfaced by the e2e.** Any failure that traces to backend behavior is an escalation, not a fix — M8 has no remaining sanctioned backend exception.
