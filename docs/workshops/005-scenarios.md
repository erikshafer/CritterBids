# Workshop 005 — Obligations BC Scenarios (Given/When/Then)

Companion to `005-obligations-bc-deep-dive.md`, Phases 4–5.
Implementation-ready scenarios for the Obligations post-sale coordination saga: the happy path, the deadline-escalation and recovery paths, the auto-confirm temporal slice, and the dispute sub-workflow.

**Conventions:**
- Placeholder IDs for readability: `listing-A`, `participant-001` (seller), `participant-002` (winner), `obligation-001` (= `UuidV5(ObligationsIdentityNamespaces.PostSaleCoordination, "obligation:listing-A")`)
- Timestamps as relative offsets from saga start (e.g., `T+0`, `T+reminder`, `T+deadline`)
- Demo-mode durations assumed in scenarios for legibility: `reminder offset = 5s`, `deadline = 10s`, `delivery auto-confirm = 8s` (real-mode counterparts configured via `ObligationsOptions`)
- Listing-A configuration: `HammerPrice: 85.00`, `FeeAmount: 8.50`, `SellerPayout: 76.50` (carried from `SettlementCompleted`)

**Test structure:**
- **Saga scenarios (Sections 1–6):** End-to-end scenarios exercising the Wolverine Saga via the message bus and scheduled-message machinery. M6-S1 implements as a Wolverine `Saga` per ADR-022 (candidate). Scheduling assertions are about `bus.ScheduleAsync()` and its cancellation.
- **Projection scenarios (Section 7):** Integration tests against `ObligationStatusView`, `ObligationsAwaitingDelivery*`, and `OperationsObligationsView`. Verify row state after each event.
- **Dispatch scenarios (Section 8):** One dispatch test per command exercising the Wolverine routing path (per M6 exit criteria).

**Canonical payloads.** `PostSaleCoordinationStarted` carries `ObligationId, ListingId, WinnerId, SellerId, ShipByDeadline, StartedAt`. `TrackingInfoProvided` carries `ObligationId, ListingId, SellerId, TrackingNumber, ProvidedAt`. `ObligationFulfilled` carries `ObligationId, ListingId, WinnerId, SellerId, FulfilledAt`. `DisputeOpened` carries `ObligationId, ListingId, DisputeId, RaisedBy, Reason, OpenedAt`. `DisputeResolved` carries `ObligationId, ListingId, DisputeId, ResolutionType, ResolvedAt`.

**21 scenarios across eight sections.**

---

## 1. Saga Start (Slice 5.1)

### 1.1 Start coordination from `SettlementCompleted`

```
Given:  no saga exists for obligation-001

When:   SettlementCompleted {
          ListingId: listing-A, WinnerId: participant-002, SellerId: participant-001,
          HammerPrice: 85.00, FeeAmount: 8.50, SellerPayout: 76.50
        } is received

Then:   [ PostSaleCoordinationStarted {
            ObligationId: obligation-001, ListingId: listing-A,
            WinnerId: participant-002, SellerId: participant-001,
            ShipByDeadline: T+10s, StartedAt: <now>
          } ]
        AND a SendShippingReminder message is scheduled for T+5s
        AND a SendDeadlineEscalation message is scheduled for T+10s
```

### 1.2 Duplicate `SettlementCompleted` is idempotent

```
Given:  a saga already exists for obligation-001 (started at T+0)

When:   a second SettlementCompleted for listing-A is received (Wolverine at-least-once redelivery)

Then:   no second PostSaleCoordinationStarted is produced
        AND no additional reminder or escalation is scheduled
        (ObligationId is deterministic from ListingId; the start is a no-op)
```

---

## 2. Shipping Reminder (Slice 5.2)

### 2.1 Reminder fires before the seller ships

```
Given:  a saga for obligation-001 in state AwaitingTracking
        AND no TrackingInfoProvided has arrived

When:   the scheduled SendShippingReminder fires at T+5s

Then:   [ ShippingReminderSent { ObligationId: obligation-001, SellerId: participant-001, SentAt: <now> } ]
        AND the saga remains in state AwaitingTracking
```

### 2.2 Stale reminder after tracking is a no-op

```
Given:  a saga for obligation-001 in state Shipped
        AND TrackingInfoProvided arrived at T+3s (before the reminder offset)

When:   a stale SendShippingReminder fires at T+5s (cancellation race)

Then:   no ShippingReminderSent is produced
        (handler no-ops on saga state — defense in depth alongside scheduled-message cancellation)
```

