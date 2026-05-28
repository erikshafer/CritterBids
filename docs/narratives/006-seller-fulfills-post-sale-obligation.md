---
slug: 006-seller-fulfills-post-sale-obligation
status: accepted
journey: seller
perspective: single-seller
scope: happy-path
bounded_contexts: [Obligations]
boundaries_touched: [Settlement, Auctions, Selling, Participants, Listings, Relay, Operations]
slices_implemented: [5.1, 5.2, 5.3, 5.4]
canonical_id: ObligationId
---

# Seller Fulfills Post-Sale Obligation (Happy Path)

An Obligations-grain narrative. GreyOwl12 sold the Vintage Mechanical Keyboard for $55.00 hammer in narrative 005 and was paid out $49.50 in narrative 002 Moment 4; this narrative picks up at the `SettlementCompleted` integration-event commit that closed narrative 002 Moment 5 and follows what the post-sale coordination saga does next. GreyOwl12 ships the keyboard, enters a carrier tracking number, and the obligation auto-confirms to fulfilled. Single seller, single sold listing, no missed deadline, no escalation, no dispute. The deadline-escalation path, the late-tracking recovery, and the three dispute resolutions belong to subsequent narratives, not as branches inside this story.

This narrative is **forward-spec**: the Obligations BC has not shipped (M6 territory, and the first OpenSpec-adopting BC per ADR 021), so there is no `src/CritterBids.Obligations/` to audit Moment-by-Moment. The spec source is W005 (`005-obligations-bc-deep-dive.md`) and `005-scenarios.md`; the upstream contract the saga consumes (`SettlementCompleted` from Settlement, W003 §1) and the downstream broadcasts the seller and winner perceive (`TrackingInfoProvided`, `ObligationFulfilled` via Relay) are the boundary surfaces. This is the first narrative whose protagonist acts inside the Obligations BC, and the first to dramatise the project's canonical cancellable-scheduled-message saga from the seller's vantage.

The journey runs in the conference demo's **demo-mode timing** (Decision 4 / `ObligationsOptions.DemoMode = true`): the reminder, ship-by deadline, and auto-confirm timers collapse from production days to projector seconds. The narrative names the demo offsets where they drive a Moment and notes that production durations are the config alternates on the same `ObligationsOptions` keys; nothing about the saga's shape changes between the two.

## Cast

