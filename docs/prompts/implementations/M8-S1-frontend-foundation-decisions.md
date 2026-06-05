# M8-S1: Frontend Foundation Decisions — Accept ADR-013 (Core Stack) + ADR-025 (SPA Monorepo Layout) + BiddingHub Connection Proof

**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — *milestone doc authored in the prerequisite M8 scoping session; see Preconditions*
**Slice:** S1 of M8 (foundation slice; the first slice to introduce a frontend code surface, scaffolding one minimal proof app only)
**Narrative:** `docs/narratives/001-bidder-wins-flash-auction.md` (the bidder-vantage live-bidding journey the eventual public SPA renders; this slice proves only the `BiddingHub` *connection*, not any Moment of the journey)
**Agent:** @PSA (with @UXE consulted on the core-stack acceptance and @DOE consulted on the SPA monorepo layout / build-integration ADR)
**Estimated scope:** one PR; 2 ADRs (one acceptance, one new) + 1 minimal Vite/React/TS proof app + a `CLAUDE.md` pointer + the M8-S1 retro. The second SPA shell, all operator/ops surfaces, and the `OperationsHub` (staff-gated) are **not** in this slice.

---

## Preconditions

This prompt assumes the **M8 milestone doc exists** at `docs/milestones/M8-frontend-spas.md`. Per AUTHORING.md rule 3 the milestone doc is authoritative for scope, and per the milestones-index "When to Add a New Milestone Document" gate, M8's definition-of-done and deliverables are scoped in a **separate milestone-scoping session that lands before this slice runs**. If that doc does not yet exist when this prompt is picked up, **stop and escalate** — do not infer M8 scope from this prompt or smuggle milestone-level scope decisions into the slice (rule 4). The M7→M8 handoff §3 and the M7 retro's "What M8 Should Know" are orientation only; they do not substitute for the milestone doc as the scope authority.

## Goal

Land the two foundation decisions the M8 frontend slices depend on, and prove the single riskiest integration assumption with the cheapest falsifiable artifact, so no later M8 slice stops to escalate a stack or layout question mid-flight. This session does three things and records them durably:

1. **Accept (or revise on acceptance) ADR-013 — Frontend Core Stack.** ADR-013 has been `Proposed` since 2026-04-19. M8-S1 is the slice that flips it to `Accepted`, confirming the focused-library composition (TypeScript strict, Zod, TanStack Query, Tailwind v4 + shadcn/ui, `react-hook-form`, `@microsoft/signalr`, Vitest + Playwright, PWA-from-day-one) against the 2026 ecosystem as it actually stands at acceptance time. The ADR's own **Deferred Questions** (routing library, UI state beyond server state, auth client pattern, the SignalR *integration* pattern → ADR-014, PWA offline scope) stay deferred — this slice accepts the stack, it does not resolve the parked questions.
2. **Author ADR-025 — SPA Monorepo Layout + Build Integration.** Decide where the SPA source lives (e.g. `client/bidder/`, `client/ops/`), how each Vite build's static output relates to the .NET API host, and the local dev-server story (proxy vs CORS) for talking to the API and the hubs. This is a hard-to-reverse, cross-cutting decision (it touches repo layout, the build pipeline, and how every later frontend slice is structured), which is why it is an ADR rather than a milestone-doc note. **025** is the next unreserved ADR number.
3. **Scaffold one minimal connection-proof app and connect it to the anonymous `BiddingHub`.** Stand up a single Vite + React + TypeScript (strict) app at the layout ADR-025 decides, wire `@microsoft/signalr`, and prove a live `HubConnection` to `/hub/bidding` (the participant-facing hub, `[AllowAnonymous]` — confirmed at `src/CritterBids.Api/Program.cs:431`). The proof is "the WebSocket negotiate + connect path works end to end against a running API host," not a rendered bidding journey. The second SPA shell and any `OperationsHub` (staff-gated) connection are out of scope.

