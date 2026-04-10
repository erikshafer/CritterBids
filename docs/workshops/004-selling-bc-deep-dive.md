# Workshop 004 — Selling BC Deep Dive

**Type:** BC-Focused (vertical depth, upstream)
**Date started:** 2026-04-09
**Status:** In progress — Phase 3 next

**Scope:** The Selling BC internals. The `SellerListing` aggregate state machine, automated approval, validation invariants, revision rules, and the `ListingPublished` payload contract.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. `@ProductOwner` on standby. `@UX` consulted.

**Why upstream now:** Selling sits at the head of the dependency chain. Everything starts with `ListingPublished`. Downstream BCs (Auctions, Settlement) were designed against assumed properties of that event — time to verify and lock down.

---

## Phase 1 — Brain Dump: Internal Structure

*(Condensed. See git history for full Phase 1 output with aggregate code sketches, validation rule enumeration, both approval-chain options, the full ListingPublished payload analysis, and detailed reasoning.)*

### Architecture Summary

```
SellerListing Aggregate (Marten event-sourced, one stream per listing)
  States: Draft → Submitted → Approved → Published
              ↘ Rejected → Draft (re-edit)
          Published → Revised (stay Published)
          Published → EndedEarly (terminal)
          EndedEarly → (relist creates NEW aggregate)

Validation Service (pure functions, no I/O)
  11 rules: required fields, numeric invariants (BIN >= Reserve, Reserve >= StartingBid),
  extended bidding bounds, format-specific rules (Timed vs Flash)
```

No saga, no projection (read models live in Listings BC), no DCB. One aggregate with validation rules.

### Parked Questions Resolved in Phase 1

| # | Source | Resolution |
|---|--------|------------|
| W001 #14 | Automated approval | Option A — single handler chain in MVP. Atomic. Post-MVP migrates to Option B without event vocabulary changes. |
| W003 cross-BC #4 | `BIN >= Reserve` invariant | Enforced at listing creation/submission. Validator rejects. Settlement's BIN-skip-reserve-check backed by upstream guarantee. |
| W001 #1 | Listing UI before session starts | Listings hidden from participant catalog until `ListingAttachedToSession`. Visible in ops dashboard immediately. |

### Phase 1 Design Decisions

1. **Aggregate state machine:** 6 states (Draft, Submitted, Approved, Rejected, Published, EndedEarly). Approved is transient in MVP.
2. **Validation rules:** 11 enumerated rules.
3. **Reserve confidentiality:** Discipline-enforced via single integration event. Listings BC MUST NOT read `ReservePrice`.
4. **Post-publish revision:** Title, Description, ShippingTerms only. All else immutable. End and relist for critical changes.
5. **End early:** Allowed at any time before close.
6. **Relist:** Creates new aggregate with new ListingId. Original stream gets `ListingRelisted` marker.
7. **Timed vs Flash:** New `ListingFormat` enum. Timed requires Duration; Flash requires null Duration.
8. **`FeePercentage` on `ListingPublished`:** Selling reads platform config at publish time. Eliminates W003 race condition.

### Vocabulary Refinements (flagged for cross-workshop pass)

1. `ListingPublished` adds `FeePercentage` and `Format` fields
2. `ListingRevised` restricted to Title, Description, ShippingTerms
3. `ListingRelisted` carries `OriginalListingId` and `NewListingId`
4. New enum `ListingFormat` (Timed | Flash)

---

## Phase 2 — Storytelling: A Listing's Complete Lifecycle

This phase walks a fresh listing through its entire lifecycle. **"Hand-Forged Damascus Steel Knife"** (listing-K), created by **participant-007 ("CleverOtter12")** for the Nebraska.Code() Flash Session.

---

### Step 0: Prerequisite — Seller Registration

**`@QA` — resolving Question #2: `SellerRegistrationCompleted` arriving out of order.**

Selling needs to verify "is this person a registered seller?" before accepting `CreateDraftListing`. Three options considered.

> **Decision: Selling maintains a `RegisteredSellers` projection** built from `SellerRegistrationCompleted` events received from Participants BC. The `CreateDraftListing` handler queries this projection. On miss, throws and Wolverine retries with backoff. Structurally identical to the `PendingSettlement` race resolved in W003. **Question #2 resolved.**

