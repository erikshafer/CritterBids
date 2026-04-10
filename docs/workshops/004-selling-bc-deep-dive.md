# Workshop 004 — Selling BC Deep Dive

**Type:** BC-Focused (vertical depth, upstream)
**Date started:** 2026-04-09
**Status:** In progress — Phase 1

**Scope:** The Selling BC internals. The `SellerListing` aggregate state machine, automated approval, validation invariants (including those flagged by downstream BCs), revision rules, and the `ListingPublished` payload contract that everything downstream depends on.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. `@ProductOwner` on standby. `@UX` consulted on the listing-before-session-starts question.

**Prerequisites:** Workshops 001, 002, and 003 completed. This workshop addresses parked questions targeting Selling BC and resolves the cross-BC invariant flagged by Workshop 003.

**Why upstream now:** Selling sits at the head of the dependency chain. Everything starts with `ListingPublished`. We've designed downstream BCs (Auctions, Settlement) against assumed properties of that event — it's time to verify those assumptions hold and lock down the contract before milestone scoping work begins.

**Parked questions from prior workshops targeting this BC:**

| # | Source | Question | Persona |
|---|--------|----------|---------|
| 1 | W001 | Listing UI before session starts? | `@UX` |
| 14 | W001 | Automated listing approval — single handler chain or separate steps? | `@BackendDeveloper` |
| Cross-BC #4 | W003 | `BuyItNowPrice >= ReservePrice` invariant — must be enforced at listing creation in Selling | `@Architect` |

**Implicit downstream contracts to lock down:**

- W003 established that the `PendingSettlement` projection captures `FeePercentage` at `ListingPublished` time. This workshop must clarify *where* that read happens — is it Selling's responsibility (carry it on the event) or Settlement's (read platform config when projecting the event)? The W003 decision pointed to Settlement's projection handler reading platform config, but we should ratify that here from Selling's side.
- W002 established that `BiddingOpened` carries the listing config (extended bidding window, extension amount, BIN price, etc.). That config originates in `ListingPublished`. The full payload contract needs to be enumerated.
- W003 established that `ListingPublished` carries the reserve value to Settlement. The reserve is confidential — Selling needs to be deliberate about which downstream BCs receive it.

---

## What Prior Workshops Established

From the vision docs and earlier workshops, Selling has:

**Storage:** PostgreSQL via Marten — event-sourced `SellerListing` aggregate.

