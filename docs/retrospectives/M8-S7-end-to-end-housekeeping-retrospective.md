# M8-S7: End-to-End + Housekeeping - Retrospective

**Date:** 2026-06-12
**Milestone:** M8 - React Frontend SPAs
**Slice:** S7 - End-to-end + housekeeping (the milestone close)
**Agent:** Claude Code
**Prompt:** `docs/prompts/implementations/M8-S7-end-to-end-housekeeping.md`

## Baseline

- Clean `main` at `7b31147` (post-S6b + the Aspire SPA-orchestration commit): one `dotnet run`
  starts Postgres, RabbitMQ, the API host, and both SPA dev servers (bidder 5173, ops 5174).
- Backend: 307 tests green across 10 projects. Frontend: 72 Vitest (ops 47, bidder 25).
- CI `frontend` job covered `@critterbids/bidder` only; Playwright not installed anywhere in
  `client/` (the S6b smoke used an ad-hoc `playwright-core` script).
- Docs stale at several altitudes: milestone doc status still "Planned", `CLAUDE.md` ops row
  still *(planned, M8-S5)*, `STATUS.md` at the M8-S4 posture, narrative 001 history ending at v0.4.

## Items completed

| Item | Description |
|------|-------------|
| 1 | Playwright harness as a third workspace member `client/e2e/` (`@playwright/test` 1.60.0, strict-base tsconfig, config without a `webServer` block) |
| 2 | Two-bidder bid-war e2e (`tests/bid-war.spec.ts`): two isolated contexts, two anonymous sessions, hub asserted before any bid, per-run unique seed title, live cross-context bids, outbid, in-window reclaim + reserve met, sold with winner correct in both contexts — **two consecutive green runs** (2.3 min each) |
| 3 | Run story: `client/e2e/README.md` (prerequisites, browser install, runtime expectation) + root workspace script `npm run e2e` |
| 4 | CI `frontend` job extended to the full workspace: ops build (tsc strict + vite) + ops Vitest + e2e type-check join the bidder steps; aggregator contract untouched |
| 5 | Playwright-in-CI: **deferred, recorded** (comment at the `frontend` job + §Item 5 below) |
| 6 | Doc refreshes: milestone doc v0.6 (status ✅, §1 ticked with annotations, §2 de-staled), `CLAUDE.md` frontend section, `bounded-contexts.md` SPA bullet, `STATUS.md` v0.6 regenerated, narrative 001 v0.5 history row |
| 7 | `client/shared/` extraction decision: **defer to M9** (reasoning below; recorded in `CLAUDE.md` + the workspace root manifest) |
| 8 | Skill correction: `frontend-slice-discipline` smoke-playbook step 3 carries the S6b Finding-2 Node-WS credential-transport caveat (pin token in URL) |
| — | M8 milestone retrospective authored (`M8-retrospective.md`) |

## Item 1: Harness placement — a workspace member, not a layout amendment

**Why this approach.** ADR 025 models `client/` as an npm-workspaces monorepo whose members are
added when they become real; `client/e2e/` is an instantiation of that layout, not an amendment
(Open question 1 never tripped). The decisive constraint was dependency hygiene: both apps run
`tsc --noEmit` inside `npm run build` with `include: ["src"]`, so a dedicated member keeps
`@playwright/test` and `@types/node` out of both app builds by construction — verified by both
builds passing with the member present. Alternatives rejected: tests inside `client/bidder/`
(leaks Playwright types into the bidder type-check, and the test spans both *contexts*, not one
app) and a repo-root `e2e/` (outside the workspace, duplicating toolchain config that
`tsconfig.base.json` already centralizes).

Chromium installs to `%LOCALAPPDATA%\ms-playwright` without elevation — the M8-S5-era "browser
download needs admin" assumption applies to the Playwright **MCP's** Chrome requirement, not to
`npx playwright install chromium`. The `PLAYWRIGHT_BROWSER_CHANNEL=msedge` escape hatch is wired
into the config for machines where the download is blocked.

## Item 2: The bid-war e2e — what it asserts and how the S6b lessons landed

