# Workshop 004 — Selling BC Scenarios (Given/When/Then)

Companion to `004-selling-bc-deep-dive.md`, Phase 3.
Implementation-ready scenarios for all Selling BC components: the `SellerListing` aggregate, the validation rule set, the `RegisteredSellers` projection, and the API gateway cross-BC state checks.

**Conventions:**
- Placeholder IDs: `listing-K` (the walkthrough listing), `participant-007` (seller), `listing-K2` (relist target)
- Timestamps as relative offsets from session start (e.g., `T-30:00`, `T+5:05`)
- Platform fee: 10% (MVP default from `appsettings.json`)
- Listing-K configuration: starting bid $50, reserve $100, BIN $200, Flash format

**Test structure:**
- **Sections 1–4:** Aggregate state transitions — event-sourced aggregate tests via Marten test harness
- **Section 5:** Validation rules — pure function tests against the `ListingValidator`
- **Section 6:** `RegisteredSellers` projection — integration tests against Marten
- **Section 7:** API gateway cross-BC checks — HTTP-level tests against the API host

---

## 1. Aggregate — Draft Lifecycle

### 1.1 Create draft — happy path

```
Given:  RegisteredSellers projection contains participant-007
        (no prior stream for listing-K)

When:   CreateDraftListing {
          SellerId: participant-007,
          Title: "Hand-Forged Damascus Steel Knife",
          Format: Flash,
          StartingBid: 50.00,
          ReservePrice: 100.00,
          BuyItNowPrice: 200.00,
          Duration: null,
          ExtendedBiddingEnabled: true,
          ExtendedBiddingTriggerWindow: 00:00:30,
          ExtendedBiddingExtension: 00:00:15
        }

Then:   DraftListingCreated { ListingId: listing-K, SellerId: participant-007, ...all fields, CreatedAt }

State:  Status: Draft
```

### 1.2 Create draft — seller not registered

```
Given:  RegisteredSellers projection does NOT contain participant-999

When:   CreateDraftListing { SellerId: participant-999, ... }

Then:   throws SellerNotRegisteredException
        (Wolverine will retry — on successful retry after projection catches up, 1.1 happens)
```

### 1.3 Update draft — valid change

```
Given:  DraftListingCreated { ListingId: listing-K, Description: "original" }
        Aggregate state: Draft

When:   UpdateDraftListing { ListingId: listing-K, Changes: { Description: "improved description" } }

Then:   DraftListingUpdated { ListingId: listing-K, Changes: { Description: "improved description" }, UpdatedAt }
```

### 1.4 Update draft — change violates invariant

```
Given:  DraftListingCreated { ListingId: listing-K, ReservePrice: 100.00, BuyItNowPrice: 200.00 }

When:   UpdateDraftListing { Changes: { BuyItNowPrice: 75.00 } }
        ($75 < $100 reserve → violates BIN >= Reserve)

Then:   throws ValidationException("BuyItNowPrice must be >= ReservePrice")
        (no event produced)
```

### 1.5 Update draft — not in Draft state

```
Given:  Listing is in Published state

When:   UpdateDraftListing { ... }

Then:   throws InvalidListingStateException("Cannot update draft on non-draft listing")
```

---

## 2. Aggregate — Submission and Publication

### 2.1 Submit — happy path (single handler chain)

```
Given:  DraftListingCreated for listing-K with all fields valid
        Platform config: FeePercentage = 10.0

When:   SubmitListing { ListingId: listing-K }

Then:   Stream appends THREE events atomically:
        [
          ListingSubmitted { ListingId: listing-K, SubmittedAt },
          ListingApproved { ListingId: listing-K, ApprovedAt, ApprovedBy: "auto" },
          ListingPublished {
            ListingId: listing-K,
            SellerId: participant-007,
            Title, Description, Format: Flash,
            StartingBid: 50.00, ReservePrice: 100.00, BuyItNowPrice: 200.00,
            FeePercentage: 10.0,    ← read from platform config at this moment
            Duration: null,
            ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow, ExtendedBiddingExtension,
            ShippingTerms,
            PublishedAt
          }
        ]

State:  Status: Published
```