```csharp
public sealed record RegisteredSeller
{
    public Guid SellerId { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
}
```

For the walkthrough, participant-007 registered at T-2 hours. The projection is ready.

---

### Step 1: T-30 minutes — Draft Creation

Participant-007 opens the seller flow, fills in the listing form:

```
CreateDraftListing {
  SellerId: participant-007,
  Title: "Hand-Forged Damascus Steel Knife",
  Description: "8-inch chef's knife, hand-forged from Damascus steel...",
  Format: Flash,
  StartingBid: 50.00,
  ReservePrice: 100.00,
  BuyItNowPrice: 200.00,
  Duration: null,
  ExtendedBiddingEnabled: true,
  ExtendedBiddingTriggerWindow: 00:00:30,
  ExtendedBiddingExtension: 00:00:15,
  ShippingTerms: { ... }
}
```

**Handler:** Verify seller registration (projection query, found). Run validation (all 11 rules pass). Generate `listing-K`. Produce event:

```
DraftListingCreated {
  ListingId: listing-K,
  SellerId: participant-007,
  ...all fields...,
  CreatedAt: T-30:00
}
```

**State:** `Draft`. Internal event (🟠). Invisible to everyone except the seller.

---

### Step 2: T-25 minutes — Draft Update

Seller improves the description. Taps "Save Draft."

**`@UX` — resolving Question #1: Auto-save vs explicit save.**

Auto-save creates noisy event streams (dozens of `DraftListingUpdated` events during a 5-minute editing session). For a focused pre-session workflow, explicit save matches the user's mental model.

> **Decision: Explicit save only.** Seller taps "Save Draft" to persist. **Question #1 resolved.**

```
DraftListingUpdated {
  ListingId: listing-K,
  Changes: { Description: "8-inch chef's knife, hand-forged from genuine Damascus steel.
    Beautiful flame pattern, razor-sharp edge, walnut handle.
    Made by hand over 40 hours of forging and finishing." },
  UpdatedAt: T-25:00
}
```

Validation runs on every update. If the seller tries to set `BuyItNowPrice: 75.00` (below reserve $100), the update is rejected and no event is produced.

---

### Step 3: T-20 minutes — Submission and Automated Approval

Seller taps "Submit Listing." The single handler chain fires (Phase 1 decision: Option A).

**Handler produces three events atomically:**

```
1. ListingSubmitted { ListingId: listing-K, SubmittedAt: T-20:00 }

2. ListingApproved { ListingId: listing-K, ApprovedAt: T-20:00, ApprovedBy: "auto" }

3. ListingPublished {
     ListingId: listing-K,
     SellerId: participant-007,
     Title: "Hand-Forged Damascus Steel Knife",
     Description: "8-inch chef's knife...",
     Format: Flash,
     StartingBid: 50.00,
     ReservePrice: 100.00,
     BuyItNowPrice: 200.00,
     FeePercentage: 10.0,        ← read from platform config at publish time
     Duration: null,
     ExtendedBiddingEnabled: true,
     ExtendedBiddingTriggerWindow: 00:00:30,
     ExtendedBiddingExtension: 00:00:15,
     ShippingTerms: { ... },
     PublishedAt: T-20:00
   }
```

**`ListingPublished` (🔵) fans out to five BCs:**

| BC | Action |
|---|---|
| **Listings** | Catalog projection creates row, `Status: Hidden` (not session-attached yet) |
| **Auctions** | Stores listing config in "awaiting session attachment" projection. No `BiddingOpened` yet (Flash). |
| **Settlement** | `PendingSettlement` row created. Reads `FeePercentage` from the event payload. |
| **Relay** | Notification to seller: "Your listing is now live." |
| **Operations** | Ops dashboard "newly published" feed updates. |

**Aggregate state:** `Published`. Stream: 5 events.

---

### Step 4: T-15 minutes — The Published-But-Waiting Gap

For 15 minutes, the listing exists but nothing happens. Listings hides it from participants (not session-attached). Ops dashboard shows it as available to attach. This is the gap W001 #1 raised, resolved in Phase 1.

---

### Step 5: T-10 minutes — Ops Attaches Listing to Session

Auctions emits `ListingAttachedToSession`. Listings catalog updates to `Visible (Upcoming)`. Selling BC is unaffected — it doesn't track session attachment.

---

### Step 6: T-5 minutes — A Late Revision

