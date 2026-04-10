# Workshop 003 — Settlement BC Scenarios (Given/When/Then)

Companion to `003-settlement-bc-deep-dive.md`, Phase 3.
Implementation-ready scenarios for all Settlement BC components: the decider (pure functions), the evolver (pure functions), the `PendingSettlement` projection, and end-to-end workflow integration.

**Conventions:**
- Placeholder IDs for readability: `listing-A`, `participant-001` (seller), `participant-002` (winner), `settlement-001` (= `UuidV5(AuctionsNamespace, "settlement:listing-A")`)
- Timestamps as relative offsets from bidding open (e.g., `T+5:05`) or absolute labels like `T-days`
- Platform fee: 10% (MVP default)
- Listing-A configuration: `ReservePrice: 50.00`, `BuyItNowPrice: 100.00`, hammer price on sale: `85.00`

**Test structure:**
- **Decider tests (Sections 1–6):** Pure function tests. No framework, no harness, no I/O. Written as `Decide(state, command) → events`. Each is a one-line assertion in production code.
- **Evolver tests (Section 7):** Pure function tests of state folding. `Evolve(state, event) → new state`.
- **Projection tests (Section 8):** Integration tests against the `PendingSettlement` read model. Verify the row state after each event.
- **Workflow integration tests (Section 9):** End-to-end scenarios exercising the full settlement flow via the hosting framework (Wolverine Saga or `ProcessManager<TState>`).

The first three sections are the reason the decider pattern earns its keep: **42 scenarios, all of them trivially testable without any framework at all.**

---

## 1. Decider — Initiation

### 1.1 Initiate settlement from `ListingSold` (bidding source)

```
Given:  state = null
        (no prior settlement for listing-A)

When:   Decide(null, InitiateSettlement {
          SettlementId: settlement-001,
          ListingId: listing-A,
          WinnerId: participant-002,
          SellerId: participant-001,
          Price: 85.00,
          Source: Bidding,
          ReservePrice: 50.00,
          FeePercentage: 10.0
        })

Then:   [
          SettlementInitiated {
            SettlementId: settlement-001,
            ListingId: listing-A,
            WinnerId: participant-002,
            SellerId: participant-001,
            Price: 85.00,
            Source: Bidding,
            InitiatedAt: <now>
          }
        ]
```

---

### 1.2 Initiate settlement from `BuyItNowPurchased`

```
Given:  state = null

When:   Decide(null, InitiateSettlement {
          SettlementId: settlement-001,
          ListingId: listing-A,
          WinnerId: participant-003,
          SellerId: participant-001,
          Price: 100.00,
          Source: BuyItNow,
          ReservePrice: 50.00,
          FeePercentage: 10.0
        })

Then:   [
          SettlementInitiated {
            ...
            Source: BuyItNow,
            Price: 100.00,
            ...
          }
        ]
```

> The `Source` field flows through to the emitted event. The evolver (Section 7) uses this field to branch the initial state.

---

### 1.3 Initiate when state already exists — invalid transition

```
Given:  state = SettlementState.Initiated { ...fields... }

When:   Decide(state, InitiateSettlement { ... })

Then:   throws InvalidSettlementTransitionException
```

> The decider rejects re-initiation. Idempotent replay is handled at the persistence layer (Wolverine inbox deduplication, deterministic SettlementId), not inside the decider.

---

## 2. Decider — Reserve Check

### 2.1 Reserve met (hammer price exceeds reserve)

```
Given:  state = SettlementState.Initiated {
          HammerPrice: 85.00,
          ReservePrice: 50.00
        }

When:   Decide(state, CheckReserve)

Then:   [
          ReserveCheckCompleted {
            HammerPrice: 85.00,
            ReservePrice: 50.00,
            WasMet: true,
            CompletedAt: <now>
          }
        ]
```

---

### 2.2 Reserve not met — defense-in-depth path