### 2.2 Submit — validation fails

```
Given:  DraftListingCreated with invalid state
        (e.g., Title has been cleared to whitespace via prior update path)

When:   SubmitListing { ListingId: listing-K }

Then:   Stream appends TWO events:
        [
          ListingSubmitted { ListingId: listing-K },
          ListingRejected { ListingId: listing-K, Reason: "Title cannot be empty" }
        ]

State:  Status: Rejected
```

### 2.3 Submit — from Rejected state (re-edit loop)

```
Given:  Events: [DraftListingCreated, ListingSubmitted, ListingRejected]
        Seller has fixed the issue via UpdateDraftListing (back to Draft)
        Events: [...above..., DraftListingUpdated]
        Current aggregate state: Draft (the Rejected → Draft transition is via UpdateDraftListing)

When:   SubmitListing { ListingId: listing-K }

Then:   Same as 2.1 — three events appended, Published state
```

> **Note:** The Rejected → Draft transition happens when the seller updates the rejected listing. The aggregate's Apply(DraftListingUpdated) resets Status to Draft if currently Rejected.

### 2.4 Submit — not in Draft state

```
Given:  Listing is in Published state

When:   SubmitListing { ... }

Then:   throws InvalidListingStateException("Cannot submit non-draft listing")
```

---

## 3. Aggregate — Post-Publication Revision

### 3.1 Revise — mutable fields only

```
Given:  ListingPublished { ListingId: listing-K, Title: "original", Description: "original", ... }
        Aggregate state: Published

When:   ReviseListing {
          ListingId: listing-K,
          Changes: {
            Title: "Updated Title",
            Description: "Updated description",
            ShippingTerms: null  // unchanged
          }
        }

Then:   ListingRevised {
          ListingId: listing-K,
          Title: "Updated Title",
          Description: "Updated description",
          ShippingTerms: null,
          RevisedAt
        }
```

### 3.2 Revise — attempt to change immutable field

```
Given:  ListingPublished for listing-K

When:   ReviseListing {
          ListingId: listing-K,
          Changes: { StartingBid: 75.00 }    // immutable after publish
        }

Then:   throws ValidationException("StartingBid cannot be changed after publish")
        (no event produced)
```

### 3.3 Revise — whitespace-only title rejected

```
Given:  ListingPublished for listing-K

When:   ReviseListing { Changes: { Title: "   " } }

Then:   throws ValidationException("Title cannot be empty")
```

### 3.4 Revise — not in Published state

```
Given:  Listing is in Draft state

When:   ReviseListing { ... }

Then:   throws InvalidListingStateException("Cannot revise non-published listing")
```

---

## 4. Aggregate — End Early and Relist

### 4.1 End listing early — happy path

```
Given:  Events: [..., ListingPublished]
        Aggregate state: Published

When:   EndListingEarly { ListingId: listing-K, Reason: "Discovered defect" }

Then:   ListingEndedEarly {
          ListingId: listing-K,
          SellerId: participant-007,
          Reason: "Discovered defect",
          EndedAt
        }

State:  Status: EndedEarly (terminal)
```

### 4.2 End listing early — not in Published state

```
Given:  Aggregate state: Draft

When:   EndListingEarly { ... }

Then:   throws InvalidListingStateException("Cannot end non-published listing")
```

### 4.3 End listing early — already ended

```
Given:  Aggregate state: EndedEarly

When:   EndListingEarly { ... }

Then:   throws InvalidListingStateException("Listing already ended")
```

### 4.4 Relist marker appended to original stream

```
Given:  listing-K stream ends with ListingEndedEarly
        New listing listing-K2 has been created separately via CreateDraftListing

When:   MarkAsRelisted { OriginalListingId: listing-K, NewListingId: listing-K2 }
        (invoked as part of the relist HTTP flow, after listing-K2 is published)

Then:   listing-K stream appends:
        ListingRelisted {
          OriginalListingId: listing-K,
          NewListingId: listing-K2,
          SellerId: participant-007,
          RelistedAt
        }

State:  listing-K remains in EndedEarly state (ListingRelisted is a marker, not a state change)
```

