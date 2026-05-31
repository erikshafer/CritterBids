# W006 — Operations BC Source Audit & Read-Model Field Freeze

**Status:** ✅ Frozen (M7-S1, 2026-05-30)
**Scope:** Operations BC — the five M7 operator read models
**Authored in:** M7-S1 (`docs/prompts/implementations/M7-S1-operations-foundation-decisions.md`)
**Jointly authoritative inputs:** `docs/narratives/008-operator-resolves-dispute-with-extension.md`
(operator-vantage spec for `OperationsObligationsView`), `docs/vision/domain-events.md` §Operations,
`src/CritterBids.Contracts/**` (the authoritative payload source)

---

## Why this artifact exists

Operations is the only M7 BC with **no Event Modeling workshop** — narrative 008 covers one operator
surface (`OperationsObligationsView`) and nothing else. This source audit is the substitute: it
**freezes the field shape** of every M7 operator read model so the S2–S5 slices build against a
locked spec instead of re-deciding fields per slice. It is the Operations equivalent of a workshop
deep-dive, scoped to a field freeze rather than Given-When-Then scenarios (hence the single-document
deviation from the paired `{NNN}-scenarios` + `{NNN}-deep-dive` convention — there are no scenarios to
write; the behaviour is "project these events into these fields").

**This is a freeze, not an implementation.** No read-model document class, handler, or endpoint is
written in S1. S2–S5 implement the views; this artifact is the contract they implement.

---

## Build strategy — ADR 014 Path A (confirmed, not re-decided)

Every view here is built with **[ADR 014](../decisions/014-cross-bc-read-model-extension-shape.md)
Path A**: a read-model document per logical entity, updated by **sibling handler classes — one per
source BC** — each reacting to that BC's integration events with **additive, tolerant upserts** and
**status-preservation guards** (a later-arriving event must not clobber a field a different event
already set, and out-of-order delivery must not regress a terminal state).

Confirmed consequences for Operations specifically:

- **No Marten multi-stream event projections.** The inbound integration-event firehose is **not**
  appended to local Operations streams (M7 milestone §3 non-goal), so there is no local event stream
  for a `MultiStreamProjection` to fold. Operations handlers receive contracts off the bus and upsert
  documents directly. This is the standard Path A consumer shape, identical to the cross-BC read
  models ADR 014 governs.
- **Operations is a pure consumer.** It publishes **no** integration events and introduces **no**
  `CritterBids.Contracts.Operations.*` type. Every field below traces to an **existing** upstream
  payload (or is explicitly marked *derived* from the event type). If a field the narrative wants is
  not on any source payload, that is recorded as a cross-view-reference finding, **not** resolved by
  adding a contract field.
- **One view per logical entity.** Where the milestone names a single "board" spanning two entities
  (the session/participant board), this freeze splits it into two upsert views keyed independently,
  each its own Path A sibling-handler family — per ADR 014's one-view-per-entity rule.

**Legend:** *upsert* = keyed document, mutated in place across many events. *append/feed* = one row
per event, never mutated. *derived* = field value comes from **which** event arrived (the event
type), not from a payload field. *cross-view* = field is not on this view's source events; the
dashboard reads it from a sibling view by shared key at render time.

---

## The five frozen views

| # | View | Classification | Key | Source BC families (Path A) |
|---|---|---|---|---|
| 1 | Settlement queue | upsert | `SettlementId` | Settlement |
| 2 | Lot board | upsert | `ListingId` | Selling + Auctions |
| 3 | Bid-activity feed | append/feed | `BidId` | Auctions |
| 4 | `OperationsObligationsView` | upsert | `ObligationId` | Obligations |
| 5a | Session activity board | upsert | `SessionId` | Auctions |
| 5b | Participant activity board | upsert | `ParticipantId` | Participants |

Views 5a and 5b are surfaced together as the milestone's single "session & participant activity
board" but are **two** Path A views (one logical entity each), joined only at render time.

---

### 1. Settlement queue — *upsert*, key `SettlementId`

Source events (Settlement BC family): `PaymentFailed`, `SettlementCompleted`, `SellerPayoutIssued`.