```
Given:  state = SettlementState.Initiated {
          HammerPrice: 45.00,
          ReservePrice: 50.00
        }

When:   Decide(state, CheckReserve)

Then:   [
          ReserveCheckCompleted {
            HammerPrice: 45.00,
            ReservePrice: 50.00,
            WasMet: false,
            CompletedAt: <now>
          }
        ]
```

> This path should never fire in practice — Auctions only emits `ListingSold` after confirming the reserve was met. The scenario exists because Settlement is the financial authority and verifies independently.

---

### 2.3 No reserve set (null) — always met

```
Given:  state = SettlementState.Initiated {
          HammerPrice: 30.00,
          ReservePrice: null
        }

When:   Decide(state, CheckReserve)

Then:   [
          ReserveCheckCompleted {
            HammerPrice: 30.00,
            ReservePrice: null,
            WasMet: true,
            CompletedAt: <now>
          }
        ]
```

> Listings without a reserve are common. The reserve check is a no-op, always returning met.

---

### 2.4 CheckReserve from wrong state — invalid transition

```
Given:  state = SettlementState.WinnerCharged { ... }

When:   Decide(state, CheckReserve)

Then:   throws InvalidSettlementTransitionException
```

---

## 3. Decider — Charge Winner

### 3.1 Charge winner after reserve met

```
Given:  state = SettlementState.ReserveChecked(WasMet: true) {
          HammerPrice: 85.00,
          WinnerId: participant-002
        }

When:   Decide(state, ChargeWinner)

Then:   [
          WinnerCharged {
            SettlementId: settlement-001,
            WinnerId: participant-002,
            Amount: 85.00,
            ChargedAt: <now>
          }
        ]
```

---

### 3.2 Any command from `ReserveChecked(WasMet: false)` produces PaymentFailed

```
Given:  state = SettlementState.ReserveChecked(WasMet: false) {
          ListingId: listing-A,
          WinnerId: participant-002
        }

When:   Decide(state, ChargeWinner)

Then:   [
          PaymentFailed {
            SettlementId: settlement-001,
            ListingId: listing-A,
            WinnerId: participant-002,
            Reason: "ReserveNotMet",
            FailedAt: <now>
          }
        ]
```

> The decider's pattern match `(ReserveChecked { WasMet: false }, _)` catches any command in a failed-reserve state and produces the same terminal event. The `_` is deliberate — once reserve is not met, nothing else in the workflow can succeed.

---

### 3.3 ChargeWinner from WinnerCharged — invalid (double charge prevention)

```
Given:  state = SettlementState.WinnerCharged { ... }

When:   Decide(state, ChargeWinner)

Then:   throws InvalidSettlementTransitionException
```

---

### 3.4 ChargeWinner from Initiated — invalid (reserve check skipped)

```
Given:  state = SettlementState.Initiated { ... }

When:   Decide(state, ChargeWinner)

Then:   throws InvalidSettlementTransitionException
```

> The workflow must go through `CheckReserve` first. The only legitimate bypass is the Buy It Now path, which the evolver handles by starting the state in `ReserveChecked(WasMet: true)` directly.

---

## 4. Decider — Calculate Fee

### 4.1 Standard fee calculation (10%)

```
Given:  state = SettlementState.WinnerCharged {
          HammerPrice: 85.00,
          FeePercentage: 10.0
        }

When:   Decide(state, CalculateFee)

Then:   [
          FinalValueFeeCalculated {
            HammerPrice: 85.00,
            FeePercentage: 10.0,
            FeeAmount: 8.50,
            SellerPayout: 76.50,
            CalculatedAt: <now>
          }
        ]
```

---

### 4.2 Rounding edge case — banker's rounding on fractional cents

```
Given:  state = SettlementState.WinnerCharged {
          HammerPrice: 33.33,
          FeePercentage: 10.0
        }
        (10% of $33.33 = $3.333)

When:   Decide(state, CalculateFee)

Then:   [
          FinalValueFeeCalculated {
            HammerPrice: 33.33,
            FeePercentage: 10.0,
            FeeAmount: 3.33,          ← rounded to 2 decimal places
            SellerPayout: 30.00,       ← 33.33 - 3.33
            CalculatedAt: <now>
          }
        ]
```

