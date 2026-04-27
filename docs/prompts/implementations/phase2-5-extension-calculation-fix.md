# Phase 2.5: Extension Calculation Fix (Finding 011)

| Field | Value |
|---|---|
| **Status** | Stub - awaits Phase 2.5 fleshing |
| **Authored** | 2026-04-27 |
| **Phase** | Foundation Refresh, Phase 2.5 (contingent on Phase 2 findings) |
| **Source finding** | [`docs/narratives/001-findings.md`](../../narratives/001-findings.md) Finding 011 |
| **Source narrative** | [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) Moment 7 (revealing the bug as Moment 6's outcome plays out) |
| **Subdirectory** | `docs/prompts/implementations/` |

---

## Why this stub exists

Phase 2's narrative session (`docs/prompts/narratives/001-bidder-wins-flash-auction.md`) audited lived M3 code against the workshop and surfaced one `code-update` finding among twelve. Per ADR 016's spec-anchored framing and the foundation-refresh handoff §4.5, `code-update` findings do not resolve in the narrative session's PR; they become stub follow-up prompts that Phase 2.5 absorbs as standard implementation slices.

This file is the stub. A future session at Phase 2.5 kickoff fleshes it into a full implementation prompt following the AUTHORING.md ten rules.

## What's broken

`src/CritterBids.Auctions/PlaceBidHandler.cs:196` (`TryComputeExtension`) computes `var candidate = now + extension`, where `now` is the trigger-bid time. The workshop's slice 5.1 scenario in `docs/workshops/001-scenarios.md` expects `NewCloseAt = PreviousCloseAt + extension`. For bids in the early part of the trigger window, the lived calculation produces `candidate < ScheduledCloseAt` - the auction shortens rather than extends.

The DCB state's `Apply(ExtendedBiddingTriggered)` at `src/CritterBids.Auctions/BidConsistencyState.cs:80` updates `ScheduledCloseAt = candidate` unconditionally, which corrupts boundary state for subsequent bids. The saga's `Handle(ExtendedBiddingTriggered)` at `src/CritterBids.Auctions/AuctionClosingSaga.cs:72` guards with `if (message.NewCloseAt <= ScheduledCloseAt) return;` and refuses to reschedule a non-monotone close, but the broken event has already committed.

## Proposed slice scope

1. Change the `candidate` computation in `TryComputeExtension` so that `NewCloseAt` is monotone with respect to `ScheduledCloseAt`. The natural fix is `candidate = state.ScheduledCloseAt + extension`. Confirm against the M3-S4 retro at `docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md:298` ("the scheduled-close timestamp moves forward on every extension").
2. Add a defensive guard in `TryComputeExtension`: `if (candidate <= state.ScheduledCloseAt) return false`. Belt-and-suspenders against future regressions or alternative calculations that re-introduce non-monotone behavior.
3. Harden the test suite to cover the early-trigger-window case. Existing test scenario 1.11 (`BidInTriggerWindow_ProducesExtendedBiddingTriggered` in M3-S4 test inventory) likely asserts a specific NewCloseAt; verify the assertion catches the early-trigger-window path. Add a test for a bid at remaining = window (e.g., remaining = 30s with extension = 15s) that asserts `NewCloseAt = previousClose + extension`, not `previousClose - extension`.
4. Verify `BidConsistencyState.Apply(ExtendedBiddingTriggered)` does not need amendment: with `TryComputeExtension` now producing monotone NewCloseAt, the `Apply` method's unconditional update is correct. Consider documenting the invariant in a code comment.

## Out of scope

- The saga's `Handle(ExtendedBiddingTriggered)` defensive guard at `AuctionClosingSaga.cs:72` is correct and should remain. It defends against any future producer of `ExtendedBiddingTriggered` (manual replay, external publisher) emitting a non-monotone reschedule.
- The `MaxDuration` upper-bound check at `TryComputeExtension:199` is unrelated and should remain.
- Multiple sequential extended-bidding triggers (W001 parked question 4) are an alternate-path scope, not part of this fix.

## Acceptance criteria

- `dotnet build` and `dotnet test` clean across all CritterBids projects.
- Test for early-trigger-window bid lands and passes.
- `PlaceBidHandler.cs:196` calculation is monotone-by-construction.
- A retrospective at `docs/retrospectives/<slug>-retrospective.md` lands in the same PR per the standing prompt-then-retro discipline.
- The narrative at `docs/narratives/001-bidder-wins-flash-auction.md` Moments 6 and 7 do not need amendment - they render the workshop-intended behavior, which the fix makes the lived behavior. Finding 011 in `docs/narratives/001-findings.md` may be updated with a "Resolved at: <commit-sha>" line at retro close.

## Document history

- **v0.1** (2026-04-27): Stub authored at narrative 001's session close per the foundation-refresh handoff §4.5 stub-prompt discipline. Awaits Phase 2.5 kickoff for fleshing into a full implementation prompt.