This slice is the M8 analogue of M5-S1 / M6-S1 / M7-S1 (foundation-decisions slices), with one difference: it is the first to introduce frontend code. That code is deliberately confined to a single minimal proof app — the decision density is in the two ADRs, not in the scaffold.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M8-frontend-spas.md` | **Authoritative for scope** (Preconditions: must already exist). M8 definition-of-done, the two-SPA split, exit criteria, and the S1 row of the slice plan. Do not re-decide anything the milestone already pinned. |
| `docs/prompts/handoffs/M7-to-M8-handoff.md` (§3 + §5) and `docs/retrospectives/M7-retrospective.md` ("What M8 Should Know") | Orientation only — the carry-forward concerns (render-time `Title` join, Relay-push = re-query signal, staff-token transport) the M8 design must honor. Not a scope substitute for the milestone doc. |
| `CLAUDE.md` | Routing + global conventions; the file this slice adds a short frontend-layout pointer to (reflecting ADR-025), the way ADR-024 recorded a `CLAUDE.md` follow-on |
| `docs/decisions/013-frontend-core-stack.md` + `012-frontend-spa-vite.md` + `004-react-frontend.md` | The frontend ADR lineage. 013 is the ADR this slice accepts; 012 (Vite SPA posture) and 004 (React) are its accepted parents and constrain ADR-025's layout options |
| `docs/decisions/README.md` (confirm next unreserved = **025**) + `docs/decisions/023-relay-reactive-broadcast-architecture.md` | The next-ADR-number check + index rows this slice edits; ADR-023 fixes the `BiddingHub`/`OperationsHub` push contract (`ReceiveMessage` payload shape) the proof's client handler reads |
| `docs/narratives/001-bidder-wins-flash-auction.md` | The bidder-vantage journey the public SPA eventually renders; jointly authoritative per rule 3 for the journey the proof connects *toward* (this slice implements no Moment) |
| `src/CritterBids.Api/Program.cs` (hub mapping, ~lines 399–439) + `docs/skills/wolverine-signalr/SKILL.md` | The authoritative `/hub/bidding` route + the client-side method name (`ReceiveMessage`) the proof's `HubConnection` registers; the skill owns the SignalR client conventions the proof follows |

## In scope

1. **ADR-013 acceptance.** Flip `docs/decisions/013-frontend-core-stack.md` status `Proposed` → `Accepted` (with a dated Document-History / status note), revising any sub-choice only if the 2026 ecosystem at acceptance time warrants it (record the revision and its reason; do not silently rewrite). The five Deferred Questions remain deferred. Update the ADR-013 row in `docs/decisions/README.md` (status badge `🟡 Proposed` → `✅ Accepted`).
2. **ADR-025 — SPA Monorepo Layout + Build Integration.** Author `docs/decisions/025-<slug>.md`, status `Accepted`, settling with rationale and alternatives weighed:
   - **Source layout** — the directory home of the SPA(s) (e.g. `client/bidder/` + planned `client/ops/`), and whether the two SPAs are independent Vite projects, a shared workspace, or a monorepo with shared config.
   - **Build-output integration with the .NET host** — how each SPA's static build output is served/shipped relative to `CritterBids.Api` (served by the host as static files, a separate static deploy, or build-time copy), consistent with ADR-012's static-SPA posture.
   - **Local dev-server story** — Vite dev server + API/hub access during development: dev-server proxy vs CORS on the API, and how the SignalR negotiate/WebSocket upgrade is reached in dev. Name the choice; do not implement CORS changes to the API host in this slice beyond what the proof strictly requires (flag any API-host edit as an Open Question if it exceeds a trivial dev-only allowance).
   - Update `docs/decisions/README.md` index row + advance the next-unreserved-number pointer to **026**.
3. **Minimal BiddingHub connection-proof app.** Scaffold a single Vite + React + TypeScript app at the ADR-025-decided path, with: TypeScript **strict** mode on; Tailwind CSS **v4** base configuration wired (per ADR-013); `@microsoft/signalr` pinned in `package.json`; and a minimal UI that opens a `HubConnection` to `/hub/bidding`, registers the `ReceiveMessage` client method (per ADR-023), uses `.withAutomaticReconnect()`, and renders the live connection state. The app must `npm install` and `npm run build` cleanly. The proof is a *connection*, not a rendered journey — no catalog, no bid placement, no second SPA, no shadcn/ui component catalog beyond what the connection-state view trivially needs.
4. **`CLAUDE.md` pointer.** Add a short frontend section (or BC-quick-reference-style note) recording the `client/` layout ADR-025 establishes and the two-SPA split — a pointer, not a duplication of either ADR.
5. `docs/retrospectives/M8-S1-frontend-foundation-decisions-retrospective.md` — written last; carries the `**Prompt:**` header line and the `## Spec delta — landed?` paragraph confirming both ADRs reached `Accepted`, the proof app exists and builds, and `/hub/bidding` connectivity was demonstrated (note the manual verification step taken).