> Math.Round with default MidpointRounding.ToEven. Seller and platform should never disagree on totals — the rounding rule is the single source of truth.

---

### 4.3 CalculateFee from already-calculated state — invalid

```
Given:  state = SettlementState.FeeCalculated(FeeAmount: 8.50, SellerPayout: 76.50) { ... }

When:   Decide(state, CalculateFee)

Then:   throws InvalidSettlementTransitionException
```

---

## 5. Decider — Issue Seller Payout

### 5.1 Issue payout after fee calculated

```
Given:  state = SettlementState.FeeCalculated(FeeAmount: 8.50, SellerPayout: 76.50) {
          SellerId: participant-001
        }

When:   Decide(state, IssueSellerPayout)

Then:   [
          SellerPayoutIssued {
            SettlementId: settlement-001,
            SellerId: participant-001,
            PayoutAmount: 76.50,
            FeeDeducted: 8.50,
            IssuedAt: <now>
          }
        ]
```

> Notice: `FeeAmount` and `SellerPayout` are **non-nullable fields on the `FeeCalculated` state type**. The decider can read them directly without null checks. This is the compile-time guarantee the decider pattern provides.

---

### 5.2 IssueSellerPayout from WinnerCharged — invalid (fee not calculated)

```
Given:  state = SettlementState.WinnerCharged { ... }

When:   Decide(state, IssueSellerPayout)

Then:   throws InvalidSettlementTransitionException
```

---

## 6. Decider — Complete Settlement

### 6.1 Complete after payout issued

```
Given:  state = SettlementState.PayoutIssued(FeeAmount: 8.50, SellerPayout: 76.50) {
          ListingId: listing-A,
          WinnerId: participant-002,
          SellerId: participant-001,
          HammerPrice: 85.00
        }

When:   Decide(state, CompleteSettlement)

Then:   [
          SettlementCompleted {
            SettlementId: settlement-001,
            ListingId: listing-A,
            WinnerId: participant-002,
            SellerId: participant-001,
            HammerPrice: 85.00,
            FeeAmount: 8.50,
            SellerPayout: 76.50,
            CompletedAt: <now>
          }
        ]
```

---

### 6.2 CompleteSettlement from FeeCalculated — invalid (payout not issued)

```
Given:  state = SettlementState.FeeCalculated(...) { ... }

When:   Decide(state, CompleteSettlement)

Then:   throws InvalidSettlementTransitionException
```

---

## 7. Evolver — State Transitions

### 7.1 Initial state from Bidding source

```
Given:  state = null

When:   Evolve(null, SettlementInitiated {
          Source: Bidding,
          SettlementId: settlement-001,
          ListingId: listing-A,
          WinnerId: participant-002,
          SellerId: participant-001,
          Price: 85.00,
          ReservePrice: 50.00,
          FeePercentage: 10.0
        })

Then:   SettlementState.Initiated {
          Id: settlement-001,
          ListingId: listing-A,
          WinnerId: participant-002,
          SellerId: participant-001,
          HammerPrice: 85.00,
          ReservePrice: 50.00,
          FeePercentage: 10.0
        }
```

---

### 7.2 Initial state from BuyItNow source — branches directly to ReserveChecked

```
Given:  state = null

When:   Evolve(null, SettlementInitiated {
          Source: BuyItNow,
          SettlementId: settlement-001,
          ListingId: listing-A,
          WinnerId: participant-003,
          SellerId: participant-001,
          Price: 100.00,
          ReservePrice: 50.00,
          FeePercentage: 10.0
        })

Then:   SettlementState.ReserveChecked(WasMet: true) {
          Id: settlement-001,
          ListingId: listing-A,
          WinnerId: participant-003,
          SellerId: participant-001,
          HammerPrice: 100.00,
          ReservePrice: 50.00,
          FeePercentage: 10.0
        }
```