---

## 3. Tracking Provided (Slice 5.3)

### 3.1 Seller provides tracking — cancels reminders, schedules auto-confirm

```
Given:  a saga for obligation-001 in state AwaitingTracking

When:   ProvideTracking { ObligationId: obligation-001, TrackingNumber: "1Z-CRITTER-001" } is handled

Then:   [ TrackingInfoProvided {
            ObligationId: obligation-001, ListingId: listing-A, SellerId: participant-001,
            TrackingNumber: "1Z-CRITTER-001", ProvidedAt: <now>
          } ]
        AND the pending SendShippingReminder is cancelled
        AND the pending SendDeadlineEscalation is cancelled
        AND a ConfirmDelivery message is scheduled for now + 8s
        AND the saga transitions to state Shipped
```

### 3.2 Tracking is rejected once already shipped

```
Given:  a saga for obligation-001 in state Shipped (tracking already provided)

When:   a second ProvideTracking is handled

Then:   no second TrackingInfoProvided is produced
        AND the existing ConfirmDelivery schedule is unchanged
        (idempotent; first tracking wins in MVP)
```

---

## 4. Delivery Auto-Confirm & Fulfilment (Slice 5.4)

### 4.1 Delivery auto-confirms and the obligation is fulfilled

```
Given:  a saga for obligation-001 in state Shipped
        AND TrackingInfoProvided arrived at T+3s

When:   the scheduled ConfirmDelivery fires at T+11s (3s + 8s)

Then:   [ DeliveryConfirmed { ObligationId: obligation-001, ConfirmedAt: <now> },
          ObligationFulfilled {
            ObligationId: obligation-001, ListingId: listing-A,
            WinnerId: participant-002, SellerId: participant-001, FulfilledAt: <now>
          } ]
        AND the saga calls MarkCompleted()
```

### 4.2 Auto-confirm does not fire if a dispute is open

```
Given:  a saga for obligation-001 in state Disputed (DisputeOpened before the auto-confirm timer)

When:   the scheduled ConfirmDelivery fires

Then:   no DeliveryConfirmed is produced
        AND no ObligationFulfilled is produced
        (handler no-ops while a dispute is unresolved; resolution drives the terminal path)
```

---

## 5. Deadline Escalation & Recovery (Slices 5.5, 5.6)

### 5.1 Deadline passes with no tracking — escalate (non-terminal)

```
Given:  a saga for obligation-001 in state AwaitingTracking
        AND no TrackingInfoProvided has arrived

When:   the scheduled SendDeadlineEscalation fires at T+10s

Then:   [ DeadlineEscalated { ObligationId: obligation-001, ListingId: listing-A,
            SellerId: participant-001, EscalatedAt: <now> } ]
        AND the saga transitions to state Escalated
        AND the saga does NOT call MarkCompleted() (escalation is non-terminal)
```

### 5.2 Late tracking after escalation recovers the happy path

```
Given:  a saga for obligation-001 in state Escalated

When:   ProvideTracking { ObligationId: obligation-001, TrackingNumber: "1Z-CRITTER-LATE" } is handled

Then:   [ TrackingInfoProvided { ObligationId: obligation-001, TrackingNumber: "1Z-CRITTER-LATE", ProvidedAt: <now> } ]
        AND a ConfirmDelivery message is scheduled for now + 8s
        AND the saga transitions from Escalated to Shipped
        (recovery: the same Shipped path as 3.1, reached from the escalated state)
```

---

## 6. Dispute Sub-Workflow (Slices 5.7, 5.8)

### 6.1 Winner opens a dispute (non-delivery)

```
Given:  a saga for obligation-001 in state Shipped

When:   OpenDispute { ObligationId: obligation-001, RaisedBy: participant-002, Reason: NonDelivery } is handled

Then:   [ DisputeOpened {
            ObligationId: obligation-001, ListingId: listing-A, DisputeId: dispute-001,
            RaisedBy: participant-002, Reason: NonDelivery, OpenedAt: <now>
          } ]
        AND the saga transitions to state Disputed
```

### 6.2 Ops opens a dispute from an escalation (missed deadline)

