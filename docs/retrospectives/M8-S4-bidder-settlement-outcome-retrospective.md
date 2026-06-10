# M8-S4: Bidder Settlement Outcome — Retrospective

**Date:** 2026-06-10
**Milestone:** M8 — React Frontend SPAs
**Slice:** S4 — Bidder Settlement Outcome (the final bidder-visible beat in the auction journey)
**Agent:** Claude Code
**Prompt:** `docs/prompts/implementations/M8-S4-bidder-settlement-outcome.md`

## Baseline

- Frontend build clean at session start: `client/bidder` → `npm run build` exit 0; `npm test` → **19 tests green** across 6 files.
- M8-S3c merged: ADR 027 sticky queue bindings in place; full seed → bid → outbid → close → settle journey runs end-to-end; `CatalogListingView.Status` transitions to `"Settled"` with `hammerPrice` and `settledAt` stamped.
- `SignalRProvider.tsx` already enrols the held participant's `bidder:{participantId}` group on connection + reconnect (lines 144–154). The `SettlementCompletedNotification` push targets this group.
- `parseHubMessage` recognizes four wire shapes; `SettlementCompletedNotification` payloads fall through to `null` (silently dropped by the forward-compatibility contract).
- `TerminalOutcome` treats "Sold" and "Settled" identically — "You won!" with no distinction between the interim and confirmed states.

## Items completed

| Item | Description |
|------|-------------|
| S4.1 | `settlementCompleted` added to the hub-message parse surface: Zod schema + `HubMessage` union variant + `parseHubMessage` recognition |
| S4.2 | Cache bridge — no code change needed; the existing generic `listingIdOf` + `invalidateQueries` handles the new kind automatically. Test added to prove it. |
| S4.3 | `TerminalOutcome` split: winner + Settled → "Charged $XX.XX / It's yours!"; winner + Sold → interim "Settlement confirmation arrives next."; non-winner unchanged |
| S4.4 | `LiveActivity.tsx` `timeOf()` updated to handle `settlementCompleted`'s `completedAt` field (TypeScript caught this at build time) |
| S4.5 | Vitest + RTL: **+6 tests (19 → 25)** — `parseHubMessage` recognition, cache bridge invalidation on `settlementCompleted`, `TerminalOutcome` rendering for winner-settled / winner-sold / non-winner-sold / non-winner-settled / passed |

## S4.1: Hub-message parse surface

The `SettlementCompletedNotification` wire shape (`{ settlementId, listingId, winnerId, hammerPrice, completedAt }`) is structurally disjoint from all existing schemas:

- `bidPlaced` requires `bidId` — absent from settlement
- `listingSold` requires `bidCount` + `soldAt` — absent from settlement
- `settlementCompleted` requires `settlementId` + `completedAt` — absent from the others
- The two `eventType`-tagged shapes require `eventType` — absent from settlement

No false positive is possible regardless of ordering. `settlementCompleted` is tried after `listingSold` (both carry `winnerId` + `hammerPrice`, but the Zod schemas require different companion fields).

## S4.2: Cache bridge — zero-code-change

The ADR 026 cache bridge's generic implementation (`listingIdOf(message)` → `invalidateQueries({ queryKey: ["listing", listingId] })`) handles all five message kinds without branching on `kind`. Adding a new kind with a `listingId` field automatically gets the right cache invalidation. This validates the design decision to keep the bridge shape-agnostic.

## S4.3: TerminalOutcome banner progression

The three-state banner the narratives describe:

| CatalogListingView Status | Winner? | Banner |
|---|---|---|
| `"Sold"` | Yes | "You won! — $55.00" + "Settlement confirmation arrives next." |
| `"Settled"` | Yes | "Charged $55.00" + "It's yours!" |
| `"Sold"` / `"Settled"` | No | "Sold — $55.00" |
| `"Passed"` / `"Withdrawn"` | — | "Bidding has closed. This listing did not sell." |

Banner text follows OQ2's recommended resolution: "Charged $55.00" without the "to your credit" phrasing (which would imply the credit-balance context the frontend cannot render without `remainingCredit`).

## S4.4: LiveActivity type fix

`LiveActivity.tsx`'s `timeOf()` assumed all non-`listingSold` messages carry `occurredAt`. The new `settlementCompleted` kind uses `completedAt`. TypeScript's exhaustive union checking caught this at build time. The fix: explicit `switch` on `kind` instead of the ternary. `describe()` returns `null` for `settlementCompleted` (settlement is not a transient-feed event), so `timeOf` is structurally unreachable for this kind — but TypeScript correctly can't prove the call-site ordering.

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Baseline | 19 | Pass |
| After S4.1–S4.5 | 25 | Pass |

New test files:
- `src/signalr/cacheBridge.test.ts` — 2 tests (settlement + bidPlaced invalidation)
- `src/bidding/LiveBidding.test.tsx` — 5 tests (winner-settled, winner-sold, non-winner-sold, non-winner-settled, passed)
- `src/signalr/messages.test.ts` — +1 test (settlement recognition)

