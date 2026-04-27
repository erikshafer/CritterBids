# Phase 2.5: Extension Calculation Fix (Finding 011)

| Field | Value |
|---|---|
| **Status** | Active — fleshed from stub on 2026-04-27 |
| **Authored** | 2026-04-27 (stub v0.1) → 2026-04-27 (full prompt v0.2) |
| **Phase** | Foundation Refresh, Phase 2.5 (single-slice contingency from Phase 2 narrative) |
| **Source finding** | [`docs/narratives/001-findings.md`](../../narratives/001-findings.md) Finding 011 |
| **Source narrative** | [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) Moments 6–7 |
| **Source workshop scenario** | [`docs/workshops/001-scenarios.md`](../../workshops/001-scenarios.md) §"Tier 5 — Extended Bidding", slice 5.1 |
| **Subdirectory** | `docs/prompts/implementations/` |
| **Estimated scope** | one PR; one production-code edit, three tests adjusted/added, one retrospective |

---

## Goal

Make the extended-bidding `NewCloseAt` calculation monotone-by-construction so that the lived behaviour matches the workshop spec (`NewCloseAt = PreviousCloseAt + extension`) and so that `Apply(ExtendedBiddingTriggered)`'s unconditional `ScheduledCloseAt` update is sound for every reachable producer of the event. Replace the existing `now + extension` formula in `PlaceBidHandler.TryComputeExtension`, add a defensive non-monotone-reschedule guard, and re-pin the affected test assertions to the workshop formula instead of the broken implementation. Close the loop by marking Finding 011 resolved with the commit SHA in the same PR's narrative-findings ledger.

The narrow scope is the entire scope. This is a Phase 2.5 contingency that exists *only* because Phase 2's narrative session surfaced one `code-update` finding among twelve. There is no intersecting refactor, no surrounding cleanup, and no opportunistic extension to harden adjacent paths beyond what Finding 011 names.

## Context to load

- [`docs/narratives/001-findings.md`](../../narratives/001-findings.md) Finding 011 — the discrepancy statement, root cause, and proposed fix shape this prompt is operationalizing
- [`docs/workshops/001-scenarios.md`](../../workshops/001-scenarios.md) Tier 5 slice 5.1 — the workshop formula `NewCloseAt = PreviousCloseAt + extension` is the spec the fix anchors to
- [`docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md`](../../retrospectives/M3-S4-dcb-place-bid-retrospective.md) — read line 298 ("the scheduled-close timestamp moves forward on every extension") as the prior-art statement of the monotone invariant
- `src/CritterBids.Auctions/PlaceBidHandler.cs` — the file the fix lands in (`TryComputeExtension` near the bottom)
- `src/CritterBids.Auctions/BidConsistencyState.cs` — read `Apply(ExtendedBiddingTriggered)` at line 80; verify the post-fix invariant holds without modifying this method
- `src/CritterBids.Auctions/AuctionClosingSaga.cs` — read `Handle(ExtendedBiddingTriggered)` at line 72; verify the saga's defensive guard remains warranted (it does, per "Out of scope")
- `tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — scenarios 1.11, 1.14, 1.15 are the regression surface; the new defensive test lands here too

(Seven items, at the AUTHORING.md ceiling — this prompt is small and the load reflects that.)

## In scope (numbered)

1. **`src/CritterBids.Auctions/PlaceBidHandler.cs` — `TryComputeExtension` rewrite.** Change the candidate-close computation so the extension grows from the current scheduled close, not from the trigger-bid clock. Concretely: replace the source of the addition from `now` to `state.ScheduledCloseAt`. The trigger-window check (`remaining > TimeSpan.Zero || remaining > window`) and the `MaxDuration` upper-bound check (`candidate > maxClose`) both stay; only the candidate's left operand changes.

2. **`src/CritterBids.Auctions/PlaceBidHandler.cs` — defensive non-monotone guard.** Immediately after computing `candidate`, refuse to produce an `ExtendedBiddingTriggered` when `candidate <= state.ScheduledCloseAt`. Belt-and-suspenders against any future regression that re-introduces a non-monotone formulation. The guard fires `return false` without setting `newCloseAt`, preserving the existing out-parameter contract.

3. **(Optional, decide in-flight) one-line invariant comment in `BidConsistencyState.Apply(ExtendedBiddingTriggered)`** — a single sentence stating that the unconditional `ScheduledCloseAt = candidate` update is sound because every reachable producer (i.e. `TryComputeExtension`) is monotone-by-construction. Add only if removing the comment would confuse a future reader. Do not add a state-side guard — that would mask producer regressions instead of catching them.

4. **`tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — re-anchor scenario 1.11 (`BidInTriggerWindow_ProducesExtendedBiddingTriggered`).** The current assertion pins `NewCloseAt` to `now + extension` (the broken formula) with a misleading comment claiming "per workshop formula." Re-pin to `PreviousCloseAt + extension`, where `PreviousCloseAt == close` for this scenario. Update the inline comment to describe what is actually asserted.