| Field | Type | Source payload → field | Notes |
|---|---|---|---|
| `SettlementId` | `Guid` | all three → `SettlementId` | key |
| `ListingId` | `Guid` | `PaymentFailed`/`SettlementCompleted` → `ListingId` | not on `SellerPayoutIssued`; set-once guard |
| `WinnerId` | `Guid` | `PaymentFailed`/`SettlementCompleted` → `WinnerId` | not on `SellerPayoutIssued` |
| `SellerId` | `Guid?` | `SettlementCompleted`/`SellerPayoutIssued` → `SellerId` | null until completed |
| `HammerPrice` | `decimal?` | `SettlementCompleted` → `HammerPrice` | |
| `FeeAmount` | `decimal?` | `SettlementCompleted` → `FeeAmount` | |
| `SellerPayout` | `decimal?` | `SettlementCompleted` → `SellerPayout` | projected payout |
| `PayoutAmount` | `decimal?` | `SellerPayoutIssued` → `PayoutAmount` | actual issued |
| `FeeDeducted` | `decimal?` | `SellerPayoutIssued` → `FeeDeducted` | |
| `FailureReason` | `string?` | `PaymentFailed` → `Reason` | |
| `Status` | enum | *derived* from event type | `PaymentFailed`→`Failed`, `SettlementCompleted`→`Completed`, `SellerPayoutIssued`→`PaidOut` |
| `LastUpdatedAt` | `DateTimeOffset` | `FailedAt`/`CompletedAt`/`IssuedAt` | latest-wins |

Status-preservation guard: `PaidOut` must not regress to `Completed` if `SettlementCompleted` is
re-delivered after `SellerPayoutIssued`.

---

### 2. Lot board — *upsert*, key `ListingId`

Source events: **Selling family** (`ListingPublished`, `ListingWithdrawn`) + **Auctions family**
(`BiddingOpened`, `BidPlaced`, `ListingSold`, `ListingPassed`). Two sibling handler classes, one per
source BC, both upserting the same `ListingId`-keyed document.

| Field | Type | Source payload → field | Notes |
|---|---|---|---|
| `ListingId` | `Guid` | all → `ListingId` | key |
| `SellerId` | `Guid` | `ListingPublished`/`BiddingOpened`/`ListingSold` → `SellerId` | set-once guard |
| `Title` | `string` | `ListingPublished` → `Title` | |
| `Format` | `string` | `ListingPublished` → `Format` | |
| `StartingBid` | `decimal` | `ListingPublished`/`BiddingOpened` → `StartingBid` | |
| `ReservePrice` | `decimal?` | `ListingPublished` → `ReservePrice` (≡ `BiddingOpened.ReserveThreshold`) | confidential; staff-only board |
| `BuyItNow` | `decimal?` | `ListingPublished` → `BuyItNow` (≡ `BiddingOpened.BuyItNowPrice`) | |
| `FeePercentage` | `decimal` | `ListingPublished` → `FeePercentage` | |
| `ScheduledCloseAt` | `DateTimeOffset?` | `BiddingOpened` → `ScheduledCloseAt` | null until bidding opens |
| `CurrentBid` | `decimal?` | `BidPlaced` → `Amount` | latest-wins (highest, bids are monotone) |
| `BidCount` | `int` | `BidPlaced`/`ListingSold`/`ListingPassed` → `BidCount` | latest-wins |
| `HammerPrice` | `decimal?` | `ListingSold` → `HammerPrice` | |
| `WinnerId` | `Guid?` | `ListingSold` → `WinnerId` | |
| `PassReason` | `string?` | `ListingPassed` → `Reason` | `"NoBids"`/`"ReserveNotMet"` |
| `WithdrawnBy` | `Guid?` | `ListingWithdrawn` → `WithdrawnBy` | |
| `WithdrawalReason` | `string?` | `ListingWithdrawn` → `Reason` | nullable on payload |
| `Status` | enum | *derived* from event type | `Draft`(Published) → `Open`(BiddingOpened) → `Sold`/`Passed`/`Withdrawn` (terminal) |
| `LastUpdatedAt` | `DateTimeOffset` | `…At` of latest event | latest-wins |

