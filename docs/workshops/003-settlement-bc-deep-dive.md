# Workshop 003 — Settlement BC Deep Dive

**Type:** BC-Focused (vertical depth)
**Date started:** 2026-04-09
**Status:** In progress — Phase 1

**Scope:** The Settlement BC internals. Settlement workflow design (including the explicit Wolverine Saga vs. Process Manager comparison), the pending settlement projection, compensation paths, and the financial authority relationship with Participants and Auctions.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. `@ProductOwner` on standby.

**Prerequisites:** Workshops 001 and 002 completed. This workshop closes the loop on the reserve check authority decision from W002 by addressing Settlement's side of it.

**Parked questions from prior workshops targeting this BC:**

| # | Source | Question |
|---|--------|----------|
| 5 (W001) | `@QA`/`@Architect` | Reserve check authority: Auctions vs Settlement — *partially resolved in W002 Phase 1 from the Auctions side. This workshop completes it from Settlement's side.* |

**Special focus this workshop:** Erik (JasperFx core team) is actively designing a `ProcessManager<TState>` framework proposal for Wolverine that draws from Emmett's `Workflow` type. Settlement is a linear, phased workflow with explicit state transitions — a natural candidate to explore the decider-style process manager pattern against the traditional Wolverine Saga. This workshop includes an explicit comparison. The sketches of the process manager approach are **conceptual only** — the actual `ProcessManager<TState>` framework API lives in Erik's working proposal, not in this document.

---

## What Prior Workshops Established

From the vision docs and earlier workshops, Settlement has:

**Storage:** SQL Server via Polecat — financial event streams, audit reporting.

**Integration in:** `ListingSold`, `BuyItNowPurchased` (from Auctions); `ListingPublished` (from Selling, for reserve value and fee configuration).

**Integration out:** `SellerPayoutIssued`, `PaymentFailed`, `SettlementCompleted`.

**Internal events:** `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`.