- **GreyOwl12** — the seller, protagonist. Continuity from narratives 004 (where he registered, drafted, and published the keyboard) and 005 (where he watched it sell at $55.00 hammer). Single protagonist; the narrative is told from his vantage. He is an observer across three of the four Moments and acts once (Moment 3, when he enters tracking); the narrator dramatises what he perceives from his seller dashboard plus the saga-grain mechanics underneath.
- **The Post-Sale Coordination saga** — onstage across all four Moments. The dramatic engine of the narrative: the Wolverine `Saga` (Decision 3; ADR-022 candidate, citing ADR-019 as precedent) that drives an obligation from `PostSaleCoordinationStarted` to terminal `ObligationFulfilled`. The narrator dramatises its progression and its cancellable scheduled-message chain, not its host primitive beyond naming it.
- **The `ObligationStatusView`** — onstage in every Moment. The single-stream Marten projection off the obligation stream that backs GreyOwl12's seller dashboard: status, `ShipByDeadline`, tracking number, dispute state. The surface through which the seller perceives the saga.
- **SwiftFerret42** — the winner. Offstage but named. The buyer-side party of the obligation (`WinnerId`); receives the `TrackingInfoProvided` and `ObligationFulfilled` broadcasts via Relay in Moments 3 and 4 but never appears in GreyOwl12's view. Her receipt-confirmation obligation is auto-discharged in MVP (Decision 1); a buyer-initiated confirmation is a `post-MVP` deferral.
- **Settlement** — the bounded context that published `SettlementCompleted` over the cross-BC bus at narrative entry. Offstage; its work — reserve check, winner charge, fee calculation, seller payout — finished in narrative 002 by the time Obligations begins.
- **Auctions** — the bounded context that resolved the keyboard to `ListingSold` in narrative 005 Moment 4. Offstage; out of frame by narrative entry.
- **Selling** — the bounded context where GreyOwl12 published the keyboard in narrative 004. Offstage.
- **Listings** — the bounded context that owns the read-side `CatalogListingView`, reading `Status: "Sold"` since narrative 005. Offstage; the catalog is not part of the post-sale loop.
- **Participants** — the bounded context that minted GreyOwl12's and SwiftFerret42's identities. Offstage; the obligation references `SellerId` and `WinnerId` without crossing back into Participants.
- **Relay** — the bounded context whose BiddingHub broadcasts `TrackingInfoProvided` and `ObligationFulfilled` to the winner's connection. Onstage in Moments 3 and 4. Forward-spec; Relay BC has not shipped (M6 territory). The Moments narrate the broadcasts as the system is designed to run.
- **Operations** — the bounded context whose `OperationsObligationsView` surfaces the escalation and dispute queues. Onstage in Moment 4 only as the queue the fulfilled obligation drops off; the escalation queue stays empty in this happy path. Operator-perspective on the post-sale floor is a `separate-narrative` deferral.
- **Wolverine, Marten, RabbitMQ, the Wolverine outbox, `bus.ScheduleAsync()`** — runtime primitives. Named in Setting and at saga / scheduled-message / integration-event commit boundaries. `bus.ScheduleAsync()` is the cancellable-timer mechanism and the only justified `IMessageBus` use in the saga.
- **The Vintage Mechanical Keyboard listing** — onstage as the journey's subject. Hammer price $55.00, fee $5.50, seller payout $49.50, inherited verbatim from narratives 002 and 005.

## Setting

Immediately after the gavel's settlement clears in narrative 002 Moment 5. Conference day at Nebraska.Code(); the Flash session demo has resolved the keyboard, SwiftFerret42 has been charged $55.00, and GreyOwl12 has been paid $49.50. The operator is mid-demo, and the projector is about to show the post-sale loop running live. GreyOwl12 is at home watching his seller dashboard, which now shows the keyboard with `Status: Sold`, `HammerPrice: $55.00`, `Payout: $49.50 (paid)`. He has not yet been asked to do anything; the post-sale coordination has not started.

Settlement has just published `SettlementCompleted { ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, HammerPrice: $55.00, FeeAmount: $5.50, SellerPayout: $49.50 }` over the cross-BC bus via the Wolverine transactional outbox. The message is in flight on RabbitMQ and the Obligations BC's handler is about to consume it. No obligation stream exists for the keyboard yet; the saga has not started.

The system's MVP infrastructure is healthy: Wolverine is processing messages, Marten's event store on PostgreSQL (ADR 011 All-Marten pivot) is up, the cross-BC RabbitMQ queues are draining cleanly, and Wolverine's scheduled-message store is available to host the saga's cancellable timers. `ObligationsOptions` is bound from configuration with `DemoMode = true` for the conference run: the demo offsets collapse the chain so it executes on the projector in seconds — the reminder fires shortly after start, the ship-by deadline lands a short interval later, and the auto-confirm fires a short interval after tracking arrives. The production-duration counterparts (reminder days before the deadline, a multi-day ship-by window, an auto-confirm window of several days after tracking) are the config alternates on the same keys; the saga's transitions are identical under either set. The exact demo second-counts are an `implementation-detail` of `ObligationsOptions` and are named here only as relative offsets, not literal values.