---

## 5. Validation Rules — Pure Function Tests

Tests against `ListingValidator.Validate(draft)`. All pure functions, no framework.

### 5.1 Valid draft passes

```
Given:  draft with all valid fields

When:   ListingValidator.Validate(draft)

Then:   result.IsValid == true
```

### 5.2 Title required

```
Given:  draft with Title: ""

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "Title is required"
```

### 5.3 Title whitespace-only

```
Given:  draft with Title: "   "

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "Title cannot be empty"
```

### 5.4 Title length limit

```
Given:  draft with Title of 201 characters

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "Title must be at most 200 characters"
```

### 5.5 StartingBid must be positive

```
Given:  draft with StartingBid: 0.00

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "StartingBid must be greater than zero"
```

### 5.6 Reserve below starting bid

```
Given:  draft with StartingBid: 50.00, ReservePrice: 40.00

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "ReservePrice must be >= StartingBid"
```

### 5.7 BIN below reserve — the W003 cross-BC invariant

```
Given:  draft with ReservePrice: 100.00, BuyItNowPrice: 75.00

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "BuyItNowPrice must be >= ReservePrice"
```

> **This is the scenario that closes W003 cross-BC #4.** Settlement's Buy It Now skip-reserve-check behavior now has an upstream guarantee.

### 5.8 BIN equals starting bid (no auction phase)

```
Given:  draft with StartingBid: 50.00, BuyItNowPrice: 50.00

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "BuyItNowPrice must be greater than StartingBid"
```

### 5.9 No reserve (null) is valid

```
Given:  draft with ReservePrice: null, BuyItNowPrice: 200.00

When:   Validate(draft)

Then:   result.IsValid == true
        (BIN >= Reserve is vacuously true when Reserve is null)
```

### 5.10 No BIN (null) is valid

```
Given:  draft with ReservePrice: 100.00, BuyItNowPrice: null

When:   Validate(draft)

Then:   result.IsValid == true
```

### 5.11 Flash format requires null Duration

```
Given:  draft with Format: Flash, Duration: TimeSpan.FromMinutes(5)

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "Flash listings cannot specify a Duration"
```

### 5.12 Timed format requires non-null Duration

```
Given:  draft with Format: Timed, Duration: null

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "Timed listings must specify a Duration"
```

### 5.13 Extended bidding bounds

```
Given:  draft with ExtendedBiddingEnabled: true,
        ExtendedBiddingTriggerWindow: TimeSpan.FromMinutes(5)  // exceeds 2-minute max

When:   Validate(draft)

Then:   result.IsRejection == true
        result.Reason == "ExtendedBiddingTriggerWindow must be <= 2 minutes"
```

### 5.14 Extended bidding disabled ignores window/extension

```
Given:  draft with ExtendedBiddingEnabled: false,
        ExtendedBiddingTriggerWindow: TimeSpan.FromHours(1)  // would be invalid if enabled

When:   Validate(draft)

Then:   result.IsValid == true
        (disabled → window/extension are ignored)
```

---

## 6. RegisteredSellers Projection

Integration tests against Marten with Testcontainers.

### 6.1 SellerRegistrationCompleted creates row

```
Given:  registered_sellers table empty

When:   SellerRegistrationCompleted { ParticipantId: participant-007, CompletedAt: T-2:00 }
        arrives at the projection handler

Then:   registered_sellers contains:
        RegisteredSeller { SellerId: participant-007, RegisteredAt: T-2:00 }
```

### 6.2 Idempotent replay

```
Given:  registered_sellers contains participant-007

When:   SellerRegistrationCompleted { ParticipantId: participant-007, ... } arrives again

Then:   No-op. Row unchanged. No duplicate.
```

### 6.3 Query by SellerId — found

```
Given:  registered_sellers contains participant-007

When:   RegisteredSellers.Exists(participant-007)

Then:   returns true
```

### 6.4 Query by SellerId — not found

```
Given:  registered_sellers does NOT contain participant-999

When:   RegisteredSellers.Exists(participant-999)

Then:   returns false
```

---

## 7. API Gateway Cross-BC Checks