**Key role established in prior workshops:** Settlement is financially authoritative. Auctions publishes `ReserveMet` as a real-time UX signal. Settlement performs the binding reserve comparison. Same source data, different authority. If they ever disagreed (shouldn't in practice), Settlement wins.

**One P0 slice assigned in W001:** Slice 6.1 (Settlement saga happy path from `ListingSold`). Two P1 slices: 6.2 (settlement from Buy It Now) and 6.3 (seller payout notification via Relay).

---

## Phase 1 — Brain Dump: Internal Structure

The Settlement BC is simpler than Auctions in component count (one workflow, one projection — no DCB, no competing sagas) but richer in a different dimension: every Settlement event represents a financial commitment. The design tension isn't concurrency, it's **getting the phases right and handling failures without losing money**.

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Settlement BC                                               │
│                                                             │
│  ┌────────────────────────────┐                             │
│  │ PendingSettlement           │ ← Polecat projection       │
│  │ Projection                  │   (built from              │
│  │ (SellerId, ReservePrice,    │    ListingPublished)       │
│  │  BuyItNowPrice, FeePct)     │                            │
│  └──────────────┬─────────────┘                             │
│                 │ loaded when settlement starts              │
│                 ▼                                            │
│  ┌─────────────────────────────────────────────────┐        │
│  │ Settlement Workflow                              │        │
│  │                                                  │        │
│  │ Initiated → ReserveChecked → WinnerCharged →     │        │
│  │   FeeCalculated → PayoutIssued → Completed       │        │
│  │                                                  │        │
│  │ (or at any step: → Failed)                       │        │
│  └─────────────────────────────────────────────────┘        │
│                                                              │
│  ┌────────────────────────────┐                              │
│  │ Financial Event Stream      │ ← append-only audit log    │
│  │ (all settlement events      │   (Polecat event store)    │
│  │  per settlement instance)   │                             │
│  └────────────────────────────┘                              │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

Three concerns: (1) preparing settlement data from published listings, (2) running the settlement workflow when a listing resolves, (3) maintaining the financial audit trail.

### Part 1: The PendingSettlement Projection

**The problem it solves:** Settlement needs the reserve value, fee percentage, and seller identity when `ListingSold` arrives. That data was established way back at `ListingPublished`, possibly days earlier. Settlement can't reach back into the Selling BC to fetch it at resolution time — that's a BC boundary violation.

**The solution:** Settlement maintains its own projection built from `ListingPublished` events. When `ListingPublished` arrives over the bus, Settlement's projection handler writes a row to a `pending_settlements` table in SQL Server (Polecat).

**Schema sketch:**

```csharp
public sealed record PendingSettlement
{
    public Guid ListingId { get; init; }      // primary key
    public Guid SellerId { get; init; }
    public decimal? ReservePrice { get; init; }  // nullable — no reserve is valid
    public decimal? BuyItNowPrice { get; init; }
    public decimal FeePercentage { get; init; }   // e.g., 10.0 for 10%
    public DateTimeOffset PublishedAt { get; init; }
    public PendingSettlementStatus Status { get; init; }
}

public enum PendingSettlementStatus
{
    Pending,      // waiting for ListingSold / BuyItNowPurchased
    Consumed,     // a settlement has started against this row
    Expired       // listing passed/withdrawn — no settlement will happen
}
```

**Lifecycle:**

- `ListingPublished` → insert `PendingSettlement` with `Status: Pending`
- `ListingSold` or `BuyItNowPurchased` → load by `ListingId`, start the settlement workflow, mark `Consumed`
- `ListingPassed` or `ListingWithdrawn` → mark `Expired` (no settlement will run)
- `ListingRevised` → update the row (e.g., seller changed the reserve mid-listing)

**`@Architect` — why a projection, not an event-sourced aggregate?**

A PendingSettlement isn't a business entity with a lifecycle the domain cares about. It's a derived read model — a convenient lookup that lets Settlement avoid querying across BC boundaries. A plain Polecat document projection is the right primitive. If we used an event-sourced aggregate here, we'd be inventing domain events for something that's really just "I saw a ListingPublished event and cached the parts I need."

**`@BackendDeveloper` note on Polecat:** Marten and Polecat have feature parity for projections. The projection class looks essentially identical; only the session type and configuration differ. No special Polecat patterns needed here.

**`@QA` — race condition question:** What if `ListingSold` arrives before the `PendingSettlement` projection has caught up from `ListingPublished`?

In practice, this should never happen — `ListingPublished` happens hours or days before `ListingSold`, giving the projection plenty of time to catch up. But for correctness under pathological conditions (e.g., a Flash Session where a listing is published, attached, started, and Buy-It-Now'd within seconds), Settlement should handle the "no pending settlement found" case gracefully. Options:

**Option A:** The settlement workflow retries with backoff when the pending settlement isn't found. Wolverine's retry policies handle this natively.

**Option B:** The projection is treated as eventually consistent, and the workflow blocks briefly on first attempt.

**Option C:** `ListingPublished` and the start of a Flash Session are gated so the projection must catch up before bidding can open.

> **Decision: Option A adopted.** Settlement workflow retries on "PendingSettlement not found" with exponential backoff. Wolverine's inbox/outbox machinery makes this natural — the triggering event stays in the queue until the handler succeeds. In practice this path should essentially never fire, but the retry guarantees correctness if the projection is a few milliseconds behind.

---

### Part 2: The Settlement Workflow — Two Approaches

This is the core of the Settlement BC and the most interesting design question in this workshop. The workflow is:

```
Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed
                                    │
                                    └── (credit check fails) → Failed
```

Six events, linear progression, one failure exit point. It's a textbook workflow — and the question is whether to model it as a traditional Wolverine Saga or as an explicit decider-style process manager.

#### Approach A: Traditional Wolverine Saga

```csharp
public sealed class SettlementSaga : Saga
{
    public Guid Id { get; set; }  // = SettlementId (deterministic from ListingId)

    public Guid ListingId { get; set; }
    public Guid WinnerId { get; set; }
    public Guid SellerId { get; set; }
    public decimal HammerPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public decimal FeePercentage { get; set; }

    public decimal? FeeAmount { get; set; }
    public decimal? SellerPayout { get; set; }
    public bool ReserveWasMet { get; set; }

    public SettlementStatus Status { get; set; }
    public string? FailureReason { get; set; }
}

public enum SettlementStatus
{
    Initiated,
    ReserveChecked,
    WinnerCharged,
    FeeCalculated,
    PayoutIssued,
    Completed,
    Failed
}
```

**Handler entry points:**

```csharp
// Saga starts on ListingSold
public static async Task<(SettlementSaga, OutgoingMessages)> Start(
    ListingSold message,
    IDocumentSession session,
    CancellationToken ct)
{
    var pending = await session.LoadAsync<PendingSettlement>(message.ListingId, ct);
    if (pending is null) throw new PendingSettlementNotFoundException(message.ListingId);

    var saga = new SettlementSaga
    {
        Id = SettlementIdFor(message.ListingId),
        ListingId = message.ListingId,
        WinnerId = message.WinnerId,
        SellerId = pending.SellerId,
        HammerPrice = message.HammerPrice,
        ReservePrice = pending.ReservePrice,
        FeePercentage = pending.FeePercentage,
        Status = SettlementStatus.Initiated
    };

    var messages = new OutgoingMessages
    {
        new SettlementInitiated(saga.Id, saga.ListingId, saga.WinnerId,
            saga.SellerId, saga.HammerPrice, DateTimeOffset.UtcNow),
        new CheckReserve(saga.Id)  // self-send next step
    };

    return (saga, messages);
}

// Reserve check step
public OutgoingMessages Handle(CheckReserve command)
{
    var met = ReservePrice is null || HammerPrice >= ReservePrice;
    ReserveWasMet = met;
    Status = SettlementStatus.ReserveChecked;

    var events = new OutgoingMessages
    {
        new ReserveCheckCompleted(Id, HammerPrice, ReservePrice, met, DateTimeOffset.UtcNow)
    };

    if (met)
        events.Add(new ChargeWinner(Id));
    else
        events.Add(new FailSettlement(Id, "ReserveNotMet"));

    return events;
}

// Charge winner step
public OutgoingMessages Handle(ChargeWinner command)
{
    // MVP: virtual credit, no real payment processor.
    // Post-MVP: call payment processor here.
    Status = SettlementStatus.WinnerCharged;

    return new OutgoingMessages
    {
        new WinnerCharged(Id, WinnerId, HammerPrice, DateTimeOffset.UtcNow),
        new CalculateFee(Id)
    };
}

// Fee calculation
public OutgoingMessages Handle(CalculateFee command)
{
    FeeAmount = Math.Round(HammerPrice * (FeePercentage / 100m), 2);
    SellerPayout = HammerPrice - FeeAmount;
    Status = SettlementStatus.FeeCalculated;

    return new OutgoingMessages
    {
        new FinalValueFeeCalculated(Id, HammerPrice, FeePercentage,
            FeeAmount.Value, SellerPayout.Value, DateTimeOffset.UtcNow),
        new IssueSellerPayout(Id)
    };
}

// Seller payout
public OutgoingMessages Handle(IssueSellerPayout command)
{
    Status = SettlementStatus.PayoutIssued;

    return new OutgoingMessages
    {
        new SellerPayoutIssued(Id, SellerId, SellerPayout!.Value,
            FeeAmount!.Value, DateTimeOffset.UtcNow),
        new CompleteSettlement(Id)
    };
}

// Complete
public OutgoingMessages Handle(CompleteSettlement command)
{
    Status = SettlementStatus.Completed;
    MarkCompleted();

    return new OutgoingMessages
    {
        new SettlementCompleted(Id, ListingId, WinnerId, SellerId,
            HammerPrice, FeeAmount!.Value, SellerPayout!.Value,
            DateTimeOffset.UtcNow)
    };
}

// Failure exit
public OutgoingMessages Handle(FailSettlement command)
{
    Status = SettlementStatus.Failed;
    FailureReason = command.Reason;
    MarkCompleted();

    return new OutgoingMessages
    {
        new PaymentFailed(Id, ListingId, WinnerId, command.Reason,
            DateTimeOffset.UtcNow)
    };
}
```

**What's good:**

- Familiar Wolverine pattern. Anyone who knows the Critter Stack reads this and understands it.
- Each step is a separate handler, so the flow is easy to trace.
- Self-sending commands (`CheckReserve`, `ChargeWinner`, etc.) give each step its own retry/durability via Wolverine's inbox.
- `MarkCompleted()` terminates the saga cleanly.

**What's awkward:**

- The state machine is implicit — spread across seven handlers. You have to read them all to understand the full flow.
- `Status` is an enum with no compile-time guarantee that the right fields are populated in each state. `FeeAmount` is nullable because it's only set after `CalculateFee`. The type system doesn't enforce that you can't read it before then.
- Transitions are scattered. When does `Status` become `WinnerCharged`? You have to find the handler that sets it.
- Testing each step in isolation requires a saga harness — the `Handle` methods depend on saga state being set correctly first.
- The command-chaining pattern (`Handle` returns the next command) is implicit control flow.

#### Approach B: Decider-Style Process Manager

The decider pattern (from Jérémie Chassaing's work and used in Emmett's `Workflow` type) models the workflow as three pure functions: **Decide** (given state + command, return events), **Evolve** (given state + event, return new state), and an explicit state type that makes invalid transitions unrepresentable.

> **Sketch disclaimer:** The exact API of `ProcessManager<TState>` lives in Erik's in-progress JasperFx proposal. The code below is a general shape of the decider pattern — not a claim about how the framework will actually wire it up. Treat this as "what the logic would look like if it were pure functions" rather than "this is how you'll write it in Wolverine."

```csharp
// State as a discriminated union — each phase is its own type
public abstract record SettlementState
{
    public Guid Id { get; init; }
    public Guid ListingId { get; init; }
    public Guid WinnerId { get; init; }
    public Guid SellerId { get; init; }
    public decimal HammerPrice { get; init; }
    public decimal? ReservePrice { get; init; }
    public decimal FeePercentage { get; init; }

    public sealed record Initiated : SettlementState;
    public sealed record ReserveChecked(bool WasMet) : SettlementState;
    public sealed record WinnerCharged : SettlementState;  // reserve was met, winner debited
    public sealed record FeeCalculated(decimal FeeAmount, decimal SellerPayout) : SettlementState;
    public sealed record PayoutIssued(decimal FeeAmount, decimal SellerPayout) : SettlementState;
    public sealed record Completed : SettlementState;
    public sealed record Failed(string Reason) : SettlementState;
}

// Commands
public abstract record SettlementCommand(Guid SettlementId);
public sealed record InitiateSettlement(Guid SettlementId, Guid ListingId, Guid WinnerId,
    Guid SellerId, decimal HammerPrice, decimal? ReservePrice, decimal FeePercentage)
    : SettlementCommand(SettlementId);
public sealed record CheckReserve(Guid SettlementId) : SettlementCommand(SettlementId);
public sealed record ChargeWinner(Guid SettlementId) : SettlementCommand(SettlementId);
public sealed record CalculateFee(Guid SettlementId) : SettlementCommand(SettlementId);
public sealed record IssueSellerPayout(Guid SettlementId) : SettlementCommand(SettlementId);
public sealed record CompleteSettlement(Guid SettlementId) : SettlementCommand(SettlementId);

// Decide: pure function — given current state + command, produce events
public static class SettlementDecider
{
    public static IEnumerable<object> Decide(SettlementState state, SettlementCommand command)
        => (state, command) switch
        {
            // Initiation
            (null, InitiateSettlement cmd) =>
                [new SettlementInitiated(cmd.SettlementId, cmd.ListingId, cmd.WinnerId,
                    cmd.SellerId, cmd.HammerPrice, DateTimeOffset.UtcNow)],

            // Reserve check from Initiated
            (SettlementState.Initiated s, CheckReserve) =>
                DecideReserveCheck(s),

            // Charge winner — only valid if reserve was met
            (SettlementState.ReserveChecked { WasMet: true } s, ChargeWinner) =>
                [new WinnerCharged(s.Id, s.WinnerId, s.HammerPrice, DateTimeOffset.UtcNow)],

            // Reserve check failed — any further command fails the settlement
            (SettlementState.ReserveChecked { WasMet: false } s, _) =>
                [new PaymentFailed(s.Id, s.ListingId, s.WinnerId, "ReserveNotMet",
                    DateTimeOffset.UtcNow)],

            // Fee calculation
            (SettlementState.WinnerCharged s, CalculateFee) => CalculateFeeEvents(s),

            // Seller payout
            (SettlementState.FeeCalculated s, IssueSellerPayout) =>
                [new SellerPayoutIssued(s.Id, s.SellerId, s.SellerPayout, s.FeeAmount,
                    DateTimeOffset.UtcNow)],

            // Completion
            (SettlementState.PayoutIssued s, CompleteSettlement) =>
                [new SettlementCompleted(s.Id, s.ListingId, s.WinnerId, s.SellerId,
                    s.HammerPrice, s.FeeAmount, s.SellerPayout, DateTimeOffset.UtcNow)],

            // Anything else is an invalid transition
            _ => throw new InvalidSettlementTransitionException(state, command)
        };

    private static IEnumerable<object> DecideReserveCheck(SettlementState.Initiated s)
    {
        var met = s.ReservePrice is null || s.HammerPrice >= s.ReservePrice;
        yield return new ReserveCheckCompleted(s.Id, s.HammerPrice, s.ReservePrice, met,
            DateTimeOffset.UtcNow);
    }

    private static IEnumerable<object> CalculateFeeEvents(SettlementState.WinnerCharged s)
    {
        var feeAmount = Math.Round(s.HammerPrice * (s.FeePercentage / 100m), 2);
        var sellerPayout = s.HammerPrice - feeAmount;
        yield return new FinalValueFeeCalculated(s.Id, s.HammerPrice, s.FeePercentage,
            feeAmount, sellerPayout, DateTimeOffset.UtcNow);
    }
}

// Evolve: pure function — given state + event, fold into new state
public static class SettlementEvolver
{
    public static SettlementState Evolve(SettlementState? state, object evt)
        => (state, evt) switch
        {
            (null, SettlementInitiated e) => new SettlementState.Initiated
            {
                Id = e.SettlementId,
                ListingId = e.ListingId,
                WinnerId = e.WinnerId,
                SellerId = e.SellerId,
                HammerPrice = e.HammerPrice,
                // ReservePrice, FeePercentage loaded from PendingSettlement context
            },

            (SettlementState.Initiated s, ReserveCheckCompleted e) =>
                new SettlementState.ReserveChecked(e.WasMet)
                {
                    Id = s.Id, ListingId = s.ListingId, WinnerId = s.WinnerId,
                    SellerId = s.SellerId, HammerPrice = s.HammerPrice,
                    ReservePrice = s.ReservePrice, FeePercentage = s.FeePercentage
                },

            (SettlementState.ReserveChecked { WasMet: true } s, WinnerCharged _) =>
                new SettlementState.WinnerCharged
                {
                    Id = s.Id, ListingId = s.ListingId, WinnerId = s.WinnerId,
                    SellerId = s.SellerId, HammerPrice = s.HammerPrice,
                    ReservePrice = s.ReservePrice, FeePercentage = s.FeePercentage
                },

            (SettlementState.WinnerCharged s, FinalValueFeeCalculated e) =>
                new SettlementState.FeeCalculated(e.FeeAmount, e.SellerPayout)
                {
                    Id = s.Id, ListingId = s.ListingId, WinnerId = s.WinnerId,
                    SellerId = s.SellerId, HammerPrice = s.HammerPrice,
                    ReservePrice = s.ReservePrice, FeePercentage = s.FeePercentage
                },

            (SettlementState.FeeCalculated s, SellerPayoutIssued _) =>
                new SettlementState.PayoutIssued(s.FeeAmount, s.SellerPayout)
                { /* copy base fields */ },

            (SettlementState.PayoutIssued s, SettlementCompleted _) =>
                new SettlementState.Completed { /* copy base fields */ },

            (_, PaymentFailed e) => new SettlementState.Failed(e.Reason) { /* copy base fields */ },

            _ => throw new InvalidSettlementEvolutionException(state, evt)
        };
}
```

The framework's job (whatever `ProcessManager<TState>` ends up looking like) is to:

1. Load the current state by rebuilding it from the event stream via `Evolve`
2. Receive a command, pass it to `Decide` with current state
3. Append the resulting events to the stream
4. Schedule the next continuation command (or the next command arrives from outside)

**What's good:**

- The state is explicit. `SettlementState.FeeCalculated` carries `FeeAmount` and `SellerPayout` as non-nullable — you literally cannot read them in a state that hasn't calculated them yet. The type system prevents entire categories of bugs.
- The decider is a pure function — trivial to unit test with no framework, no harness, no I/O mocks. You write `Decide(someState, someCommand)` and assert on the returned events.
- Invalid transitions are a pattern-match miss, not a runtime state check. Adding a new state forces you to update every `switch` that references it — the compiler is your assistant.
- The entire workflow is visible in one place (the decider switch), not spread across seven handlers.
- Emmett, Decider, EventCraft and other ecosystems have validated this pattern in production.

**What's awkward:**

- More boilerplate than the stateful saga — every phase is a type, every transition is explicit.
- Requires framework support (`ProcessManager<TState>` or equivalent). Hand-rolling the load-decide-evolve loop defeats the point.
- Two `switch` expressions (Decide and Evolve) that must stay in sync. A more complete framework could derive one from the other or validate consistency, but in the raw pattern they're separate.
- Unfamiliar to developers who haven't seen the decider pattern before. Comes with a learning curve.
- Copying base fields in every Evolve branch is tedious (the `with` expressions help when state types share structure, but discriminated unions via inheritance make this awkward in C#).

#### The Comparison

| Dimension | Wolverine Saga | Process Manager (decider) |
|---|---|---|
| State representation | Mutable document with enum `Status` | Discriminated union, immutable |
| Invalid state access | Runtime nullable check | Compile-time — cannot access |
| Testability | Requires saga harness | Pure function, trivial |
| Framework coupling | Strong (inherits `Saga`) | Weak (pure functions + framework wrapper) |
| Flow visibility | Spread across handlers | Single decider switch |
| Boilerplate | Less | More |
| Familiarity | Standard Wolverine idiom | New pattern |
| C# ergonomics | Natural | Awkward state copying |
| Compensation clarity | Handler per failure step | Pattern-matched in decider |

#### `@Architect` — Which Approach for Settlement?

Settlement is a particularly strong candidate for the decider approach because:

1. **The workflow is linear and phased.** There's no branching except for the reserve-not-met failure exit. The state machine is small and fits comfortably in one decider function.
2. **The type safety matters.** Financial workflows benefit from "you cannot read the fee amount before it was calculated" being a compile error instead of a runtime nullable check.
3. **Test density is high.** We're going to write many scenarios for Settlement in Phase 3 — each one becomes a trivial pure-function test rather than a saga harness setup.
4. **It's small enough to be a proof of concept.** If `ProcessManager<TState>` is ready when M5 (Settlement milestone) begins, Settlement is a low-risk place to validate the framework. If not, we fall back to the Wolverine Saga approach without losing anything.

**For MVP (Milestone 5):** Use whichever is ready. If `ProcessManager<TState>` is landed in Wolverine by the time M5 is reached, Settlement is the natural first consumer. If not, the Wolverine Saga approach is fine and migrates cleanly later because the decisions (phase order, events produced, state transitions) are identical between the two approaches. **The business logic is the same; only the hosting pattern differs.**

> **Decision: Design Settlement around the decider pattern semantically, regardless of implementation choice.** The events, phases, and transitions documented in this workshop are the authoritative specification. When implementation begins, choose Wolverine Saga or ProcessManager based on framework readiness. Migration from one to the other preserves all scenarios and events — only the hosting code changes.
>
> This decision is deliberately noncommittal on the framework choice because Erik has visibility into `ProcessManager<TState>` maturity that this workshop doesn't. He can make the call at implementation time.

---

### Part 3: Compensation and Failure Paths

**`@QA` — The compensation question.**

What happens if `WinnerCharged` fails in production? Right now I've designed this as "no compensation needed because MVP uses virtual credit and can't actually fail." But that's a lie of convenience. Real payment failures can happen post-MVP, and the workflow design needs to accommodate them.

**The clean answer:** `WinnerCharged` is the first side effect that moves real money. If it fails, nothing else has happened yet (reserve check is a pure computation, settlement initiation is just a record). No compensation needed — just `PaymentFailed` and terminate.

**The harder question:** What if the payment processor says "success" but then retracts it later (chargebacks, fraud reversals)? This is a post-MVP concern because MVP has no real payment processor. Flag it now, defer the design.

**The edge case:** What if `SellerPayoutIssued` fails? In MVP it's just a record update, so failure implies infrastructure problems (database down). Wolverine retries will handle transient failures. Permanent failures require operator intervention.

> **Decision for MVP: No compensation logic in the Settlement workflow.** The only failure path is "reserve check failed" → `PaymentFailed` and terminate. Post-MVP, when real payment processing is wired in, compensation for `WinnerCharged` will need design — likely a "refund winner" step before terminating. Parked as a post-MVP question.

---

### Part 4: The Credit Ledger Question

**`@QA` — Where does the actual credit ceiling debit live?**

Settlement issues `WinnerCharged`. But the winner's credit ceiling is a Participants BC concern. When the DCB in Auctions validates a future bid, it checks the ceiling. Does it see the drawdown?

Three options:

**Option A: Ceiling is static, Settlement tracks charges in its own ledger, Auctions ignores charges.**
The credit ceiling is a per-bid maximum, not a running balance. A bidder with a $500 ceiling can bid up to $500 on every listing they enter. If they win multiple listings totaling $1000, Settlement records the charges in its own ledger but the DCB never reads them. This is the simplest model and matches the MVP philosophy (virtual credit, no real money).

**Option B: Ceiling is static, Participants projects `WinnerCharged` into an "available credit" field, DCB reads from Participants.**
The DCB's tag query already loads `ParticipantSessionStarted` for the credit ceiling. It would also load `WinnerCharged` events tagged with the bidder's stream and compute available credit. More complex, enforces consistency across listings.

**Option C: Settlement sends a `ChargeCredit` command to Participants, Participants enforces and records.**
Inter-BC command. Adds complexity without clear value in MVP.

**`@DomainExpert` perspective:** On eBay, the credit limit isn't a concept — you can keep bidding as long as you have real payment methods. CritterBids' credit ceiling is an artificial demo constraint, not a faithful eBay model.

**`@ProductOwner` input:** For MVP, Option A is sufficient. The ceiling is "max bid on any single listing" — a demo constraint, not a running balance. If a participant wins ten listings, that's fine; the demo doesn't need to stop them.

> **Decision: Option A for MVP.** Credit ceiling is a per-bid maximum, not a running balance. Settlement records `WinnerCharged` in its own financial event stream for audit purposes. The DCB in Auctions does NOT subtract prior charges. The credit ceiling is the invariant "this single bid cannot exceed $X." Post-MVP, if a more realistic balance model is needed, the design can evolve — likely to Option B using cross-BC event projection.

---

### Part 5: The Buy It Now Settlement Path

**`@BackendDeveloper` — Does `BuyItNowPurchased` go through the same workflow as `ListingSold`?**

Both trigger Settlement. The question is whether the workflow is identical or has a branch.

**What's the same:**
- Settlement loads `PendingSettlement` by ListingId
- Winner is charged
- Fee is calculated
- Seller payout is issued
- `SettlementCompleted` is published

**What's different:**
- `BuyItNowPurchased` implies the buyer paid the Buy It Now price, not a hammer price from bidding
- Reserve check is moot — the Buy It Now price is the agreed price, whatever the reserve was
- There may or may not be a `ReserveCheckCompleted` event in the stream

**`@DomainExpert` perspective:** On eBay, if a seller sets BIN < reserve, that's the seller's choice — they committed to selling at that price. The platform doesn't second-guess. However, there's an argument that the BIN price should implicitly satisfy the reserve because the seller set both values deliberately.

**Two approaches:**

**Approach A: Skip the reserve check for Buy It Now settlements.** The state machine has a branch at initiation: `InitiateSettlement` from `ListingSold` starts in `Initiated`, from `BuyItNowPurchased` starts in `ReserveChecked(WasMet: true)`. Both then converge at `WinnerCharged`.

**Approach B: Run the reserve check anyway, always pass.** Emit `ReserveCheckCompleted` with `WasMet: true` unconditionally for BIN. Keeps the event stream uniform — every settlement has a `ReserveCheckCompleted` event. Slightly wasteful but consistent.

**`@QA` edge case:** What if seller set `BuyItNowPrice: $50` and `ReservePrice: $100`? Under Approach A, reserve check is skipped and the sale completes at $50 (below the reserve). Under Approach B, the reserve check runs, compares $50 to $100, and returns "not met" — settlement fails, money doesn't move, but the buyer already committed via BIN. This would be a business disaster.

The right answer is Approach A. The business rule is: when a seller sets Buy It Now, they're agreeing to sell at that price regardless of reserve. Settlement should honor that.

> **Decision: Approach A adopted.** Buy It Now settlements start the workflow in the `ReserveChecked(WasMet: true)` state. No reserve comparison runs for BIN purchases. This is a business rule: setting Buy It Now means "I'll sell at this price, period." A related invariant lives in the Selling BC: `BuyItNowPrice >= ReservePrice` should be validated at listing creation to prevent nonsensical configurations. Flagged as a Selling BC concern for a future workshop.

---

### Part 6: Settlement ID Strategy

**`@BackendDeveloper` — How is SettlementId generated?**

Options:

**Option A: `Guid.NewGuid()` at `SettlementInitiated` time.** Every settlement gets a fresh random ID. Requires explicit idempotency handling — if `ListingSold` is delivered twice, the second attempt must detect that a settlement already exists for this ListingId and no-op.

**Option B: Deterministic — `SettlementId = ListingId`.** The same Guid is used for both. Trivial idempotency — starting a saga with an ID that already exists is a no-op. But conflates identities.

**Option C: Deterministic via UUID v5 — `SettlementId = UuidV5(SettlementNamespace, ListingId.ToString())`.** Distinct from ListingId but still deterministic. Idempotent by construction. This matches the CritterBids convention used elsewhere (e.g., the Proxy Bid Manager saga correlation key from W002).

> **Decision: Option C adopted.** `SettlementId = UuidV5(AuctionsNamespace, $"settlement:{ListingId}")`. Deterministic, unique per listing, idempotent. Matches the stream ID convention from `CLAUDE.md`.

---

## Phase 1 Summary

**Vocabulary changes:** None. The Settlement events remain as established in the vocabulary.

**Design decisions made:**

| # | Question | Resolution |
|---|----------|------------|
| 1 | How does Settlement get the reserve value? | `PendingSettlement` projection built from `ListingPublished` events, stored in Polecat. |
| 2 | Projection race condition (ListingSold before projection catches up)? | Wolverine retry policy. Settlement workflow retries if `PendingSettlement` not found. |
| 3 | Wolverine Saga vs Process Manager? | Design around decider semantics. Implementation choice deferred to Erik at M5 time based on `ProcessManager<TState>` framework readiness. Migration between the two preserves all scenarios. |
| 4 | Compensation logic for MVP? | None. The only failure path is reserve-not-met → `PaymentFailed` → terminate. Real compensation is post-MVP when real payment processing is wired in. |
| 5 | Credit ledger — does the DCB see charges? | No. Ceiling is per-bid maximum, not running balance. Settlement records charges in its own stream for audit. |
| 6 | Buy It Now settlement path | Starts in `ReserveChecked(WasMet: true)`. Reserve check skipped for BIN. Seller's BIN price is the agreed price. |
| 7 | SettlementId strategy | Deterministic UUID v5 from ListingId. Idempotent by construction. |

**Parked questions resolved (from prior workshops):**

- **W001 #5 (Reserve check authority)** — fully closed from both sides. Auctions emits `ReserveMet` as a real-time UX signal (W002 Phase 1). Settlement performs the authoritative comparison via `ReserveCheckCompleted` using the reserve value it cached from `ListingPublished` into the `PendingSettlement` projection (this workshop).

**New design elements identified:**

- `PendingSettlement` projection (Polecat document) — schema and lifecycle defined
- Settlement workflow state machine — 7 phases (including `Failed`), transitions explicit
- Decider pattern sketched as alternative to Wolverine Saga — both approaches documented
- Financial event stream — audit log pattern for all settlement events

**New questions surfaced:**

| # | Question | Persona | Notes |
|---|----------|---------|-------|
| 1 | What happens if `SellerPayoutIssued` fails (infrastructure issue)? | `@QA` | Wolverine retries for transient. Permanent failures need operator intervention. Ops tooling question. |
| 2 | Post-MVP: How does compensation work when real payment processor is wired in? | `@QA` | Refund-winner step before terminating. Parked until payment integration. |
| 3 | Is there a "second chance offer" fallback if the winner's payment fails? | `@DomainExpert` | eBay has this. Post-MVP consideration. |
| 4 | `BuyItNowPrice >= ReservePrice` invariant — where does it live? | `@Architect` | Selling BC. Flag for Selling workshop. |
| 5 | Does Settlement need a manual retry mechanism for ops staff? | `@ProductOwner` | Post-MVP. Requires ops command interface. |
| 6 | Where does `FeePercentage` come from? Platform config, per-seller, or per-listing? | `@ProductOwner` | Currently assumed in `PendingSettlement`, meaning it's set at `ListingPublished` time. Does that come from Selling BC or from a platform config loaded at publish time? |
| 7 | Should the `PendingSettlement` projection be cleaned up after `SettlementCompleted`, or retained for audit? | `@BackendDeveloper` | Argument for retention: audit trail. Argument for cleanup: prevents table growth. Could mark as `Consumed` but keep the row. |

---

## Phase 2 — Storytelling: A Settlement's Complete Lifecycle

*Next: Walk a single settlement through its full lifecycle from `ListingPublished` (projection population) through `SettlementCompleted`. Resolve the Phase 1 questions that remain open. Demonstrate both the happy path and the two failure paths (reserve not met, BIN alternate start).*

*(to be continued)*

---

## Phase 3 — Scenarios (Given/When/Then)

*(not yet started — implementation-ready scenarios written against the decider semantics so they apply to either hosting approach)*