> **This is the critical branching test.** The evolver handles the Buy It Now path by constructing a different initial state from the same event type. The key is matching on `e.Source`. If this test passes, the BIN path correctness is proven regardless of which framework hosts the workflow.

---

### 7.3 Initiated + ReserveCheckCompleted(WasMet: true) → ReserveChecked(true)

```
Given:  state = SettlementState.Initiated { ...fields... }

When:   Evolve(state, ReserveCheckCompleted { WasMet: true, ... })

Then:   SettlementState.ReserveChecked(WasMet: true) { ...same base fields... }
```

---

### 7.4 Initiated + ReserveCheckCompleted(WasMet: false) → ReserveChecked(false)

```
Given:  state = SettlementState.Initiated { HammerPrice: 45.00, ReservePrice: 50.00 }

When:   Evolve(state, ReserveCheckCompleted { WasMet: false })

Then:   SettlementState.ReserveChecked(WasMet: false) { ...same base fields... }
```

---

### 7.5 ReserveChecked(true) + WinnerCharged → WinnerCharged state

```
Given:  state = SettlementState.ReserveChecked(WasMet: true) { HammerPrice: 85.00, ... }

When:   Evolve(state, WinnerCharged { Amount: 85.00 })

Then:   SettlementState.WinnerCharged { HammerPrice: 85.00, ...same base fields... }
```

---

### 7.6 WinnerCharged + FinalValueFeeCalculated → FeeCalculated with populated fields

```
Given:  state = SettlementState.WinnerCharged { HammerPrice: 85.00, FeePercentage: 10.0, ... }

When:   Evolve(state, FinalValueFeeCalculated { FeeAmount: 8.50, SellerPayout: 76.50 })

Then:   SettlementState.FeeCalculated(FeeAmount: 8.50, SellerPayout: 76.50) {
          HammerPrice: 85.00,
          FeePercentage: 10.0,
          ...same base fields...
        }
```

> The target state type carries `FeeAmount` and `SellerPayout` as required (non-nullable) constructor parameters. After this fold, subsequent code can read them as plain decimals, not `decimal?`.

---

### 7.7 FeeCalculated + SellerPayoutIssued → PayoutIssued

```
Given:  state = SettlementState.FeeCalculated(FeeAmount: 8.50, SellerPayout: 76.50) { ... }

When:   Evolve(state, SellerPayoutIssued { PayoutAmount: 76.50, FeeDeducted: 8.50 })

Then:   SettlementState.PayoutIssued(FeeAmount: 8.50, SellerPayout: 76.50) {
          ...same base fields...
        }
```

---

### 7.8 PayoutIssued + SettlementCompleted → Completed

```
Given:  state = SettlementState.PayoutIssued(FeeAmount: 8.50, SellerPayout: 76.50) { ... }

When:   Evolve(state, SettlementCompleted { ... })

Then:   SettlementState.Completed { ...base fields... }
```

---

### 7.9 ReserveChecked(false) + PaymentFailed → Failed

```
Given:  state = SettlementState.ReserveChecked(WasMet: false) { ... }

When:   Evolve(state, PaymentFailed { Reason: "ReserveNotMet" })

Then:   SettlementState.Failed("ReserveNotMet") { ...base fields... }
```

---

### 7.10 Invalid evolution — event doesn't match current state

```
Given:  state = SettlementState.Initiated { ... }

When:   Evolve(state, WinnerCharged { ... })
        (skipping the reserve check)

Then:   throws InvalidSettlementEvolutionException
```

> This defends against event streams with gaps or reordering. If the evolver sees an event that doesn't fit the current state, something is fundamentally wrong with the stream — fail loudly.

---

## 8. PendingSettlement Projection

### 8.1 Create on ListingPublished