**Lifecycle:** `Draft → Submitted → Approved → Published` (with `Rejected → Draft` loop). Plus post-publish actions: `Revised`, `EndedEarly`, `Relisted`, `Withdrawn` (the last is ops-initiated and crosses into Auctions BC for force-close, but the listing's catalog status is also affected).

**Events produced:**

| Event | Type | Notes |
|---|---|---|
| `DraftListingCreated` | 🟠 Internal | Seller starts a new listing |
| `DraftListingUpdated` | 🟠 Internal | Seller edits a draft |
| `ListingSubmitted` | 🟠 Internal | Seller submits for approval |
| `ListingApproved` | 🟠 Internal | Approval (automated in MVP) |
| `ListingRejected` | 🟠 Internal | Approval rejection |
| `ListingPublished` | 🔵 Integration | The big one — fan-out to Auctions, Listings, Settlement, Relay, Operations |
| `ListingRevised` | 🔵 Integration | Post-publish edit |
| `ListingEndedEarly` | 🔵 Integration | Seller-initiated early termination |
| `ListingRelisted` | 🔵 Integration | Passed/ended listing brought back |

**Slices assigned in W001:** 1.1 (Create draft), 1.2 (Submit and publish). Both P0.

**Key established design points:**

- Sellers own all listing parameters: starting bid, reserve (confidential), Buy It Now price, duration, extended bidding toggle and configuration, shipping terms.
- Reserve is opaque downstream — only Settlement and the DCB threshold check in Auctions see the value (and only as a comparison number, not a confidential price).
- Flash listings inherit duration from the Session, not from a seller-chosen value. This creates an interesting wrinkle for the listing's own duration field.

---

## Phase 1 — Brain Dump: Internal Structure

The Selling BC is the **only producer of new business intent** on the upstream side. Participants and Auctions create lifecycle data, Settlement and Obligations resolve it. Selling is where a human seller's decisions enter the system. That makes it the most validation-heavy BC and the most exposed to "bad input" scenarios.

The good news is that Selling is structurally simple compared to Auctions or Settlement. There's one aggregate, one lifecycle, one validation pass at submission, and a small number of post-publish mutations. Most of the design tension lives in **getting the validation rules right** and **getting the published payload right** — because once `ListingPublished` fires, downstream BCs commit to that data and there's no taking it back.

### Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│ Selling BC                                                   │
│                                                              │
│  ┌─────────────────────────────────────────────────┐         │
│  │ SellerListing Aggregate                          │         │
│  │ (Marten event-sourced, one stream per listing)   │         │
│  │                                                  │         │
│  │ Draft → Submitted → Approved → Published         │         │
│  │              ↘ Rejected → Draft (re-edit)         │         │
│  │                                                  │         │
│  │ Published lifecycle:                              │         │
│  │   → Revised (still Published)                     │         │
│  │   → EndedEarly (terminal)                         │         │
│  │   → Relisted (back to Published)                  │         │
│  └────────────────────┬────────────────────────────┘          │
│                       │                                       │
│                       │ ListingPublished                      │
│                       ▼                                       │
│  ┌─────────────────────────────────────────────────┐          │
│  │ Validation Service                               │          │
│  │ (pure functions, no I/O)                         │          │
│  │ - Required fields                                │          │
│  │ - Numeric invariants (BIN >= Reserve, etc.)      │          │
│  │ - Format validation (title length, etc.)         │          │
│  │ - Cross-field rules                              │          │
│  └─────────────────────────────────────────────────┘          │
│                                                                │
└────────────────────────────────────────────────────────────────┘
```

There is no saga, no projection (Selling is a pure write-side BC — its read models live in Listings BC), and no DCB. Just an event-sourced aggregate with validation rules.

---

### Part 1: The `SellerListing` Aggregate

**`@Architect` — aggregate state:**

```csharp
public sealed class SellerListing
{
    public Guid Id { get; private set; }                  // = ListingId
    public Guid SellerId { get; private set; }
    public ListingStatus Status { get; private set; }

    // Seller-configured parameters
    public string Title { get; private set; }
    public string Description { get; private set; }
    public decimal StartingBid { get; private set; }
    public decimal? ReservePrice { get; private set; }    // confidential
    public decimal? BuyItNowPrice { get; private set; }
    public TimeSpan? Duration { get; private set; }       // null for Flash listings
    public bool ExtendedBiddingEnabled { get; private set; }
    public TimeSpan ExtendedBiddingTriggerWindow { get; private set; }
    public TimeSpan ExtendedBiddingExtension { get; private set; }
    public ShippingTerms ShippingTerms { get; private set; }

    // Audit
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    // Apply methods (event-sourced fold)
    public void Apply(DraftListingCreated e) { ... }
    public void Apply(DraftListingUpdated e) { ... }
    public void Apply(ListingSubmitted e) { ... }
    public void Apply(ListingApproved e) { ... }
    public void Apply(ListingRejected e) { ... }
    public void Apply(ListingPublished e) { ... }
    public void Apply(ListingRevised e) { ... }
    public void Apply(ListingEndedEarly e) { ... }
    public void Apply(ListingRelisted e) { ... }
}

public enum ListingStatus
{
    Draft,
    Submitted,
    Approved,         // very brief — typically followed immediately by Published in MVP
    Rejected,
    Published,
    EndedEarly
}
```

**State machine:**

```
        ┌─────────────────────────────────────────────────────┐
        │                                                     │
        ▼                                                     │
    Draft ──[SubmitListing]──► Submitted                       │
                                  │                            │
                                  ├──[approve]──► Approved      │
                                  │                  │         │
                                  │                  └──[publish]──► Published
                                  │                                      │
                                  └──[reject]──► Rejected                 │
                                                  │                      │
                                                  └──────[edit]──────────┤
                                                                         │
                                                                         ▼
                                                            [RevisePublished] (stay Published)
                                                            [EndEarly] → EndedEarly (terminal)
                                                            [Relist (after EndedEarly)] → Published
```

**`@DomainExpert` note on the Approved state:** In MVP, `Approved` is essentially a transient state. The automated approval chain runs `ListingSubmitted → ListingApproved → ListingPublished` in a single handler invocation. The `Approved` state only exists between the moment of approval and the moment of publication. **Why does it exist as a distinct state at all?** Because it's a real business concept and post-MVP will likely separate them (manual approval queue, scheduled publication, embargoed listings). Modeling it as a real state now means post-MVP doesn't require event vocabulary changes — only the handler chain changes.

---

### Part 2: Automated Approval — Parked W001 #14

**`@BackendDeveloper`** — The W001 question: when a seller submits a listing in MVP, do `ListingSubmitted`, `ListingApproved`, and `ListingPublished` get produced as a single chain in one handler invocation, or as separate handlers chaining via Wolverine messages?

**Option A: Single handler chain.**

```csharp
public static IEnumerable<object> Handle(SubmitListing command, SellerListing state)
{
    // Validate
    var validationResult = ListingValidator.Validate(state);
    if (validationResult.IsRejection)
    {
        yield return new ListingSubmitted(state.Id, ...);
        yield return new ListingRejected(state.Id, validationResult.Reason, ...);
        yield break;
    }

    // Auto-approve and publish atomically
    yield return new ListingSubmitted(state.Id, ...);
    yield return new ListingApproved(state.Id, ...);
    yield return new ListingPublished(state.Id, /* full payload */, ...);
}
```

All three (or two, on rejection) events are appended to the listing's stream in a single transaction. Atomic. Simple. No inter-handler messaging.

**Option B: Separate handlers chained via Wolverine messages.**

```csharp
public static OutgoingMessages Handle(SubmitListing command, SellerListing state)
{
    var validationResult = ListingValidator.Validate(state);
    var submitted = new ListingSubmitted(state.Id, ...);

    return validationResult.IsRejection
        ? [submitted, new ListingRejected(state.Id, validationResult.Reason, ...)]
        : [submitted, new ApproveListing(state.Id)];  // self-send next command
}

public static OutgoingMessages Handle(ApproveListing command, SellerListing state)
{
    return [new ListingApproved(state.Id, ...), new PublishListing(state.Id)];
}

public static OutgoingMessages Handle(PublishListing command, SellerListing state)
{
    return [new ListingPublished(state.Id, /* full payload */, ...)];
}
```

Three handlers, three messages, three transactions.

**`@Architect` analysis:**

The downstream consequences differ in subtle ways:

- **Atomicity:** Option A guarantees that if `ListingSubmitted` is committed, `ListingApproved` and `ListingPublished` are also committed (or none are, on transaction rollback). Option B has three separate transactions — a crash between `ListingSubmitted` and `ListingApproved` means the listing is submitted but not yet approved, requiring Wolverine's inbox/outbox to recover.
- **Observability:** Option B writes three durable message records (one per command). Option A writes one. Option B is easier to inspect and debug because each step is a queryable message.
- **Testability:** Option A is one handler with three event outputs — easy to assert. Option B is three handlers, each independently testable in isolation but requiring orchestration to test the full chain.
- **Future flexibility:** Option B is the more honest model for what post-MVP becomes — when approval becomes manual, the `ApproveListing` command will be triggered by a staff action instead of self-sent. The seam is already in place.
- **Performance:** Option A is faster (one round trip to the DB). Option B has more overhead but is well within MVP scale.

**`@BackendDeveloper` recommendation:** Option A for MVP. Atomicity is the dominant concern when the goal is "the listing is published, full stop." The post-MVP migration to Option B requires changing the handler implementation but no event vocabulary changes — same three events, same downstream contracts. The listing stream looks identical.

The key trade-off accepted: in MVP, you cannot inspect "the listing is currently in Approved state but not yet Published" because that state never exists in practice (it's a transient zero-duration step inside a handler). When manual approval lands post-MVP, that state becomes meaningful and inspectable.

> **Decision: Option A adopted for MVP.** Single handler chain. `SubmitListing` produces `ListingSubmitted + ListingApproved + ListingPublished` atomically (or `ListingSubmitted + ListingRejected` on validation failure). Post-MVP migration to manual approval changes the handler implementation but not the event vocabulary. **W001 #14 resolved.**

---

### Part 3: Validation Invariants — Where the Cross-BC Flag Lands

**`@Architect`** — W003 flagged that `BuyItNowPrice >= ReservePrice` must be enforced at listing creation. Let's enumerate all the validation rules.

**Required fields:**

- `Title` — non-empty, max 200 characters
- `Description` — max 5000 characters (empty allowed)
- `StartingBid` — must be > 0
- `SellerId` — must reference a registered seller (verified at command-handler level via Participants integration)
- `Duration` — required for non-Flash listings; must be ignored/null for Flash listings

**Numeric invariants:**

- `StartingBid > 0`
- `ReservePrice > 0` if set; null is valid (no reserve)
- `BuyItNowPrice > 0` if set; null is valid (no BIN)
- **`ReservePrice >= StartingBid`** if both are set — a reserve below the starting bid is nonsensical because the starting bid must be exceeded for any sale to happen
- **`BuyItNowPrice >= ReservePrice`** if both are set — the W003 flag. Setting BIN below reserve creates the "buyer bypasses reserve at a price the seller wouldn't have accepted via bidding" disaster scenario.
- **`BuyItNowPrice > StartingBid`** if BIN is set — buying at BIN should be a premium over starting at the floor; if BIN equals starting bid, the listing has no auction phase.

**Extended bidding rules:**

- If `ExtendedBiddingEnabled = false`, the trigger window and extension fields are ignored
- If enabled: `ExtendedBiddingTriggerWindow > 0` and `ExtendedBiddingExtension > 0`
- Reasonable bounds: trigger window ≤ 2 minutes, extension ≤ 2 minutes (prevents pathological configurations)

**Format rules:**

- `Title` cannot be only whitespace
- `Title` cannot contain control characters

**`@QA` — the BIN >= Reserve question in detail:**

W003 raised this because Settlement's Buy It Now path explicitly skips the reserve check. The seller's BIN price is treated as the agreed price, regardless of reserve. If Selling allowed `BIN < Reserve`, a buyer hitting BIN would commit the seller to a sale at less than their stated reserve — a business disaster.

There are three possible defensive postures:

**Option A: Reject `BIN < Reserve` at listing creation/submission.** The validator refuses to accept the listing. Seller must adjust before submitting. Simplest, safest, prevents the bad state from ever existing.

**Option B: Allow it but warn the seller.** UI shows a warning at submission time but allows the seller to confirm. Honors seller intent but creates a class of listings where downstream behavior is "weird." Defensive code in Settlement still has to assume this can happen.

**Option C: Allow it silently and let Settlement honor BIN.** No validation at all. Settlement's existing design (BIN > Reserve check is skipped) handles this correctly. But it's a footgun.

**`@DomainExpert` perspective:** On eBay, BIN is generally constrained to be ≥ reserve at listing creation. The platform enforces it because the alternative is constant seller complaints ("I lost money on a BIN sale"). For CritterBids, the principle is the same — sellers will not understand the implication of BIN < Reserve, and Settlement honoring the lower BIN price will produce surprised, angry sellers.

> **Decision: Option A adopted.** `BuyItNowPrice >= ReservePrice` is enforced at listing creation/submission. The validator rejects any submission where this invariant is violated. **W003 cross-BC #4 resolved.** Settlement's Buy It Now skip-reserve-check behavior is now backed by an upstream guarantee — Settlement can trust that any BIN price it sees was at least equal to the reserve when the listing was published.

**`@Architect` note on revision:** This invariant must also hold across revisions. If a seller revises a published listing and tries to lower BIN below the (still confidential) reserve, the revision must be rejected. See Part 5 (Revision Rules).

---

### Part 4: The `ListingPublished` Payload Contract

This is the load-bearing event for the entire system. Everything downstream depends on it. Let's enumerate exactly what fields it carries.

**`@BackendDeveloper`** — proposed event shape:

```csharp
public sealed record ListingPublished(
    Guid ListingId,                              // first property — convention
    Guid SellerId,
    string Title,
    string Description,
    decimal StartingBid,
    decimal? ReservePrice,                       // confidential — see consumer notes
    decimal? BuyItNowPrice,
    TimeSpan? Duration,                          // null for Flash listings
    bool ExtendedBiddingEnabled,
    TimeSpan ExtendedBiddingTriggerWindow,
    TimeSpan ExtendedBiddingExtension,
    ShippingTerms ShippingTerms,
    DateTimeOffset PublishedAt
);
```

**Consumer-by-consumer field usage:**

| BC | Fields it reads | Notes |
|---|---|---|
| **Listings** | All except `ReservePrice` | Catalog projection. Reserve is confidential. |
| **Auctions** | All including `ReservePrice` (as opaque threshold) | Used by DCB to compute `ReserveMet`. Stored in `BidConsistencyState` but never exposed to UX. |
| **Settlement** | `ReservePrice`, `SellerId`, `BuyItNowPrice` | Cached in `PendingSettlement` projection. |
| **Relay** | `Title`, `SellerId`, `PublishedAt` | "Your listing is live" notification to seller. |
| **Operations** | All fields | Ops dashboard "newly published" feed. |

**`@Architect`** — the reserve-confidentiality concern: how do we prevent the reserve from leaking to participants?

The reserve appears on `ListingPublished` as a regular field. Auctions, Settlement, Operations all see it. The risk is Listings — if the catalog projection accidentally projects the reserve into a public read model, participants can see it.

**Defense in depth:**

1. **Listings BC's projection handler explicitly does not read `ReservePrice`.** It carries `HasReserve: bool` (true if `ReservePrice != null`) but never the value.
2. **The integration event itself cannot be partitioned per consumer** — it's published once on the bus, all subscribers receive the same payload. Confidentiality is enforced by consumer discipline, not by the message shape.
3. **Code review and tests must verify** that no Listings projection writes `ReservePrice` into a queryable field.

This is a discipline-enforced rule rather than a structurally-enforced one. Acceptable for MVP because the consumer count is small and the rule is clear.

**`@QA` question:** Should there be a separate `ListingReservePublished` event consumed only by Settlement and Auctions, while `ListingPublished` carries everything else? That would structurally enforce confidentiality.

**`@BackendDeveloper` response:** Two events instead of one introduces ordering concerns (does `ListingPublished` arrive before `ListingReservePublished`?), partial-failure scenarios (what if one is delivered and the other isn't?), and doubles the bus traffic. The discipline approach is simpler and the leak risk is low at MVP scale. Reconsider post-MVP if security requirements tighten.

> **Decision: Single `ListingPublished` event carrying `ReservePrice` as a regular nullable field.** Confidentiality is enforced by consumer-side discipline. Listings BC projection MUST NOT read `ReservePrice`. Code review and tests verify. Post-MVP can revisit if a real adversarial threat model emerges.

**`@ProductOwner` — resolving the `FeePercentage` question:**

W003 left open whether `FeePercentage` is read at publish time by Selling or by Settlement. Two arguments:

**Argument A (Selling reads it, carries it on `ListingPublished`):** The fee is part of the listing contract — what the seller agreed to. Carrying it on the event makes the contract explicit and immutable. Settlement's projection just copies the value from the event.

**Argument B (Settlement reads it from platform config when projecting):** Selling shouldn't need to know about platform fees. It's a Settlement concern. The fee is read from config at projection time and stored in `PendingSettlement`.

W003 leaned toward Argument B because it kept Selling clean. But there's a wrinkle: what if platform config changes between `ListingPublished` being emitted and Settlement's projection processing it? Argument B has a tiny race window where the projection could read a different fee than was in effect at publish time.

**`@Architect` reconsideration:** This race is real but small (milliseconds in normal operation). The cleaner architecture is Argument A — Selling reads platform config at publish time and bakes the fee into `ListingPublished`. This makes the event self-describing, eliminates the race, and gives sellers a clear "you agreed to a 10% fee" record. The tradeoff is Selling now has a dependency on platform fee config, which W003 wanted to avoid.

> **Decision (refining W003): `FeePercentage` IS carried on `ListingPublished`.** Selling reads platform config at publish time and includes the fee in the event. Settlement's projection copies it from the event into `PendingSettlement` — no platform config read in the projection handler. This eliminates the race condition and makes the listing's fee terms self-documenting.
>
> **Update to `ListingPublished` payload:**

```csharp
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    string Description,
    decimal StartingBid,
    decimal? ReservePrice,
    decimal? BuyItNowPrice,
    decimal FeePercentage,                       // NEW — read from platform config at publish time
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    TimeSpan ExtendedBiddingTriggerWindow,
    TimeSpan ExtendedBiddingExtension,
    ShippingTerms ShippingTerms,
    DateTimeOffset PublishedAt
);
```

**This is a vocabulary refinement that ripples back to W003.** The `PendingSettlement` projection scenarios from W003 (8.1, 8.2) need to be updated: instead of "handler reads platform config: FeePercentage = 10.0", they should read "handler reads `e.FeePercentage` from the event payload." The behavior is identical, the source is cleaner. The "fee is fixed at publish time" guarantee from W003 becomes structurally enforced rather than convention-enforced. **Flag for vocabulary pass.**

---

### Part 5: Revision Rules — Which Fields Are Mutable?

Sellers can revise published listings. But not all fields can be safely changed once bidding is in progress. What's the rule set?

**`@QA`** — let's go field by field:

| Field | Mutable when? | Why |
|---|---|---|
| `Title` | Always (until EndedEarly/sold) | Cosmetic |
| `Description` | Always | Cosmetic |
| `StartingBid` | Only before any bids | Bids are placed against the floor — changing it after bids exist invalidates the floor-comparison invariant |
| `ReservePrice` | Only **downward** before reserve is met | Lowering reserve is seller-favorable to buyers; raising it is hostile (buyers thought they were close to the threshold). After reserve is met, locked. |
| `BuyItNowPrice` | Only before any bids; subject to BIN >= Reserve | First bid removes BIN; revising after first bid is meaningless |
| `Duration` | Only before bidding opens | Once bidding is open, the close time is committed |
| `ExtendedBiddingEnabled` | Only before bidding opens | Once bidding is open, the saga is configured |
| `ExtendedBiddingTriggerWindow` | Only before bidding opens | Saga is configured |
| `ExtendedBiddingExtension` | Only before bidding opens | Saga is configured |
| `ShippingTerms` | Always (until EndedEarly/sold) | Updates obligation expectations but doesn't affect bidding |
| `FeePercentage` | **Never** | Locked at publish time |

**`@DomainExpert` note:** This rule set is much more restrictive than what eBay allows in some cases (eBay lets sellers add to descriptions but not edit them post-bid). For CritterBids' MVP, the simpler rule is "if changing it could affect a bidder's decision retroactively, it's locked once bids exist."

**`@Architect` question:** How does Selling know the listing has bids? It's an Auctions concern.

Two options:

**Option A:** Selling subscribes to `BidPlaced` events and tracks "has bids" on its aggregate. State pollution — Selling shouldn't care about Auctions internals.

**Option B:** The revision command takes a "current bidding state snapshot" parameter passed by the API gateway. The gateway queries Auctions before issuing the revise command. Adds an inter-BC query.

**Option C:** Revision is restrictive by default. Only the always-mutable fields (Title, Description, ShippingTerms) can be revised once `ListingPublished` has been emitted. The seller cannot revise StartingBid, ReservePrice, BIN, or extended bidding config at all post-publish — they have to end the listing early and relist if they need to change those.

> **Decision: Option C adopted.** Post-publish revision is restricted to **Title, Description, and ShippingTerms only**. All other parameters are immutable post-publish. Sellers who need to change critical parameters must `EndListingEarly` and `Relist` (which creates a new listing with a new ListingId). This sidesteps the "does Selling know about bids?" question entirely and gives sellers a clear mental model.
>
> **`@ProductOwner` note:** This is more restrictive than commercial auction sites but appropriate for MVP. Post-MVP can add field-by-field revision rules with cross-BC state checks if user feedback demands it.

**`@QA`** — implications for `ListingRevised` event shape:

```csharp
public sealed record ListingRevised(
    Guid ListingId,
    Guid SellerId,
    string? Title,                  // null = unchanged
    string? Description,            // null = unchanged
    ShippingTerms? ShippingTerms,   // null = unchanged
    DateTimeOffset RevisedAt
);
```

Only the three mutable fields. Downstream consumers (Listings, Settlement, Relay) update only what changed.

**Settlement implication:** W003's PendingSettlement projection has a `ListingRevised` handler that updates `BuyItNowPrice` and `ReservePrice`. **That's now wrong** — those fields are no longer mutable post-publish. The W003 scenario (8.3) needs revision: `ListingRevised` only updates fields that the projection cares about (which now is *no fields* in PendingSettlement, since PendingSettlement doesn't track Title, Description, or ShippingTerms).

**Flag for vocabulary/W003 pass.** The W003 PendingSettlement scenarios need updating to reflect this restriction.

---

### Part 6: End Early, Relist, and the Full Lifecycle

**`@DomainExpert`** — what happens when a seller ends a listing early?

```csharp
public sealed record ListingEndedEarly(
    Guid ListingId,
    Guid SellerId,
    string Reason,                   // free-text seller reason
    DateTimeOffset EndedAt
);
```

Three downstream consequences:

1. **Auctions BC** receives `ListingEndedEarly` and treats it similarly to `ListingWithdrawn` — terminates the Auction Closing saga and any active Proxy Bid Manager sagas. **But there's a subtle distinction:** `ListingEndedEarly` is seller-initiated; `ListingWithdrawn` is ops-initiated. They're different events because the audit trail and downstream notifications differ.
2. **Listings BC** marks the catalog entry as "Ended" (distinct from "Sold" or "Passed").
3. **Relay** notifies any participants who had bids active or had the listing watchlisted.

**`@QA` question:** Can a seller end a listing early after bids have been placed?

Two business postures:

**Posture A: Yes, but display all current bidders' bids as "withdrawn" with no consequence.** Sellers can panic-pull a listing.

**Posture B: No, once bids exist, the seller is committed.** Sellers must wait for the listing to close naturally.

**`@DomainExpert`:** eBay generally allows ending early but actively discourages it (penalties for sellers who do it frequently). For CritterBids MVP, simplicity wins.

> **Decision: Sellers can end early at any time before listing close.** No restrictions in MVP. Bids on an ended-early listing simply do not result in a sale — same effect as `ListingPassed` from a settlement perspective (no money moves). Post-MVP can add seller penalties or restrictions if needed.

**`@QA`** — the relist flow:

```csharp
public sealed record ListingRelisted(
    Guid OriginalListingId,         // the listing being relisted
    Guid NewListingId,              // the new listing's id
    Guid SellerId,
    DateTimeOffset RelistedAt
);
```

**Critical question:** Does relisting create a new aggregate (new stream, new ID) or extend the existing one?

**Option A: New aggregate.** A relisted listing is a brand new `SellerListing` with its own stream. The old listing remains in `EndedEarly` or `Passed` state, immutable. The `ListingRelisted` event lives on the *original* listing's stream as a marker, and a fresh `ListingPublished` (with the same parameters, copied) starts the new listing's stream.

**Option B: Extend existing aggregate.** A relisted listing reuses the original ListingId. New events are appended to the same stream. Auctions and Listings see the same ID for "round 1" and "round 2."

**`@Architect`:** Option A is dramatically cleaner. The whole point of event sourcing is "facts that happened are immutable." A relisted listing is genuinely a new auction event with a new lifecycle — pretending it's a continuation of the old one creates ID-collision headaches in Auctions (two `BiddingOpened` events for the same ListingId?), Listings (does the catalog show "round 1" and "round 2" or merge them?), and Settlement (two `PendingSettlement` rows for the same ListingId? merged how?).

> **Decision: Option A — relisting creates a new aggregate with a new ListingId.** The original listing's stream contains a `ListingRelisted` marker pointing to the new ListingId. The new listing's stream starts with `DraftListingCreated → ListingSubmitted → ListingApproved → ListingPublished`, with parameters copied from the original. The two listings are linked by the marker but otherwise independent.

**`@DomainExpert` note:** This means the seller's "relist" UX is actually "pre-fill a new draft from this passed listing." That matches the user mental model — clicking "relist" puts you in a familiar draft-edit flow with all your previous values, ready to tweak before submitting.

---

### Part 7: Flash Listings vs Timed Listings — The Duration Wrinkle

**`@QA`** — surfaced earlier: Flash listings inherit duration from the Session, not from the seller. How does that work mechanically?

Two cases:

**Case 1: Timed listings (eBay-style, days-long).** Seller picks a duration when creating the draft. `Duration` is a required field. The `ListingPublished` event carries it. Auctions BC's handler that processes `ListingPublished` for timed listings schedules a `BiddingOpened` event immediately with `ScheduledCloseAt = PublishedAt + Duration`.

**Case 2: Flash listings (CritterBids' demo format).** The listing is created as a draft, then attached to a Session. The Session has its own `DurationMinutes`. When `SessionStarted` fires, the handler that produces `BiddingOpened` per attached listing computes `ScheduledCloseAt = SessionStartedAt + Session.DurationMinutes`. The listing's own `Duration` field is **ignored** for Flash listings.

**`@BackendDeveloper`** — should `Duration` be required, optional, or split into two fields?

**Option A:** `Duration` is nullable. Sellers creating timed listings must set it; Flash listings leave it null. Validation rule: `Duration is required IF the listing is not destined for a Session`. But Selling doesn't know whether the listing will be session-attached!

**Option B:** Distinguish at draft creation: `ListingFormat` enum (`Timed | Flash`). Timed listings require `Duration`; Flash listings forbid it (or ignore it).

**Option C:** Always require `Duration` (timed listings need it, Flash listings just ignore it downstream). Wasteful but eliminates the format-specific validation.

> **Decision: Option B adopted.** A `ListingFormat` field is part of the draft. `Timed` requires `Duration`; `Flash` requires `Duration == null` and signals that the listing must be session-attached before bidding can open. The validator enforces this. The `ListingPublished` event includes `ListingFormat` so downstream consumers can branch on it.
>
> **Updated `ListingPublished` payload (additional field):**

```csharp
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    string Description,
    ListingFormat Format,            // NEW — Timed | Flash
    decimal StartingBid,
    decimal? ReservePrice,
    decimal? BuyItNowPrice,
    decimal FeePercentage,
    TimeSpan? Duration,              // required for Timed, null for Flash
    bool ExtendedBiddingEnabled,
    TimeSpan ExtendedBiddingTriggerWindow,
    TimeSpan ExtendedBiddingExtension,
    ShippingTerms ShippingTerms,
    DateTimeOffset PublishedAt
);

