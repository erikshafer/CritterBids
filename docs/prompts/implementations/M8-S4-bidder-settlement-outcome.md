# M8-S4: Bidder Settlement Outcome — "Charged $55.00. The keyboard is yours."

**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — slice plan §7, row M8-S4
**Slice:** S4 of M8 (the settlement-outcome bidder slice; the final bidder-visible beat in the bidder app's narrative arc)
**Narrative:** `docs/narratives/001-bidder-wins-flash-auction.md` (Moment 8 — SwiftFerret42 is charged and the sale is complete) + `docs/narratives/002-winner-clears-settlement.md` (Moment 5 — "The keyboard is hers")
**Agent:** Claude Code
**Estimated scope:** one PR; **frontend-only** (`client/bidder/` tree) **plus one doc** (the retro). **No `.cs`, `.csproj`, or `.slnx` file is touched.**

---

## Preconditions

This prompt assumes:

- **M8-S3b has merged** — `client/bidder/` is a live bidding surface: bid placement against `POST /api/auctions/bids`, the ADR 026 SignalR integration pattern (`SignalRProvider` + `useListen` + TanStack Query cache bridge), outbid/extended-bidding/gavel-fall affordances, all over `BiddingHub`.
- **M8-S3c has merged** — ADR 027 sticky queue bindings are in place; the full seed → bid → outbid → close → settle journey runs end-to-end with exactly-once delivery; `CatalogListingView.Status` transitions to `"Settled"` and `settledAt` is stamped by the Listings BC's `SettlementStatusHandler`.
- **The `bidder:{participantId}` group is already joined** — `SignalRProvider.tsx` enrols the held participant's bidder group on connection + reconnect (lines 144-154). The `SettlementCompletedNotification` is winner-targeted to this group.
- **The `SettlementCompletedNotification` is currently silently dropped** — `parseHubMessage` in `messages.ts` returns `null` for payloads matching `{ settlementId, listingId, winnerId, hammerPrice, completedAt }` because no schema recognizes that shape. The cache bridge never fires; the listing detail view never re-queries on settlement completion.

## Goal

Close the bidder app's narrative arc by rendering the **settlement outcome**: when the winning bidder's settlement completes, the listing detail view transitions from "You won! Settlement confirmation arrives next." to a confirmed **"Charged $XX.XX. The keyboard is yours."** state — driven by the `SettlementCompletedNotification` hub push triggering a re-query of the `CatalogListingView` (now at `Status: "Settled"` with `hammerPrice` and `settledAt` populated). This is **narrative 001 Moment 8** and **narrative 002 Moment 5** — the final bidder-visible beat in the auction journey. After S4, the bidder SPA covers the full story from anonymous session start through catalog → bid → win → settlement confirmation.

The work is **frontend-only**: the backend pipeline (`SettlementCompleted` → Listings `SettlementStatusHandler` → `CatalogListingView.Status = "Settled"`, and Relay's `SettlementCompletedHandler` → `BiddingHub` push to `bidder:{WinnerId}`) is fully wired and live-verified at M8-S3c. S4 teaches the client to recognize the push and render the result.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M8-frontend-spas.md` | **Authoritative for scope.** §7 row M8-S4; §2 bidder settlement outcome surface; §3 non-goals (no backend changes). |
| `docs/narratives/001-bidder-wins-flash-auction.md` (Moment 8) | SwiftFerret42 is charged and the sale is complete. The banner progression: "You Won" → "Charged $55.00 to your credit. The keyboard is yours." Jointly authoritative with the milestone. |
| `docs/narratives/002-winner-clears-settlement.md` (Moments 3 + 5) | The Settlement-grain companion. Moment 3: SwiftFerret42's balance drops to $445. Moment 5: the closing banner update, the three-state banner progression. |
| `client/bidder/src/signalr/messages.ts` | Current hub-message parse surface. `SettlementCompletedNotification` is **not recognized** — the session adds a schema + kind for it. |
| `client/bidder/src/signalr/cacheBridge.ts` | The ADR 026 cache bridge. Must handle the new `settlementCompleted` kind (invalidate the listing query so the view re-queries to "Settled" status). |
| `client/bidder/src/bidding/LiveBidding.tsx` | The `TerminalOutcome` component. Already handles "Sold" and "Settled" with "You won!" — but the "Settled" view needs to show the confirmed charge, not just the same message as "Sold". |
| `client/bidder/src/catalog/schema.ts` | The Zod schema. Already has `status`, `hammerPrice`, `winnerId`, `settledAt` — all the fields S4 needs are already parsed. |
| `src/CritterBids.Relay/Notifications/SettlementCompletedNotification.cs` | The **lived wire shape**: `{ settlementId, listingId, winnerId, hammerPrice, completedAt }`. Note: `remainingCredit` is deliberately omitted (M6-S5 docstring). |
| `src/CritterBids.Relay/Handlers/SettlementCompletedHandler.cs` | Confirms the push targets `bidder:{WinnerId}` group via `SendAsync(ReceiveMessage, notification)`. |
| `docs/decisions/026-signalr-integration-pattern.md` | ADR 026 — the Provider + `useListen` + cache bridge pattern S4 follows. |
| `CLAUDE.md` | Global conventions. |

## In scope

1. **Add `settlementCompleted` to the hub-message parse surface.** A new Zod schema in `messages.ts` matching the lived `SettlementCompletedNotification` wire shape (`{ settlementId, listingId, winnerId, hammerPrice, completedAt }`); a new `kind: "settlementCompleted"` variant in the `HubMessage` union; `parseHubMessage` updated to recognize the shape. The discriminator: `settlementId` + `winnerId` + `hammerPrice` + `completedAt` (no `bidId`, no `bidCount`, no `eventType` — structurally disjoint from existing schemas).
2. **Handle the new kind in the cache bridge.** `applyHubMessage` invalidates the listing query on `settlementCompleted` — the re-query picks up `Status: "Settled"` + `hammerPrice` + `settledAt` from the authoritative `CatalogListingView`.
3. **Upgrade `TerminalOutcome` for the "Settled" state.** The current component treats "Sold" and "Settled" identically ("You won!"). After S4, a "Settled" listing for the winner shows a **confirmed charge view**: the hammer price as a charge amount, and a confirmation message (narrative: "Charged $55.00 to your credit. The keyboard is yours."). Non-winners see "Sold" or a neutral settled message. The "Sold" state retains its current interim message ("Settlement confirmation arrives next.") as the brief transition between gavel and settlement.
4. **Vitest + RTL coverage.** Prove: (a) the `parseHubMessage` recognizes a `SettlementCompletedNotification`-shaped payload and assigns `kind: "settlementCompleted"`; (b) the cache bridge invalidates the listing query on the new kind; (c) the `TerminalOutcome` component renders the charge confirmation for a "Settled" listing when the held participant is the winner, and renders a non-winner message otherwise.
5. **`docs/retrospectives/M8-S4-bidder-settlement-outcome-retrospective.md`** — written last; `**Prompt:**` header + `## Spec delta -- landed?` paragraph.

## Explicitly out of scope

- **Any backend / API-host change.** No new endpoints, no notification enrichment, no Relay handler change, no `.cs`/`.csproj`/`.slnx` touch. The backend pipeline is consumed as-is.
- **`remainingCredit` display.** Narrative 001 Moment 8 names `remainingCredit: 445.00` on the push and a credit-balance update on the bidder's phone. The **lived** `SettlementCompletedNotification` deliberately omits `remainingCredit` (M6-S5 docstring: "composing it requires reading Settlement's `BidderCreditView` projection"). No HTTP endpoint exposes `BidderCreditView`. Displaying remaining credit requires either: (a) a new query endpoint (backend change), or (b) enriching the notification from the projection (Relay handler change). Both are backend work and out of S4's frontend-only scope. The prompt recommends deferring remaining-credit display and rendering only the hammer-price charge confirmation. See Open Questions.
- **`PaymentFailed` bidder push.** No Relay handler exists for `PaymentFailed` → `BiddingHub`. The failure path is a separate narrative and a future slice; S4 is the happy path only.
- **Seller-perspective settlement notification.** The seller console is out of M8 entirely (§3).
- **The ops app / `client/ops/` / `OperationsHub`** (M8-S5/S6).
- **Playwright e2e** (M8-S7).
- **New catalog page surfaces.** The catalog tile may incidentally show "Settled" status (it already shows status badges), but no new catalog-specific settlement surface is built.

## Conventions to pin or follow

- **Relay push = re-query, never render-the-payload:** ADR 026 / milestone §6. The `SettlementCompletedNotification` triggers a cache invalidation; the confirmed charge values (`hammerPrice`, `settledAt`) come from the re-queried `CatalogListingView`, not from the push payload. This is especially relevant here because the notification shape is a subset of what the read model carries.
- **Zod at the wire boundary:** the new schema is the single parse point for the settlement notification; the `HubMessage` union is the internal type.
- **In-set libraries only:** ADR 013's accepted set. No new dependencies.
- **Internal-doc prose** (the retro) follows the project's internal-doc conventions; em-dash hygiene is external-prose-only.

## Spec delta

Per ADR 020, this slice's spec consequences are: (1) **narrative 001 Moment 8 lands** -- the bidder app renders the settlement outcome: the winner sees the confirmed charge amount; the "You Won → Settlement confirmation arrives next → Charged $XX.XX" banner progression completes. (2) **narrative 002 Moment 5's bidder-visible beat lands** -- the three-state banner progression is now renderable end-to-end. (3) **The bidder app's narrative arc is complete** -- from anonymous session start (M8-S2) through catalog → bid → outbid → extended bidding → gavel → settlement confirmation, the full narrative 001 journey is live in the SPA. The retro's `## Spec delta -- landed?` paragraph confirms: the settlement push is recognized; the "Settled" view shows the charge; the banner progression works against a live Aspire host.

## Acceptance criteria

- [ ] `parseHubMessage` recognizes a `SettlementCompletedNotification`-shaped payload (`{ settlementId, listingId, winnerId, hammerPrice, completedAt }`) and returns `kind: "settlementCompleted"`
- [ ] The cache bridge invalidates the listing query on a `settlementCompleted` message (the re-query picks up `Status: "Settled"` + `hammerPrice` + `settledAt`)
- [ ] The listing detail's terminal view distinguishes **"Sold" (interim)** from **"Settled" (confirmed)**:
  - **Winner + Settled:** charge confirmation showing the hammer price (e.g. "Charged $55.00 to your credit. The keyboard is yours.")
  - **Winner + Sold:** interim "You won! Settlement confirmation arrives next." (the brief transition state)
  - **Non-winner + Sold/Settled:** neutral outcome message (existing behavior)
  - **Passed / Withdrawn:** existing "did not sell" message (unchanged)
- [ ] Vitest covers: (a) `parseHubMessage` schema recognition for `settlementCompleted`; (b) cache bridge invalidation on the new kind; (c) `TerminalOutcome` rendering for winner-settled vs winner-sold vs non-winner
- [ ] `client/bidder/` builds (`npm run build`, exit 0) and type-checks under strict from a clean checkout; only in-set libraries
- [ ] No backend change -- no `.cs`, `.csproj`, `.slnx`, `Program.cs` touch
- [ ] No `client/ops/`, no `OperationsHub`, no `PaymentFailed` handling, no `remainingCredit` display
- [ ] **Live smoke against a running Aspire host:** seed a listing, bid to win, observe the "Sold → You won!" state, then confirm the view transitions to the "Settled → Charged" state after the settlement pipeline completes (the settlement pipeline runs in milliseconds -- the transition should be near-instantaneous)
- [ ] `docs/retrospectives/M8-S4-bidder-settlement-outcome-retrospective.md` written with the `**Prompt:**` header, `## Spec delta -- landed?`, and a record of the `remainingCredit` gap disposition
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Open questions

1. **`remainingCredit` gap.** The narrative says the bidder's credit balance updates from $500 to $445 on settlement. The lived backend does not expose this: the `SettlementCompletedNotification` omits `remainingCredit` (M6-S5 deliberate choice), and no HTTP endpoint exposes `BidderCreditView`. **Recommended resolution:** defer remaining-credit display to a future slice that adds either (a) a `GET /api/settlement/credit/{bidderId}` endpoint or (b) notification enrichment in the Relay handler (both are backend changes). S4 shows the hammer price as the charge amount and omits the balance. Record the gap in the retro as a finding and a deferred item on STATUS.md.

2. **Banner text.** Narrative 001 Moment 8 says "Charged $55.00 to your credit. The keyboard is yours." The "to your credit" phrasing implies the credit-balance context S4 cannot render (see OQ1). **Recommended resolution:** use "Charged $55.00" or "Charged $55.00 -- it's yours!" without the credit-balance implication. Settle the exact copy at implementation time; the acceptance criterion is that the hammer price appears and the message is distinct from the interim "Sold" state.

3. **`parseHubMessage` discrimination order.** The new `settlementCompleted` schema's required fields (`settlementId`, `winnerId`, `hammerPrice`, `completedAt`) are structurally disjoint from all existing schemas (`bidPlaced` needs `bidId`; `listingSold` needs `bidCount` + `soldAt`; `bidderGroupSchema`/`listingGroupSchema` need `eventType`). Confirm at implementation time that no false positive is possible -- the ordering should be most-specific-first per the existing pattern, and `settlementCompleted` can slot before or after `listingSold` safely.