```
Given:  (pending_settlements table empty for listing-A)
        Platform config: FeePercentage = 10.0

When:   ListingPublished {
          ListingId: listing-A,
          SellerId: participant-001,
          ReservePrice: 50.00,
          BuyItNowPrice: 100.00,
          PublishedAt: T-days
        }
        arrives at the projection handler

Then:   pending_settlements contains:
        PendingSettlement {
          ListingId: listing-A,
          SellerId: participant-001,
          ReservePrice: 50.00,
          BuyItNowPrice: 100.00,
          FeePercentage: 10.0,        ← read from platform config
          PublishedAt: T-days,
          Status: Pending
        }
```

---

### 8.2 FeePercentage is immutable after creation

```
Given:  pending_settlements contains PendingSettlement for listing-A with FeePercentage: 10.0
        Platform config changes: FeePercentage = 15.0

When:   ListingRevised {
          ListingId: listing-A,
          Changes: { BuyItNowPrice: 120.00 }
        }
        arrives at the projection handler

Then:   PendingSettlement row updated:
          BuyItNowPrice: 120.00
          FeePercentage: 10.0          ← UNCHANGED, still the original value
```

> Revision handling must not re-read platform config. The fee captured at publish time is the fee for the life of the listing.

---

### 8.3 ListingRevised updates mutable fields

```
Given:  PendingSettlement { ListingId: listing-A, ReservePrice: 50.00, BuyItNowPrice: 100.00 }

When:   ListingRevised { ListingId: listing-A, Changes: { ReservePrice: 75.00 } }

Then:   PendingSettlement { ReservePrice: 75.00, BuyItNowPrice: 100.00, ... }
```

---

### 8.4 Mark Expired on ListingPassed

```
Given:  PendingSettlement { ListingId: listing-A, Status: Pending }

When:   ListingPassed { ListingId: listing-A, Reason: "ReserveNotMet" }

Then:   PendingSettlement { Status: Expired, ...other fields unchanged... }
```

---

### 8.5 Mark Expired on ListingWithdrawn

```
Given:  PendingSettlement { ListingId: listing-A, Status: Pending }

When:   ListingWithdrawn { ListingId: listing-A, WithdrawnBy: "ops-staff" }

Then:   PendingSettlement { Status: Expired, ...other fields unchanged... }
```

---

### 8.6 Mark Consumed on SettlementCompleted

```
Given:  PendingSettlement { ListingId: listing-A, Status: Pending }

When:   SettlementCompleted { ListingId: listing-A, ... }

Then:   PendingSettlement { Status: Consumed, ...other fields unchanged... }
```

---

### 8.7 Mark Failed on PaymentFailed

```
Given:  PendingSettlement { ListingId: listing-A, Status: Pending }

When:   PaymentFailed { ListingId: listing-A, Reason: "ReserveNotMet" }

Then:   PendingSettlement { Status: Failed, ...other fields unchanged... }
```

> The `Failed` status was added to `PendingSettlementStatus` in Phase 2. It distinguishes "settlement attempted and failed" from "no settlement will ever run" (`Expired`).

---

### 8.8 Idempotent replay — ListingPublished arriving twice

```
Given:  PendingSettlement { ListingId: listing-A, Status: Pending, FeePercentage: 10.0 }
        Platform config: FeePercentage = 10.0 (same as before)

When:   ListingPublished { ListingId: listing-A, ... } arrives again

Then:   Either: (a) no-op (row already exists, handler detects and skips)
        Or: (b) upsert — same row, same data
```

> Idempotency is the projection handler's responsibility. Wolverine's inbox deduplication should prevent this in practice, but the handler must be safe against replay.

---

## 9. Workflow Integration — End to End

These scenarios exercise the full workflow via the hosting framework. They test the combined behavior of: the `PendingSettlement` projection, the triggering event handler, the decider/evolver, and the event stream persistence. Same scenarios should pass regardless of whether the workflow is hosted as a Wolverine Saga or a `ProcessManager<TState>`.