The test is narrative 001's Moments 1–7 with two real contexts: A bids $30 → B sees it live
(headline price + bid count, no reload — and the bidder app has no polling, so any UI change
without navigation is push-driven by construction) → B bids $35 → A renders the outbid alert →
A reclaims at $55 inside the trigger window ("Reserve met." rendered from the acceptance
response) → the close extends → the gavel falls with A seeing the winner affordance and B seeing
"Sold — $55.00" and never the winner text. Session distinctness is asserted directly
(`sessionStorage` participant ids non-null and different).

The two S6b smoke lessons are now automated invariants: `openBidder()` gates on the hub
indicator reading **Connected** before returning (so a dead connection can never masquerade as a
missing push), and the seed title is per-run unique (`E2E Bid War ${Date.now().toString(36)}-…`),
so a stale row from a previous run can never satisfy an assertion.

**Timing design (Open question 3).** The extension *trigger* is deterministic: the backend
anchors the new close to `ScheduledCloseAt + 15s` for any bid inside the 30s window
(`PlaceBidHandler.TryComputeExtension`), so the test waits against the authoritative
`scheduledCloseAt` (one API read) and bids mid-window — a 30s-wide target hit from a
millisecond-resolution clock on the same machine. The auction runs 2 minutes (not the 1-minute
minimum) so setup and the early war never eat into the window.

## Item 2 — Finding 1 (run #1 red): the extended-bidding banner is structurally unreachable

The first live run failed asserting B's "Extended bidding" banner. The error-context snapshot
showed the *transient* feed entry "Close moved from …13:36:10 to …13:36:25" (the push arrived)
while the *re-queried* view still rendered the old close — and inspection of the lived
`src/CritterBids.Listings/AuctionStatusHandler.cs` closed the question: it handles six auction
events, and **`ExtendedBiddingTriggered` is not one of them**. `CatalogListingView.ScheduledCloseAt`
is written once at `BiddingOpened` and never advances. The banner (M8-S3b) derives from the
re-queried close moving later, so it can never fire from the lived backend — and the
`"Extended"` status branch in `LiveBidding.tsx` is equally unreachable (the catalog status
vocabulary never produces it). Narrative 001 Moment 6's "Listings upserts
`ScheduledCloseAt = NewCloseAt`" was forward-spec that never landed, and S3b bound the banner to
the narrative rather than the lived read model — the exact Rule-1 failure the
`frontend-slice-discipline` skill warns about, surviving until now because no prior smoke drove
a bid into the trigger window and then watched the *view*.

**Disposition (Open question 4):** backend gap → **escalated, not fixed** (M8's three sanctioned
exceptions are spent). Recorded as a carry-forward (STATUS §3, narrative 001 v0.5 row, milestone
exit-criteria annotation). The e2e asserts the extension's **effect** against the authoritative
read model instead: after the in-window bid, the listing is still `Open` five seconds past its
original close, and the gavel falls only at the extended close — the one deliberate API-level
assertion in an otherwise UI-asserted test, because the UI structurally cannot render this fact.

## Item 2 — Finding 2: the push-refetch can lose the race to the projection

The same snapshot demonstrated a second, distinct mechanism: each integration event reaches
Relay (push) and Listings (projection) on **separate queues**, so the push-triggered re-query
can land before the projection applies — and for the *last* event of a burst there is no later
push to reconcile. Harmless for bids today (the next push always arrives within the war), but it
is the eventual-consistency sharp edge under every "push = re-query" surface. Recorded in STATUS
§4 with a frontend-hardening candidate (a delayed re-invalidate in the cache bridge); no change
shipped in this slice.

## Item 5: Playwright-in-CI — deferred

The e2e needs Postgres + RabbitMQ + the API host + the bidder dev server live, plus a real
2-minute auction per run. Standing that up in Actions (Aspire-in-CI or a hand-rolled compose
equivalent, readiness gating, ~4 min added wall clock) is its own infrastructure piece with its
own flake surface — exactly what the prompt budgeted out. Deferral recorded as a comment at the
`frontend` job; what CI *does* get from the harness is the e2e member's strict type-check, so the
test source cannot rot silently. Re-evaluate when CI infrastructure work is on the table.