## Explicitly out of scope

- **The second SPA** (the staff ops dashboard / `client/ops/`) — ADR-025 *plans* its layout home, but no ops-app code, shell, or scaffold is created this slice.
- **Any `OperationsHub` (`/hub/operations`) connection** — staff-gated, requires the `access_token` query-string credential dance (ADR-024). The proof targets the anonymous `BiddingHub` only.
- **Resolving ADR-013's Deferred Questions** — routing library, UI state management, auth client pattern, the SignalR *integration* pattern (ADR-014), and PWA offline scope all stay parked. This slice accepts the stack; it does not pick a router or write ADR-014.
- **The render-time `Title` join, catalog rendering, bid placement, optimistic-update UX, or any TanStack Query data wiring** — M8 later slices. The proof renders connection state, not domain data.
- **shadcn/ui component-catalog scaffolding, `react-hook-form`/Zod form wiring, Vitest/Playwright test suites** — ADR-013 pins these as the stack; standing them up is later-slice work. (A trivial Vite-default test, if the scaffold includes one, is acceptable; building an e2e/unit suite is not.)
- **Backend / API-host behavior changes** — no new endpoints, no hub changes, no auth changes. The only tolerable API-host touch is a trivial dev-only CORS allowance if ADR-025's dev-server choice strictly requires it; anything beyond that is an Open Question, not a silent edit.
- **CI matrix extension** (the Settlement/Obligations/Relay/Operations tests that run locally only, and any new frontend-build CI job) — a natural standalone PR or later M8 housekeeping slice, not this one. Keep S1 a single reviewable PR.
- **PWA service-worker/manifest wiring** beyond what the Open Questions resolve (see below).

## Conventions to pin or follow

- **Frontend stack:** ADR-013 (once accepted this slice) owns the library composition; the proof app follows it (TypeScript strict, Tailwind v4, `@microsoft/signalr`) and does not introduce libraries outside it.
- **SPA layout & build integration:** ADR-025 is the first encoding of the `client/` layout and the dev-server/build-output story; later M8 slices point at it rather than re-deciding.
- **SignalR client conventions:** `docs/skills/wolverine-signalr/SKILL.md` owns the client-side `HubConnection` patterns; ADR-023 owns the `ReceiveMessage` payload contract. The proof points at both rather than restating them.
- **Static-SPA posture:** ADR-012 (the SPA ships as static Vite output; backend owns all contracts) constrains ADR-025 — no meta-framework, no server-rendered route ownership.
- Markdown/doc prose follows the project's internal-doc conventions (em-dash hygiene is external-prose-only and does not apply to ADRs/prompts/retros).

## Spec delta

