# Workshop 003 — Settlement BC Deep Dive

**Type:** BC-Focused (vertical depth)
**Date started:** 2026-04-09
**Status:** In progress — Phase 1

**Scope:** The Settlement BC internals. Settlement workflow design (including the explicit Wolverine Saga vs. Process Manager comparison), the pending settlement projection, compensation paths, and the financial authority relationship with Participants and Auctions.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. `@ProductOwner` on standby.

**Prerequisites:** Workshops 001 and 002 completed. This workshop closes the loop on the reserve check authority decision from W002 by addressing Settlement's side of it.

**Implementation status:** The W001 slices this workshop deep-dives - 6.1 (Settlement saga happy path), 6.2 (Settlement from Buy It Now), and 6.3 (Seller payout notification) - all carry status `planned` per W001's slice tables (M5 milestone allocation; no implementation prompts yet). Per-slice status using the four-vocabulary (`design | planned | in progress | done`) lands here when W003's Phase 4 slice walk is authored; until then, the workshop's own title-block "Status" line is workshop-grain, not slice-grain.

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

## Narrative Cross-References

The following narratives implement slices that this workshop deep-dives. Each narrative cites its slices via `Implements:` lines on its Moments; this section is the inverse index per the narratives README v0.1 bidirectional-referencing convention.

- **[Narrative 002 - Winner Clears Settlement (Happy Path)](../narratives/002-winner-clears-settlement.md)** implements slices 6.1 (Settlement saga happy path from `ListingSold`) and 6.3 (seller payout notification via Relay). Single-bidder perspective; happy-path; companion to narrative 001 Moment 8 at finer grain. Five Moments dramatising the Settlement saga's per-phase progression (Initiated, ReserveChecked, WinnerCharged, FeeCalculated, PayoutIssued, Completed). Forward-spec across all five Moments because the Settlement BC is unshipped at narrative authoring time (M5 ship target). The narrative renders the saga as W003 designs it; lived-code audit defers until Settlement ships. Findings surfaced by narrative 002 against W003: F002 (`Price` / `HammerPrice` rename across initiation, `document-as-intentional`, deferred to W003 follow-up); F003 (Polecat / SQL Server storage references against ADR 011's All-Marten Pivot, `workshop-update`, minimum-scope correction landed in narrative 002's PR); F004 (`SettlementInitiated` payload mismatch between scenarios §1.1 and §7.1, `workshop-update`, deferred); F005 (missing named bidder-credit projection, `workshop-update`, deferred).

---

## Ubiquitous Language

