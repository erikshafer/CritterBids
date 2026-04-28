# Workshop 004 — Selling BC Deep Dive

**Type:** BC-Focused (vertical depth, upstream)
**Date started:** 2026-04-09
**Status:** Complete — all 3 phases done

**Scope:** The Selling BC internals. The `SellerListing` aggregate state machine, automated approval, validation invariants, revision rules, and the `ListingPublished` payload contract.

**Companion file:** [`004-scenarios.md`](./004-scenarios.md) — Phase 3 Given/When/Then scenarios.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. `@ProductOwner` on standby. `@UX` consulted.

---

## Ubiquitous Language

The Selling BC owns the pre-publish listing lifecycle: from `DraftListingCreated` through `ListingPublished`, plus post-publish revision (`ListingRevised`) and end-of-life (`ListingEndedEarly`, `ListingWithdrawn`, `ListingRelisted`). The in-flight bidding lifecycle is owned by Auctions ([W002 §3](./002-auctions-bc-deep-dive.md#ubiquitous-language)); post-resolution settlement is owned by Settlement ([W003 §3](./003-settlement-bc-deep-dive.md#ubiquitous-language)).

Each term carries a one-line definition with optional cross-references and "what it is *not*" notes. Domain events are catalogued in [`docs/vision/domain-events.md`](../vision/domain-events.md) and in this workshop's Phase 1 architecture summary; events are not duplicated here.

| Term | Definition | Notes |
|---|---|---|
| **Listing** | The auctionable unit, identified by `ListingId`. From the Selling BC perspective, the lifecycle runs from Draft through Submitted, Approved, Published, Revised, EndedEarly. | Post-publish bidding lifecycle (`BiddingOpened` through resolution) is owned by Auctions BC; see W002 §3. |
| **SellerListing Aggregate** | The Marten event-sourced aggregate that enforces the listing state machine. One per listing. | States: Draft, Submitted, Approved, Published, Revised (stays Published), EndedEarly (terminal), Rejected (back to Draft). |
| **Draft Listing** | A listing that has been created but not yet submitted for approval. Editable freely. | Created via `CreateDraftListing`; updates via `UpdateDraftListing`. |
| **Listing Submission** | The seller's act of submitting a complete draft for approval. Triggers the validator. | Single command (`SubmitListing`). On validation pass in MVP, atomically produces `ListingSubmitted`, `ListingApproved`, `ListingPublished` in one transaction (W001 #14 resolution; single-handler-chain with planned post-MVP split). |
| **Listing Publish** | The state transition that makes the listing visible in the catalog and eligible for session attachment or timed open. Recorded as `ListingPublished`. | The integration boundary between Selling and downstream BCs (Listings, Auctions, Settlement). Carries `FeePercentage`, `Format`, reserve, and BIN price. |
| **Listing Revision** | A post-publish edit. Restricted to Title, Description, ShippingTerms - not price, reserve, or format. Recorded as `ListingRevised`. | During an active Flash session, Listings BC's catalog projection filters revisions out (W004 Phase 2 Q6 resolution). |
| **End Early** | The seller's act of pulling a published listing before it would otherwise close. Recorded as `ListingEndedEarly`. | Distinct from `ListingWithdrawn` (different business context, same saga handling). Sellers ending early after bids do not receive payment (W004 Phase 2 Design Refinement 5). |
| **Relist** | Creating a fresh listing aggregate from a previously ended-early or expired one. New aggregate, new agreement at current rates. | Carries `ListingRelisted` linking `OriginalListingId` and `NewListingId` (W004 Phase 2 Q5 resolution). |
| **Auction Format** | The listing's selling format: `Timed` or `Flash`. Set at draft time; immutable after publish. | Enum `ListingFormat`; carried on `ListingPublished`. See W002 §3 for downstream behavior per format. |
| **Reserve** | The minimum hammer price below which the listing does not sell at auction. May be null. | Defined in W002 §3 from the bidding perspective. Selling owns the seller-side capture and the upstream invariant `BuyItNowPrice >= ReservePrice`. |
| **Buy It Now Price** | The fixed-price purchase amount for the listing, presented alongside auction bidding. | Defined in W002 §3 from the bidding perspective. Invariant: `BuyItNowPrice >= ReservePrice` enforced at submission (W004 §5 rule 7). |
| **Seller** | The participant who creates and owns the listing. Identified by `SellerId`. | Same `SellerId` as in Participants BC. Validated via the `RegisteredSellers` projection before draft creation. |
| **RegisteredSellers Projection** | A Marten-backed projection enumerating all participants who have completed seller registration. Used by Selling BC pre-checks and the API gateway. | Built from `SellerRegistrationCompleted` events. Defense in depth: also checked at API layer (W004 §7) and at command time inside Selling. |
| **Validation Service** | A pure-function module enforcing 14 invariants across listing fields, numeric constraints, extended-bidding parameters, and format-specific rules. | Pure function - testable without framework or harness. 14 of W004's 41 scenarios are pure-function tests of this module. Full unit-test coverage is the implication of the W004 §5 status `in progress`. |
| **API Gateway Cross-BC Validation** | A pattern: pre-checks at the HTTP API layer prevent commands that would create state divergence between BCs. | First emerged in W004 (seller registration race; end-early-after-sold). Returns HTTP 409. Distinct from in-BC validation. |

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

| Section | Component | Scenarios | Type | Status |
|---|---|---|---|---|
| 1 | Draft lifecycle | 5 | Aggregate (Marten harness) | done |
| 2 | Submission and publication | 4 | Aggregate | done |
| 3 | Post-publication revision | 4 | Aggregate | design |
| 4 | End early and relist | 4 | Aggregate | in progress |
| 5 | Validation rules | 14 | Pure function | in progress |
| 6 | RegisteredSellers projection | 4 | Marten integration | done |
| 7 | API gateway cross-BC checks | 6 | HTTP-level | planned |

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
