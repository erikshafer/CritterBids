# Phase 2.5: Extension Calculation Fix (Finding 011) — Retrospective

**Date:** 2026-04-27
**Phase:** Foundation Refresh, Phase 2.5 (single-slice contingency from Phase 2 narrative)
**Source finding:** [`docs/narratives/001-findings.md`](../narratives/001-findings.md) Finding 011
**Source workshop:** [`docs/workshops/001-scenarios.md`](../workshops/001-scenarios.md) §"Tier 5", slice 5.1
**Prompt:** [`docs/prompts/implementations/phase2-5-extension-calculation-fix.md`](../prompts/implementations/phase2-5-extension-calculation-fix.md)
**Branch:** `phase-2-5/extension-calculation-fix`
**Duration:** ~1h (single session, kickoff to retro)

## Baseline

- `dotnet build CritterBids.slnx` at session open: 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` at session open: 86 tests green (Auctions: 35; Selling: 32; Listings: 11; Participants: 6; Api: 1; Contracts: 1)
- M3 closed since the foundation-refresh handoff; M4-S2 was the last implementation slice to land before the foundation-refresh phases. Phase 2 (narrative 001) closed at commit `439e938` (Phase 2.1 — further refreshing).
- Stub at `docs/prompts/implementations/phase2-5-extension-calculation-fix.md` v0.1 was the only `code-update` lane open from narrative 001's twelve findings; the other eleven landed in their respective lanes during Phase 2.

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| 0 | Flesh stub into full implementation prompt (v0.1 → v0.2) | `b6123e6` |
| 1 | `TryComputeExtension` candidate anchored to `state.ScheduledCloseAt` | `ec1cab2` |
| 2 | Defensive `candidate <= state.ScheduledCloseAt → return false` guard | `ec1cab2` |
| 3 | Producer-monotone invariant comment on `Apply(ExtendedBiddingTriggered)` | `ec1cab2` |
| 4 | Scenario 1.11 re-anchored to workshop formula | `8acc480` |
| 5 | Scenario 1.14 setup recalibrated | `8acc480` |
| 6 | Scenario 1.15 comment updated; assertion unchanged | `8acc480` |
| 7 | Scenario 1.11b regression-defense test added | `8acc480` |
| 8 | Finding 011 marked `Resolved at: ec1cab2` in findings ledger | `905ab15` |
| 9 | This retrospective | (this commit) |

Item codes mirror the prompt's "In scope (numbered)" 1–9 exactly. Item 0 is the prompt-fleshing meta-step that the foundation-refresh stub-prompt discipline calls for at Phase 2.5 kickoff and is included for traceability.

## Item 1+2: `TryComputeExtension` rewrite

### Why this approach

Anchoring to `state.ScheduledCloseAt` is the workshop's literal formula (slice 5.1: `NewCloseAt = PreviousCloseAt + extension`). The prompt's Open Question 1 weighed `state.ScheduledCloseAt + extension` against `max(now, state.ScheduledCloseAt) + extension`; the second was rejected because `now > state.ScheduledCloseAt` is already rejected upstream by `EvaluateRejection`'s `now >= state.ScheduledCloseAt → "ListingClosed"` guard at `PlaceBidHandler.cs:150`, making the second option's added `max` complexity defend against an unreachable state. The first option is the implementation.

The defensive `candidate <= state.ScheduledCloseAt → return false` guard is reachable only if a future refactor re-introduces a non-monotone formulation (e.g., bringing `now` back into the candidate, or supporting a configurable extension that could go to zero). Today the guard is unreachable by construction; it is documentation-as-code for the monotone postcondition.

### Structural metrics table

| Metric | Before | After |
|--------|--------|-------|
| Method line count (`TryComputeExtension`) | 27 | 32 |
| `now`-references inside the candidate computation | 1 | 0 |
| Guards before `MaxDuration` check | 1 (window) | 2 (window, monotone) |
| Method signature, return type, out-parameter contract | unchanged | unchanged |
| Cross-method dependencies | unchanged | unchanged |

### `Apply(ExtendedBiddingTriggered)` invariant comment

Added (item 3 was optional in the prompt; chose to add it). Verbatim:

```csharp
// Unconditional update is sound because TryComputeExtension is monotone-by-construction
// (NewCloseAt > ScheduledCloseAt for every reachable producer).
```

Removing this would have left a future reader wondering whether the unconditional `ScheduledCloseAt = @event.NewCloseAt` was an oversight or an intentional invariant. The two-line comment names the producer it trusts and the postcondition that producer guarantees. Costs nothing; documents the producer-monotone-plus-trusting-state shape.

## Items 4–7: test re-anchor and regression defense

### Why scenario 1.11's assertion was wrong

Verbatim before (`PlaceBidHandlerTests.cs:299`):

```csharp
triggered.NewCloseAt.ShouldBe(now.AddSeconds(15)); // T+4:55 per workshop formula
```

Verbatim after:

```csharp
triggered.NewCloseAt.ShouldBe(close.AddSeconds(15)); // T+5:15 per workshop slice 5.1: NewCloseAt = PreviousCloseAt + extension
```

The original assertion pinned `NewCloseAt` to `now + extension` — the broken implementation's formula — and labeled it "per workshop formula" in the comment, which is precisely backwards. The workshop formula is `PreviousCloseAt + extension`. The test was authored by reading `TryComputeExtension`'s source, not by reading the workshop scenario, and the misleading inline comment papered over the divergence so successfully that M3-S4's full test pass and the foundation refresh's M3 audit both signed off without flagging it. The narrative-001 audit caught it only because that audit's discipline is to read the workshop scenario *first* and the lived code *against* the workshop, not the other way around.

The generalizable lesson: **test assertions anchor to the spec, not to the implementation.** Future scenario-style tests in CritterBids should derive their expected values from the corresponding workshop scenario — copying a value out of the implementation defeats the test's regression-detection purpose. The workshop-scenarios doc is authoritative; the implementation is the thing under test.

### Scenario 1.14 setup recalibration

Pre-fix the test seeded `currentClose = T+9:50, now = T+9:40, originalClose = T+5:00, maxClose = T+10:00`. Old formula: `candidate = now + extension = T+9:55`, within max, scenario passes asserting "fires within max." New formula under the same setup: `candidate = currentClose + extension = T+10:05`, exceeds max, scenario would have flipped to asserting blocked — colliding with scenario 1.15's intent. Recalibration:

| Variable | Before | After | Why |
|----------|--------|-------|-----|
| `currentClose` | `T+9:50` | `T+9:40` | Move earlier so `currentClose + extension = T+9:55` lands under `maxClose = T+10:00` |
| `now` | `T+9:40` (10s before close) | `T+9:20` (20s before close) | Keep the bid inside the 30s trigger window and clear of `currentClose` |
| Asserted `NewCloseAt` | `now.AddSeconds(15)` (T+9:55) | `currentClose.AddSeconds(15)` (T+9:55) | Same expected timestamp, but derived from the spec formula |

The expected `NewCloseAt` value happens to be identical by coincidence (both arithmetics produce T+9:55 for the new setup), which is convenient — the assertion shape changed in a way that matches the workshop formula even though the timestamp matches the old test's expectation.

### Scenario 1.15 comment update

Assertion stayed `events.OfType<ExtendedBiddingTriggered>().ShouldBeEmpty();` because the test's setup (`currentClose = T+9:55`, `extension = 15s`) produces `candidate = T+10:10` under the new formula — still over `maxClose = T+10:00`, still blocked. Comment changed from "newCloseAt would be T+10:05" to "candidate = currentClose + extension = T+10:10" so the inline arithmetic stays accurate.

### New regression-defense test

Name: `BidAtEarlyTriggerWindow_NewCloseAtIsMonotone` (numbered "1.11b" in the inline comment block, not as a workshop scenario — it is a regression-defense test, not a slice-5.1 scenario). What it pins:

- `triggered.PreviousCloseAt.ShouldBe(close)` — the event carries the actual prior close
- `triggered.NewCloseAt.ShouldBe(close.AddSeconds(15))` — workshop formula directly
- `triggered.NewCloseAt.ShouldBeGreaterThan(triggered.PreviousCloseAt)` — explicit monotone postcondition; future refactors that break monotonicity hit this assertion regardless of what specific formula they substitute
- `triggered.NewCloseAt.ShouldNotBe(now.AddSeconds(15))` — negative assertion against the pre-fix value; if this assertion ever fails under future code, the regression is the exact one Finding 011 caught

Setup (`remaining = 30s`, `extension = 15s`) is the maximum legal early position in the trigger window. Under the broken formula, `now + extension = T+4:45`, which is *15 seconds earlier* than `PreviousCloseAt = T+5:00` — the worst-case shortening the bug could produce within the trigger window. The test names that scenario explicitly so future readers see what the regression looks like in time arithmetic.

## Test results

| Phase | Auctions tests | Solution total | Result |
|-------|----------------|----------------|--------|
| Baseline (pre-fix) | 35 | 86 | green |
| After item 1+2 (production-code fix only, before test edits) | (would have been 34 green + 1 failing — scenario 1.11's stale assertion) | (would have been 85 green + 1 failing) | not committed in this state |
| After items 4–6 (test re-anchor) | 35 | 86 | green |
| After item 7 (new regression-defense test) | 36 | 87 | green |
| Session close | 36 | 87 | green |

## Build state at session close

- `dotnet build CritterBids.slnx -warnaserror`: 0 errors, 0 warnings
- `dotnet test CritterBids.slnx`: 87/87 green, 0 skipped, 0 failing
- `src/CritterBids.Auctions/PlaceBidHandler.cs` line count: 211 (was 215; reformatted to add the spec-citation comment and the defensive guard, no net change in scope)
- `src/CritterBids.Auctions/PlaceBidHandler.cs` `now`-references inside the `candidate` computation: **0** (negative-assertion proof of fix)
- `src/CritterBids.Auctions/BidConsistencyState.cs`: `Apply(ExtendedBiddingTriggered)` body byte-identical to the pre-fix state; only a two-line `// ... ` comment added above
- `src/CritterBids.Auctions/AuctionClosingSaga.cs`: byte-identical (defensive guard at line 72 untouched)
- Files modified outside `src/CritterBids.Auctions/`, `tests/CritterBids.Auctions.Tests/`, `docs/narratives/`, `docs/retrospectives/`, and `docs/prompts/`: **0** (negative-assertion proof of scope discipline)