5. **`tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — re-shape scenario 1.14 (`ExtensionWithinMaxDuration_Fires`).** Under the monotone fix, the existing setup (`currentClose = T+9:50`, `extension = 15s`, `maxClose = T+10:00`) computes `candidate = T+10:05`, which now exceeds `MaxDuration` and produces no event — turning the "within max" test into a "blocked by max" test. Slide `currentClose` earlier (and adjust `now` so the bid still falls in the trigger window) so that `currentClose + extension` lands at or under `maxClose` and the scenario continues to assert the within-max happy path. Update the assertion to the new `currentClose + extension` value.

6. **`tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — verify scenario 1.15 (`ExtensionExceedsMaxDuration_Blocked`) still asserts blocked.** Under the monotone fix, the existing setup (`currentClose = T+9:55`, `extension = 15s`) produces `candidate = T+10:10`, still over `maxClose = T+10:00`, so the `ShouldBeEmpty()` assertion remains green by construction. The inline comment at the assertion needs the new candidate value (`T+10:10`, not `T+10:05`); the assertion itself does not move.

7. **`tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — add a regression-defense test for the early-trigger-window case.** The test seeds an extended-bidding-enabled listing with `triggerWindow = 30s` and `extension = 15s`, places a bid at `remaining = window` (the maximum legal early position in the trigger window), and asserts `NewCloseAt == previousClose + extension` rather than `now + extension`. Under the broken formula the asserted `NewCloseAt` would have been *earlier than* `previousClose`; the test name should make the regression-defense intent explicit (e.g., a name that names "monotone" or "early-trigger-window" — pick whichever reads better in the test list and keeps the existing `Scenario 1.x` numbering convention out of it, since this is not a workshop scenario).

8. **`docs/narratives/001-findings.md` — mark Finding 011 resolved.** Append a `**Resolved at:** <commit-sha>` line to Finding 011's body in the same PR. Use the commit SHA of the production-code-fix commit (item 1 + 2), not the retrospective commit. The narrative document body itself does not need amendment per the stub's note — Moments 6–7 already render the workshop-intended behaviour.

9. **`docs/retrospectives/phase2-5-extension-calculation-fix-retrospective.md` — written last, gates on green build + test.** See "Retrospective gate" below.

## Explicitly out of scope

- **`AuctionClosingSaga.Handle(ExtendedBiddingTriggered)` defensive guard at `AuctionClosingSaga.cs:72`** — stays. It defends against any *future* producer of `ExtendedBiddingTriggered` (manual replay, external publisher, an alternative handler) emitting a non-monotone reschedule, regardless of whether `TryComputeExtension` is itself monotone-by-construction now. Removing it would couple the saga's correctness to the producer's correctness, which is the wrong direction.
- **The `MaxDuration` upper-bound check at `TryComputeExtension`** — unrelated to Finding 011 and stays unchanged.
- **`BidConsistencyState.Apply(ExtendedBiddingTriggered)` behavioural change** — stays an unconditional `ScheduledCloseAt = candidate` assignment. Adding a state-side `if (candidate > ScheduledCloseAt)` guard would mask producer bugs instead of letting them surface.
- **Multiple sequential extended-bidding triggers on the same listing** — workshop W001 parked-question 4. Out of scope; an alternate-path scope, not a fix.
- **Any new contract type, event registration, module wiring, projection, or saga handler** — none of these are affected by Finding 011.
- **Renumbering or restructuring the existing `PlaceBidHandlerTests` scenario block** — items 4–6 edit individual scenarios; the file's overall layout, helpers, and `FixedTimeProvider` shape stay byte-identical.
- **Cross-BC test-fixture changes** — Finding 011 is Auctions-internal; Selling, Listings, Participants, and Operations test fixtures are not touched.
- **A skill-file update.** No skill file's pattern guidance is contradicted by this fix; the bug was an arithmetic error inside an already-skill-conformant handler shape, not a missing pattern. If the retro surfaces a skill-level learning anyway, capture it as a future skill-pass note in the retro's "what remains" section rather than appending mid-fix.
- **Any change to `Program.cs`, `AuctionsModule.cs`, `csproj` files, or `Directory.*.props`** — production-code surface changes are confined to a single method body in `PlaceBidHandler.cs` (plus the optional one-line comment in `BidConsistencyState.cs`).

## Conventions to pin or follow

Inherit all conventions from M3-S4 (DCB handler shape, `FixedTimeProvider` for time-dependent tests, `_fixture.GetDocumentSession()` seeding pattern, `Decide`-vs-`Handle` separation). No new behavioural conventions are introduced; one design rule is **re-affirmed by the fix** and worth naming in the retro:

- **Producer-monotone is the strong shape; consumer-defensive is the safety net.** When an event carries a value that downstream state must treat as monotone (here: `NewCloseAt > ScheduledCloseAt`), the producer should make it monotone-by-construction, the state's `Apply` should trust it, and the consuming saga should still defensively guard against future producers. Phase 2.5 makes the producer monotone (item 1), keeps the state trusting (item 3 documents the trust), and leaves the saga's defensive guard intact (out of scope §1). All three layers stay in their right roles.

One working-practice convention this session also exercises:

- **Test assertions anchor to the spec, not to the implementation.** The reason Finding 011 went undetected through M3-S4's full test pass and the foundation refresh's M3 audit is that scenario 1.11's assertion was written by reading the implementation rather than the workshop. The fix re-anchors the assertion to the workshop formula. Future test authors should derive expected values from the workshop scenario directly; copying a value out of the implementation defeats the test's purpose. Capture this in the retro as a generalizable lesson, not just a scenario-1.11 fix.

## Commit sequence (proposed)

1. `fix(auctions): monotone NewCloseAt in TryComputeExtension (Finding 011)` — items 1, 2, optional 3. Production-code-only commit so the fix surfaces cleanly in `git log` and the SHA used to mark Finding 011 resolved (item 8) is unambiguous.
2. `test(auctions): re-anchor extended-bidding scenarios to workshop formula` — items 4, 5, 6, 7. Test-only commit; the four scenarios move together because all four are about the same calculation.
3. `docs(narratives): mark Finding 011 resolved at <sha>` — item 8. Standalone commit so the resolution-link in the findings ledger is legible.
4. `docs: write Phase 2.5 retrospective` — item 9.

## Acceptance criteria

- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test CritterBids.slnx` — full solution green; baseline preserved; +1 new test from item 7; the four test edits in items 4–6 do not change the green test count for those scenarios
- [ ] `src/CritterBids.Auctions/PlaceBidHandler.cs` — `TryComputeExtension` computes the candidate close from `state.ScheduledCloseAt`, not from `now`; the defensive guard `candidate <= state.ScheduledCloseAt → return false` is present; `MaxDuration` check is unchanged
- [ ] `src/CritterBids.Auctions/BidConsistencyState.cs` — `Apply(ExtendedBiddingTriggered)` is byte-identical except for the optional one-line invariant comment from item 3
- [ ] `src/CritterBids.Auctions/AuctionClosingSaga.cs` — byte-identical (defensive guard at line 72 untouched)
- [ ] `tests/CritterBids.Auctions.Tests/PlaceBidHandlerTests.cs` — scenarios 1.11 and 1.14 assert `NewCloseAt = previousClose + extension`; scenario 1.15 still asserts `ExtendedBiddingTriggered` is empty; one new regression-defense test for the early-trigger-window case asserts the workshop formula at `remaining == window`
- [ ] No other production file in `src/CritterBids.Auctions/` is modified
- [ ] No file outside `src/CritterBids.Auctions/`, `tests/CritterBids.Auctions.Tests/`, `docs/narratives/001-findings.md`, and `docs/retrospectives/` is modified
- [ ] `docs/narratives/001-findings.md` Finding 011 carries a `**Resolved at:** <commit-sha>` line pointing at the item-1+2 commit
- [ ] `docs/retrospectives/phase2-5-extension-calculation-fix-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test CritterBids.slnx` shows all tests green and `dotnet build` shows 0 errors + 0 warnings. If any test fails or any warning lands, fix the code first, then write the retro.