Seller fixes a typo in the description.

**`@UX` — resolving Question #6: Mid-session revision rules.**

Three discrete revision windows exist: (1) after publish, before session attachment — fine; (2) after attachment, before session start — visible to participants but not harmful; (3) during active session — disorienting, bidders may be mid-decision.

> **Decision: Selling accepts revisions any time during `Published` state.** The `ListingRevised` event is appended regardless. **Downstream consumers decide what to do with it.** Listings BC's catalog projection ignores revisions for listings in active sessions (the projection knows session state from `SessionStarted`). The seller sees their revision succeed; participants see the original until the session ends. **Question #6 resolved.**

For our walkthrough, the revision happens at T-5 (before session start), so it propagates normally:

```
ListingRevised {
  ListingId: listing-K,
  Title: null,                  // unchanged
  Description: "8-inch chef's knife...(fixed typo)",
  ShippingTerms: null,          // unchanged
  RevisedAt: T-5:00
}
```

**Settlement's `PendingSettlement` projection receives this.** All three mutable fields are irrelevant to PendingSettlement (it doesn't store Title, Description, or ShippingTerms). **The handler is effectively a no-op.** This validates the Phase 1 vocabulary refinement — W003 scenario 8.3 should be removed.

---

### Step 7: T+0 — Session Starts, Bidding Opens

Auctions emits `SessionStarted` → `BiddingOpened` per listing. W002's lifecycle takes over. **Selling BC is unaffected** — the aggregate stays in `Published` state.

**Critical design point: Selling has no `Sold` state.** The aggregate represents seller intent, not downstream resolution. When the listing sells at T+5:05 (cross-reference to W002), Selling's aggregate still says `Published`. The "is my listing sold?" answer comes from Listings BC's catalog projection or from Settlement's `SellerPayoutIssued` notification via Relay.

> **Phase 2 refinement:** The `SellerListing` state machine does NOT include `Sold`, `Passed`, `Settled`, or any downstream resolution states. The aggregate's lifecycle ends at `Published` for the happy path. Only seller-initiated actions (`EndListingEarly`) move it further. Querying cross-BC state belongs to Listings or Operations.

---

### Alternate Path A: End Early Before Session Start

At T-2 minutes, seller discovers a defect and pulls the listing.

```
ListingEndedEarly {
  ListingId: listing-K,
  SellerId: participant-007,
  Reason: "Discovered defect in the item",
  EndedAt: T-2:00
}
```

**Downstream:**

- **Auctions:** Removes from session attachment. `BiddingOpened` will not fire for this listing.
- **Listings:** Catalog → `Ended`. Disappears from participant view.
- **Settlement:** `PendingSettlement` → `Expired`. No settlement workflow runs.
- **Relay:** Notifies seller: "Your listing has been ended."

**State:** `EndedEarly` (terminal).

---

### Alternate Path B: End Early After Bidding Opens

Same `ListingEndedEarly` event. Auctions terminates the Auction Closing saga and any active Proxy Bid Manager sagas (structurally identical to `ListingWithdrawn` from W002 — different event, same saga logic).

**`@QA` edge case:** What if the seller tries to end a listing that's already sold (BIN purchased or normal close)?

Selling can't know the listing is sold — it doesn't subscribe to Auctions events. The check must live elsewhere.

> **Decision: The `EndListingEarly` API endpoint queries Auctions for current listing state before routing the command to Selling.** If the listing is already sold/BIN-purchased, return HTTP 409 Conflict. Selling only receives the command if the early-end is actually possible.
>
> **This is the "API gateway pattern for cross-BC validation"** — the same pattern used for seller registration in Step 0. When one BC needs knowledge of another BC's state to validate a command, the validation lives at the API layer, not inside the BC. BCs remain internally unaware of each other.

**Other Alt B decisions:**

- `ListingEndedEarly` and `ListingWithdrawn` remain **distinct events** despite similar Auctions handling. Audit clarity: seller-initiated vs ops-initiated.
- Sellers who end early after bids exist do NOT receive payment. Same effect as `ListingPassed` from Settlement perspective. Protects bidders from being charged.

---

### Alternate Path C: Relist After Ended Early

The seller fixes the defect and wants to try again. They go to "Relist" in the seller dashboard.

**The relist flow (Phase 1 decision: new aggregate):**