The cleanest possible run: GreyOwl12 ships promptly, well inside the ship-by deadline; the single reminder fires but is informational rather than corrective; the tracking number is accepted on the first try; no carrier exception, no buyer dispute, no missed deadline. The reminder and escalation timers are scheduled at saga start and the escalation is cancelled the moment tracking arrives, so `DeadlineEscalated` never fires in this narrative. The auto-confirm lands cleanly and the obligation reaches `ObligationFulfilled` with the saga calling `MarkCompleted()` — the standard terminal. Every alternate path (the reminder going un-actioned to escalation, the late-tracking recovery, the three dispute resolutions including the one non-terminal `Extension`) documents what happens when one of these clean conditions is not in fact clean.

## Moment 1: The obligation begins

**Implements:** W005 slice 5.1.

**Context.** The `SettlementCompleted` integration event is in flight on RabbitMQ from narrative entry. The Obligations BC's handler pool is registered against the cross-BC bus and ready to consume. GreyOwl12's seller dashboard shows the keyboard with `Status: Sold` and `Payout: $49.50 (paid)`; there is no obligation row for it yet, because the post-sale coordination has not started. Wolverine's scheduled-message store is available to host the timers the saga is about to set.

**Interaction.** Obligations consumes `SettlementCompleted { ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, HammerPrice: $55.00, FeeAmount: $5.50, SellerPayout: $49.50 }`. The handler derives `ObligationId` deterministically as `UuidV5(ObligationsIdentityNamespaces.PostSaleCoordination, "obligation:" + ListingId)` per W005's idempotence convention, mirroring W003's `SettlementId` strategy. It computes the `ShipByDeadline` from `ObligationsOptions` (the demo-mode offset for the conference run) and starts the saga against `state = null`.

**Response.** The saga emits `PostSaleCoordinationStarted { ObligationId, ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, ShipByDeadline: <start + demo offset> }`, appended to a fresh stream keyed on `ObligationId`; the saga's state advances from `null` to awaiting-shipment. As part of the same workflow start, the saga schedules two cancellable messages through `bus.ScheduleAsync()`: a `SendShippingReminder` at the reminder offset and a `SendDeadlineEscalation` at the `ShipByDeadline`. The `ObligationStatusView` for the keyboard is created reading `Status: "Awaiting shipment"`, `ShipByDeadline: <deadline>`, no tracking number. GreyOwl12's dashboard ticks the keyboard's row forward from `Sold` to "Action needed: ship your item by <deadline>." **First seller-visible beat in narrative 006.**

**Why this matters to the seller.** GreyOwl12's obligation to ship has just become a tracked commitment with a deadline and a system watching it. The deterministic `ObligationId` is the saga's idempotence guarantee: any replay of `SettlementCompleted` derives the same `ObligationId`, the start against a non-null state is a no-op, and exactly one obligation exists for the keyboard regardless of Wolverine's at-least-once delivery. He perceives only "ship your item by..."; underneath, the two timers that will either nudge him or escalate his silence are already counting down.

### Things deliberately not included

- The literal duplicate-`SettlementCompleted` race (a second delivery arriving before the first commits). The deterministic `ObligationId` makes the second start a no-op; the failure-handling detail is W005 Phase 5's idempotent-start scenario. *(`alternate-path-failure`.)*
- Any buyer-side action at obligation start. SwiftFerret42's payment obligation was discharged at Settlement; her receipt obligation is auto-confirmed later (Decision 1). *(`post-MVP` for a buyer-initiated variant; otherwise out of frame.)*
- The exact demo second-counts on the `ShipByDeadline` and reminder offsets. *(`implementation-detail`; `ObligationsOptions` territory.)*

## Moment 2: The reminder nudges him

**Implements:** W005 slice 5.2.

**Context.** The saga's state is awaiting-shipment. The state carries `ShipByDeadline` and the participant identities, hydrated from `PostSaleCoordinationStarted`. Both scheduled messages from Moment 1 are pending in Wolverine's scheduled-message store: the reminder fires first, the escalation at the deadline. GreyOwl12 has seen "Action needed" but has not yet boxed the keyboard.

