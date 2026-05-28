# Post-sale obligations and notification fan-out

**Maturity:** **Partial.** The trace crosses from implemented code (Auctions outcome events, Settlement saga, Listings catalog) into Planned-only territory (Obligations, Relay, Operations) at the hop noted below.

## Trigger

Any of the three Settlement-out integration events emitted at the saga's terminal phase:

- `SettlementCompleted` (sold path, both bidding and BIN sources)
- `SellerPayoutIssued` (sold path; emitted at the `PayoutIssued` phase immediately before `Completed`)
- `PaymentFailed` (failure path; M5 only produces it on the reserve-not-met branch)

Source: `src/CritterBids.Settlement/SettlementSaga.cs:149-233`.

## Hops (implemented portion)

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 1 | Settlement (saga) | Phase 5 → 6 transition emits `SellerPayoutIssued` via `OutgoingMessages` AND appends to financial event stream | `src/CritterBids.Settlement/SettlementSaga.cs:149-174` |
| 2 | Settlement (saga) | Phase 6 → terminal emits `SettlementCompleted` via `OutgoingMessages` AND appends to financial event stream; `MarkCompleted()` | `src/CritterBids.Settlement/SettlementSaga.cs:208-233` |
| 2' | Settlement (saga, failure branch) | Reserve-not-met emits `PaymentFailed` via `OutgoingMessages` AND appends to financial event stream; `MarkCompleted()` | `src/CritterBids.Settlement/SettlementSaga.cs:176-206` |
| 3 | (transport) | RabbitMQ publish routes | `SellerPayoutIssued` → `relay-settlement-events`; `SettlementCompleted` → `listings-settlement-events`; `PaymentFailed` → `operations-settlement-events` | `src/CritterBids.Api/Program.cs:145-163` |
| 4 | Settlement (self-handler) | `BidderCreditViewHandler.Handle(WinnerCharged)` (in-process via local dispatch under `MultipleHandlerBehavior.Separated`; fires earlier in the saga at phase 3, not at terminal) — debits `RemainingCredit` | `src/CritterBids.Settlement/BidderCreditViewHandler.cs:55-86` |
| 5 | Settlement (self-handler) | `PendingSettlementHandler.Handle(SettlementCompleted)` — `Pending → Consumed`; or `Handle(PaymentFailed)` — `Pending → Failed` | `src/CritterBids.Settlement/PendingSettlementHandler.cs:91-115` |
| 6 | Listings | `SettlementStatusHandler.Handle(SettlementCompleted)` — catalog `Status: "Sold" → "Settled"`, stamps `SettledAt` | `bcs/listings.md`, `tests/CritterBids.Listings.Tests/SettlementStatusHandlerTests.cs` |

## ⚠️ The trace crosses into Planned-only code at hop 7

| # | BC | Step | Event / command | Status |
|---|---|---|---|---|
| 7a | **Relay** | `BiddingHub` SignalR push to winner: `{ type: "SettlementCompleted", listingId, hammerPrice, remainingCredit }`. `remainingCredit` composed by reading Settlement's `BidderCreditView` projection. | **Planned-only.** `src/CritterBids.Relay` project does not exist. Declared in `docs/vision/bounded-contexts.md:165-186`. The `relay-settlement-events` queue route exists in `Program.cs:155-156` but no `ListenToRabbitQueue` consumer is wired. Per `Contracts.Settlement.SellerPayoutIssued.cs:14-15` ("slice 6.3 implementation lands when Relay ships"). |
| 7b | **Relay** | SignalR push to seller for `SellerPayoutIssued` — payout amount + fee deducted | **Planned-only.** Same status as 7a. |
| 7c | **Operations** | Live-board failed-settlement indicator for `PaymentFailed` — drives ops-dashboard diagnostic copy from `Reason` field | **Planned-only.** `src/CritterBids.Operations` project does not exist. Declared in `docs/vision/bounded-contexts.md:189-211`. The `operations-settlement-events` queue route exists in `Program.cs:162-163` but no `ListenToRabbitQueue` consumer is wired. Per `Contracts.Settlement.PaymentFailed.cs:13-16`. |
| 8 | **Obligations** | Post-sale obligation tracking — buyer payment confirmation timer; seller shipment confirmation timer; obligation reminders; potential dispute opening | **Planned-only.** `src/CritterBids.Obligations` project does not exist. Declared in `docs/vision/bounded-contexts.md:139-162`. No queue route, no consumer wired. The vision doc describes this BC consuming `SettlementCompleted` and emitting reminder/timeout events; nothing is registered. |
| 9 | **Operations** | Live-board "settled" indicator for the auction operator's dashboard | **Planned-only.** Same status as 7c. |

