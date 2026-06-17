# M9-S8: End-to-End + Housekeeping (M9 close)

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md)) — slice plan §7 (the milestone close; labelled "M9-S7" in the doc, renumbered to **S8** because the race fix took S7)
**Slice:** S8 of M9 — the final slice; lands on a clean `main` after the M9-S7 cross-queue race fix (PR #112, `78443c1`)
**Narrative:** `docs/narratives/006-seller-fulfills-post-sale-obligation.md` (the obligation-fulfillment spine the seller e2e dramatizes; Moments 1–4, with narrative 007's escalation-recovery as the adjacent UX). The housekeeping half anchors to the milestone doc, not a narrative.
**Agent:** Claude Code
**Estimated scope:** one PR; a seller-perspective Playwright e2e in `client/e2e/` + a CI-coverage verification + doc refreshes (`bounded-contexts.md`, `STATUS.md`, milestone doc) + a pre-M10 skills audit + the M9-S8 slice retro **and** the M9 milestone retrospective. **No backend domain change. No `CritterBids.Contracts` change.**

---

## Baseline this slice inherits

- All functional M9 slices are merged: S1 (`client/shared/` extraction + seller scaffold), S2/S3 (seller listing + query endpoints + the Listings `ExtendedBiddingTriggered` handler + cache-bridge evaluation), S4a/S4b (registration + listing management), S5 (live auction observation), S6 (obligation fulfillment), and S7 (the `CatalogListingView` cross-queue create-race fix — `Insert`-on-create + `DocumentAlreadyExistsException` retry).
- **Backend: 328 tests green** at the M9-S7 close (full `dotnet test CritterBids.slnx`); 0 errors / 0 warnings net-new (the `NU1903` MessagePack advisory in `CritterBids.AppHost` is a pre-existing baseline, not net-new).
- **Frontend:** four-app `client/` workspace (`bidder`, `ops`, `seller`, `shared`) plus the `e2e` member. Seller SPA at **117 Vitest tests** (M9-S6 close); bidder 25, ops 47. The seller dev server is the fourth Aspire child (`:5175`, base `/seller/`).
- **CI is already at full frontend coverage.** PR #111 (`7ba744f`) restructured the `frontend` job into a matrix that *already* builds+tests `bidder`/`ops`/`seller` and type-checks `shared`/`e2e`. **This slice does not add CI coverage — it verifies the matrix is correct and leaves it alone unless the seller e2e changes the `e2e` member's needs.** (Read the lived `ci.yml` before assuming the handoff's "extend CI" item is open work — it is not.)
- **`CLAUDE.md` §Frontend is already current** (three SPAs + `shared` + `e2e`, member counts correct — refreshed in earlier M9 slices). The stale doc surfaces are `docs/vision/bounded-contexts.md` (still describes *two* SPAs) and `docs/STATUS.md` (frozen at M8 close, v0.6).
- The M8-S7 bid-war e2e (`client/e2e/tests/bid-war.spec.ts`) is the harness this slice extends — its conventions (live Aspire stack, hub-asserted-before-traffic, per-run unique seed title, assert-through-the-UI, system-browser fallback) are the precedent.
- **The full obligation-lifecycle e2e was explicitly deferred to this slice** by the M9-S6 retro: "seller publishes → operator starts session → bidder bids → settlement → obligation → seller provides tracking → fulfilled."

## Goal

Close M9. Ship the milestone's last owed artifact — a **seller-perspective Playwright e2e** that drives the seller console through the obligation-fulfillment spine against the live Aspire stack (a seller's listing reaches sold, the seller console provides tracking through its own UI, and the obligation reaches `Fulfilled`) — verify the CI `frontend` matrix already covers the seller and shared members, refresh the documentation surface that still describes CritterBids as a two-SPA M8 system (`bounded-contexts.md`, `STATUS.md`) and correct the milestone doc's own slice table for the S7→S8 renumber, run the pre-M10 skills audit, and write both the M9-S8 slice retro and the M9 milestone retrospective with the exit criteria checked off honestly.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | **Authoritative for scope.** §1 exit criteria (the checklist this slice closes), §7 slice table (the row to renumber S7→S8 and the close-slice scope), §3 non-goals (operator session management is *not* a seller-console surface). |
| `docs/retrospectives/M9-S6-seller-obligation-fulfillment-retrospective.md` | The deferred full-lifecycle e2e (§"What remains"), the seller obligation UI surfaces the e2e asserts (`/obligations`, provide-tracking dialog, "Completed" terminal), and the `BidderGroupNotification` obligation push channel. |
| `docs/retrospectives/M9-S7-listings-cross-queue-race-fix-retrospective.md` + `docs/retrospectives/M8-retrospective.md` | The just-merged backend state (`Insert`-on-create + retry); and the **shape** for the M9 milestone retro (exit-criteria walk, session-by-session table, test-count table, key-learnings, deferred ledger). |
| `.claude/skills/frontend-slice-discipline/SKILL.md` + `.claude/skills/signalr/SKILL.md` | Working rules (live-host verification, browser-fallback playbook, assert-through-UI) — and the two are the **subjects** of the pre-M10 skills audit. |
| `client/e2e/` (`tests/bid-war.spec.ts`, `playwright.config.ts`, `README.md`) | The harness to extend: conventions, the seed-then-drive pattern, the run story to amend. |
| `src/CritterBids.Api/Dev/DemoSeedEndpoint.cs` | The dev-only seed (`POST /api/dev/seed-flash`) the e2e leans on — it drives a server-minted **registered seller** through publish→attach→start to `Open` and returns `sellerId`/`listingId`/`sessionId`. The identity-bridge open question turns on it. |
| `docs/vision/bounded-contexts.md` (§ frontend/SPA, ~line 206) + `docs/STATUS.md` + `CLAUDE.md` §Frontend | The refresh targets — read for what is stale, not for scope. (CLAUDE.md is already current; verify rather than rewrite.) |

## In scope

**The e2e (the slice's headline):**

1. **A seller-perspective test in `client/e2e/`.** Extend the harness with one new spec that exercises the obligation-fulfillment spine *from the seller console's vantage* (`:5175`, base `/seller/`): a seller's listing reaches **sold**, the seller console surfaces the resulting obligation, the seller **provides tracking through the console UI** (the `react-hook-form` dialog from M9-S6), and the obligation reaches the **`Fulfilled` ("Completed")** terminal — asserted through what the seller *sees*, never into hub internals (ADR 026 applied to assertions). Carry the bid-war conventions verbatim: assert the seller console's `BiddingHub` connection before any traffic, seed with a per-run unique title, tear down after.
2. **Reach "sold" with the existing seed + a single winning bid.** The listing reaching `Open` is the existing `POST /api/dev/seed-flash` pipeline; the winning bid is a supporting action (place it however reads cleanest and stays timing-stable — the bidder is not the subject under test). Use a short Flash duration so the close + settlement + obligation chain completes inside the Playwright timeout.
3. **A documented run story.** Amend `client/e2e/README.md` so a contributor can run the new seller e2e from a clean checkout: prerequisites (the seller dev server must be up at `:5175`), invocation, and the expected wall-clock duration. The seed/identity-bridge technique the test uses is recorded there or in the retro.

**CI (verify, do not extend):**

4. **Confirm the `frontend` matrix already covers seller + shared.** Read `ci.yml`; confirm `seller` is `build-test`, `shared` is `typecheck`, `e2e` is `typecheck`, and the `CI` aggregator contract is unchanged. Record in the retro that the coverage was already complete (PR #111) — **no matrix edit** unless the new e2e spec breaks the `e2e` type-check, in which case the fix is to keep the member type-clean, not to add a Playwright-in-CI run (M8-D2 stands; the e2e is local-only).

**Housekeeping (doc refresh):**

5. **`docs/vision/bounded-contexts.md`** — the frontend/SPA framing (~line 206) still says two SPAs (`bidder`, `ops`). Update it to the lived three-SPA + `shared` + `e2e` workspace (seller console = the seller's window on the same engine; `client/shared/` = the frontend analogue of `CritterBids.Contracts`).
6. **`docs/STATUS.md`** — regenerate to the M9-close posture per the file's own regeneration rule: milestone ladder (M9 → ✅ Complete), the M9 slice ledger, test counts (328 backend / seller+bidder+ops Vitest / Playwright e2e count), the deferred/risk ledgers re-derived from the M9 retros, and the "As of" line bumped to this PR's commit.
7. **`docs/milestones/M9-seller-console.md`** — flip status to ✅ Complete; **correct §7's slice table** so S7 is the cross-queue race fix and S8 is this close (the table currently shows the close as S7); tick the §1 exit-criteria checkboxes with honest annotations (e.g. the "307 backend tests" baseline is now 328; CI coverage landed earlier than this slice; `client/shared/` extracted at S1).
8. **`CLAUDE.md` §Frontend** — verify it is current (three SPAs, `shared`, `e2e`, member counts). Edit only what is actually stale; record "verified current" in the retro if no edit is needed.

**Pre-M10 skills audit (per the `m9-skills-review` carry-forward):**

9. **Audit the frontend skill surface before M10 scoping** and record the findings (in the M9 retro or a short note the retro points at): does `.claude/skills/frontend-slice-discipline` need M9 lessons (seller as a third consumer; the seed-then-inject e2e identity bridge)? Is `.claude/skills/signalr` bloated or does it need the seller `BidderGroupNotification` obligation-channel rule? Are the M9 patterns worth capturing — the `FakeHubConnection` triple-copy → `@critterbids/shared` test-util extraction candidate, the actor-form-identity-from-context pattern, the shared-schema extraction the seller revealed? Each finding lands as either an applied skill edit **in this PR** or a recorded carry-forward to M10; do not author a brand-new skill file speculatively (that is a milestone-level decision).

**Close:** the **M9-S8 slice retro** (per `docs/retrospectives/README.md`) **and** the **M9 milestone retrospective** (the ladder's final deliverable; M8-retro shape) — the latter walking the milestone against its §1 exit criteria, recording the per-slice ledger, the test-count growth (M8's 307 → M9's 328), the key cross-slice learnings, the new ADRs/decisions if any, and what M9 defers outward to M10.

## Explicitly out of scope

- **Any backend domain change.** M9's sanctioned backend exceptions (S2/S3 endpoints, the S3/S7 housekeeping) are spent. If the e2e exposes a domain defect, stop and escalate — do not fix it inside this slice. (A *dev-only* harness affordance for the operator session-start bridge is the one possible exception — see Open question 1 — and only if `Insert`-on-`DemoSeedEndpoint`-style seed-then-inject proves insufficient; it is recorded and sanctioned, never a silent `.cs` touch, and adds no domain event/contract.)
- **Operator session management in the seller console.** `CreateSession`/`AttachListingToSession`/`StartSession` are staff/`StaffOnly` commands (milestone §3 non-goal). The e2e reaches `Open` via the dev seed or a dev bridge — never by surfacing operator controls in the seller SPA.
- **Playwright-in-CI infrastructure** beyond the recorded verification (item 4) — M8-D2 stands; no bespoke Aspire-in-Actions plumbing.
- **New seller SPA surfaces or journeys.** No settlement-summary view, no dispute UI, no Timed-listing path — all standing M9 non-goals. The e2e renders the already-shipped surfaces.
- **`CritterBids.Contracts` changes**, new domain events, new saga transitions, new BC modules.
- **Authoring a new skill file from scratch** — the audit applies edits or records carry-forwards; a net-new skill is an M10-scoping decision.

## Conventions to pin or follow

- The seller e2e follows the M8-S7 harness conventions promoted to automation: hub connection asserted before any traffic, per-run unique seed title, assert through the rendered re-query result (a push is a signal, never authoritative data — ADR 026), full teardown.
- E2e dependencies live only in the `client/e2e/` workspace member; neither app's production build or type-check sees them (ADR 025 dependency hygiene).
- Frontend slice discipline (`.claude/skills/frontend-slice-discipline`): read the lived backend (the seed pipeline, the obligation read model, the seller session/registration code) before writing the test; render/assert the lived subset; close with the live smoke the e2e *is*.
- Retro discipline (`docs/retrospectives/README.md`): item codes mirror this prompt; verbatim error messages for any failure; negative assertions where they prove the work; the mandatory `## Spec delta — landed?` paragraph.

## Spec delta

- `docs/narratives/006-seller-fulfills-post-sale-obligation.md` gains a Document History row: the obligation-fulfillment spine (Moments 1–4) is now validated end-to-end by an automated seller-perspective Playwright test, not only by the M9-S6 component tests + HTTP smoke.
- `docs/milestones/M9-seller-console.md` §1 exit criteria are checked off and the milestone closes; its §7 slice table is corrected for the S7 (race fix) / S8 (close) renumber; the **M9 milestone retrospective** becomes the canonical record of what M9 shipped and what it defers to M10.
- No ADR and no canonical-spec (workshop/contract/event) amendment: this slice renders and verifies existing surfaces. Any skill-file edit from the audit is recorded in the retro, not a spec delta.

## Acceptance criteria

- [ ] A seller-perspective Playwright spec exists in `client/e2e/` and passes locally against the Aspire-orchestrated stack: a seeded listing reaches sold, the seller console surfaces the obligation, tracking is provided through the console's `react-hook-form` dialog, and the obligation reaches the `Fulfilled` / "Completed" terminal — the run recorded in the retro.
- [ ] The spec asserts the seller console's `BiddingHub` connection before the first action and seeds with a per-run unique title (both greppable in the test source).
- [ ] `client/e2e/README.md` documents the new seller e2e's prerequisites, invocation, and expected duration; the seed/identity-bridge technique is written down (README or retro).
- [ ] The CI `frontend` matrix is confirmed to already cover `seller` (build-test) and `shared` (typecheck); the retro records that coverage predates this slice (PR #111); the `e2e` member still type-checks clean; the `CI` aggregator contract is unchanged.
- [ ] `docs/vision/bounded-contexts.md` describes the lived three-SPA + `shared` + `e2e` frontend (no longer two SPAs).
- [ ] `docs/STATUS.md` regenerated to the M9-close posture (M9 ✅ Complete, slice ledger, 328 backend test count, deferred/risk ledgers re-derived, "As of" bumped).
- [ ] `docs/milestones/M9-seller-console.md` status is ✅ Complete, §7's slice table reflects S7=race fix / S8=close, and §1 exit-criteria boxes are ticked with honest annotations.
- [ ] `CLAUDE.md` §Frontend verified current (edited only if stale; "verified" noted in the retro otherwise).
- [ ] The pre-M10 skills audit is recorded, each finding either applied as a skill edit in this PR or carried forward to M10 with rationale.
- [ ] `dotnet build` + `dotnet test CritterBids.slnx` green (328+, untouched); all four SPA Vitest suites green; all SPA builds exit 0; the `e2e` member type-checks clean and does not enter any app's production type-check.
- [ ] `docs/retrospectives/M9-S8-end-to-end-housekeeping-retrospective.md` authored **and** the M9 milestone retrospective authored.
- [ ] Branched off `main`; one PR; no commit to `main`; no `Co-Authored-By` trailer.

## Open questions

1. **The e2e identity bridge (the central design question — flag, do not guess past the lean).** The seller console mints its own anonymous `ParticipantId`, but the operator `create-session/attach/start` step that opens a listing is staff/bus-only. **Lean: seed-then-inject** — `POST /api/dev/seed-flash` already creates a *registered seller* and drives the listing to `Open`, returning that `sellerId`; inject it into the seller console's session (`sessionStorage`) so the console adopts the seeded seller identity and renders that seller's listing + obligation. This needs **no backend change**. If the console's registered-seller gate does not recognize an externally-registered participant (read the seller session/registration code to confirm), the fallbacks, in order: (a) drive the console's own one-click register-seller for the injected id if it is idempotent; (b) escalate a *dev-only* harness affordance (an operator-bridge seed that starts a session for a console-created listing) — sanctioned and recorded per frontend-slice-discipline Rule 2, adding no domain capability. Resolve by reading the lived code; record the choice in the retro.
2. **How much of the live-observation beat (narrative 005) to assert from the seller's vantage.** The seller seeing the bid arrive / gavel fall on their own listing enriches the test but adds timing surface already covered by the bid-war e2e. Lean: make the obligation spine the criterion; include the seller-side live-observation assertions only if timing-stable, else record them as covered-by-bid-war. Do not ship a flaky test to tick a box (the M8-S7 extended-bidding-beat precedent).
3. **Backend defects surfaced by the e2e** are escalations, not fixes — M9 has no remaining sanctioned domain exception.
4. **Skills-audit scope creep.** If a finding implies a net-new skill file or a cross-cutting restructure, record it as an M10-scoping carry-forward rather than authoring it here (Rule 4 / item 9).