**Interaction.** The scheduled `SendShippingReminder` reaches its offset and Wolverine delivers it to the saga. In the demo run this is seconds after start; in production it is the reminder-days-before-deadline alternate. The reminder is the single nudge the MVP sends (Decision 2: one reminder, then escalate) — not a recurring cadence.

**Response.** The saga emits `ShippingReminderSent { ObligationId, RemindedAt: <now> }`, appended to the `ObligationId` stream; the saga's state is unchanged (still awaiting-shipment — the reminder is a nudge, not a transition). The `ObligationStatusView` updates to show `LastRemindedAt: <now>` alongside the unchanged `Status: "Awaiting shipment"` and `ShipByDeadline`. GreyOwl12's dashboard surfaces a "Reminder: ship by <deadline>" banner on the keyboard's row. The `SendDeadlineEscalation` message remains pending and uncancelled; the reminder does not touch it.

**Why this matters to the seller.** The single reminder is the platform's one courtesy before the deadline turns into an escalation. GreyOwl12 now knows, without having to remember on his own, that the clock is real and that silence past the deadline has a consequence (Operations review). The MVP deliberately sends one reminder rather than nagging; the design choice trades completeness of prodding for a non-annoying seller experience, and the escalation path — not more reminders — is what catches a seller who ignores this one.

### Things deliberately not included

- A multi-reminder cadence (a second or third nudge before escalation). Decision 2 fixed MVP at one reminder; richer cadence is a `post-MVP` policy concern.
- The stale-reminder race: a `SendShippingReminder` arriving *after* `TrackingInfoProvided` already transitioned the saga. The handler no-ops on saga state as defense in depth alongside scheduled-message cancellation (W005 Phase 5). *(`alternate-path-failure`.)*
- The reminder banner's screen design. *(`UX-or-UI-detail`.)*

## Moment 3: He ships and provides tracking

**Implements:** W005 slice 5.3.

**Context.** The saga's state is awaiting-shipment; the reminder has fired and is visible. The `SendDeadlineEscalation` message is still pending in the scheduled-message store, counting toward the `ShipByDeadline`. GreyOwl12 has boxed the keyboard, taken it to the carrier, and has a tracking number in hand. This is the one Moment in the narrative where the protagonist acts rather than observes.

**Interaction.** GreyOwl12 enters the carrier and tracking number in the seller "Provide Tracking" form. The frontend screen is forward-spec for the M8 frontend MVP; the backend seam that lands in M6 is an in-process HTTP command endpoint (the real carrier-tracking webhook is a `post-MVP` deferral, W005-1). The endpoint issues `ProvideTracking { ObligationId, Carrier, TrackingNumber }` to the saga against `state = awaiting-shipment`.

**Response.** The saga emits `TrackingInfoProvided { ObligationId, ListingId: keyboard, Carrier, TrackingNumber, ProvidedAt: <now> }`, appended to the `ObligationId` stream; the saga's state advances from awaiting-shipment to awaiting-delivery. Reaching this state cancels both pending scheduled messages — the (already-fired) reminder slot and, critically, the still-pending `SendDeadlineEscalation` — by scheduled-message cancellation keyed on the saga id, so `DeadlineEscalated` will never fire for this obligation. In the same transition the saga schedules a single new cancellable message through `bus.ScheduleAsync()`: a `ConfirmDelivery` at `now + delivery offset` (the demo offset for the conference run). This is the temporal-automation timer that will close the obligation. The `ObligationStatusView` updates to `Status: "Shipped"`, `TrackingNumber: <value>`, `Carrier: <value>`. Because `TrackingInfoProvided` is an integration event, Relay's BiddingHub broadcasts it to SwiftFerret42's connection; on the winner's side, her order view ticks to "Your item shipped — tracking #<value>." GreyOwl12's dashboard ticks the keyboard's row to "Shipped — tracking #<value>; delivery confirmation pending."