The Settlement BC owns the post-resolution financial workflow: from `ListingSold` or `BuyItNowPurchased` through `SettlementCompleted`. The in-flight bidding lifecycle is owned by Auctions ([W002 §3](./002-auctions-bc-deep-dive.md#ubiquitous-language)); the pre-publish listing lifecycle is owned by Selling ([W004 §3](./004-selling-bc-deep-dive.md#ubiquitous-language)).

Each term carries a one-line definition with optional cross-references and "what it is *not*" notes. Domain events are catalogued in [`docs/vision/domain-events.md`](../vision/domain-events.md) and in this workshop's Phase 1 architecture overview; events are not duplicated here.

| Term | Definition | Notes |
|---|---|---|
| **Settlement** | The financial workflow that runs after a listing resolves to a sale. Settles the buyer charge, fee calculation, and seller payout. Identified by `SettlementId`. | Distinct from Auction Closing - Auctions resolves the bidding outcome; Settlement moves the money. |
| **SettlementId** | A deterministic UUID v5 derived from `ListingId` (`UuidV5(SettlementsIdentityNamespaces.SettlementSaga, $"settlement:{ListingId}")`). Idempotent by construction. | Per W003 Phase 1 Part 6 decision. Distinct from `ListingId`; allows tracing a settlement back to its source listing without conflating identities. The namespace is Settlement-owned per the BC-isolation discipline (M5-S4 workshop-update fix; the original "AuctionsNamespace" reference was a drift). |
| **PendingSettlement** | A Marten document projection built from `ListingPublished` events. Cached so the Settlement workflow has reserve, fee, and seller data when `ListingSold` arrives without crossing the BC boundary. | Lifecycle states: Pending, Consumed, Expired. Settlement workflow retries with backoff if not found at workflow-start time (W003 Phase 1 Part 1 decision). |
| **Settlement Workflow** | The seven-phase progression: Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed. Failure exit at any phase via `PaymentFailed`. | Implementation choice deferred - Wolverine Saga or `ProcessManager<TState>` decider. Same business logic, only hosting differs (W003 Phase 1 Part 2 decision). |
| **Reserve** | The minimum hammer price below which the listing does not sell at auction. May be null. | Defined in W002 §3 from the bidding-time perspective. Settlement is the financial authority for the binding comparison via `ReserveCheckCompleted`; Auctions' `ReserveMet` is a real-time UX signal only. |
| **Hammer Price** | The final accepted bid amount when bidding closes. | Defined in W002 §3; carried into Settlement via `ListingSold`. |
| **Reserve Check** | The Settlement workflow phase that compares hammer price to reserve and decides whether settlement proceeds or fails. | Skipped for Buy It Now settlements (BIN price is the agreed price; W003 Phase 1 Part 5 decision). |
| **Winner Charge** | The phase that debits the winning bidder by the hammer price. First side effect that moves real money. | MVP: virtual credit, no real payment processor. Post-MVP wires real payment integration; compensation design also post-MVP. |
| **Final Value Fee** | The platform's percentage cut of the hammer price. CritterBids default: 10%. Computed as `Math.Round(HammerPrice * (FeePercentage / 100), 2)`. | `FeePercentage` is carried on `PendingSettlement`, set at `ListingPublished` time. |
| **Seller Payout** | The amount transferred to the seller after fee deduction (`HammerPrice - FeeAmount`). Issued via `SellerPayoutIssued`. | Pushed to seller via Relay BC (W001 slice 6.3). |
| **Buy It Now Settlement Path** | The variant Settlement workflow triggered by `BuyItNowPurchased` (vs. `ListingSold`). Starts in `ReserveChecked(WasMet: true)` to skip the reserve comparison. | Per W003 Phase 1 Part 5 decision. The `BuyItNowPrice >= ReservePrice` invariant in Selling BC (W004 §3) is the upstream guarantee that makes this safe. |
| **Financial Event Stream** | The append-only audit log of every event in a settlement's lifecycle. One stream per `SettlementId`. | Marten-backed (PostgreSQL) per ADR 011 All-Marten Pivot; never deleted; persists for compliance and audit. |
| **BidderCreditView** | A Marten document projection per bidder per session. Carries `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)`. Read by Relay's broadcast handler when composing `SettlementCompleted` pushes. | Owned by Settlement BC. Lifecycle defined in W003 Phase 1 Part 7. Distinct from the per-bid ceiling enforced by Auctions' DCB — this view is post-charge running balance, not the bid-time invariant. |
| **Bidder** | A participant who has placed at least one bid. The settlement's "buyer" when they win. Identified by `BidderId` (= `WinnerId` on `ListingSold`). | Same `BidderId` as in W002 §3 and Participants BC. |
| **Seller** | The participant who originally listed the item. Identified by `SellerId`, cached on `PendingSettlement` from `ListingPublished`. | Same `SellerId` as in W004 §3 and Participants BC. |

---

## Phase 1 — Brain Dump: Internal Structure

The Settlement BC is simpler than Auctions in component count (one workflow, one projection — no DCB, no competing sagas) but richer in a different dimension: every Settlement event represents a financial commitment. The design tension isn't concurrency, it's **getting the phases right and handling failures without losing money**.

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Settlement BC                                               │
│                                                             │
│  ┌────────────────────────────┐                             │
│  │ PendingSettlement           │ ← Marten projection        │
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
│  │ (all settlement events      │   (Marten event store)     │
│  │  per settlement instance)   │                             │
│  └────────────────────────────┘                              │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

Three concerns: (1) preparing settlement data from published listings, (2) running the settlement workflow when a listing resolves, (3) maintaining the financial audit trail.

### Part 1: The PendingSettlement Projection

**The problem it solves:** Settlement needs the reserve value, fee percentage, and seller identity when `ListingSold` arrives. That data was established way back at `ListingPublished`, possibly days earlier. Settlement can't reach back into the Selling BC to fetch it at resolution time — that's a BC boundary violation.

**The solution:** Settlement maintains its own projection built from `ListingPublished` events. When `ListingPublished` arrives over the bus, Settlement's projection handler writes a row to a `pending_settlements` document store in PostgreSQL (Marten) per ADR 011's All-Marten Pivot.

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

A PendingSettlement isn't a business entity with a lifecycle the domain cares about. It's a derived read model - a convenient lookup that lets Settlement avoid querying across BC boundaries. A plain Marten document projection is the right primitive. If we used an event-sourced aggregate here, we'd be inventing domain events for something that's really just "I saw a ListingPublished event and cached the parts I need."

**`@BackendDeveloper` note (post-ADR-011):** ADR 011's All-Marten Pivot makes the earlier Polecat-vs-Marten projection comparison moot for Settlement. The PendingSettlement projection is a standard Marten document projection; the Financial Event Stream is a Marten event-sourced stream. No cross-BC storage heterogeneity remains; the projection class follows the standard Marten patterns established by the other seven BCs.

**`@QA` — race condition question:** What if `ListingSold` arrives before the `PendingSettlement` projection has caught up from `ListingPublished`?

In practice, this should never happen — `ListingPublished` happens hours or days before `ListingSold`, giving the projection plenty of time to catch up. But for correctness under pathological conditions (e.g., a Flash Session where a listing is published, attached, started, and Buy-It-Now'd within seconds), Settlement should handle the "no pending settlement found" case gracefully. Options:

**Option A:** The settlement workflow retries with backoff when the pending settlement isn't found. Wolverine's retry policies handle this natively.

**Option B:** The projection is treated as eventually consistent, and the workflow blocks briefly on first attempt.

**Option C:** `ListingPublished` and the start of a Flash Session are gated so the projection must catch up before bidding can open.

> **Decision: Option A adopted.** Settlement workflow retries on "PendingSettlement not found" with exponential backoff. Wolverine's inbox/outbox machinery makes this natural — the triggering event stays in the queue until the handler succeeds. In practice this path should essentially never fire, but the retry guarantees correctness if the projection is a few milliseconds behind.

---

### Part 2: The Settlement Workflow — Hosting Comparison

This is the core of the Settlement BC and the most interesting design question in this workshop. The workflow is:

```
Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed
                                    │
                                    └── (credit check fails) → Failed
```

Six events, linear progression, one failure exit point. It's a textbook workflow — and the question is which Wolverine coordination primitive to host it on.

> **CritterBids' framing constraint.** CritterBids uses **shipped Wolverine features only**. Two coordination patterns are available within shipped Wolverine: **Wolverine Saga** (Approach A) and **Process Managers via Handlers** (Approach B). The decider pattern via the proposed `ProcessManager<TState>` framework primitive — design work Erik is leading at JasperFx — is documented in this Part as a *design lens* (the third subsection below), not as an implementation option. The lens is useful regardless of host; the framework primitive is out of scope as a CritterBids implementation choice. M5-S1's ADR-019 closes the host choice as Wolverine Saga; this Part's structure was reorganized at M5-S1 to reflect the shipped-Wolverine stance.

#### Approach A: Wolverine Saga (chosen for Settlement per ADR-019)

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

#### Approach B: Process Managers via Handlers

The handler-based process manager pattern is Wolverine's shipped event-reactive coordination primitive. Each handler reacts to a specific event independently; coordination emerges from the handler chain rather than from a stateful saga document. No `Saga` base class, no `MarkCompleted()`, no shared mutable state across handlers — just a sequence of `[WolverineHandler]` methods consuming events and emitting the next.

```csharp
// Each handler is a separate static method; no shared state
public static class SettlementHandlers
{
    public static async Task<OutgoingMessages> Handle(
        ListingSold message,
        IDocumentSession session,
        CancellationToken ct)
    {
        var pending = await session.LoadAsync<PendingSettlement>(message.ListingId, ct);
        if (pending is null) throw new PendingSettlementNotFoundException(message.ListingId);

        var settlementId = SettlementIdFor(message.ListingId);

        return new OutgoingMessages
        {
            new SettlementInitiated(settlementId, message.ListingId, message.WinnerId,
                pending.SellerId, message.HammerPrice, /* Source: */ "Bidding",
                pending.ReservePrice, pending.FeePercentage, DateTimeOffset.UtcNow),
            // Self-send next step — but with the full state payload, since no saga document holds it
            new CheckReserveCommand(settlementId, message.HammerPrice, pending.ReservePrice)
        };
    }

    public static OutgoingMessages Handle(CheckReserveCommand command)
    {
        var met = command.ReservePrice is null || command.HammerPrice >= command.ReservePrice;
        // ...emit ReserveCheckCompleted; emit ChargeWinnerCommand carrying state forward...
    }

    // ...one handler per phase, each carrying the state it needs in its inbound command...
}
```

**The state-threading question.** State that subsequent phases need (`HammerPrice`, `FeePercentage`, the materialized `FeeAmount` after calculation) has to live somewhere. Two options:

- **Option B1: Thread state through self-sent commands as growing payloads.** Each command carries every field a downstream handler will read. Command shapes accumulate fields as the workflow progresses (`CheckReserveCommand` carries 3 fields; `ChargeWinnerCommand` carries 5; `IssueSellerPayoutCommand` carries 7). Handler signatures are coupled to evolving payload contracts.
- **Option B2: Hydrate state from the event stream on every handler entry.** Each handler loads the financial event stream, replays `SettlementInitiated` plus all subsequent events to rebuild current state, then makes its decision. This re-implements event sourcing's read path on a per-handler basis — work that the Saga primitive's state document handles natively.

**What's good:**

- The shipped Wolverine pattern for event-reactive coordination. Familiar to anyone who's authored cross-BC handlers in CritterBids (M3-S6's `AuctionStatusHandler` and `ListingSnapshotHandler` are the canonical examples).
- No saga lifecycle to reason about — no `MarkCompleted()`, no saga-state persistence cleanup, no saga-correlation rules.
- Each handler is independently testable — a unit test against the static method and its inputs.
- Fits leaf-reaction pipelines naturally: when each handler is a one-shot reaction with no continuation state, this shape is the right tool.

**What's awkward for Settlement specifically:**

- Settlement has phased shared state by design. Forcing the handler chain to thread state via Option B1 or rebuild state via Option B2 reinvents what the Saga primitive ships natively.
- Self-sent commands grow large as phase progression accumulates fields. A `CompleteSettlementCommand` carrying every field downstream phases need (`SettlementId, ListingId, WinnerId, SellerId, HammerPrice, FeeAmount, SellerPayout, ...`) is essentially the saga state, but expressed as a transient message rather than a persisted document. The persistence guarantee differs (the saga document is durable; the command is in-flight on the message bus).
- No single place to ask "what state is this settlement in?" Without a saga document, "current state" requires loading the event stream — a per-query cost the Saga primitive amortizes via its persisted document.

This is the wrong host for Settlement, but the right host for future BCs whose coordination shape is event-reactive without phased state. Relay's broadcast pipeline is the canonical post-M5 candidate. M3-S6's `AuctionStatusHandler` (consumes `BiddingOpened`, `BiddingClosed`, `ListingSold`, `ListingPassed` to update `CatalogListingView`) is the lived precedent.

#### Design Lens: The Decider Pattern

> **Out of scope as a CritterBids implementation primitive.** The code sketches in this subsection illustrate the decider pattern as a *design lens* — useful for shaping the events, state transitions, and scenario tests in `003-scenarios.md` Sections 1-7 regardless of which Wolverine host is chosen. They are **not** an implementation option for CritterBids: the proposed `ProcessManager<TState>` framework primitive Erik is designing for Wolverine is JasperFx framework-design work, not a CritterBids implementation roadmap item. The Saga host (Approach A) is the M5 implementation choice per ADR-019; the lens below survives as design discipline applied within the Saga.

The decider pattern (from Jérémie Chassaing's work and used in Emmett's `Workflow` type) models the workflow as three pure functions: **Decide** (given state + command, return events), **Evolve** (given state + event, return new state), and an explicit state type that makes invalid transitions unrepresentable. The pattern is reusable as a design discipline: extract `Decide` and `Evolve` as pure-function helpers from a Saga's per-phase handlers, and Sections 1-7's 28 scenarios pass against the helpers directly without a saga harness.

> **Sketch disclaimer:** The code below illustrates the decider pattern's shape, drawn from Erik's in-progress JasperFx `ProcessManager<TState>` proposal as a reference. It is included here as design-lens documentation, not as a CritterBids implementation target.

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

The choice within shipped Wolverine is between Saga (Approach A) and Handlers (Approach B). The decider lens (the third subsection above) applies as design discipline within either host; it is not a separate column in this comparison.

| Dimension | Wolverine Saga (A) | Process Managers via Handlers (B) |
|---|---|---|
| Phased shared state | Held in saga document; durable; loaded once per command | Threaded through commands or rehydrated from event stream per handler |
| State representation | Mutable document with `Status` enum | No persisted state document; state is in-flight on commands or in the event stream |
| Lifecycle primitive | `Saga` base class + `MarkCompleted()` | None — handlers are leaf reactions |
| Self-send shape | Continuation commands carry only `Id`; saga state holds the rest | Continuation commands carry every field downstream handlers need |
| Wolverine inbox / retry | Per-step (continuation commands hit the inbox individually) | Per-handler (each handler is its own inbox entry) |
| Testability | Saga harness or pure-function helper extraction (Option C in ADR-019) | Each handler is a static method, unit-testable directly |
| Familiarity in CritterBids | Lived precedent: M3-S5 Auction Closing saga | Lived precedent: M3-S6 `AuctionStatusHandler` / `ListingSnapshotHandler` |
| Fit shape | Workflows with phased shared state | Event-reactive coordination without phased state |

#### `@Architect` — Which Approach for Settlement?

Settlement's seven phases share evolving state by design: `HammerPrice` and `FeePercentage` are read at multiple phases; `FeeAmount` and `SellerPayout` materialize at the FeeCalculated phase and are read at PayoutIssued and Completed; the participant identifiers persist across the entire saga. This is the shape Approach A is built to host.

Approach B (Handlers) would force the workflow onto either Option B1 (threading state through ever-growing self-sent commands) or Option B2 (rehydrating state from the event stream on every handler entry). Both reinvent saga state outside the primitive Wolverine ships for that purpose. The Auction Closing saga (M3-S5) is the lived precedent for Approach A; Settlement's seven-phase shape is structurally similar (phased progression with evolving shared state) but longer and richer in financial fields, making the saga primitive even more load-bearing here than at M3-S5.

The decider design lens applies within Approach A: M5-S4's implementation may extract pure-function `Decide` and `Evolve` helpers from the saga's per-phase handlers, exercising `003-scenarios.md` Sections 1-7's 28 pure-function scenarios as helper-method tests rather than saga-harness tests. That extraction is implementation-detail per ADR-019 Option C, not a separate hosting choice.

Approach B remains the right tool for future CritterBids BCs whose coordination shape is leaf-reactive: Relay's broadcast pipeline (post-M5) is the canonical candidate.

> **Decision (W003-grade): Settlement is hosted on the Wolverine Saga primitive (Approach A), with the decider pattern applied as a design lens (pure-function helper extraction permitted) per ADR-019.** The events, phases, and transitions documented in this workshop are the authoritative specification regardless of host shape; the 41 scenarios in `003-scenarios.md` apply. Process Managers via Handlers (Approach B) remains the right host for future BCs whose coordination shape is event-reactive without phased state.
>
> M5-S1 closure: ADR-019 records the choice with a single revisit trigger (Saga-shape friction during M5 implementation that the decider design lens or Handlers shape would prevent). The proposed `ProcessManager<TState>` framework primitive is out of scope per CritterBids' shipped-Wolverine stance and is not a revisit trigger.

#### Field Name Convention: `Price` at Initiation, `HammerPrice` Post-Initiation

The command and event vocabulary uses two different names for the final accepted price across the saga's lifecycle. This is deliberate and load-bearing — readers of Part 2's sketches alone may interpret the asymmetry as a workshop inconsistency, especially because the sketches above use `HammerPrice` throughout while the canonical scenarios in `003-scenarios.md` §1 and §7 render the asymmetry verbatim. The scenarios are the authoritative source; this section names the convention so the asymmetry is documented rather than discovered.

**The convention.** Pre-initiation field names use the source-agnostic name `Price`. Post-initiation names use the source-specific name `HammerPrice`. The evolver at `003-scenarios.md` §7.1 is the single point of rename — taking the source-agnostic `Price` from `SettlementInitiated`'s payload and producing a `HammerPrice` field on `SettlementState.Initiated`.

| Touchpoint | Field name | Why |
|---|---|---|
| `InitiateSettlement` command (input to decider, §1.1 / §1.2) | `Price` | Source-agnostic. Bidding source carries the hammer price; BIN source carries the BIN price. The command is constructed by the inbound-event handler before the decider sees it; the handler's vocabulary should not commit to one source. |
| `SettlementInitiated` event (decider output, §1.1 / §1.2 / §1.3; evolver input, §7.1 / §7.2) | `Price` | Same rationale. The event is the durable record of the decider's initiation decision; its payload carries `Source` as the disambiguator. |
| `SettlementState.Initiated` (evolver output for Bidding source, §7.1) | `HammerPrice` | The rename happens here. Once `Source: Bidding` is committed in state, the value semantically *is* the hammer price by definition. |
| `SettlementState.ReserveChecked(WasMet: true)` (evolver output for BIN source, §7.2) | `HammerPrice` | BIN source branches directly past the reserve check; the BIN price is absorbed as the hammer-price-equivalent for downstream phases (per Part 5's BIN settlement path decision). |
| `ReserveCheckCompleted` event (§2.1, §2.2, §2.3) | `HammerPrice` | Post-initiation: state has committed to a source and the field is the hammer price. |
| `WinnerCharged` event payload `Amount` field (§3.1) | `Amount` | Naming difference here is intentional: `Amount` is the charge against the bidder's credit, semantically equal to `HammerPrice` from state but rendered with payment-domain vocabulary at the moment money moves. |
| All other downstream events (`FinalValueFeeCalculated`, `SellerPayoutIssued`, `SettlementCompleted`) | `HammerPrice` | The hammer-price-grain field carries through the lifecycle. |

**Why source-agnostic at initiation.** The decider's `Decide(null, InitiateSettlement)` pattern match must accept both source paths. If the command field were named `HammerPrice` at this layer, BIN-source initiation would either misname the field (semantically wrong: the value is the BIN price, not a hammer price) or require two command shapes (`InitiateSettlementFromBidding`, `InitiateSettlementFromBuyItNow`) that double the decider's pattern-match surface for no design benefit. The source-agnostic `Price` plus discriminator `Source` is the simpler shape that serves both flows.

**Why source-specific post-initiation.** Once the saga has committed to a source (recorded in `SettlementInitiated`'s `Source` field; folded into state by the evolver), the value is no longer ambiguous. Downstream code (the reserve check in Part 2's Approach A `Handle(CheckReserve)`, the fee calculation, the payout issuance) reads `HammerPrice` from state with no need to re-disambiguate. The W003 §Part 4 fee calculation `Math.Round(HammerPrice * (FeePercentage / 100m), 2)` is legible at a glance because `HammerPrice` is the right name at that phase.

**Naming-discipline implication.** When implementing the saga in M5 (per ADR-019's Wolverine Saga choice): the saga document carries `HammerPrice` (matching post-initiation state); the inbound-event handler that constructs `InitiateSettlement` from `ListingSold` or `BuyItNowPurchased` reads `ListingSold.HammerPrice` or `BuyItNowPurchased.Price` from the cross-BC contract and maps it to the command's `Price` field, then passes through `Source: Bidding | BuyItNow` as the discriminator. The rename at the saga's entry point (handler → command construction) is the host-side analogue of the evolver-level rename the decider sketches above describe.

This convention was surfaced as a finding (F002) during narrative 002 authoring per `docs/narratives/002-findings.md`; the routing is `document-as-intentional` because the convention is correct as designed, only the workshop documentation was incomplete.

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

**Option C: Deterministic via UUID v5 — `SettlementId = UuidV5(SettlementsIdentityNamespaces.SettlementSaga, $"settlement:{ListingId}")`.** Distinct from ListingId but still deterministic. Idempotent by construction. This matches the CritterBids convention used elsewhere (e.g., the Proxy Bid Manager saga correlation key from W002 / `AuctionsIdentityNamespaces.ProxyBidManagerSaga`).

> **Decision: Option C adopted.** `SettlementId = UuidV5(SettlementsIdentityNamespaces.SettlementSaga, $"settlement:{ListingId}")`. Deterministic, unique per listing, idempotent. Matches the stream ID convention from `CLAUDE.md` and the BC-isolation discipline — the namespace constant is owned by the Settlement BC, not Auctions. (M5-S4 workshop-update: corrected from the original "AuctionsNamespace" drift; the lived helper at `src/CritterBids.Settlement/SettlementsIdentityNamespaces.cs` is the canonical source.)

---

### Part 7: The BidderCreditView Projection

**`@Architect` — Where does the bidder's visible credit balance live?**

Part 4 settled the *authority* question for the credit ledger: the credit ceiling is a per-bid maximum, the DCB in Auctions does not subtract prior charges, and Settlement records `WinnerCharged` in its own financial event stream for audit. What Part 4 did *not* settle is the *read-model* question — what document does Relay's BiddingHub load when composing the `SettlementCompleted` broadcast that carries `remainingCredit: 445.00` to a bidder's phone? Without a named projection, the broadcast handler has no defined source for the field; without a named source, the field's lifecycle is undefined.

The narrative-002 backfill surfaced this gap as Finding 005 (`docs/narratives/002-findings.md`). Narrative 001 Moment 8 references "Settlement's bidder-credit projection" with a definite article, treating it as a named system component; narrative 002 Moment 3 dramatises the credit debit but renders it as "the bidder-credit ledger" without naming a projection precisely to avoid overcommitting to a name the workshop had not yet defined. This Part defines the projection that retroactively legitimizes both narratives' references.

**The projection.**

```csharp
public sealed class BidderCreditView
{
    public Guid BidderId { get; init; }                  // primary key
    public decimal RemainingCredit { get; init; }
    public Guid? LastChargedSettlementId { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
```

**Field rationale.**

- `BidderId` — the participant's BidderId from `ParticipantSessionStarted` and `WinnerCharged.WinnerId`. Primary key.
- `RemainingCredit` — the running balance. Initialised to the assigned credit ceiling at `ParticipantSessionStarted` consumption; decremented by `WinnerCharged.Amount` at each charge. The bidder-visible field; carried verbatim into Relay's `SettlementCompleted` broadcast as `remainingCredit`.
- `LastChargedSettlementId` — the most recent settlement that debited this bidder. Nullable because a session that has never won a settlement has no charges. Useful for ops-side traceability ("which settlement does this credit balance reflect?") and for idempotency checks if `WinnerCharged` is replayed.
- `UpdatedAt` — handler-stamped timestamp, not outbox-dispatch time. Distinguishes "no charges yet" (`UpdatedAt = ParticipantSessionStartedAt`) from "charged at least once."

**Lifecycle.**

- **Initialise on `ParticipantSessionStarted`** (cross-BC integration event from Participants). Settlement consumes the event, derives `BidderId` and the assigned credit ceiling, and writes `BidderCreditView { BidderId, RemainingCredit: <ceiling>, LastChargedSettlementId: null, UpdatedAt: <SessionStartedAt> }`. The projection is initialized once per bidder per session.
- **Update on `WinnerCharged`** (Settlement-internal event). The handler loads the existing `BidderCreditView` by `WinnerCharged.WinnerId` (the `BidderId` correlation), debits `RemainingCredit` by `WinnerCharged.Amount`, sets `LastChargedSettlementId` to the event's `SettlementId`, and updates `UpdatedAt`. Idempotency on replay: if `LastChargedSettlementId` already equals the event's `SettlementId`, the handler no-ops.
- **No terminal state.** The projection persists for the duration of the session; cleanup is post-MVP and follows whatever session-lifecycle convention Participants establishes when session expiry is implemented.

**Consumer model.**

- **Relay BC's BiddingHub broadcast handler** (slice 6.3, post-M5). When composing the `SettlementCompleted` push to the winning bidder's connection, the handler loads `BidderCreditView` by `BidderId` and reads `RemainingCredit`. The broadcast payload's `remainingCredit` field is the verbatim value at broadcast time. This is the read path that surfaces the bidder's credit balance to her phone (per narrative 001 Moment 8 and narrative 002 Moment 5).
- **Future bidder-balance endpoint** (post-MVP). When a bidder-facing balance display materializes outside the SettlementCompleted broadcast (e.g., a "your account" view), the endpoint loads `BidderCreditView` directly. The endpoint shape is out of scope for M5; the projection is shaped to support it.
- **No DCB consumer.** Per Part 4's Option A: the DCB in Auctions does NOT read `BidderCreditView` to subtract prior charges. The DCB validates against the per-bid ceiling, not the running balance.

**Why a Settlement-side projection rather than a Participants-side projection.**

The credit-ledger debit is a Settlement-domain action — `WinnerCharged` is a Settlement-internal event, not a Participants event. The projection's lifecycle is owned by the BC that owns the events feeding it. A Participants-side projection would force Settlement to publish a cross-BC `BidderCharged` integration event for Participants to consume, doubling the contract surface for no clear benefit at MVP scale. Per W003's BC-isolation discipline, the read model lives where the events that update it live.

The naming `BidderCreditView` (not `BidderCreditLedger`) follows CritterBids' `*View` projection convention from `CatalogListingView` and `ListingBidSummary`. The "credit-ledger posture" framing in narrative 002's Setting refers to MVP-vs-real-payment-processor, not to the document's name. The projection is a read model derived from events, not a domain-grade ledger.

> **Decision: `BidderCreditView` projection adopted.** Initialised on `ParticipantSessionStarted`; updated on `WinnerCharged`; consumed by Relay's broadcast handler and any future bidder-balance endpoint. Settlement BC owns the projection. No DCB consumer per Part 4 Option A. Projection lifecycle persists for session duration; post-MVP cleanup follows Participants' session-expiry convention.

This Part was authored at M5-S1 to close narrative 002 Finding 005. The narrative-001 Moment 8 reference to "Settlement's bidder-credit projection" is retroactively legitimized by this Part's naming; narrative 002 Moment 3's prose "bidder-credit ledger" remains as-is since the framing is about MVP posture, not the projection's name.

---

## Phase 1 Summary

**Vocabulary changes:** None. The Settlement events remain as established in the vocabulary.

**Design decisions made:**

| # | Question | Resolution |
|---|----------|------------|
| 1 | How does Settlement get the reserve value? | `PendingSettlement` projection built from `ListingPublished` events, stored in Polecat. |
| 2 | Projection race condition (ListingSold before projection catches up)? | Wolverine retry policy. Settlement workflow retries if `PendingSettlement` not found. |
| 3 | Wolverine Saga vs Process Managers via Handlers? | Wolverine Saga adopted for Settlement (M5-S1, ADR-019) — phased shared state fits the Saga primitive. Handlers remain the right host for future event-reactive BCs (e.g. Relay's broadcast pipeline). Decider pattern applied as design lens within the Saga. The proposed `ProcessManager<TState>` framework primitive is out of scope per CritterBids' shipped-Wolverine stance. |
| 4 | Compensation logic for MVP? | None. The only failure path is reserve-not-met → `PaymentFailed` → terminate. Real compensation is post-MVP when real payment processing is wired in. |
| 5 | Credit ledger — does the DCB see charges? | No. Ceiling is per-bid maximum, not running balance. Settlement records charges in its own stream for audit. |
| 6 | Buy It Now settlement path | Starts in `ReserveChecked(WasMet: true)`. Reserve check skipped for BIN. Seller's BIN price is the agreed price. |
| 7 | SettlementId strategy | Deterministic UUID v5 from ListingId. Idempotent by construction. |
| 8 | Bidder-credit read model | `BidderCreditView` Marten document projection. Settlement BC owns it. Initialised on `ParticipantSessionStarted` with assigned credit ceiling; updated on `WinnerCharged`. Consumed by Relay's `SettlementCompleted` broadcast handler and any future bidder-balance endpoint. No DCB consumer per Part 4 Option A. (M5-S1; closes narrative 002 Finding 005.) |

**Parked questions resolved (from prior workshops):**

- **W001 #5 (Reserve check authority)** — fully closed from both sides. Auctions emits `ReserveMet` as a real-time UX signal (W002 Phase 1). Settlement performs the authoritative comparison via `ReserveCheckCompleted` using the reserve value it cached from `ListingPublished` into the `PendingSettlement` projection (this workshop).

**New design elements identified:**

- `PendingSettlement` projection (Polecat document) — schema and lifecycle defined
- Settlement workflow state machine — 7 phases (including `Failed`), transitions explicit
- Two shipped Wolverine hosts compared: Approach A (Wolverine Saga) and Approach B (Process Managers via Handlers); M5-S1 ADR-019 chose Approach A for Settlement with the decider pattern as design lens. The proposed `ProcessManager<TState>` framework primitive is out of scope per CritterBids' shipped-Wolverine stance.
- Financial event stream — audit log pattern for all settlement events
- `BidderCreditView` projection (Marten document) — schema, lifecycle, and consumer model defined in Part 7 (added M5-S1; closes narrative 002 Finding 005)

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
