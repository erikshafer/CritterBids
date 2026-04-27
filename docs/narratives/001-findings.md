# Narrative 001 - Findings

Findings surfaced while authoring `001-bidder-wins-flash-auction.md` against lived M3 and M4 code. Each finding is routed via the four-lane discipline established in ADR 016 and detailed in the narrative-authoring prompt at `docs/prompts/narratives/001-bidder-wins-flash-auction.md`:

| Lane | Resolved in this PR? |
|---|---|
| `narrative-update` | Yes. Narrative edited. |
| `workshop-update` | Yes. Workshop edited. |
| `code-update` | No. Stub follow-up prompt under `docs/prompts/implementations/`. Resolved in Phase 2.5. |
| `document-as-intentional` | Yes. Relationship documented. |

---

### Finding 001 - Setting's credit-ceiling band drifted from lived code

**Routing:** narrative-update

**Surfaced at:** Moment 1

**Discrepancy.** The narrative's Setting paragraph 3 originally claimed the credit ceiling was "between $200 and $500". Lived code at `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs:62` derives the ceiling as `200m + (bytes[14] % 9) * 100m`, yielding nine discrete values from $200 to $1000 in $100 steps. The M1-S5 retrospective (`docs/retrospectives/M1-S5-slice-0-2-start-participant-session.md`) confirms the 200-1000 range. The Setting paragraph was authored without first reading the lived code; the narrower band claim was a fabrication introduced at Cast-and-Setting drafting time.

**Resolution.** Setting paragraph 3 edited from "between $200 and $500" to "drawn from one of nine values between $200 and $1000 in $100 steps". SwiftFerret42's specific value of $500 remains valid (it is the median of the band).

---

### Finding 002 - Workshop slice 0.2 asserts hard display-name uniqueness; lived code provides probabilistic uniqueness only

**Routing:** workshop-update

**Surfaced at:** Moment 1

**Discrepancy.** The workshop scenario "Display name is unique within active sessions" in `docs/workshops/001-scenarios.md` slice 0.2 asserts a hard uniqueness invariant ("DisplayName: 'BoldPenguin7' // different from any active session"). The lived code at `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs:49-52` derives the display name from UUID v7 random bytes (one of twenty-five Adjectives × one of twenty-nine Animals × a 1-9999 number suffix, roughly 7.25M tuples) with no active-session lookup or collision check. For the demo audience size (~40 concurrent bidders), collision probability is well below 0.001%, but it is non-zero. The M1-S5 retrospective at `docs/retrospectives/M1-S5-slice-0-2-start-participant-session.md` records the same misclaim ("Uniqueness guaranteed by stream ID uniqueness") - which is a category error: stream-ID uniqueness does not propagate through a lossy derivation onto a finite tuple space. The code comment at `StartParticipantSession.cs:12` carries the misclaim too, but its correction is a separate `code-update` candidate not blocking this PR.

**Resolution.** Workshop scenario in `docs/workshops/001-scenarios.md` slice 0.2 reframed: the heading rewritten from "Display name is unique within active sessions" to "Display names are probabilistically unique within active sessions"; the inline comment on `BoldPenguin7` rewritten from "different from any active session" to "probabilistically distinct, derived from UUID v7 random bytes"; a "Note on uniqueness" block added below the scenario describing the MVP posture and the trigger condition under which a uniqueness index would become warranted (audience size at which collisions become practically observable).