## Key learnings

1. **Test assertions must anchor to the spec, not the implementation.** Scenario 1.11's failure mode — assertion derived by reading the source rather than the workshop — is a generalizable trap. The discipline applies to every scenario-style test in CritterBids: derive expected values from the workshop scenario, not from the implementation.

2. **Producer-monotone is the strong shape; consumer-defensive is the safety net.** When a domain event carries a value that downstream state must treat as monotone, the producer should make it monotone-by-construction, the consuming state's `Apply` should trust it, and any consuming saga should still defensively guard against future producers. Phase 2.5 makes the producer monotone (`TryComputeExtension`), keeps the state trusting (`Apply(ExtendedBiddingTriggered)`), and leaves the saga's defensive guard intact (`AuctionClosingSaga.Handle(ExtendedBiddingTriggered):72`). All three layers stay in their right roles.

3. **Misleading inline comments are worse than no comments.** Scenario 1.11's `// T+4:55 per workshop formula` comment actively harmed the test's reviewability — a reader reasonably skipping past it inferred (correctly per the comment, incorrectly per reality) that the assertion already matched the workshop. Comments that describe the assertion's *meaning* (not just its computation) are higher-leverage; comments that misrepresent the meaning are negative-leverage.

4. **Phase 2.5 is the foundation refresh's proof-of-concept for the spec-anchored loop.** The cycle Finding 011 → stub follow-up prompt → Phase 2.5 fleshing → execution → retro → findings-ledger close ran end-to-end in one short session. The discipline scales: a single workshop-vs-code divergence becomes a single PR with a single commit per concern (production fix, tests, findings update, retro), and the findings ledger ends up with a fully-traceable resolution line. ADR 016's spec-anchored framing is justified by the throughput.