1. UI loads original listing data into a pre-filled draft form
2. Seller edits anything before submitting
3. New `SellerListing` aggregate created with new `ListingId` (listing-K2)
4. Original stream gets a `ListingRelisted` marker pointing to listing-K2

**`@ProductOwner` — resolving Question #5: Does relist preserve `FeePercentage` or read fresh config?**

eBay treats relisting as a new listing at current rates. The "new aggregate = new agreement" principle applies.

> **Decision: Fresh config.** Relisting reads current platform `FeePercentage`. The new listing is a new agreement at current rates. Original listing's events preserve the original fee. **Question #5 resolved.**

**On listing-K's stream (the original):**

```
ListingRelisted {
  OriginalListingId: listing-K,
  NewListingId: listing-K2,
  SellerId: participant-007,
  RelistedAt: T+1 day
}
```

This is a marker — it doesn't change listing-K's terminal state (`EndedEarly`), but provides a forward link. The new listing-K2 goes through its own full lifecycle: `DraftListingCreated → ... → ListingPublished` with its own stream.

---

## Phase 2 Summary

**Vocabulary changes (beyond Phase 1):**

- `DraftListingCreated` carries optional `RelistedFromListingId` (analytics-only, not load-bearing)
- No new events introduced. Phase 2 validated the Phase 1 design against a realistic walkthrough.

**Questions resolved:**

| # | Question | Resolution |
|---|----------|------------|
| 1 | Draft auto-save vs explicit save | Explicit save only. MVP simplicity. |
| 2 | `SellerRegistrationCompleted` out of order | Selling maintains a `RegisteredSellers` projection. `CreateDraftListing` queries it; on miss, Wolverine retries. |
| 5 | Relist preserves `FeePercentage`? | Fresh config. Relisting is a new agreement at current rates. |
| 6 | Mid-session revision allowed? | Selling accepts. Listings BC catalog projection ignores revisions during active sessions. Seller sees success; participants see original. |

**Questions deferred:**

| # | Question | Reason |
|---|----------|--------|
| 3 | Platform config location | Implementation detail, MVP uses `appsettings.json` |
| 4 | Image/media handling | Separate concern, post-MVP |

**Phase 2 design refinements:**

1. **Selling has no downstream resolution states.** The aggregate ends at `Published` for the happy path. No `Sold`, `Passed`, `Settled` states. Cross-BC queries for current listing state belong to Listings or Operations.
2. **API gateway pattern for cross-BC validation** emerged twice: seller registration check and listing-state check before end-early. When one BC needs another BC's state to validate a command, the validation lives at the API layer, not inside the BC.
3. **`ListingEndedEarly` after `BuyItNowPurchased` rejected at API level** (HTTP 409). Prevents Selling/Auctions state divergence.
4. **`ListingEndedEarly` and `ListingWithdrawn` remain distinct events.** Different business context (seller vs ops), same saga handling in Auctions.
5. **Sellers who end early after bids exist do NOT receive payment.** Protects bidders.

**Cross-workshop ripple effects (added to vocabulary pass list):**

- **W002:** Mention `ListingEndedEarly` alongside `ListingWithdrawn` in saga handlers. Both are terminal triggers. API gateway rejects end-early if saga is `Resolved`.
- **W003:** Scenario 8.3 (`ListingRevised` updates `ReservePrice`) should be removed — `ReservePrice` is no longer mutable post-publish.

**New questions surfaced:**

| # | Question | Persona | Notes |
|---|----------|---------|-------|
| 7 | Seller UX for finding relisted versions of their listings | `@UX` | Selling has the marker event, just needs UI exposure |
| 8 | Does Selling produce a Relay notification on publish, or is the HTTP 200 sufficient? | `@UX`/`@BackendDeveloper` | Phase 1 said Relay receives `ListingPublished`. Confirm UX intent in Phase 3. |
| 9 | Is `RegisteredSellers` the only projection Selling owns? | `@Architect` | Likely yes. Selling is otherwise pure write-side. |

---

## Phase 3 — Scenarios (Given/When/Then)

*Next: Implementation-ready scenarios for the SellerListing aggregate (every state transition), the validation rule set (every invariant with happy and failing cases), the RegisteredSellers projection, and the API-gateway cross-BC state checks.*

*(to be continued)*