## Item 7: `client/shared/` — defer extraction to M9

The measured duplication between the apps' `signalr/` directories is **the pattern, not the
bytes**: same file names, materially different contents — different hubs and auth (anonymous vs
`StaffToken` + `skipNegotiation`), different message vocabularies (the bidder's five
client-assigned `kind`s over raw records vs the ops 22-value `eventType` surface), different
cache-bridge targets, different reconnect behavior (group re-join vs one-shot `["operations"]`
invalidation). The genuinely identical surface is the `RECEIVE_MESSAGE` constant and the
Provider's subscribe-fan-out plumbing — small against the cost of an abstraction parameterized
by auth, parser, and bridge for exactly two consumers. ADR 025 already names the trigger:
extract "when the duplication becomes real." The M9 seller console is the third consumer that
reveals the real shared subset; extracting now would guess at it. Recorded in `CLAUDE.md` and the
workspace root manifest; no extraction code in this diff.

## Live e2e run record

| Run | Result | Notes |
|---|---|---|
| #1 | red at the "Extended bidding" banner assert | Produced Findings 1+2; all beats up to and including the in-window $55 reclaim passed |
| #2 (post-reshape) | **green, 2.3 min** | Full war: sessions distinct, cross-context live bid, outbid, reserve met, extension effect, sold/winner in both contexts |
| #3 | **green, 2.3 min** | Confidence rerun against the same host; new unique title, prior runs' rows present and harmless |

Host: `dotnet run --project src/CritterBids.AppHost --launch-profile http`; torn down after
(API + dev servers confirmed unreachable; session containers stopped and removed).

## Test results