public enum ListingFormat
{
    Timed,
    Flash
}
```

**`@Architect` flag:** This is **another vocabulary refinement that affects W001 and W002**. Workshop 001 didn't distinguish between timed and Flash listings explicitly — it just walked the demo journey, which is exclusively Flash. The slice tables in W001 are fine because the Flash path is the MVP path. But the framing should be updated to acknowledge Timed listings exist as a future format and the design accommodates them. Workshop 002's `BiddingOpened` event should also carry `ListingFormat` for completeness, though the Auctions BC behavior is identical regardless. **Flag for vocabulary pass.**

---

### Part 8: The "Listing UI Before Session Starts" Question (W001 #1)

**`@UX` consulted:** W001 parked the question of what participants see for listings that are published but not yet in an active session.

There are three states a Flash listing can be in from the participant's perspective:

1. **Published, not yet attached to a session.** The listing exists in the catalog but cannot be bid on. Should it even be visible?
2. **Published, attached to an upcoming session.** The listing is visible with a "starts at [time]" indicator. No bidding yet.
3. **Published, session is live.** Bidding is open. This is the well-understood state.

**`@DomainExpert`** — for the conference demo, listings exist briefly before the session starts. The Flash format means listings are typically created and published just minutes before the session begins. The "published but not attached" state lasts seconds.

**`@UX` recommendation:** For MVP, hide listings from the participant catalog until they're attached to a session. The catalog is curated by the act of attachment. The "starts at" preview state is valuable post-MVP for marketing the upcoming session, but in MVP the participant catalog only shows listings that are part of an active or imminent session.

**`@Architect` implication:** The Listings BC's catalog projection needs to know whether a listing is attached to a session. It already receives `ListingAttachedToSession` from Auctions, so this is a projection rule rather than a new event. The `CatalogListingView` carries a `Status` field that becomes `Visible` when `ListingAttachedToSession` fires for a listing in a session that hasn't ended yet, and `Hidden` otherwise.

> **Decision: Listings are hidden from the participant catalog until `ListingAttachedToSession` fires.** This is a Listings BC projection rule, not a Selling BC concern, but it surfaces here because the question was originally parked against "what happens between Selling publishing and Auctions opening bidding." The answer: in the participant catalog, nothing is visible. In the ops dashboard (Operations BC), all published listings are visible immediately because ops staff need to see what's available to attach to sessions.
>
> **W001 #1 resolved.** This is a Listings/Operations BC concern flagged for the appropriate workshops, but the answer is captured here because it was a parked question.

---

## Phase 1 Summary

**Vocabulary changes (significant — affects multiple prior workshops):**

1. **`ListingPublished` adds `FeePercentage` field.** Read from platform config at publish time. Eliminates the race condition flagged in W003. Updates Settlement's `PendingSettlement` projection: it now copies the value from the event instead of reading platform config.
2. **`ListingPublished` adds `Format` field** (`ListingFormat` enum: `Timed | Flash`). Distinguishes the two listing formats. Affects W001 framing and W002's `BiddingOpened` event.
3. **`ListingRevised` payload restricted** to mutable fields only (`Title`, `Description`, `ShippingTerms`). All other parameters become immutable post-publish. Updates W003's `PendingSettlement` projection — the `ListingRevised` handler is now effectively a no-op because no PendingSettlement fields are mutable.
4. **`ListingRelisted` event shape clarified** to carry `OriginalListingId` and `NewListingId` — relisting creates a new aggregate, not a continuation.
5. **New enum: `ListingFormat`** (`Timed | Flash`). Lives in the Selling vocabulary.

**Parked questions resolved:**

| # | Source | Question | Resolution |
|---|--------|----------|------------|
| 14 | W001 | Automated approval — single chain or separate steps? | Option A — single handler chain in MVP. Atomic. Migrates to Option B (separate handlers) post-MVP without event vocabulary changes. |
| Cross-BC #4 | W003 | `BuyItNowPrice >= ReservePrice` invariant | Enforced at listing creation/submission. Validator rejects violations. Settlement's BIN-skip-reserve-check is now backed by an upstream guarantee. |
| 1 | W001 | Listing UI before session starts? | Listings hidden from participant catalog until `ListingAttachedToSession` fires. Visible in ops dashboard immediately. Resolved as a Listings/Operations BC projection rule. |

**Design decisions made in Phase 1:**

| # | Question | Resolution |
|---|----------|------------|
| 1 | `SellerListing` aggregate state machine | 6 states: Draft, Submitted, Approved, Rejected, Published, EndedEarly. `Approved` is transient in MVP (immediate publish) but modeled as a real state for post-MVP migration. |
| 2 | Validation rule set | Enumerated 11 rules across required fields, numeric invariants, extended bidding, format. |
| 3 | `BIN >= Reserve` invariant | Enforced at creation; also at revision (though revision can't change BIN, see #5). |
| 4 | Reserve confidentiality | Discipline-enforced. Listings BC projection MUST NOT read `ReservePrice`. Single integration event for simplicity. |
| 5 | Post-publish revision rules | Restricted to Title, Description, ShippingTerms only. All other parameters immutable. Sellers must end and relist for critical changes. |
| 6 | End early rules | Allowed at any time before close. Same effect as ListingPassed from settlement perspective. |
| 7 | Relist mechanism | Creates new aggregate with new ListingId. Original listing's stream gets a `ListingRelisted` marker. |
| 8 | Timed vs Flash listing format | New `ListingFormat` enum. Timed requires Duration; Flash requires null Duration and session attachment. |

**New questions surfaced in Phase 1:**

| # | Question | Persona | Notes |
|---|----------|---------|-------|
| 1 | Should the Selling BC have a "draft auto-save" mechanism, or only explicit save? | `@UX`/`@BackendDeveloper` | Affects whether `DraftListingUpdated` is fired on every keystroke or only on explicit save. UX call. |
| 2 | What happens if `SellerRegistrationCompleted` arrives at Selling BC out of order? Can a draft be created before the seller's registration is durably known to Selling? | `@QA` | Selling needs to verify seller status. Inter-BC concern. |
| 3 | Where does the `FeePercentage` platform config live? In `appsettings.json`, in a database table, or in a feature flag system? | `@BackendDeveloper`/`@ProductOwner` | Implementation detail. MVP should keep it simple — `appsettings.json`. |
| 4 | Image/media handling for listings — what's the MVP scope? | `@ProductOwner`/`@UX` | Not addressed in this workshop. Likely a separate sub-workshop or post-MVP. |
| 5 | Does relisting copy ALL fields from the original, or only some? Specifically, does it preserve `FeePercentage` from the original or read fresh platform config? | `@ProductOwner`/`@Architect` | Leaning toward "fresh config" — relisting is a new agreement at current rates. |
| 6 | Revising a listing in the middle of a Flash session: is this allowed? What's the UX? | `@UX`/`@BackendDeveloper` | Currently the rules say Title/Description/ShippingTerms can be revised always, but mid-session revision feels weird. Probably should be restricted to "before session starts." |

**Cross-workshop ripple effects (vocabulary pass needed):**

- **W001:** Acknowledge the existence of `ListingFormat`. The slice tables are MVP-correct (all Flash) but framing should be updated.
- **W002:** `BiddingOpened` event should carry `ListingFormat` for downstream consumers. Behavior unchanged.
- **W003:** `PendingSettlement` projection's `ListingPublished` handler reads `FeePercentage` from the event payload, not platform config. Scenario 8.1 updated. Scenario 8.2 remains valid (FeePercentage is still immutable post-publish, but for a different reason — it's now event-sourced rather than config-cached). Scenario 8.3 (`ListingRevised` updates `ReservePrice`) is **invalid** and should be removed because `ReservePrice` is no longer mutable post-publish.

**Domain-events.md updates needed (vocabulary pass):**

- Add `ListingFormat` enum reference under Selling
- Update `ListingPublished` description to mention `FeePercentage` and `Format` fields
- Update `ListingRevised` description to clarify limited mutable fields
- Update `ListingRelisted` description to clarify new-aggregate semantics

---

## Phase 2 — Storytelling: A Listing's Complete Lifecycle

*Next: Walk a single listing from draft creation through publication and into one of the terminal states (sold via the W002 path, ended early, relisted). Resolve any remaining Phase 1 open questions that come up naturally in the walkthrough.*

*(to be continued)*

---

## Phase 3 — Scenarios (Given/When/Then)

*(not yet started — implementation-ready scenarios for the SellerListing aggregate, validation rules, and the published payload contract)*