HTTP-level tests against the API host. These verify the "API gateway pattern" surfaced in Phase 2.

### 7.1 Create draft — seller registered

```
Given:  RegisteredSellers projection contains participant-007

When:   POST /api/listings/draft with SellerId: participant-007

Then:   HTTP 201 Created
        Command CreateDraftListing routed to Selling BC
        Scenario 1.1 executes
```

### 7.2 Create draft — seller not registered

```
Given:  RegisteredSellers projection does NOT contain participant-999

When:   POST /api/listings/draft with SellerId: participant-999

Then:   HTTP 403 Forbidden
        { error: "Seller is not registered" }
        Command NOT routed to Selling BC
```

### 7.3 End early — listing exists and is biddable

```
Given:  listing-K is Published and session has not started
        (Auctions query returns: status = "awaiting session start")

When:   POST /api/listings/listing-K/end-early with { reason: "..." }

Then:   HTTP 200 OK
        Command EndListingEarly routed to Selling BC
        Scenario 4.1 executes
```

### 7.4 End early — listing already sold (BIN purchased)

```
Given:  listing-K had BuyItNowPurchased fire
        Auctions query returns: status = "resolved (BIN purchased)"

When:   POST /api/listings/listing-K/end-early

Then:   HTTP 409 Conflict
        { error: "Cannot end listing: already sold via Buy It Now" }
        Command NOT routed to Selling BC
```

### 7.5 End early — listing already sold (normal close)

```
Given:  listing-K had ListingSold fire via normal bidding close
        Auctions query returns: status = "resolved (sold)"

When:   POST /api/listings/listing-K/end-early

Then:   HTTP 409 Conflict
        { error: "Cannot end listing: already sold" }
```

### 7.6 End early — listing already ended

```
Given:  listing-K is in EndedEarly state

When:   POST /api/listings/listing-K/end-early

Then:   HTTP 409 Conflict
        { error: "Listing already ended" }
```

---

## Scenario Coverage Summary

| Section | Component | Scenarios | Notes |
|---|---|---|---|
| 1 | Draft lifecycle | 5 | Marten aggregate tests |
| 2 | Submission & publication | 4 | Aggregate tests, includes atomic 3-event chain |
| 3 | Post-publication revision | 4 | Aggregate tests |
| 4 | End early & relist | 4 | Aggregate tests, terminal state verification |
| 5 | Validation rules | 14 | Pure function tests |
| 6 | RegisteredSellers projection | 4 | Marten integration tests |
| 7 | API gateway cross-BC checks | 6 | HTTP-level tests |
| **Total** | | **41** | |

**Distribution:** 17 aggregate tests (Marten harness), 14 pure function tests (no framework), 4 projection integration tests, 6 HTTP-level tests.

### Scenarios That Resolved Workshop 004 Questions

| Question | Resolved By |
|---|---|
| W001 #14 (automated approval chain) | 2.1 (three events appended atomically) |
| W001 #1 (listing UI before session) | Not directly tested here — a Listings BC projection concern |
| W003 cross-BC #4 (BIN >= Reserve) | 5.7 |
| Phase 1 #5 (post-publish revision rules) | 3.1 (allowed), 3.2 (rejected) |
| Phase 2 #1 (explicit save) | Implicit in 1.3 — each save fires one event |
| Phase 2 #2 (seller registration race) | 1.2 (throws), 6.1–6.4 (projection), 7.2 (API-layer check) |
| Phase 2 #6 (mid-session revision) | Not directly tested here — a Listings BC projection concern |

### Remaining Open Questions

| # | Question | Target |
|---|----------|--------|
| Phase 1 #3 | Platform config location | Implementation detail, MVP uses `appsettings.json` |
| Phase 1 #4 | Image/media handling scope | Post-MVP or sub-workshop |
| Phase 2 #7 | Seller UX for finding relisted versions | Listings/Frontend workshop |
| Phase 2 #8 | Publish notification via Relay or HTTP 200 sufficient? | Relay workshop |
| Phase 2 #9 | Is RegisteredSellers the only Selling projection? | Likely yes; confirmed during implementation |
