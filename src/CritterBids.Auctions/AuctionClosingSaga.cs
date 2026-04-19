using CritterBids.Contracts.Auctions;
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

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(CloseAuction.ListingId))] CloseAuction message)
    {
        // TODO(M3-S5b): real close evaluation lives here — emit BiddingClosed + ListingSold/ListingPassed, then MarkCompleted().
        return new OutgoingMessages();
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
