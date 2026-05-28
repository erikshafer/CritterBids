## ADDED Requirements

### Requirement: Deterministic obligation identity

The system SHALL identify each obligation by an `ObligationId` derived deterministically as a UUID v5 from the sold listing's `ListingId`, so that duplicate delivery of the triggering event does not create a second obligation.

#### Scenario: Idempotent start on duplicate settlement completion

- **WHEN** a second `SettlementCompleted` for the same `ListingId` is consumed after an obligation already exists
- **THEN** the start derives the same `ObligationId`, finds existing saga state, and the duplicate is a no-op (no second obligation, no duplicate timers)

### Requirement: Post-sale coordination starts on settlement completion

The system SHALL start the post-sale coordination saga when a `SettlementCompleted` integration event is consumed, emitting `PostSaleCoordinationStarted` carrying `ObligationId`, `ListingId`, `WinnerId`, `SellerId`, and a computed `ShipByDeadline`, and SHALL schedule a shipping reminder and a deadline escalation as cancellable messages.

#### Scenario: Saga starts and schedules timers

- **WHEN** `SettlementCompleted { ListingId, WinnerId, SellerId, HammerPrice, FeeAmount, SellerPayout }` is consumed and no obligation exists for the listing
- **THEN** the saga emits `PostSaleCoordinationStarted` with a `ShipByDeadline` computed from `ObligationsOptions`, schedules `SendShippingReminder` at the reminder offset and `SendDeadlineEscalation` at the deadline, and `ObligationStatusView` reads status "Awaiting shipment"

### Requirement: A single shipping reminder is sent before the deadline

The system SHALL send exactly one shipping reminder before the ship-by deadline, emitting `ShippingReminderSent`, without changing the saga's awaiting-shipment state.

#### Scenario: Reminder fires while awaiting shipment

- **WHEN** the scheduled `SendShippingReminder` fires and tracking has not yet been provided
- **THEN** the saga emits `ShippingReminderSent`, `ObligationStatusView` records the reminder time, and the saga remains in the awaiting-shipment state with the escalation still pending

#### Scenario: Stale reminder after tracking is a no-op

- **WHEN** a `SendShippingReminder` is delivered after `TrackingInfoProvided` has already transitioned the saga out of awaiting-shipment
- **THEN** the reminder handler takes no action (no `ShippingReminderSent` emitted)

### Requirement: Seller tracking discharges shipping and cancels timers

The system SHALL accept seller-provided tracking via `ProvideTracking`, emit `TrackingInfoProvided`, cancel the pending shipping reminder and deadline escalation, and schedule the delivery auto-confirmation timer.

#### Scenario: Tracking provided while awaiting shipment

- **WHEN** `ProvideTracking { ObligationId, Carrier, TrackingNumber }` is received in the awaiting-shipment state
- **THEN** the saga emits `TrackingInfoProvided`, cancels the pending `SendDeadlineEscalation` so `DeadlineEscalated` never fires, schedules `ConfirmDelivery` at the auto-confirm offset, and `ObligationStatusView` reads status "Shipped" with the tracking number

### Requirement: Delivery auto-confirms and fulfills the obligation

The system SHALL auto-confirm delivery on a clock-triggered `ConfirmDelivery` scheduled after tracking is provided, emitting `DeliveryConfirmed` and then `ObligationFulfilled`, and SHALL terminate the saga with `MarkCompleted()`. No buyer command is required in MVP.

#### Scenario: Delivery auto-confirms after the configured window

- **WHEN** the scheduled `ConfirmDelivery` fires in the awaiting-delivery state
- **THEN** the saga emits `DeliveryConfirmed` and `ObligationFulfilled`, calls `MarkCompleted()`, and `ObligationStatusView` reads status "Fulfilled"

### Requirement: A missed deadline escalates without terminating

The system SHALL escalate when the ship-by deadline passes with no tracking, emitting `DeadlineEscalated` routed to Operations, and SHALL keep the saga alive so a later tracking submission can still recover.

#### Scenario: Deadline passes without tracking

- **WHEN** the scheduled `SendDeadlineEscalation` fires and no tracking has been provided
- **THEN** the saga emits `DeadlineEscalated`, `OperationsObligationsView` shows the obligation in its escalation queue, and the saga remains alive (non-terminal)

### Requirement: Late tracking recovers the happy path

The system SHALL accept `ProvideTracking` from the escalated state, emit `TrackingInfoProvided`, schedule the delivery auto-confirmation, and continue to the fulfilled outcome.

#### Scenario: Tracking provided after escalation

- **WHEN** `ProvideTracking` is received after `DeadlineEscalated` has fired
- **THEN** the saga emits `TrackingInfoProvided`, schedules `ConfirmDelivery`, and the obligation proceeds toward `ObligationFulfilled`

### Requirement: A dispute can be opened against an obligation

The system SHALL allow a dispute to be opened via `OpenDispute`, emitting `DisputeOpened` with a reason of `NonDelivery`, `ItemCondition` (winner-opened), or `MissedDeadline` (Operations-opened from an escalation).

#### Scenario: Winner opens a non-delivery dispute

- **WHEN** the winner issues `OpenDispute { ObligationId, Reason: NonDelivery }`
- **THEN** the saga emits `DisputeOpened` and `OperationsObligationsView` shows the obligation in its open-dispute queue

### Requirement: Operations resolves a dispute with one of three resolutions

The system SHALL resolve a dispute via `ResolveDispute`, emitting `DisputeResolved` with a resolution of `Refund`, `Extension`, or `Closed`. `Refund` and `Closed` SHALL terminate the saga with `MarkCompleted()`. `Extension` SHALL reschedule a fresh ship-by deadline and continue the saga without terminating.

#### Scenario: Refund resolution terminates

- **WHEN** Operations issues `ResolveDispute { ObligationId, Resolution: Refund }`
- **THEN** the saga emits `DisputeResolved(Refund)` and calls `MarkCompleted()`

#### Scenario: Extension resolution reschedules and continues

- **WHEN** Operations issues `ResolveDispute { ObligationId, Resolution: Extension }`
- **THEN** the saga emits `DisputeResolved(Extension)`, reschedules a new `ShipByDeadline`, returns to the awaiting-tracking state, and does NOT call `MarkCompleted()`

### Requirement: Timer durations are configurable for production and demo

The system SHALL read the reminder offset, ship-by deadline, and auto-confirm window from an `ObligationsOptions` configuration section that provides both production durations and demo-mode durations, selectable without recompilation.

#### Scenario: Demo mode collapses the chain to seconds

- **WHEN** `ObligationsOptions.DemoMode` is enabled
- **THEN** the reminder, deadline, and auto-confirm timers use the demo-second durations so the full lifecycle runs live within a demo session