### 9.1 Full bidding happy path

```
Given:  PendingSettlement {
          ListingId: listing-A,
          SellerId: participant-001,
          ReservePrice: 50.00,
          FeePercentage: 10.0,
          Status: Pending
        }

When:   ListingSold {
          ListingId: listing-A,
          WinnerId: participant-002,
          HammerPrice: 85.00,
          BidCount: 12,
          SoldAt: T+5:05
        }
        is delivered to the Settlement workflow handler

Then:   Event stream for settlement-001 contains (in order):
        [
          SettlementInitiated { Source: Bidding, ... },
          ReserveCheckCompleted { WasMet: true, ... },
          WinnerCharged { Amount: 85.00, ... },
          FinalValueFeeCalculated { FeeAmount: 8.50, SellerPayout: 76.50, ... },
          SellerPayoutIssued { PayoutAmount: 76.50, FeeDeducted: 8.50, ... },
          SettlementCompleted { HammerPrice: 85.00, FeeAmount: 8.50, SellerPayout: 76.50, ... }
        ]

        Integration events published to bus:
          SellerPayoutIssued → Relay (notification)
          SettlementCompleted → Obligations (starts post-sale coordination)

        PendingSettlement for listing-A updated:
          Status: Consumed

        Workflow terminated (Saga: MarkCompleted / ProcessManager: Completed state is terminal)
```

---

### 9.2 Buy It Now happy path

```
Given:  PendingSettlement {
          ListingId: listing-A,
          SellerId: participant-001,
          ReservePrice: 50.00,
          BuyItNowPrice: 100.00,
          FeePercentage: 10.0,
          Status: Pending
        }

When:   BuyItNowPurchased {
          ListingId: listing-A,
          BuyerId: participant-003,
          Price: 100.00,
          PurchasedAt: T+0:20
        }
        is delivered to the Settlement workflow handler

Then:   Event stream for settlement-001 contains (in order):
        [
          SettlementInitiated { Source: BuyItNow, Price: 100.00, ... },
          // NO ReserveCheckCompleted — skipped by evolver branching
          WinnerCharged { Amount: 100.00, WinnerId: participant-003, ... },
          FinalValueFeeCalculated { FeeAmount: 10.00, SellerPayout: 90.00, ... },
          SellerPayoutIssued { PayoutAmount: 90.00, FeeDeducted: 10.00, ... },
          SettlementCompleted { HammerPrice: 100.00, FeeAmount: 10.00, SellerPayout: 90.00, ... }
        ]

        PendingSettlement Status: Consumed
```

> The absence of `ReserveCheckCompleted` in the stream is intentional and meaningful. An audit query "show me all BIN settlements" is literally "event streams where no `ReserveCheckCompleted` appears."

---

### 9.3 Reserve-not-met defense in depth

```
Given:  PendingSettlement {
          ListingId: listing-A,
          ReservePrice: 50.00,
          ...
        }

When:   ListingSold {
          ListingId: listing-A,
          WinnerId: participant-002,
          HammerPrice: 45.00,         ← malformed: below reserve
          ...
        }
        arrives at the Settlement workflow handler

Then:   Event stream for settlement-001 contains:
        [
          SettlementInitiated { Source: Bidding, Price: 45.00, ... },
          ReserveCheckCompleted { HammerPrice: 45.00, ReservePrice: 50.00, WasMet: false },
          PaymentFailed { Reason: "ReserveNotMet", ... }
        ]

        Integration event published:
          PaymentFailed → Operations (ops dashboard flags for attention)

        PendingSettlement Status: Failed

        NO events produced:
          WinnerCharged, FinalValueFeeCalculated, SellerPayoutIssued, SettlementCompleted

        Workflow terminated in Failed state
```

---

### 9.4 PendingSettlement not found — retry via Wolverine