```
Given:  a saga for obligation-001 in state Escalated

When:   OpenDispute { ObligationId: obligation-001, RaisedBy: "ops", Reason: MissedDeadline } is handled

Then:   [ DisputeOpened { ObligationId: obligation-001, DisputeId: dispute-001,
            RaisedBy: "ops", Reason: MissedDeadline, OpenedAt: <now> } ]
        AND the saga transitions to state Disputed
```

### 6.3 Ops resolves a dispute — Refund (terminal)

```
Given:  a saga for obligation-001 in state Disputed

When:   ResolveDispute { ObligationId: obligation-001, DisputeId: dispute-001, ResolutionType: Refund } is handled

Then:   [ DisputeResolved { ObligationId: obligation-001, ListingId: listing-A,
            DisputeId: dispute-001, ResolutionType: Refund, ResolvedAt: <now> } ]
        AND the saga calls MarkCompleted()
```

### 6.4 Ops resolves a dispute — Closed (terminal)

```
Given:  a saga for obligation-001 in state Disputed

When:   ResolveDispute { ..., ResolutionType: Closed } is handled

Then:   [ DisputeResolved { ..., ResolutionType: Closed, ResolvedAt: <now> } ]
        AND the saga calls MarkCompleted()
```

### 6.5 Ops resolves a dispute — Extension (non-terminal, reschedules)

```
Given:  a saga for obligation-001 in state Disputed

When:   ResolveDispute { ..., ResolutionType: Extension } is handled

Then:   [ DisputeResolved { ..., ResolutionType: Extension, ResolvedAt: <now> } ]
        AND a fresh ShipByDeadline is computed (now + deadline offset)
        AND a new SendDeadlineEscalation is scheduled for the fresh deadline
        AND the saga transitions back to AwaitingTracking
        AND the saga does NOT call MarkCompleted() (Extension is the deliberate non-terminal path)
```

---

## 7. Projections (Section 7)

### 7.1 `ObligationStatusView` reflects each transition

```
Given:  an empty ObligationStatusView

When:   PostSaleCoordinationStarted → ShippingReminderSent → TrackingInfoProvided → DeliveryConfirmed → ObligationFulfilled
        are applied in order

Then:   the row for obligation-001 ends as:
          Status: Fulfilled, ShipByDeadline: T+10s, TrackingNumber: "1Z-CRITTER-001",
          LastRemindedAt: T+5s, FulfilledAt: <ObligationFulfilled time>
```

### 7.2 `ObligationsAwaitingDelivery*` self-removes on confirmation

```
Given:  an empty ObligationsAwaitingDelivery* todo-list

When:   TrackingInfoProvided is applied
Then:   a row for obligation-001 exists (awaiting auto-confirm)

When:   DeliveryConfirmed is applied
Then:   the row for obligation-001 is removed (temporal-automation list, Bruun asterisk convention)
```

### 7.3 `OperationsObligationsView` surfaces escalations and disputes

```
Given:  an empty OperationsObligationsView

When:   DeadlineEscalated is applied
Then:   obligation-001 appears in the escalations queue

When:   DisputeOpened is applied
Then:   obligation-001 appears in the open-disputes queue

When:   DisputeResolved is applied
Then:   obligation-001 is removed from the open-disputes queue
```

---

## 8. Command Dispatch (Section 8)

One dispatch test per command, asserting the Wolverine routing path resolves to the expected handler (per M6 exit criteria "at least one dispatch test per Obligations command").

| # | Command | Asserts |
|---|---|---|
| 8.1 | `ProvideTracking` | routes to the saga's `Handle(ProvideTracking)` and produces `TrackingInfoProvided` |
| 8.2 | `OpenDispute` | routes to the saga's `Handle(OpenDispute)` and produces `DisputeOpened` |
| 8.3 | `ResolveDispute` | routes to the saga's `Handle(ResolveDispute)` and produces `DisputeResolved` |
| 8.4 | `SendShippingReminder` (scheduled) | routes to the saga's `Handle(SendShippingReminder)` |
| 8.5 | `SendDeadlineEscalation` (scheduled) | routes to the saga's `Handle(SendDeadlineEscalation)` |
| 8.6 | `ConfirmDelivery` (scheduled) | routes to the saga's `Handle(ConfirmDelivery)` |
| 8.7 | `SettlementCompleted` (integration) | routes to the saga-start handler and produces `PostSaleCoordinationStarted` |