Retrospective content requirements (per `docs/retrospectives/README.md` template):
- Baseline test count + post-fix test count, with a phase table showing the count after each commit
- Per-item status table mirroring "In scope (numbered)" 1–9 with commit references
- A short subsection on **why scenario 1.11's assertion was wrong**, with the verbatim before/after assertion line and a sentence on the test-anchored-to-spec convention this session re-affirms
- A short subsection on the scenario 1.14 setup recalibration, naming the new `currentClose` / `now` values and why they were chosen (the candidate-vs-maxClose arithmetic)
- The exact name of the new regression-defense test and what specific arithmetic it pins
- Whether the optional invariant comment in `BidConsistencyState` was added; if so, the verbatim line; if not, why removing it would not have confused a future reader
- A **"What Phase 3 should know"** section — at minimum, a one-line note that Phase 2.5 closed and that no further `code-update` lanes from narrative 001 remain open
- A verification checklist mirroring the acceptance-criteria list 1:1 so the retro doubles as sign-off

## Open questions (pre-mortems — flag, do not guess)

1. **Should the fix produce `NewCloseAt = state.ScheduledCloseAt + extension` or `NewCloseAt = max(now, state.ScheduledCloseAt) + extension`?** The first matches the workshop verbatim. The second would handle the (currently unreachable, but conceivable) edge case where a clock-skew or replay scenario delivers a `now > state.ScheduledCloseAt` to `TryComputeExtension`. The first option is preferred — `now > state.ScheduledCloseAt` is already rejected by `EvaluateRejection`'s `now >= state.ScheduledCloseAt → "ListingClosed"` guard, so the second option's added complexity defends against an unreachable state. Flag this if the implementation surfaces a path where the rejection guard is bypassed.

