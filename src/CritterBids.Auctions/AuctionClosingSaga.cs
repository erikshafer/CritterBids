using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Marten;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.ScheduledMessageManagement;
using Wolverine.Persistence.Sagas;

namespace CritterBids.Auctions;

/// <summary>
/// First CritterBids saga. Orchestrates the close of a single listing: schedules the initial
/// CloseAuction at BiddingOpened, tracks high-bid + reserve state as bids arrive, and
/// cancels-and-reschedules the close on ExtendedBiddingTriggered.
///
/// M3-S5 scope: forward path only (scenarios 3.1–3.4). Handle(CloseAuction) is a stub that
/// no-ops — S5b lands the real close evaluation and outcome-event emission. Terminal-state
/// handlers (BuyItNowPurchased, ListingWithdrawn) and MarkCompleted() calls are S5b.
///
/// Correlation (M3-S5 OQ1 Path A): Saga.Id = ListingId. Each handler parameter for an
/// integration event carries [SagaIdentityFrom(nameof(X.ListingId))] — this overrides
/// Wolverine's default {SagaName}Id convention so the contracts stay unchanged and the
/// saga id matches the listing id 1:1 as §3 scenarios specify.
///
/// Idempotency (M3-S5 OQ2): BidCount monotonicity. BidPlaced is ignored if its BidCount
/// is ≤ the saga's stored count — DCB guarantees monotonic BidCount per listing, so stale
/// re-deliveries drop without an allocation-growing hash set. ReserveMet is idempotent by
/// set-to-true. The Start handler checks for saga existence before creating.
///
/// Concurrency (M3-S5 OQ3): numeric revisions + the existing
/// AuctionsConcurrencyRetryPolicies.OnException&lt;ConcurrencyException&gt; policy registered
/// in AuctionsModule covers saga document writes — no saga-specific retry wiring added.
/// </summary>
public sealed class AuctionClosingSaga : Wolverine.Saga
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid? CurrentHighBidderId { get; set; }
    public decimal CurrentHighBid { get; set; }
    public int BidCount { get; set; }
    public bool ReserveHasBeenMet { get; set; }
    public DateTimeOffset ScheduledCloseAt { get; set; }
    public DateTimeOffset OriginalCloseAt { get; set; }
    public bool ExtendedBiddingEnabled { get; set; }
    public AuctionClosingStatus Status { get; set; } = AuctionClosingStatus.AwaitingBids;

    public void Handle([SagaIdentityFrom(nameof(BidPlaced.ListingId))] BidPlaced message)
    {
        if (message.BidCount <= BidCount) return;

        CurrentHighBid = message.Amount;
        CurrentHighBidderId = message.BidderId;
        BidCount = message.BidCount;

        if (Status == AuctionClosingStatus.AwaitingBids)
        {
            Status = AuctionClosingStatus.Active;
        }
    }

    public void Handle([SagaIdentityFrom(nameof(ReserveMet.ListingId))] ReserveMet message)
    {
        ReserveHasBeenMet = true;
    }

    public async Task Handle(
        [SagaIdentityFrom(nameof(ExtendedBiddingTriggered.ListingId))] ExtendedBiddingTriggered message,
        IMessageBus bus,
        IMessageStore messageStore,
        CancellationToken cancellationToken)
    {
        if (message.NewCloseAt <= ScheduledCloseAt) return;

        await CancelPendingCloseAsync(messageStore, ScheduledCloseAt, cancellationToken);

        await bus.ScheduleAsync(
            new CloseAuction(ListingId, message.NewCloseAt),
            message.NewCloseAt);

        ScheduledCloseAt = message.NewCloseAt;
        Status = AuctionClosingStatus.Extended;
    }

    public async Task<OutgoingMessages> Handle(
        [SagaIdentityFrom(nameof(CloseAuction.ListingId))] CloseAuction message,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // Idempotency — a CloseAuction arriving for a saga already terminated by
        // BuyItNowPurchased or ListingWithdrawn (and not cancelled in time) returns
        // empty without emitting a second outcome (workshop scenario 3.9).
        if (Status == AuctionClosingStatus.Resolved) return new OutgoingMessages();

        var now = time.GetUtcNow();
        var messages = new OutgoingMessages
        {
            new BiddingClosed(ListingId, now),
        };

        if (BidCount > 0 && ReserveHasBeenMet)
        {
            // SellerId is not tracked on the saga because StartAuctionClosingSagaHandler
            // (frozen from M3-S5) does not capture it. Load the Listing aggregate's live
            // projection — populated by Apply(BiddingOpened) — to read SellerId at close.
            var listing = await session.Events.AggregateStreamAsync<Listing>(
                ListingId, token: cancellationToken);

            messages.Add(new ListingSold(
                ListingId: ListingId,
                SellerId: listing!.SellerId,
                WinnerId: CurrentHighBidderId!.Value,
                HammerPrice: CurrentHighBid,
                BidCount: BidCount,
                SoldAt: now));
        }
        else if (BidCount > 0)
        {
            messages.Add(new ListingPassed(
                ListingId: ListingId,
                Reason: "ReserveNotMet",
                HighestBid: CurrentHighBid,
                BidCount: BidCount,
                PassedAt: now));
        }
        else
        {
            messages.Add(new ListingPassed(
                ListingId: ListingId,
                Reason: "NoBids",
                HighestBid: null,
                BidCount: BidCount,
                PassedAt: now));
        }

        Status = AuctionClosingStatus.Resolved;
        MarkCompleted();
        return messages;
    }

    // Wolverine's named-method convention — invoked instead of throwing UnknownSagaException
    // when a CloseAuction arrives but no saga document is found (e.g. the saga was already
    // deleted by MarkCompleted from a terminal handler and the pending CloseAuction slipped
    // through cancellation). Source: Wolverine SagaChain.cs — `NotFound` constant + branch
    // emitted instead of AssertSagaStateExistsFrame when a static method by that name exists.
    public static OutgoingMessages NotFound(CloseAuction message) => new();

    public async Task Handle(
        [SagaIdentityFrom(nameof(BuyItNowPurchased.ListingId))] BuyItNowPurchased message,
        IMessageStore messageStore,
        CancellationToken cancellationToken)
    {
        // Idempotency — replay-safe terminal guard. Without this, a redelivered
        // BuyItNowPurchased would re-cancel the (already-cancelled) close and re-emit
        // any cascade. Currently no cascade on this path (workshop scenario 3.8 — BIN is
        // its own terminal outcome, no BiddingClosed per BiddingClosed.cs contract docs),
        // but the guard keeps the terminal-handler shape uniform.
        if (Status == AuctionClosingStatus.Resolved) return;

        // Cancel the pending CloseAuction explicitly (M3-S5b OQ2 Path a — belt-and-suspenders
        // alongside MarkCompleted's saga-doc deletion + the static NotFound(CloseAuction)
        // safety net above). Without this, the scheduled CloseAuction would still fire and
        // hit the NotFound branch — correct behaviour, but observably noisier in the
        // scheduled-message store.
        await CancelPendingCloseAsync(messageStore, ScheduledCloseAt, cancellationToken);

        Status = AuctionClosingStatus.Resolved;
        MarkCompleted();
    }

    public async Task Handle(
        [SagaIdentityFrom(nameof(ListingWithdrawn.ListingId))] ListingWithdrawn message,
        IMessageStore messageStore,
        CancellationToken cancellationToken)
    {
        // Idempotency — same guard shape as Handle(BuyItNowPurchased). Withdrawal is the
        // "terminate without evaluation" path: no reserve check, no outcome event, no money
        // moves (workshop scenario 3.10; ListingWithdrawn.cs §Consumed by → Auctions BC).
        if (Status == AuctionClosingStatus.Resolved) return;

        // Same explicit-cancel rationale as Handle(BuyItNowPurchased) — Path a from M3-S5b
        // OQ2. Until a Selling-side publisher lands (deferred per M3 §3), the only producer
        // is the test fixture; cancellation discipline still matters because future producers
        // will inherit this saga's terminal contract unchanged.
        await CancelPendingCloseAsync(messageStore, ScheduledCloseAt, cancellationToken);

        Status = AuctionClosingStatus.Resolved;
        MarkCompleted();
    }

    internal static async Task CancelPendingCloseAsync(
        IMessageStore messageStore,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        // Narrow ±100ms window — scheduled messages are stored at the exact DateTimeOffset we
        // passed to ScheduleAsync, so this bracket isolates the one pending CloseAuction for
        // this listing without MessageType filtering. Wider windows risk cross-listing cancels
        // if two listings happen to share a scheduled time.
        var query = new ScheduledMessageQuery
        {
            ExecutionTimeFrom = at.AddMilliseconds(-100),
            ExecutionTimeTo = at.AddMilliseconds(100),
            MessageType = typeof(CloseAuction).FullName,
        };

        await messageStore.ScheduledMessages.CancelAsync(query, cancellationToken);
    }
}
