# Open Questions

Single corpus-level register of everything the extraction could not resolve from code. Appended to across phases. Each entry: where it surfaced (artifact + section), the question, and what was observed that could not be reconciled.

---

_No entries yet — Phase 0 raised no unresolvable items. The Phase 0 inventory completed cleanly: src/ confirms five Implemented / Partial / Scaffolded projects and three Planned-only BCs, matching the handoff prompt's authoring-time expectation._

---

## Phase 2

### OQ-P2-01 — `IsProxy` flag on `BidPlaced` from saga-emitted `PlaceBid`

**Surfaced in:** `workflows/proxy-bidding.md` — Reactive hop R5b open question.

**Observed:** The `ProxyBidManagerSaga` emits a `PlaceBid` command in R5b with semantic intent that the resulting `BidPlaced` integration event carries `IsProxy: true` (to let downstream observers distinguish auto-bids from human bids). However:

- The `PlaceBid` command has no `IsProxy` field (`src/CritterBids.Contracts/Auctions/PlaceBid.cs`).
- `PlaceBidHandler.AcceptanceEvents` hardcodes `IsProxy: false` on the emitted `BidPlaced` (`src/CritterBids.Auctions/PlaceBidHandler.cs:116`).
- The `BidPlaced` contract docstring says "M4 wires the Proxy Bid Manager saga to set `IsProxy=true` on auto-bids."

The wiring described in the contract is not present in the code path. Either the docstring is aspirational, or the field is intended to be set by a different mechanism not yet implemented.

**Could not be reconciled because:** the discrepancy is between contract intent and code behavior — both are sources of truth in their own register. Recorded for Phase 4 as drift but flagged here because the **intended** behavior cannot be determined from code alone.

---

## Phase 6

No new open questions surfaced through synthesis. Phase 6 reviewed every dossier, workflow, glossary entry, gap-register row, and lesson against the source paths they cite and against each other; no fact-level ambiguity remained unresolved. OQ-P2-01 above is the only unresolvable item in the corpus at close.