2. **Does the new regression-defense test belong in `PlaceBidHandlerTests.cs` alongside scenarios 1.1–1.15, or in a separate file?** Co-location is the recommendation — the test exercises `PlaceBidHandler.Decide` against the same fixture and asserts on the same event type. A separate file would make the regression-defense intent more visible at the cost of fragmenting the `Decide`-path test surface. Flag if a strong reason for separation surfaces during writing.

3. **Should Finding 011's resolution line cite *which* commit SHA — the production-code-fix (item 1+2) or the test-anchor (items 4–7)?** The production-code-fix is the resolution; the test-anchor is the proof. Cite the production-code-fix SHA per item 8. Flag if the commit-sequence reordering during execution makes a different choice cleaner.

## Document history

- **v0.1** (2026-04-27): Stub authored at narrative 001's session close per the foundation-refresh handoff §4.5 stub-prompt discipline.
- **v0.2** (2026-04-27): Stub fleshed into a full implementation prompt at Phase 2.5 kickoff, following the AUTHORING.md ten-rules. The stub's "Why this stub exists", "What's broken", and "Proposed slice scope" prose is preserved in spirit and superseded section-by-section: stub's "Why" → this prompt's "Source finding" / "Source narrative" header rows; stub's "What's broken" → this prompt's "Goal" + "In scope" item 1; stub's "Proposed slice scope" → this prompt's "In scope" items 1–7; stub's "Acceptance criteria" → this prompt's "Acceptance criteria" expanded with build/test gates and per-item file-touch checks. The seven-item context-load ceiling, the four-commit sequence, and the three open questions are net-new at v0.2.
