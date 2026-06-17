# M9-S8: End-to-End + Housekeeping (M9 close) - Retrospective

**Date:** 2026-06-17
**Milestone:** M9 - Seller Console
**Slice:** S8 - end-to-end + housekeeping (the milestone close)
**Agent:** Claude Code
**Prompt:** `docs/prompts/implementations/M9-S8-end-to-end-housekeeping.md`

## Baseline

- Clean `main` at `78443c1` (M9-S7 cross-queue race fix merged, PR #112).
- .NET build: 0 errors; warnings are the held baseline (`NU1903` MessagePack advisory + two
  `CS0108` saga-`Version` hides). Backend tests: **328** green.
- Frontend: five-member `client/` workspace (`shared`, `bidder`, `ops`, `seller`, `e2e`). Vitest:
  seller 117, ops 47, bidder 25 (189 total). Playwright e2e: 1 (M8 bid-war).
- **CI `frontend` matrix already covered `seller` (build-test) + `shared`/`e2e` (type-check)** —
  coverage landed at M9-S1 (#104), restructured into the current matrix at PR #111. The handoff's
  "extend CI to seller+shared" was already-done work.
- `CLAUDE.md` §Frontend already current (three SPAs + `shared` + `e2e`); `bounded-contexts.md`
  (still "two SPAs") and `STATUS.md` (v0.6, M8 close) were the stale doc surfaces.

## Items completed

| Item | Description |
|------|-------------|
| S8-1 | Seller-perspective Playwright e2e — `client/e2e/tests/seller-obligation.spec.ts` |
| S8-2 | Seed-then-inject identity bridge + Buy-It-Now deterministic sale (OQ-1 resolved) |
| S8-3 | `Obligations__DemoMode=true` in the AppHost (sanctioned dev-host config) |
| S8-4 | `client/e2e/README.md` — seller-obligation run story + the two backend facts it relies on |
| S8-5 | CI `frontend` coverage verified (already complete; no matrix edit) |
| S8-6 | Doc refresh — `bounded-contexts.md`, `STATUS.md` (v0.7), milestone doc (status + §7 renumber + §1 ticks); `CLAUDE.md` verified current |
| S8-7 | Pre-M10 skills audit (`frontend-slice-discipline` edited; three carry-forwards recorded) |
| S8-8 | M9-S8 slice retro (this file) + the M9 milestone retrospective |

## S8-1 / S8-2: The seller-obligation e2e and its identity bridge

**The test.** One spec drives the obligation-fulfillment spine (narrative 006) from the *seller's*
vantage against the live Aspire stack: a seeded listing sells, the seller console surfaces the
post-sale obligation, the seller provides tracking through the console's `react-hook-form` dialog,
and the obligation auto-confirms to "Completed". Assertions go through what the seller *sees* (the
re-queried `ObligationStatusView`), with read-model reads only as readiness gates — the same
assert-through-the-UI discipline the bid-war test established.

**Why seed-then-inject (OQ-1 resolved).** The seller console mints its own anonymous session, and
the operator `create-session/attach/start` step that opens a listing is staff/bus-only — so the
console cannot both publish *and* open a listing on its own (milestone §3 keeps operator session
management out of the seller console). Reading the lived code resolved this cleanly:

- `POST /api/dev/seed-flash` already drives a server-minted **registered seller** through
  publish→attach→start to `Open`, returning that `sellerId`.
- The seller console's `SessionProvider` keys off `sessionStorage["critterbids.seller.participantId"]`
  + `["critterbids.seller.isRegisteredSeller"]` and boots straight to an *established*,
  *registered-seller* session when both are pre-set (no session POST, no register call).
- So the test injects the seeded `sellerId` into those two keys via `context.addInitScript` before
  any app script runs; the console adopts the seeded identity and renders that seller's obligations
  via `GET /api/obligations/status?sellerId=`. **No backend change.**

**Why Buy-It-Now for the sale.** The sale is forced with the dev `POST /api/dev/buy-now` trigger (a
single deterministic purchase at the seed's $100 BIN) rather than riding a wall-clock auction close
— the auction mechanics are already the bid-war test's job; this test's subject is the obligation
lifecycle. This keeps the test under a minute instead of riding a 2-minute Flash auction.

**Why a read-model readiness gate, not a push wait.** The obligation's first appearance and its
`Fulfilled` flip are gated on a poll of `GET /api/obligations/status?sellerId=` (then a reload),
not on the SignalR push. The M8-S7 burst-final race (a push-triggered re-query can lose to a
sibling-queue projection, and the *last* event of a burst has no later push to self-heal) makes the
push unreliable precisely for the terminal event — so the test reloads after the read model
confirms the state, asserting the rendered result through the UI.

## S8-3: `Obligations__DemoMode` — sanctioned dev-host config

**The finding (lived-surface, Rule 1).** The seller-obligation flow's back half (provide tracking →
`Fulfilled`) depends on the post-sale auto-confirm timer firing. Reading the lived host config:
`ObligationsOptions.DemoMode` defaults to **false** (production durations — ship-by 5 days,
auto-confirm 3 days *after* tracking), and **nothing** in the dev/Aspire run enabled it
(`Program.cs` binds the section and never flips it under `IsDevelopment()`; there is no
`appsettings.json`; `DemoMode=true` existed only in test fixtures). The bid-war e2e never touched
obligations, so no prior test surfaced this. The conference-demo posture the `ObligationsOptions`
docstring describes ("the full lifecycle must run live within a demo session (seconds)") was, in
practice, un-reachable on the live stack.

**Resolution (user-sanctioned).** Add `.WithEnvironment("Obligations__DemoMode", "true")` to the
API project in the AppHost — scoped to the orchestrated demo run, mirroring the existing
`ASPNETCORE_ENVIRONMENT` injection. Demo durations are reminder +5s, ship-by +10s, auto-confirm
+10s, so the full lifecycle completes in ~15–20s. Production binds `DemoMode=false` by default (no
appsettings override). This was escalated as a scope-expanding decision (it touches host config
beyond the "no backend change" envelope) and confirmed before proceeding; it also closes a latent
demo gap — the obligation lifecycle was previously un-demoable live.

**A consequence the test asserts around.** With demo timers, the obligation can **escalate**
(Overdue) before the test provides tracking. The "Provide Tracking" affordance survives escalation
(narrative 007's recovery door — `ACTIONABLE_STATUSES` includes `Escalated`), so the test gates on
the affordance, not on a not-yet-escalated status.

## S8-1: the live run, and the one fix

Two consecutive green runs against the live Aspire stack (17.6s, then 13.3s) after a single fix.
The first run reached the terminal correctly — the `"Completed."` assertion passed, proving the
full lifecycle ran live including the demo-mode auto-confirm — but the *next* line, a redundant
status-badge assertion, hit a Playwright strict-mode violation:

```
Error: strict mode violation: getByText('Fulfilled') resolved to 2 elements:
    1) <span data-slot="badge" ...>Fulfilled</span>
    2) <p class="text-muted-foreground text-xs">Fulfilled Jun 17, 2026, 8:34 AM</p>
```

The Fulfilled card renders both a "Fulfilled" status badge and a "Fulfilled {timestamp}" line;
`getByText("Fulfilled", { exact: true })` pins the badge. The *product* behaved correctly through
every beat — the only defect was test-assertion specificity (the live tier finding a test bug, not
a product bug).

## S8-5 / S8-6: CI verify + doc refresh

- **CI:** verified the `frontend` matrix covers `seller` (build-test), `shared` (typecheck), and
  `e2e` (typecheck); the `CI` aggregator contract is unchanged. **No matrix edit** — coverage has
  existed since M9-S1, restructured at PR #111. The new e2e spec type-checks clean and stays
  execution-excluded in CI (M8-D2 holds).
- **`bounded-contexts.md`:** the frontend framing now describes three SPAs + `shared` + `e2e`
  (was two SPAs).
- **`STATUS.md`:** regenerated to v0.7 (M9 ✅ Complete; final M9 slice ledger; 328/189/2 numbers;
  deferred ledger re-derived — closed the `client/shared/` and `ExtendedBiddingTriggered` items,
  added the OQ-3 update-update race / Operations audit / `FakeHubConnection` extraction / seller
  settlement-summary UI; recorded the DemoMode + seed-then-inject decisions).
- **Milestone doc:** status → ✅ Complete; §7 slice table corrected (S7 = race fix, **S8** = close);
  §1 exit-criteria boxes ticked with honest annotations (the 307→328 test growth; CI coverage
  landed earlier than this slice; `shared` is the fifth member, not the "fourth" ADR 025 counted).
- **`CLAUDE.md` §Frontend:** verified current (three SPAs + `shared` + `e2e`, member counts correct
  against `client/package.json`); **no edit needed**.

## S8-7: Pre-M10 skills audit

Per the `m9-skills-review` carry-forward. Audited `.claude/skills/frontend-slice-discipline`,
`.claude/skills/signalr`, and the relevant global skills (react-hook-form, e2e-testing/playwright).

| Finding | Disposition |
|---|---|
| `frontend-slice-discipline` "When to apply" named only `bidder`/`ops`; M9 added `seller` + the `e2e` harness | **Applied in-PR** — extended to all SPAs + the e2e harness |
| Rule 1 ("read the lived backend") had no case for *host runtime config* being part of the lived surface | **Applied in-PR** — added the M9-S8 `DemoMode` lesson (verify the host's timer config before a live smoke that rides a timer) |
| `signalr` skill is scoped to "bidder and ops"; M9 added the seller as a third consumer, the shared `createSignalRProvider<TMessage>()` factory (M9-S1) that reshaped the provider the skill describes, and the `BidderGroupNotification` obligation-push channel (M9-S6) | **Carry-forward (M10)** — a focused signalr-skill refresh; folding the factory + seller is a substantive rewrite, out of scope for a close slice (prompt OQ-4) |
| `FakeHubConnection` has three copies across the SPAs | **Carry-forward (M10)** — extract to `@critterbids/shared` test utilities |
| The actor-form-identity-from-context pattern (M9-S6) recurs across the seller's three `react-hook-form` forms | **Carry-forward** — candidate for a CritterBids-local seller-form note if M10 grows seller forms; not a global-skill gap |

No net-new skill file authored (an M10-scoping decision per OQ-4).

## Test results

| Phase | Suite | Result |
|-------|-------|--------|
| e2e — first live run | `seller-obligation.spec.ts` | flow green to "Completed"; failed on redundant `Fulfilled` badge selector (strict-mode) |
| e2e — after `{ exact: true }` fix | `seller-obligation.spec.ts` | **pass (17.6s)** |
| e2e — second consecutive run | `seller-obligation.spec.ts` | **pass (13.3s)** |
| e2e member type-check | `@critterbids/e2e` | **clean** (`tsc --noEmit`) |
| Backend regression | full `dotnet test CritterBids.slnx` | **328/328** (unchanged — no domain code touched) |

## Build state at session close

- `.cs` files changed: **0 domain** — only `src/CritterBids.AppHost/Program.cs` (one
  `.WithEnvironment` line, dev-host config).
- `CritterBids.Contracts` changes: **0**.
- New e2e specs: **1** (`seller-obligation.spec.ts`); Playwright e2e total 1 → **2**.
- Errors: **0**; warnings: held baseline (no net-new).
- Doc/skill files changed: `bounded-contexts.md`, `STATUS.md`, `M9-seller-console.md`,
  `frontend-slice-discipline/SKILL.md`, `client/e2e/README.md`, the M9-S8 prompt, this retro, the
  M9 milestone retro.

## Key learnings

1. **Host runtime config is part of the lived surface.** The most consequential finding this slice
   was not in any `src/` type — it was that `Obligations:DemoMode` was off in the dev host, making
   the demo posture's "seconds" lifecycle actually "days". A live smoke that rides a timer must
   first confirm the host is configured for that timing. (Folded into `frontend-slice-discipline`.)
2. **Seed-then-inject is the clean bridge for a console whose identity is its own session.** When a
   journey needs an actor whose listing was opened by a staff-only step, reuse the dev seed's
   server-minted identity and inject it into the console's session storage — no backend change, no
   leaking operator controls into the wrong SPA.
3. **Gate terminal-event assertions on the read model, then reload — don't trust the burst-final
   push.** The M8-S7 burst-final race is exactly the case where the last event's push can be lost;
   an e2e that asserts a terminal state should read the model for readiness and reload, keeping the
   assertion on the rendered UI.
4. **The live tier finds test bugs too.** Every prior milestone's live smoke caught a *product*
   defect; here the product was correct end-to-end and the live run caught an over-broad test
   selector. The tier still earned its place — a green build + type-check shipped an ambiguous
   `getByText` that only a real DOM exposed.
5. **"Verify, don't assume" applies to the handoff itself.** Two of the five handoff items (CI
   extension, `CLAUDE.md` refresh) were already done; reading `ci.yml` and `client/package.json`
   first turned them into verifications, not rework.

## Findings against narrative

**Narrative 006** (`docs/narratives/006-seller-fulfills-post-sale-obligation.md`): the
obligation-fulfillment spine (Moments 1–4) is now validated end-to-end by an automated
seller-perspective Playwright test, complementing the M9-S6 component tests + HTTP smoke. The test
implements the narrative as drafted — no drift. **Narrative 007**'s escalation-recovery property
(the "Provide Tracking" affordance survives `Escalated`) is exercised incidentally: under demo
timers the obligation may escalate before tracking is provided, and the test still completes
through the recovery door. Routed `narrative-update`: a Document History row noting the e2e
coverage (added in this PR).

## Spec delta - landed?

The prompt declared: narrative 006 gains a Document History row for the automated seller e2e; the
milestone doc §1 exit criteria are ticked and §7 renumbered (S7 race fix / S8 close); the M9
milestone retrospective becomes the canonical record; no ADR or canonical-spec amendment.
**Landed as written.** Narrative 006's Document History gained the e2e-coverage row; the milestone
doc closed with the renumber and honest annotations; the M9 milestone retro is authored. The one
divergence from the prompt's *expectation* (not its declared delta): the prompt anticipated "no
backend change", and this slice took one **sanctioned dev-host config** change
(`Obligations__DemoMode=true`) — escalated and user-confirmed mid-session, recorded here and in
STATUS §3 (Decided, not deferred). No domain event, contract, or ADR changed.

## Verification checklist

- [x] A seller-perspective Playwright spec exists in `client/e2e/` and passes locally against the
  live Aspire stack (seeded listing sells → console surfaces the obligation → tracking provided
  through the dialog → `Fulfilled`/"Completed"); two consecutive green runs recorded
- [x] The spec asserts the seller console's `BiddingHub` connection before any obligation action
  and seeds with a per-run unique title (both greppable in the source)
- [x] `client/e2e/README.md` documents the seller e2e's prerequisites, invocation, duration, and
  the two backend facts (DemoMode, seed-then-inject) it relies on
- [x] CI `frontend` matrix confirmed to already cover `seller` (build-test) + `shared` (typecheck);
  retro records coverage predates this slice; `e2e` type-checks clean; aggregator unchanged
- [x] `docs/vision/bounded-contexts.md` describes three SPAs + `shared` + `e2e`
- [x] `docs/STATUS.md` regenerated to the M9-close posture (v0.7)
- [x] `docs/milestones/M9-seller-console.md` status ✅ Complete; §7 reflects S7=race fix / S8=close;
  §1 boxes ticked with annotations
- [x] `CLAUDE.md` §Frontend verified current (no edit needed; "verified" recorded)
- [x] Pre-M10 skills audit recorded; two edits applied in-PR, three carry-forwards to M10
- [x] `dotnet test CritterBids.slnx` green (328, untouched); e2e member type-checks clean; e2e not
  in any app's production type-check
- [x] M9-S8 slice retro authored **and** the M9 milestone retrospective authored
- [x] Branched off `main`; one PR; no commit to `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M10 scoping session** — no `docs/milestones/M10-*.md` exists; the precondition gate applies.
  Inputs: the M9 milestone retro's "what M9 defers outward", the STATUS §3 deferred ledger, and
  this slice's skills-audit carry-forwards.
- **signalr-skill refresh** (carry-forward) — fold in the seller consumer, the shared
  `createSignalRProvider` factory, and the `BidderGroupNotification` obligation channel.
- **`FakeHubConnection` shared extraction** (carry-forward) — three copies → `@critterbids/shared`.
- **Seller settlement-summary UI** — the `GET /api/settlement/summaries?sellerId=` endpoint shipped
  at M9-S3b; no seller-console surface renders it yet.
- **Cache-bridge burst-final hardening** — still deferred (the seller e2e sidesteps it with a
  readiness-gate + reload); bake the delayed re-invalidate into the shared cache bridge if/when the
  cache-bridge surface is extracted.