## Cross-BC publishers without consumers (recorded in `gaps-and-drift.md`)

Per `Program.cs` audit:

| Publisher | Event | Queue route | Consumer status |
|---|---|---|---|
| Settlement | `SellerPayoutIssued` | `relay-settlement-events` | No `ListenToRabbitQueue` — Relay post-M5 |
| Settlement | `PaymentFailed` | `operations-settlement-events` | No `ListenToRabbitQueue` — Operations post-M5 |
| Auctions | `ProxyBidRegistered` | **No `PublishMessage` route** | Lands in `tracked.NoRoutes` — Relay post-M5 (see [`proxy-bidding.md`](./proxy-bidding.md)) |
| Auctions | `ProxyBidExhausted` | **No `PublishMessage` route** | Lands in `tracked.NoRoutes` — Relay post-M5 |

## Implementation status by hop

```
Auctions (implemented)
   ↓ ListingSold / BuyItNowPurchased / ListingPassed
Settlement (implemented)
   ↓ SettlementCompleted / SellerPayoutIssued / PaymentFailed
─────────────────────────────────────────────────────────────
implemented downstream consumers:
   • Listings.SettlementStatusHandler (catalog Status → "Settled")
   • Settlement.PendingSettlementHandler (Pending → Consumed/Failed)
   • Settlement.BidderCreditViewHandler (WinnerCharged debit, fires earlier in saga)
─────────────────────────────────────────────────────────────
planned-only downstream consumers:
   • Relay BC: SignalR push to winner/seller — project absent
   • Operations BC: live-board, dashboard, demo reset — project absent
   • Obligations BC: post-sale coordination, reminders, timers — project absent
```

## What downstream traffic would look like (per vision docs and contract docstrings — not implemented)

From `docs/vision/bounded-contexts.md`:

- **Obligations (lines 139-162):** Buyer payment obligation with reminder chain; seller shipment obligation with reminder chain; dispute opening. Triggered by `SettlementCompleted`. Would emit `BuyerPaymentReceived`, `ShipmentConfirmed`, `ObligationOverdue`, `DisputeOpened`.
- **Relay (lines 165-186):** SignalR push for outbid, won-item, payment-confirmed, shipment-shipped, etc. Subscribes to most domain events via the integration bus.
- **Operations (lines 189-211):** Cross-BC read models for ops dashboard; demo reset capability.

These behaviors are not extracted in this dossier because no code exists for them. The vision-doc text is the canonical declarative source; reading further would be inference, which Phase 1 deliberately excludes.

## Outcome

In the implemented portion:

- `CatalogListingView.Status == "Settled"` with `SettledAt`.
- `PendingSettlement.Status == "Consumed"` (or `Failed` on the failure branch).
- `BidderCreditView.RemainingCredit` debited by the hammer-price amount, with `LastChargedSettlementId` set.
- Financial event stream contains the full audit (6 events sold path, 5 events BIN path, 3 events failure path).

In the planned portion, no participant-observable behavior occurs — the messages land in `tracked.NoRoutes` or in queues with no listener.

## Notes

- The financial event stream is the audit ground per W003 §"Financial Event Stream" — it persists at terminal state and is never deleted, even after `MarkCompleted()` removes the saga document.
- `MultipleHandlerBehavior.Separated` (`Program.cs:20`) means each terminal event's local in-process consumer (Settlement self-handlers) runs on its own sticky queue alongside the cross-BC RabbitMQ publish. In tests, this requires `SendMessageAndWaitAsync` rather than `InvokeMessageAndWaitAsync`.
- The `PendingSettlement` projection's `Failed` status (added in W003 Phase 2 per `PendingSettlementStatus.cs:5-8`) is distinct from `Expired` so consumers can tell "settlement attempted and failed" from "no settlement will ever run".