## Verification checklist

- [x] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [x] `dotnet test CritterBids.slnx` — 87/87 green; baseline 86 preserved; +1 new test from item 7; the four edits in items 4–6 do not change the green count for those scenarios
- [x] `src/CritterBids.Auctions/PlaceBidHandler.cs` — `TryComputeExtension` computes the candidate close from `state.ScheduledCloseAt`, not from `now`; the defensive guard `candidate <= state.ScheduledCloseAt → return false` is present; `MaxDuration` check is unchanged
- [x] `src/CritterBids.Auctions/BidConsistencyState.cs` — `Apply(ExtendedBiddingTriggered)` body byte-identical except for the optional one-line invariant comment from item 3
- [x] `src/CritterBids.Auctions/AuctionClosingSaga.cs` — byte-identical (defensive guard at line 72 untouched)
- [x] `tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — scenarios 1.11 and 1.14 assert `NewCloseAt = previousClose + extension`; scenario 1.15 still asserts `ExtendedBiddingTriggered` is empty; one new regression-defense test for the early-trigger-window case asserts the workshop formula at `remaining == window`
- [x] No other production file in `src/CritterBids.Auctions/` is modified
- [x] No file outside `src/CritterBids.Auctions/`, `tests/CritterBids.Auctions.Tests/`, `docs/narratives/001-findings.md`, `docs/prompts/implementations/`, and `docs/retrospectives/` is modified
- [x] `docs/narratives/001-findings.md` Finding 011 carries a `**Resolved at:** ec1cab2` line pointing at the production-fix commit
- [x] This retrospective exists and meets the retrospective content requirements from the prompt

## Open questions answered

The prompt named three Open Questions to flag rather than guess:

1. **`state.ScheduledCloseAt + extension` vs `max(now, state.ScheduledCloseAt) + extension`?** Took the first. `now > state.ScheduledCloseAt` is already rejected upstream by `EvaluateRejection`'s `"ListingClosed"` guard at `PlaceBidHandler.cs:150`, so the `max` defends against an unreachable state. Cited the upstream rejection as the warrant.

2. **New regression-defense test in `PlaceBidHandlerTests.cs` or a separate file?** Co-located. The test exercises `PlaceBidHandler.Decide` against the same fixture and asserts on the same event type as scenarios 1.11–1.15; separation would have fragmented the `Decide`-path test surface for no readability gain. Numbered "1.11b" in the inline comment block to signal regression-defense lineage rather than a workshop scenario.

3. **Finding 011 resolution SHA — production-fix or test-anchor?** Production-fix (`ec1cab2`). The fix is the resolution; the test re-anchor is the proof. The test-anchor's commit (`8acc480`) is referenced in the findings line's prose, but the SHA itself is the production fix per item 8.

## What Phase 3 should know

- **Phase 2.5 is closed.** No further `code-update` lanes from narrative 001 remain open. The four-lane discipline (`narrative-update`, `workshop-update`, `code-update`, `document-as-intentional`) closed all twelve findings within the two-PR shape: eleven in narrative 001's PR (#13 — Phase 2.1), one here.
- **The producer-monotone-plus-trusting-state shape is now documented in code at two sites** — `PlaceBidHandler.TryComputeExtension`'s spec citation comment, and `BidConsistencyState.Apply(ExtendedBiddingTriggered)`'s invariant comment. Future event-sourcing slices that introduce similar "downstream state must trust a monotone value" relationships should point at this shape rather than re-derive it. The convention is candidate skill-file material if Phase 3+ surfaces a second instance.
- **The spec-anchored test discipline** captured under Key Learning 1 is candidate content for the testing skill file at `docs/skills/critter-stack-testing-patterns.md`. Phase 2.5 did not touch the skill file (out of scope per the prompt); a future session should append a "deriving expected values from workshops, not implementations" subsection if the discipline surfaces a second time.

## Document history

- **v0.1** (2026-04-27): Authored at Phase 2.5 close, after the build+test gate cleared (87/87 green, 0 warnings). Mirrors the `docs/retrospectives/README.md` template; includes the prompt's required content (per-item table with commit refs, scenario 1.11 wrong-assertion explainer, scenario 1.14 recalibration, regression-defense test name, optional invariant-comment outcome, "What Phase 3 should know"). Verification checklist mirrors the prompt's acceptance criteria 1:1.