Per ADR 020, this slice's spec consequence is the **acceptance of ADR-013** (the frontend core stack moves from a `Proposed` proposal to a governing, accepted decision) and the **authoring of ADR-025** (a new governing decision: SPA monorepo layout + build integration + dev-server story). Together they fix the frontend's architectural surface for all later M8 slices. A secondary, code-level consequence is the first frontend code surface in the repo — a single minimal proof app whose only verified behavior is a live `BiddingHub` connection. No Moment of narrative 001 is implemented; no domain data is rendered. The retro's `## Spec delta — landed?` paragraph confirms: ADR-013 shows `Accepted` and is re-badged in the index; ADR-025 exists, is `Accepted`, and the next-unreserved pointer advanced to 026; the proof app exists, builds, and demonstrated `/hub/bidding` connectivity.

## Acceptance criteria

- [ ] `docs/decisions/013-frontend-core-stack.md` status is `Accepted` (dated), with any on-acceptance revision and its reason recorded; the five Deferred Questions remain listed as deferred
- [ ] `docs/decisions/README.md` ADR-013 row shows `✅ Accepted`
- [ ] `docs/decisions/025-<slug>.md` exists, status `Accepted`, settling source layout, build-output integration with the .NET host, and the local dev-server (proxy vs CORS) story — each with alternatives weighed
- [ ] `docs/decisions/README.md` gains the ADR-025 row and the next-unreserved-number pointer advances to **026**
- [ ] A single Vite + React + TypeScript app exists at the ADR-025-decided path; TypeScript **strict** is on; Tailwind v4 base config is present; `@microsoft/signalr` is pinned in `package.json`
- [ ] `npm install` and `npm run build` (or the ADR-025-named equivalents) succeed in the proof app from a clean checkout
- [ ] The proof app contains a `HubConnection` to `/hub/bidding` that registers the `ReceiveMessage` client method and `.withAutomaticReconnect()`, and renders the live connection state; the retro records the manual verification (connected against a running API host)
- [ ] No second SPA / `client/ops/` code; no `OperationsHub` connection; no catalog, bid, or TanStack Query data wiring
- [ ] No API-host behavior change beyond (at most) a trivial dev-only CORS allowance the ADR-025 dev-server choice strictly requires; any larger API touch was escalated, not made
- [ ] Existing .NET build + test baseline (0 errors / 0 warnings; 281 tests) is unchanged — this slice adds no backend code; if the proof app introduces a build/test step, it does not break `dotnet build CritterBids.slnx` / `dotnet test CritterBids.slnx`
- [ ] `CLAUDE.md` gains a short frontend-layout pointer reflecting ADR-025 (a pointer, not a duplication)
- [ ] `docs/retrospectives/M8-S1-frontend-foundation-decisions-retrospective.md` written with the `**Prompt:**` header and `## Spec delta — landed?` paragraph
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Open questions

- **PWA from day one vs single-proof minimalism.** ADR-013 commits to `vite-plugin-pwa` "from the first commit" as cheaper-than-retrofit. This slice scaffolds only a minimal proof app. Confirm against the accepted ADR-013 whether S1 wires the manifest + service-worker registration now (honoring "day one") or defers PWA wiring to the first real SPA-shell slice — and record the call. Lean toward the accepted ADR's stance; if deferring, note why in the retro and flag it as a carry-forward.
- **Dev-server reach to the hub: proxy vs API-host CORS.** ADR-025 decides this, but the *implementation* may require a trivial dev-only allowance on the API host. Confirm the proof can reach `/hub/bidding`'s WebSocket upgrade via the chosen mechanism without a non-trivial API-host change; if it can't, escalate rather than widening the API's CORS/auth surface.
- **Proof-app path & project shape.** Whether the single proof app is scaffolded directly at its final `client/bidder/` home or at a clearly-temporary proof location to be promoted later — decide consistently with ADR-025's layout so the proof does not strand code at a path the layout ADR disowns.
- **On-acceptance revisions to ADR-013.** If any 2026-ecosystem shift (a library deprecation, a successor that inverted the LLM-fluency premise per ADR-013's own Revisit Triggers) surfaces during acceptance, flag it — accept with a recorded revision, or escalate if the revision is large enough to be its own decision rather than an acceptance note.