**Why this matters to the seller.** GreyOwl12 has discharged his shipping obligation, and the system has acknowledged it in the way that protects him: the moment tracking lands, the deadline-escalation timer is cancelled, so there is no risk of a false "you missed the deadline" escalation arriving after he has in fact shipped. The cancellation is the saga's defining behavior — the same mechanism that the late-tracking recovery and dispute-`Extension` paths reuse — and from GreyOwl12's vantage it means the act of providing tracking is the act that takes the pressure off. The obligation is now the buyer's and the clock's to close, not his.

### Things deliberately not included

- The real carrier-tracking webhook receiver (W005-1) replacing the in-process command seam. *(`post-MVP`.)*
- A carrier-rejected or malformed tracking number (a validation-failure path on `ProvideTracking`). *(`alternate-path-failure`.)*
- The late-tracking recovery: `ProvideTracking` arriving from the *escalated* state rather than awaiting-shipment (W005 slice 5.6). This narrative ships inside the deadline, so escalation never fires. *(`alternate-path-failure`.)*
- The "Provide Tracking" form's screen design. *(`UX-or-UI-detail`.)*

## Moment 4: Delivery auto-confirms; the obligation is fulfilled

**Implements:** W005 slice 5.4.

**Context.** The saga's state is awaiting-delivery. The `ConfirmDelivery` message scheduled in Moment 3 is pending in the scheduled-message store, counting toward its offset. GreyOwl12's dashboard reads "Shipped — delivery confirmation pending"; SwiftFerret42's order view reads "Your item shipped." Neither party has any further action to take in MVP — the buyer's receipt confirmation is auto-discharged (Decision 1).

**Interaction.** The `ConfirmDelivery` message reaches its offset and Wolverine delivers it to the saga. In the demo run this is seconds after tracking; in production it is the auto-confirm-window-after-tracking alternate (W005-4). No human acts: this is the clock-triggered temporal-automation beat, the Bruun-pattern slice where the trigger is the passage of time, not an incoming command from a person.

**Response.** The saga emits `DeliveryConfirmed { ObligationId, ConfirmedAt: <now> }` and, as the terminal transition, `ObligationFulfilled { ObligationId, ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, FulfilledAt: <now> }`, both appended to the `ObligationId` stream; the saga calls `MarkCompleted()` — the standard terminal that retires the saga state. The `ObligationStatusView` updates to `Status: "Fulfilled"`. Because `ObligationFulfilled` is an integration event, Relay broadcasts it to SwiftFerret42's connection (her order view reads "Delivery confirmed — order complete") and the `OperationsObligationsView` drops the keyboard's obligation from any active queue it tracked. GreyOwl12's dashboard ticks the keyboard's row to its final state: "Completed." His payout of $49.50 has been in his ledger since narrative 002 Moment 4; fulfillment closes the post-sale loop on top of money that already moved.

**Why this matters to the seller.** GreyOwl12's sale is now fully, definitively closed: he listed it (narrative 004), it sold (narrative 005), he was paid (narrative 002), he shipped it (Moment 3), and the system has confirmed delivery and retired the obligation. The auto-confirm means the happy path required no action from the buyer and only one from him — the tracking entry — which is the MVP's deliberate posture (Decision 1). The `MarkCompleted()` call is the ordinary terminal for the saga; GreyOwl12 will never know that the one path that does *not* call it — the dispute-`Extension` resolution that reschedules a fresh deadline — even exists, because his obligation never entered a dispute. From his vantage, the platform simply finished the job.

### Things deliberately not included

