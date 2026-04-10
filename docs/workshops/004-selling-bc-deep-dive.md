# Workshop 004 — Selling BC Deep Dive

**Type:** BC-Focused (vertical depth, upstream)
**Date started:** 2026-04-09
**Status:** Complete — all 3 phases done

**Scope:** The Selling BC internals. The `SellerListing` aggregate state machine, automated approval, validation invariants, revision rules, and the `ListingPublished` payload contract.

**Companion file:** [`004-scenarios.md`](./004-scenarios.md) — Phase 3 Given/When/Then scenarios.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. `@ProductOwner` on standby. `@UX` consulted.

---

## Phase 1 — Brain Dump: Internal Structure

*(Condensed. See git history for full Phase 1 output with aggregate code sketches, validation rule enumeration, both approval-chain options, and detailed reasoning.)*

### Architecture Summary

```
SellerListing Aggregate (Marten event-sourced)
  States: Draft → Submitted → Approved → Published
              ↘ Rejected → Draft
          Published → Revised (stay Published)
          Published → EndedEarly (terminal)
          EndedEarly → (relist creates NEW aggregate)

Validation Service (pure functions)
  14 rules across required fields, numeric invariants, extended bidding, format-specific
```

### Parked Questions Resolved in Phase 1

| # | Source | Resolution |
|---|--------|------------|
| W001 #14 | Automated approval | Single handler chain in MVP. Migrates to separate handlers post-MVP without event vocabulary changes. |
| W003 cross-BC #4 | BIN >= Reserve invariant | Enforced at listing creation/submission. Settlement's BIN-skip-reserve-check backed by upstream guarantee. |
| W001 #1 | Listing UI before session | Hidden from participant catalog until ListingAttachedToSession. Visible in ops dashboard immediately. |

### Vocabulary Refinements

1. `ListingPublished` adds `FeePercentage` and `Format` fields
2. `ListingRevised` restricted to Title, Description, ShippingTerms
3. `ListingRelisted` carries `OriginalListingId` and `NewListingId`
4. New enum `ListingFormat` (Timed | Flash)

---

## Phase 2 — Storytelling: A Listing's Complete Lifecycle

*(Condensed. See git history for full Phase 2 walkthrough with all 7 steps and 3 alternate paths for "Hand-Forged Damascus Steel Knife" (listing-K).)*

### Questions Resolved in Phase 2

| # | Resolution |
|---|------------|
| Q1 (auto-save) | Explicit save only. |
| Q2 (seller registration race) | `RegisteredSellers` projection with Wolverine retry pattern. |
| Q5 (relist fee) | Fresh config. New agreement at current rates. |
| Q6 (mid-session revision) | Selling accepts. Listings BC catalog projection filters during active sessions. |

### Phase 2 Design Refinements

1. **Selling has no downstream resolution states.** Aggregate ends at `Published` for happy path. No `Sold`/`Passed`/`Settled`.
2. **API gateway pattern for cross-BC validation** emerged twice: seller registration check, listing-state check before end-early. Validation at the API layer, not inside BCs.
3. **`ListingEndedEarly` after BIN rejected at API level** (HTTP 409).
4. **`ListingEndedEarly` and `ListingWithdrawn` remain distinct** events (different business context, same saga handling).
5. **Sellers ending early after bids do NOT receive payment.** Protects bidders.

### Cross-Workshop Ripple Effects (for vocabulary pass)

- **W002:** Mention `ListingEndedEarly` alongside `ListingWithdrawn` in saga handlers.
- **W003:** Scenario 8.3 (`ListingRevised` updates `ReservePrice`) should be removed.

---

## Phase 3 — Scenarios (Given/When/Then)

**41 scenarios** covering all Selling BC components. Full scenarios in companion file: **[`004-scenarios.md`](./004-scenarios.md)**

### Coverage by Component

| Section | Component | Scenarios | Type |
|---|---|---|---|
| 1 | Draft lifecycle | 5 | Aggregate (Marten harness) |
| 2 | Submission & publication | 4 | Aggregate |
| 3 | Post-publication revision | 4 | Aggregate |
| 4 | End early & relist | 4 | Aggregate |
| 5 | Validation rules | 14 | Pure function |
| 6 | RegisteredSellers projection | 4 | Marten integration |
| 7 | API gateway cross-BC checks | 6 | HTTP-level |

**14 of 41 scenarios (34%) are pure-function tests** of the validator — no framework, no harness, no I/O. Every validation rule has at least one scenario.

### Key Scenario Highlights

**2.1 — Atomic 3-event chain.** Single `SubmitListing` command produces `ListingSubmitted + ListingApproved + ListingPublished` in one transaction. Demonstrates the Phase 1 Option A decision.

**5.7 — BIN >= Reserve invariant.** The scenario that closes W003 cross-BC #4. Settlement can now trust that any BIN price it sees is at least the reserve.

**7.4 / 7.5 — End early after sold rejected at API level.** Demonstrates the API gateway pattern. HTTP 409 before the command reaches Selling BC. Prevents Selling/Auctions state divergence.

**1.2 / 6.1 / 7.2 — Seller registration race handling at three layers.** Shows defense in depth: projection-based check inside Selling (with retry), projection integration test, and API gateway pre-check.

---

## Workshop 004 — Complete Output Summary

| Artifact | Count |
|---|---|
| Parked questions from prior workshops resolved | 3 (W001 #1, W001 #14, W003 cross-BC #4) |
| Phase 1 questions raised | 6 |
| Phase 1 questions resolved in Phase 2 | 4 |
| Phase 1 questions explicitly deferred | 2 (platform config location, media handling) |
| Phase 2 questions raised | 3 (implementation-level) |
| Vocabulary refinements | 4 (FeePercentage, Format, restricted Revised, Relisted shape) |
| New enum | 1 (`ListingFormat`) |
| Given/When/Then scenarios | 41 |
| Pure-function scenarios | 14 (34%) |
| Design patterns named | 1 (API gateway cross-BC validation) |