```
Given:  pending_settlements table empty for listing-A
        (race condition: ListingSold arrived before projection caught up)

When:   ListingSold { ListingId: listing-A, ... } delivered to handler

Then:   Handler throws PendingSettlementNotFoundException
        Wolverine's retry policy re-queues the message with backoff
        On retry, after projection catches up, handler succeeds
        Event stream contains the full happy path (as in scenario 9.1)
```

---

### 9.5 Idempotent re-delivery of ListingSold

```
Given:  pending_settlements { ListingId: listing-A, Status: Consumed }
        Event stream for settlement-001 already contains the full happy path
        (settlement already completed)

When:   ListingSold { ListingId: listing-A, ... } is re-delivered
        (Wolverine inbox deduplication failed or message source bug)

Then:   SettlementId computed deterministically: same as before
        Handler loads existing event stream for settlement-001
        Evolver rebuilds state → SettlementState.Completed
        Handler detects already-completed state, no-ops
        (Or: decider rejects InitiateSettlement on non-null state — scenario 1.3)

        No duplicate events, no double payout, no duplicate PendingSettlement updates
```

> Idempotency is enforced at two layers: the deterministic `SettlementId` (same ListingId always produces the same settlement ID) and the decider's refusal to re-initiate an existing settlement.

---

## Scenario Coverage Summary

| Section | Component | Scenarios | Valid Transitions | Invalid/Edge |
|---|---|---|---|---|
| 1 | Decider — Initiation | 3 | 2 | 1 |
| 2 | Decider — Reserve Check | 4 | 3 | 1 |
| 3 | Decider — Charge Winner | 4 | 2 | 2 |
| 4 | Decider — Calculate Fee | 3 | 2 | 1 |
| 5 | Decider — Seller Payout | 2 | 1 | 1 |
| 6 | Decider — Complete | 2 | 1 | 1 |
| 7 | Evolver | 10 | 9 | 1 |
| 8 | PendingSettlement Projection | 8 | 8 | 0 |
| 9 | Workflow Integration | 5 | 3 | 2 |
| **Total** | | **41** | **31** | **10** |

**41 scenarios.** The decider and evolver account for 28 of them (68%) — and every one of those is a pure-function test with no framework setup, no harness, no async, no I/O. You write `Decide(state, command).Should().BeEquivalentTo([expectedEvent])` and you're done. This is the payoff for designing around decider semantics.

The 8 projection scenarios need a real Polecat session to exercise, but they're simple CRUD verifications — a clean integration test pattern using Testcontainers.

The 5 workflow integration scenarios are the ones that'll require framework-specific test harness code. Whichever framework hosts the workflow (Wolverine Saga or `ProcessManager<TState>`), the expected outcomes are identical.

### Scenarios That Resolved Workshop 003 Questions

| Question | Resolved By |
|---|---|
| Source of FeePercentage (Phase 1 Q6) | 8.1 (created on publish), 8.2 (immutable on revision) |
| PendingSettlement cleanup (Phase 1 Q7) | 8.4–8.7 (four terminal transitions) |
| SellerPayoutIssued failure (Phase 1 Q1) | 9.4 (retry via Wolverine) |
| Reserve-not-met defense (Phase 2 Alt B) | 2.2, 3.2, 7.9, 8.7, 9.3 |
| Buy It Now path branching (Phase 2 Alt A) | 1.2, 7.2, 9.2 |
| Rounding consistency | 4.2 (banker's rounding) |
| Idempotency guarantees | 1.3, 9.5 |

### Remaining Open Questions (tracked, not resolved here)

| # | Question | Persona | Target |
|---|----------|---------|--------|
| Phase 2 #8 | Failed pending settlements visibility in ops dashboard | `@UX`/`@QA` | Cross-BC (Operations workshop) |
| Phase 2 #9 | `PaymentFailed` event should carry `ListingId` explicitly | `@BackendDeveloper` | Event shape refinement — add to vocabulary pass |
| Phase 2 #10 | Continuation command scheduling — framework or application concern? | `@Architect` | Depends on `ProcessManager<TState>` framework design |