- Buyer-initiated delivery confirmation (`ConfirmDelivery` as a winner "Confirm receipt" command rather than the auto-timer), W005-2. *(`post-MVP`.)*
- The auto-confirm window as a per-category or seller-configurable value rather than a single `ObligationsOptions` default, W005-4. *(`post-MVP` policy concern.)*
- The deadline-escalation path (W005 slice 5.5) and its late-tracking recovery (slice 5.6). *(`alternate-path-failure`; warrants its own narrative.)*
- The three dispute resolutions — `Refund` and `Closed` terminal, `Extension` non-terminal — opened by the winner or by Operations from an escalation (W005 slices 5.7, 5.8). *(`separate-narrative`.)*
- The Operations-staff vantage on the escalation and dispute queues. *(`separate-narrative`.)*

## Deferred from this narrative

Cumulative aggregation of the per-Moment deferred items, bucketed by disposition.

### `post-MVP`

- Real carrier-tracking webhook receiver replacing the in-process `ProvideTracking` command seam (W005-1). Moment 3.
- Buyer-initiated delivery confirmation — a winner "Confirm receipt" command rather than the auto-timer (W005-2). Moments 1 and 4.
- Auto-confirm window as a per-category or seller-configurable value rather than a single `ObligationsOptions` default (W005-4). Moment 4.
- A multi-reminder cadence before escalation, rather than the single MVP reminder. Moment 2.

### `alternate-path-failure`

- The literal duplicate-`SettlementCompleted` race before the first start commits (the determinism makes it a no-op; the handling detail is W005 Phase 5's idempotent-start scenario). Moment 1.
- The stale-reminder race: `SendShippingReminder` arriving after `TrackingInfoProvided` already transitioned the saga. Moment 2.
- A carrier-rejected or malformed tracking number on `ProvideTracking`. Moment 3.
- The deadline-escalation path (W005 slice 5.5) and its late-tracking recovery (slice 5.6), where the seller does not ship inside the deadline. Moments 3 and 4. Warrants its own narrative.

### `separate-narrative`

- The three dispute resolutions — `Refund` and `Closed` (terminal), `Extension` (non-terminal, the one path that does not call `MarkCompleted()`) — opened by the winner (`NonDelivery` / `ItemCondition`) or by Operations from an escalation (`MissedDeadline`), W005 slices 5.7 and 5.8. Moment 4.
- The Operations-staff vantage on the escalation and dispute queues (`OperationsObligationsView`). Moment 4. The same post-sale floor told from the operator's perspective.

### `implementation-detail`

- The Wolverine `Saga` host primitive beyond naming it; the hosting decision is the ADR-022 candidate (Decision 3), to be authored in the M6-S1 design opening. Cast / all Moments.
- The exact demo second-counts on the `ShipByDeadline`, reminder, and auto-confirm offsets; `ObligationsOptions` territory. Setting, Moments 1 and 4.

### `UX-or-UI-detail`

- The seller dashboard, the "Provide Tracking" form, and the reminder banner screen designs (M8 frontend MVP). Moments 1-3.

## Retrospective

### What this narrative establishes

Narrative 006 is the first CritterBids narrative authored *before* its BC ships under the ADR 017 design sequence (workshop → narrative → prompt → code), rather than as a retroactive backfill of lived code. It is the journey-prose companion to W005 and the source the M6-S1 implementation prompt cites. It dramatises the project's canonical cancellable-scheduled-message saga — the pattern W005 named as Obligations' defining shape, distinct from Settlement's linear phased workflow (W003) — from the seller's vantage, showing the cancellation mechanic (`TrackingInfoProvided` cancelling the pending `SendDeadlineEscalation`) as the protagonist-protecting behavior it is rather than as a framework detail.

### Audit posture

Fully forward-spec. There is no `src/CritterBids.Obligations/` to audit Moment-by-Moment; the Obligations BC is the M6-S1 ship target and the first OpenSpec-adopting BC (ADR 021). The audit surface is W005 (`005-obligations-bc-deep-dive.md`) and `005-scenarios.md` for the saga's shape and the four happy-path slices (5.1-5.4), plus the upstream `SettlementCompleted` contract (W003 §1) the saga consumes and the downstream Relay broadcasts the seller and winner perceive. Cross-narrative consistency with narratives 002 (the $49.50 payout, the `SettlementCompleted` payload) and 005 (the $55.00 hammer, the keyboard's identity, GreyOwl12 as seller) is the principal consistency surface; the dollar amounts, listing identity, and participant identities carry through unchanged. No `code-update` findings are possible against Obligations by structural impossibility; any drift discovered at M6-S1 implementation time routes per ADR 016's four lanes and amends this narrative via a Document History row (ADR 020).