Status-preservation guard: terminal states (`Sold`/`Passed`/`Withdrawn`) must not regress to `Open`
on a late `BidPlaced`.

---

### 3. Bid-activity feed — *append/feed*, key `BidId`

Source event (Auctions family): `BidPlaced`. **Append-only** — one immutable row per bid, never
upserted. This is the single feed-shaped view; it has no status and no guards.

| Field | Type | Source payload → field | Notes |
|---|---|---|---|
| `BidId` | `Guid` | `BidPlaced` → `BidId` | key; unique per event |
| `ListingId` | `Guid` | `BidPlaced` → `ListingId` | feed filter / grouping |
| `BidderId` | `Guid` | `BidPlaced` → `BidderId` | participant identifier (never "paddle") |
| `Amount` | `decimal` | `BidPlaced` → `Amount` | |
| `BidCount` | `int` | `BidPlaced` → `BidCount` | sequence at time of bid |
| `IsProxy` | `bool` | `BidPlaced` → `IsProxy` | proxy vs direct |
| `PlacedAt` | `DateTimeOffset` | `BidPlaced` → `PlacedAt` | feed sort key |

Idempotency: re-delivery of the same `BidId` is a no-op insert (dedupe on key), not a second row.

---

### 4. `OperationsObligationsView` — *upsert*, key `ObligationId`

The narrative-008 operator surface: the **escalation queue** and the **open-dispute queue**. Source
events (Obligations family): `DeadlineEscalated`, `DisputeOpened`, `DisputeResolved`,
`ObligationFulfilled`.

| Field | Type | Source payload → field | Notes |
|---|---|---|---|
| `ObligationId` | `Guid` | all → `ObligationId` | key |
| `ListingId` | `Guid` | all → `ListingId` | join key to lot board (see cross-view note) |
| `DisputeId` | `Guid?` | `DisputeOpened`/`DisputeResolved` → `DisputeId` | |
| `RaisedBy` | `Guid?` | `DisputeOpened` → `RaisedBy` | the disputing party (the buyer in narrative 008) |
| `DisputeReason` | `string?` | `DisputeOpened` → `Reason` | `"NonDelivery"`/`"ItemCondition"`/`"MissedDeadline"` |
| `ResolutionType` | `string?` | `DisputeResolved` → `ResolutionType` | `"Refund"`/`"Extension"`/`"Closed"` |
| `ResolutionParticipantId` | `Guid?` | `DisputeResolved` → `ParticipantId` | optional on payload |
| `WinnerId` | `Guid?` | `ObligationFulfilled` → `WinnerId` | **only at fulfilment** (see finding) |
| `SellerId` | `Guid?` | `ObligationFulfilled` → `SellerId` | only at fulfilment |
| `EscalatedAt` | `DateTimeOffset?` | `DeadlineEscalated` → `EscalatedAt` | |
| `DisputeOpenedAt` | `DateTimeOffset?` | `DisputeOpened` → `OpenedAt` | |
| `DisputeResolvedAt` | `DateTimeOffset?` | `DisputeResolved` → `ResolvedAt` | |
| `FulfilledAt` | `DateTimeOffset?` | `ObligationFulfilled` → `FulfilledAt` | |
| `QueueState` | enum | *derived* from event type | `Escalated` → `Disputed` → (`Resolved` or back to active) → `Fulfilled` |

Queue membership (per narrative 008): **escalation queue** = `QueueState == Escalated`;
**open-dispute queue** = `QueueState == Disputed`. `DisputeResolved` with `Extension` returns the row
to the active set (out of both queues, not terminal); `Refund`/`Closed` resolve terminally;
`ObligationFulfilled` drops the row from every active queue. Status-preservation guard: `Fulfilled`
is terminal and must not regress.

> **Finding — narrative 008 wants fields the obligations events don't carry.** The narrative's
> open-dispute card reads *"NonDelivery — boxed vintage synthesizer — raised by buyer"*: a listing
> **Title** and a human sense of **who**. But:
> - **Listing `Title`** is on **no** Obligations event — only `ListingId`. It is *cross-view*: the
>   dashboard joins `OperationsObligationsView.ListingId` → lot-board `Title` at render time. S4 must
>   **not** mint a contract field for it (Operations is a pure consumer); it reads the sibling view.
> - **"The winner"** on the *open-dispute* card is `DisputeOpened.RaisedBy` (the buyer who raised it),
>   **not** `WinnerId` — `WinnerId` arrives only with `ObligationFulfilled`, after the dispute is
>   resolved. S4 surfaces `RaisedBy` for the open-dispute card and reserves `WinnerId` for the
>   fulfilment view. Recorded here so S4 does not wait on a `WinnerId` that is structurally absent at
>   dispute-open time.

