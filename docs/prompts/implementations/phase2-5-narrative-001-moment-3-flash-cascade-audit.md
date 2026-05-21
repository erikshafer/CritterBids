# Phase 2.5: Narrative 001 Moment 3 — Flash Cascade Lived-Code Audit (Finding 013)

| Field | Value |
|---|---|
| **Status** | Stub — awaiting flesh-out |
| **Authored** | 2026-05-21 (stub v0.1) |
| **Phase** | Foundation Refresh, Phase 2.5 (deferred narrative-update finding from M4 close) |
| **Source finding** | [`docs/narratives/001-findings.md`](../../narratives/001-findings.md) Finding 013 |
| **Source narrative** | [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) Moment 3 |
| **Source workshop** | [`docs/workshops/001-flash-session-demo-day-journey.md`](../../workshops/001-flash-session-demo-day-journey.md) slice 2.3 |
| **Subdirectory** | `docs/prompts/implementations/` |
| **Estimated scope** | one PR; one narrative edit, one finding ledger update, one retrospective; zero `.cs` diff |

---

## Why this stub exists

Narrative 001 was authored against lived M3 code, with Moment 3 (the Flash session-start cascade) carrying a `defer` disposition because the Auctions-BC Flash session aggregate, `StartSession` handler, `SessionStartedHandler` fan-out, and Listings-side session-membership handler were forward-spec at narrative-authoring time. M4-S5 + M4-S6 have now landed the cascade end-to-end. The `defer` disposition's trigger has fired: a lived-code audit of Moment 3 can run, and the audit surfaces one narrative-update divergence.

The narrative names the Listings-side handler `SessionMembershipHandler` (singular, multi-source — consuming the Auctions session trio AND Selling's `ListingWithdrawn`). The lived M4-S6 implementation pins ADR-014 Sub-Option A: two single-source classes, `AuctionsSessionHandler` (Auctions-sourced, session trio) + `SellingListingWithdrawnHandler` (Selling-sourced, withdrawal). Both expressions describe the same logical feature ("session membership reflected on the catalog"); the topology differs.

Phase 2.5 separation of audit-and-fix from author-and-ship (ADR 016 Phase 2 discipline) routes the fix to its own slice. M4-S7 (M4 close) surfaces the finding and stubs this prompt; the fix lands here.

## What's narrated vs lived

**Narrated (Moment 3, lines ~83-104):**
- Single `SessionStartedHandler` reads `ListingIds` and emits one `BiddingOpened` per attached listing — accurate at M4-S5 close.
- Integration events flow over `listings-auctions-events` to Listings — accurate.
- "Listings-side `SessionMembershipHandler` consumes each one and tolerantly upserts `CatalogListingView`" — this names a single class doing the session-membership reflection.

**Lived at M4-S6 close:**
- `src/CritterBids.Listings/AuctionsSessionHandler.cs` — handles `ListingAttachedToSession` + `SessionStarted` from `listings-auctions-events`.
- `src/CritterBids.Listings/SellingListingWithdrawnHandler.cs` — handles `ListingWithdrawn` from `listings-selling-events`.
- `src/CritterBids.Listings/AuctionStatusHandler.cs` — gains a `Withdrawn`-preservation guard on `Handle(BiddingOpened)`.

Three single-source classes, not one multi-source class. The naming convention (`Auctions*Handler`, `Selling*Handler`) is the M4-S6 source-prefix pin per ADR-014 §"Decision" §1 (strengthened to unconditional single-source-per-sibling).

## Proposed slice scope

1. **`docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 amendment.** Replace the `SessionMembershipHandler` reference with the lived two-class topology, with a one-line note that the source-split is per ADR-014 Sub-Option A. Preserve the surrounding prose — the audit is name-and-topology-only, not a re-narration. Specific edits expected:
   - The "Response" paragraph names `AuctionStatusHandler` rather than the narrated `SessionMembershipHandler` for the `BiddingOpened` upsert (the narrated catalog handler exists; the *new* M4-S6 handlers are `AuctionsSessionHandler` and `SellingListingWithdrawnHandler` for membership and withdrawal respectively).
   - "Things deliberately not included" — strike the `defer` line about Moment 3's lived-code audit and replace with a one-line note that the audit landed at Phase 2.5 with the finding ledger pointer.
2. **`docs/narratives/001-findings.md` Finding 013 — mark resolved.** Append a `**Resolved at:** <commit-sha>` line to Finding 013's body using the SHA of the narrative-edit commit (item 1).
3. **`docs/retrospectives/`** — one Phase 2.5 retrospective covering this slice. Title format per the extension-calculation-fix precedent.

## Out of scope

- **Any `.cs` file edit.** The lived implementation is correct; only the narrative drifts.
- **Any workshop edit.** Workshop 001 §"Phase 3 — Storyboarding" and slice 2.3 do not name the handler class; the narrative is the only document that does.
- **Any ADR-014 edit.** The ADR is the authoritative source-of-truth for the Sub-Option A pin; the narrative converges on it.
- **Authoring narrative 002 / 003 / 004 / 005.** The operator-perspective narrative candidate flagged in Moment 3's "Things deliberately not included" is a separate session if it ever ships.
- **OQ3 Path α observation gap (the Auctions-side terminal observation).** M4-S6 pinned the Listings-side terminal; the Auctions-side remains unobserved and is M5/M6 carry-forward per the M4 close retro. Not Phase 2.5 scope.
- **Renaming the lived classes to match the narrative.** Per ADR-014 Sub-Option A naming convention, the lived names are the canonical names; the narrative converges on them.

## Why Path B (this stub) over Path A (inline narrative amendment at M4-S7)

ADR 016 Phase 2 discipline separates audit-and-fix from author-and-ship. M4-S7 is a milestone-close retrospective session, not a narrative-authoring session; folding a narrative amendment into M4-S7 would conflate the two surfaces. The extension-calculation-fix precedent (Finding 011 → Phase 2.5) is the established pattern for `narrative-update` and `code-update` findings surfaced at milestone close.

## Document history

- **v0.1** (2026-05-21): Stub authored at M4-S7 (M4 close) per OQ1 Path B disposition. Recommended flesh-out timing: post-M4 docs-cleanup slot, or whenever the next narrative-update slice is convenient. Not blocking M5 / M6 work.