| Phase | Suite | Result |
|-------|-------|--------|
| e2e member scaffold | `tsc --noEmit` (e2e) + `playwright test --list` | clean; 1 test discovered |
| Live e2e | Playwright vs Aspire host | red (run #1, Finding 1) → **green ×2** (runs #2–#3) |
| Regression | `@critterbids/bidder` build + Vitest | exit 0; **25/25** (untouched) |
| Regression | `@critterbids/ops` build + Vitest | exit 0; **47/47** (untouched) |
| Regression | `dotnet build` + `dotnet test` (solution) | 0 errors / 0 warnings; **307/307** (untouched — no `.cs` in the diff) |

## Build state at session close

- `.cs` files changed: **0**. `CritterBids.Contracts` changes: **0** (the no-backend-change rule held).
- `client/e2e/` production dependencies: **0** (devDependencies only); `@playwright/test` in
  either app's `package.json`: **0**; both app builds green with the member present.
- Per-run unique seed title and pre-bid hub assert: greppable in `bid-war.spec.ts`
  (`uniqueTitle`, "Hub connection asserted before the first bid").
- CI `frontend` job steps: 2 → 5 (bidder build/test + ops build/test + e2e type-check);
  aggregator `needs`/logic untouched; path filters untouched.
- `*(planned)*` markers for the ops app in `CLAUDE.md`: **0**.

## Key learnings

1. **An e2e is an audit, not just a regression net.** Its first run falsified a UI feature that
   had survived four slices of green unit tests and three manual smokes — because no prior
   verification drove the specific beat (a bid inside the trigger window) and then watched the
   authoritative view rather than the transient feed.
2. **"Push = re-query" has a structural blind spot at burst end.** The re-query can win the race
   against the projection on a sibling queue, and the last event of a burst has no later push to
   self-heal. Surfaces that depend on burst-final events need a reconciliation affordance.
3. **Derived UI beats need a lived-backend reachability check, not just a unit test.** The
   extended-bidding banner's unit tests passed forever by feeding it synthetic view transitions
   the backend never produces. When a component derives state from *changes between re-queries*,
   verify the backend can actually produce that change.
4. **Anchor e2e timing to authoritative reads, not sleeps.** The one API read of
   `scheduledCloseAt` turned a "real wall-clock window" problem into a deterministic mid-window
   target; the only fixed waits in the test are computed from it.
5. **Workspace-member isolation is enforceable by construction.** A separate package with
   devDependencies-only and an `include: ["src"]` app type-check makes "e2e deps must not leak"
   a property of the layout, not a discipline.

## Findings against narrative

The bid-war spine (Moments 1–7) is implemented and now machine-validated as drafted, with one
drift surfaced: Moment 6's Listings-side close upsert
(`AuctionStatusHandler` consuming `ExtendedBiddingTriggered`) is still forward-spec — the lived
handler does not consume the event, so the bidder-visible "close moved" beat cannot render from
the read model. Lanes: `narrative-update` (resolved in this PR — narrative 001 v0.5 Document
History row records both the e2e validation and the audit note) + `code-update` candidate
(a future Listings slice adds the handler; carried in STATUS §3, not resolved here).

## Spec delta - landed?

**Landed as written, all three consequences.** (1) Narrative 001 gained the v0.5 Document
History row: the bid-war spine is validated end-to-end by the automated two-bidder Playwright
test, with the Moment-6 audit note. (2) The M8 milestone doc's §1 exit criteria are checked off
with honest annotations (281→307 attributed to the sanctioned exceptions; the negotiate-wording
and extended-bidding-banner annotations), status flipped to ✅ Complete (v0.6 row), and the M8
milestone retrospective (`M8-retrospective.md`) is the canonical close record naming the outward
deferrals (seller console → M9, `client/shared/` → M9, Playwright-in-CI). (3) The
`frontend-slice-discipline` smoke playbook step 3 carries the corrected Node-WS
credential-transport note per S6b Finding 2.

## Verification checklist

- [x] Playwright e2e exists at `client/e2e/` with the placement decision (§Item 1) and run instructions written down (README + root `npm run e2e` script)
- [x] The e2e passes locally against the Aspire-orchestrated host: two contexts, two anonymous sessions, live cross-context bid visibility, an outbid observed, the listing sold with the winner correct in both contexts — runs recorded above (two consecutive greens)
- [x] Hub connection asserted before the first bid; per-run unique seed title — both greppable in `bid-war.spec.ts`
- [x] `ci.yml` frontend job builds and tests both `@critterbids/bidder` and `@critterbids/ops`; aggregator contract unchanged
- [x] Playwright-in-CI decision recorded (comment at the frontend job + §Item 5)
- [x] `CLAUDE.md` ops row no longer *(planned)*; milestone doc status/§2 reflect shipped reality; §1 boxes ticked with the 307-baseline annotation; `STATUS.md` (v0.6) and `bounded-contexts.md` refreshed
- [x] `client/shared/` decision recorded with reasoning (§Item 7 + `CLAUDE.md`); extraction code in the diff: none
- [x] `frontend-slice-discipline/SKILL.md` carries the corrected Node-WS note
- [x] `dotnet build` + `dotnet test` green (307/307, untouched); `npm test` green in both apps (25 + 47); both SPA builds exit 0; the e2e member is outside both apps' production type-checks
- [x] This retrospective **and** the M8 milestone retrospective authored

## What remains / next session should verify

- **Out of M8, deferred outward (see the M8 milestone retro):** the seller console (M9, needs
  its milestone-scoping session first — no `docs/milestones/M9-*.md` exists), `client/shared/`
  extraction (M9 trigger), Playwright-in-CI (re-evaluate with CI infra work).
- **Backend carry-forward (escalated here, not fixed):** Listings `ExtendedBiddingTriggered`
  handler, so `CatalogListingView.ScheduledCloseAt` advances and the bidder extended-bidding
  banner / `"Extended"` status become reachable; when it lands, add the banner assert back to
  the e2e (the test comment marks the spot).
- **Frontend-hardening candidate:** cache-bridge reconciliation for the burst-final
  push-refetch race (Finding 2) — pairs naturally with the row above.
- **e2e extension candidates:** distinguish the Sold → Settled ("It's yours!") transition
  (currently accepted as either terminal text); an ops-dashboard context joining the same war.