---

### 5a. Session activity board — *upsert*, key `SessionId`

Source events (Auctions family): `SessionCreated`, `SessionStarted`, `ListingAttachedToSession`.

| Field | Type | Source payload → field | Notes |
|---|---|---|---|
| `SessionId` | `Guid` | all → `SessionId` | key |
| `Title` | `string` | `SessionCreated` → `Title` | |
| `DurationMinutes` | `int` | `SessionCreated` → `DurationMinutes` | |
| `AttachedListingIds` | `IReadOnlyList<Guid>` | `ListingAttachedToSession` → `ListingId` (accumulated) + `SessionStarted` → `ListingIds` | additive set-union; dedupe |
| `Status` | enum | *derived* from event type | `Created` → `Started` |
| `CreatedAt` | `DateTimeOffset` | `SessionCreated` → `CreatedAt` | |
| `StartedAt` | `DateTimeOffset?` | `SessionStarted` → `StartedAt` | null until started |

`AttachedListingIds` uses `IReadOnlyList<T>` per the global record convention; the upsert unions
late-arriving `ListingAttachedToSession` ids with the `SessionStarted.ListingIds` snapshot.

---

### 5b. Participant activity board — *upsert*, key `ParticipantId`

Source event (Participants family): `ParticipantSessionStarted`.

| Field | Type | Source payload → field | Notes |
|---|---|---|---|
| `ParticipantId` | `Guid` | `ParticipantSessionStarted` → `ParticipantId` | key |
| `DisplayName` | `string` | `ParticipantSessionStarted` → `DisplayName` | |
| `BidderId` | `string` | `ParticipantSessionStarted` → `BidderId` | **string** on the payload (not `Guid`); never "paddle" |
| `CreditCeiling` | `decimal` | `ParticipantSessionStarted` → `CreditCeiling` | |
| `StartedAt` | `DateTimeOffset` | `ParticipantSessionStarted` → `StartedAt` | |

---

## The freeze

The field sets above are **frozen for M7**. S2–S5 implement these views and **must not** add a field
that does not trace to a source payload (or a marked *derived*/*cross-view* entry) without amending
this artifact first. Adding such a field is the signal to reopen the freeze — and, if it would
require a new contract type, to challenge the "Operations is a pure consumer" premise (it should not
need to).

Slice mapping (per M7 milestone §7): the settlement queue, lot board, and bid-activity feed land in
S2–S3; `OperationsObligationsView` lands in S4 (narrative 008's surface); the session/participant
board lands in S5. Each slice implements one Path A sibling-handler family per source BC named above.

What is **not** frozen here (downstream slice decisions): document storage shape (single vs split
documents per view), exact enum member names, pagination/sort for the feed, and the SignalR push
projection onto `OperationsHub` (Relay/ADR 023 territory, gated by ADR 024). This artifact freezes
**which fields exist and where each comes from** — not the storage or transport mechanics.

---

## References

- [ADR 014 — Cross-BC Read-Model Extension Shape](../decisions/014-cross-bc-read-model-extension-shape.md)
  — Path A, the build strategy this audit confirms
- [ADR 024 — Staff Auth Posture Resumption](../decisions/024-staff-auth-posture-resumption.md) — the
  `StaffOnly` gate over the dashboards these views feed
- `docs/milestones/M7-operations-bc.md` §2 (consumer table), §3 (no-local-streams non-goal), §6
  (read-model conventions), §7 (slice table)
- `docs/narratives/008-operator-resolves-dispute-with-extension.md` — operator-vantage spec for view 4
- `docs/vision/domain-events.md` §Operations — the event catalogue audited
- `src/CritterBids.Contracts/**` — the authoritative payload source every field traces to