### Voice and grain

Mixed observer/active protagonist. GreyOwl12 observes in Moments 1, 2, and 4 (the saga starts, the reminder fires, the auto-confirm closes the loop — all system-driven) and acts once in Moment 3 (entering tracking). This is a narrower active surface than the bidder narratives (001, 003) and wider than the pure observer-protagonist of narrative 005. The narrator's responsibility-split — GreyOwl12's dashboard window versus the saga-internal dramatisation underneath — is the technique inherited from narrative 005 and applied here to a saga whose work is mostly clock-driven. The auto-confirm Moment (4) is the narrative's purest temporal-automation beat: no human acts, and the journey voice holds by dramatising what the passage of time does to the obligation.

### Decisions exercised (not made here)

The five W005 decisions are the design record; this narrative dramatises four of them on the happy path and consciously defers the fifth's non-terminal branch. Decision 1 (auto-confirm, no buyer command) drives Moment 4. Decision 2 (one reminder, then non-terminal escalation) drives Moment 2, with the escalation half deferred as `alternate-path-failure`. Decision 3 (Wolverine Saga hosting) is named in Cast and left to ADR-022. Decision 4 (`ObligationsOptions` real + demo durations) drives the Setting's demo-mode framing. Decision 5 (three dispute resolutions, `Extension` non-terminal) is named in Moment 4's "why" as the path GreyOwl12 never sees and deferred whole as `separate-narrative` — the narrative deliberately keeps the one non-`MarkCompleted()` path off-page so the happy-path terminal reads as the ordinary case.

### Follow-ups generated

- This narrative completes the inverse index W005's "Narrative Cross-References" section was holding open; that section's back-reference to narrative 006 can now be confirmed.
- The M6-S1 implementation prompt cites this narrative plus W005 and authors ADR-022 (Obligations saga hosting). The prompt carries the `## Spec delta` section per ADR 020; any amendment this narrative needs at implementation time lands as a Document History row.
- The escalation/recovery narrative (W005 slices 5.5, 5.6) and the dispute narrative (slices 5.7, 5.8) are the two `alternate-path-failure` / `separate-narrative` successors flagged above; neither is required for M6-S1.

### Narrative status

**Complete (v0.1, 2026-05-28).** Four Moments covering W005 slices 5.1-5.4, cumulative deferred section, retrospective. First forward-of-code narrative under the ADR 017 sequence; first narrative inside the Obligations BC; first dramatisation of the cancellable-scheduled-message saga from the seller's vantage. Mixed observer/active protagonist Voice. Status set to `accepted` at session-close commit.

## Document History

- **2026-05-28** — `M6 Obligations BC opening (W005 design sequence)`: Initial authoring as ADR 017 step 4, following the W005 Event Modeling workshop. Four Moments dramatising the post-sale coordination happy path (saga start, single reminder, seller provides tracking with escalation-timer cancellation, clock-triggered auto-confirm to `ObligationFulfilled` + `MarkCompleted()`). Forward-spec against the unshipped Obligations BC; audit surface is W005 + `005-scenarios.md`. Cross-narrative continuity with narratives 002 ($49.50 payout, `SettlementCompleted` payload) and 005 ($55.00 hammer, keyboard identity, GreyOwl12 as seller). Escalation/recovery (W005 5.5/5.6) and disputes (5.7/5.8) deferred to successor narratives.
