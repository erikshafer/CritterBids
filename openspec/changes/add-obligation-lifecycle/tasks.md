## 1. BC scaffold and registration

- [x] 1.1 Create `src/CritterBids.Obligations` class library and add it to the solution
- [x] 1.2 Add `AddObligationsModule()` extension with `services.ConfigureMarten()` contributing the saga state document and projections (no `AddMarten()` call — the host owns the single one)
- [x] 1.3 Register `AddObligationsModule()` in `src/CritterBids.Api/Program.cs`
- [x] 1.4 Add `ObligationsOptions` config section (production + demo durations, `DemoMode` flag) bound from configuration
- [x] 1.5 Add `ObligationsIdentityNamespaces.PostSaleCoordination` namespace constant and the deterministic `ObligationId` helper (UUID v5 from `ListingId`)
- [x] 1.6 Author ADR-022 (Obligations saga hosting = Wolverine Saga, citing ADR-019)

## 2. Contracts

- [x] 2.1 Add integration events `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` to `CritterBids.Contracts` (sealed records, no "Event" suffix)
- [x] 2.2 Confirm `SettlementCompleted` shape carries `ListingId`, `WinnerId`, `SellerId`, `HammerPrice`, `FeeAmount`, `SellerPayout`

## 3. Saga start and identity (spec: start, deterministic identity)

- [x] 3.1 Implement the `SettlementCompleted` handler that starts the saga and emits `PostSaleCoordinationStarted` with computed `ShipByDeadline`
- [x] 3.2 Schedule `SendShippingReminder` and `SendDeadlineEscalation` via `bus.ScheduleAsync()` at saga start
- [x] 3.3 Implement idempotent start: a duplicate `SettlementCompleted` for an existing `ObligationId` is a no-op

## 4. Reminder and tracking (spec: reminder, tracking)

- [x] 4.1 Implement `SendShippingReminder` handler emitting `ShippingReminderSent`, with a no-op guard when state has advanced past awaiting-shipment
- [x] 4.2 Implement the `ProvideTracking` command + in-process HTTP endpoint emitting `TrackingInfoProvided`
- [x] 4.3 Cancel the pending reminder and escalation on tracking; schedule `ConfirmDelivery` at the auto-confirm offset

## 5. Auto-confirm and fulfillment (spec: delivery auto-confirms)

- [x] 5.1 Implement `ConfirmDelivery` handler emitting `DeliveryConfirmed` then `ObligationFulfilled`
- [x] 5.2 Call `MarkCompleted()` on fulfillment

## 6. Escalation and recovery (spec: missed deadline, late tracking)

- [ ] 6.1 Implement `SendDeadlineEscalation` handler emitting `DeadlineEscalated` (non-terminal; saga stays alive)
- [ ] 6.2 Make `ProvideTracking` state-tolerant so it recovers from the escalated state

## 7. Dispute sub-workflow (spec: open dispute, resolve dispute)

- [ ] 7.1 Implement `OpenDispute` command + endpoint emitting `DisputeOpened` (reasons: `NonDelivery`, `ItemCondition`, `MissedDeadline`)
- [ ] 7.2 Implement `ResolveDispute` command + endpoint emitting `DisputeResolved` (`Refund`, `Extension`, `Closed`)
- [ ] 7.3 Terminate the saga (`MarkCompleted()`) on `Refund` and `Closed`
- [ ] 7.4 On `Extension`, reschedule a fresh `ShipByDeadline` and return to awaiting-tracking without terminating

## 8. Read models

- [x] 8.1 `ObligationStatusView` single-stream projection (status, `ShipByDeadline`, tracking, dispute state)
- [ ] 8.2 `ObligationsAwaitingDelivery*` todo-list projection (rows on `TrackingInfoProvided`, self-remove on `DeliveryConfirmed`)
- [ ] 8.3 `OperationsObligationsView` projection (escalation queue + open-dispute queue)

## 9. Tests (spec scenarios as test cases)

- [x] 9.1 Integration tests covering the happy path (start → reminder → tracking → auto-confirm → fulfilled) with demo durations injected via `ObligationsOptions`
- [x] 9.2 Idempotent-start test (duplicate `SettlementCompleted`)
- [x] 9.3 Stale-reminder-after-tracking no-op test
- [ ] 9.4 Escalation + late-tracking recovery test
- [ ] 9.5 Dispute resolution tests: `Refund` terminates, `Extension` reschedules and continues
- [ ] 9.6 `dotnet build` and `dotnet test` pass; `openspec validate add-obligation-lifecycle --strict` passes