## Build state at session close

- `tsc --noEmit` → 0 errors, 0 warnings
- `vite build` → exit 0 (two `INVALID_ANNOTATION` warnings from `@microsoft/signalr` — third-party, pre-existing)
- `npm test` → 25 tests green across 8 files
- No `.cs`, `.csproj`, `.slnx`, or `Program.cs` touch
- No `client/ops/` or `OperationsHub` change
- No new dependencies added

## Key learnings

1. **ADR 026's generic cache bridge scales to new message kinds with zero code.** The `listingIdOf` + blanket `invalidateQueries` pattern means adding a new push type is purely a parse-surface and rendering concern — the reactive data pipeline requires no modification.

2. **TypeScript's exhaustive union checking is load-bearing for heterogeneous push surfaces.** Adding a union variant to `HubMessage` without handling it everywhere is a compile error, not a runtime surprise. The `timeOf` fix was surfaced at `tsc --noEmit`, not by a test failure.

3. **The "push = re-query" discipline makes banner progression trivial.** The settlement notification triggers a cache invalidation; the re-queried `CatalogListingView` arrives with `Status: "Settled"` + `hammerPrice` + `settledAt`; the component switches on that status. No payload from the push is rendered as truth.

## Findings against narrative

- **Narrative 001 Moment 8** and **narrative 002 Moment 5** both name `remainingCredit: 445.00` on the push and a credit-balance update on the bidder's phone. The lived `SettlementCompletedNotification` deliberately omits `remainingCredit` (M6-S5 docstring). S4 renders the hammer price as the charge amount and omits the balance — routed as a **deferred gap** (see below), not a narrative-update, because the narrative describes the *designed* system; the *lived* notification shape is a documented intentional subset.
- **Banner text divergence:** narrative 001 says "Charged $55.00 to your credit. The keyboard is yours." S4 renders "Charged $55.00" / "It's yours!" — dropping the "to your credit" phrasing per OQ2. Routed as `document-as-intentional`: the phrasing change is deliberate to avoid implying credit-balance context the frontend cannot display.

## Spec delta — landed?

The session prompt's spec delta declared three consequences: (1) narrative 001 Moment 8 lands — the bidder app renders the settlement outcome; (2) narrative 002 Moment 5's bidder-visible beat lands — the three-state banner progression is renderable end-to-end; (3) the bidder app's narrative arc is complete from anonymous session start through settlement confirmation.

**Landed with one gap.** All three consequences are structurally in place: the settlement push is recognized (`parseHubMessage` + `kind: "settlementCompleted"`); the cache bridge invalidates the listing query; the `TerminalOutcome` component renders the confirmed charge for the winner; the banner progression (Sold → Settled) transitions correctly. The `remainingCredit` display is the one narrative-described feature S4 cannot render (no HTTP endpoint, no notification field) — deferred as documented.

The live smoke test against a running Aspire host was not performed in this session (API host not running at session time). The progression should be verified at M8-S7 end-to-end or at the next session where the Aspire stack is live.

## Verification checklist

- [x] `parseHubMessage` recognizes a `SettlementCompletedNotification`-shaped payload and returns `kind: "settlementCompleted"`
- [x] The cache bridge invalidates the listing query on a `settlementCompleted` message
- [x] The listing detail's terminal view distinguishes "Sold" (interim) from "Settled" (confirmed): winner + Settled shows charge; winner + Sold shows interim; non-winner shows "Sold"; Passed shows "did not sell"
- [x] Vitest covers: (a) `parseHubMessage` schema recognition; (b) cache bridge invalidation; (c) `TerminalOutcome` rendering variants
- [x] `client/bidder/` builds (`npm run build`, exit 0) and type-checks under strict
- [x] No backend change — no `.cs`, `.csproj`, `.slnx` touch
- [x] No `client/ops/`, no `OperationsHub`, no `PaymentFailed` handling, no `remainingCredit` display
- [ ] Live smoke against a running Aspire host — **deferred** (API host not running; verify at M8-S7 or next live session)
- [x] Retrospective written with `**Prompt:**` header, `## Spec delta — landed?`, and `remainingCredit` gap disposition
- [x] No commit to `main`; PR off `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **Live smoke test.** Start the Aspire host, seed a listing, bid to win, observe the Sold → Settled banner transition. The settlement pipeline runs in milliseconds; the transition should be near-instantaneous. This is the one unchecked acceptance criterion.
- **`remainingCredit` display (deferred).** Requires either: (a) a `GET /api/settlement/credit/{bidderId}` endpoint, or (b) enriching `SettlementCompletedNotification` in the Relay handler to include `remainingCredit` from `BidderCreditView`. Both are backend changes — out of M8's frontend-only scope. Track as a deferred item on STATUS.md at milestone close.
- **M8-S5 (Ops SPA Shell + Staff Auth + OperationsHub)** is the next slice in the M8 ladder.
