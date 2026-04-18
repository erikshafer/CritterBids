using CritterBids.Contracts.Auctions;

namespace CritterBids.Auctions;

/// <summary>
/// DCB boundary model for bid-acceptance decisions. Projected from events tagged with
/// <see cref="ListingStreamId"/> — the tag query loaded by <c>PlaceBidHandler.Load</c> selects
/// every acceptance-relevant event in the listing's bidding lifecycle (<c>BiddingOpened</c>,
/// <c>BidPlaced</c>, <c>BuyItNowOptionRemoved</c>, <c>ReserveMet</c>,
/// <c>ExtendedBiddingTriggered</c>). <c>BidRejected</c> is excluded by type (W002-7 decision)
/// so rejected bids are invisible to the decision model by construction.
///
/// Canonical Wolverine state classes (Jeremy Miller's University example) omit
/// <c>public Guid Id</c>. We start without it per M3-S4 Open Question 2 — the property is
/// added only if test-harness teardown throws <c>InvalidDocumentException</c>.
///
/// <c>Apply(BiddingOpened)</c> populates <c>BuyItNowPrice</c> and <c>BuyItNowAvailable</c>
/// even though this slice's PlaceBid flow uses <c>BuyItNowAvailable</c> only for scenario 1.1
/// (first-bid removes BIN). M3-S4b reuses the same state class for <c>BuyNowHandler</c>
/// without extending it.
/// </summary>
public class BidConsistencyState
{
    public Guid ListingId { get; private set; }
    public Guid SellerId { get; private set; }
    public decimal StartingBid { get; private set; }
    public decimal? ReserveThreshold { get; private set; }
    public decimal? BuyItNowPrice { get; private set; }
    public DateTimeOffset ScheduledCloseAt { get; private set; }
    public DateTimeOffset OriginalCloseAt { get; private set; }
    public bool ExtendedBiddingEnabled { get; private set; }
    public TimeSpan? ExtendedBiddingTriggerWindow { get; private set; }
    public TimeSpan? ExtendedBiddingExtension { get; private set; }
    public TimeSpan MaxDuration { get; private set; }

    public decimal CurrentHighBid { get; private set; }
    public Guid? CurrentHighBidderId { get; private set; }
    public int BidCount { get; private set; }
    public bool BuyItNowAvailable { get; private set; }
    public bool ReserveMet { get; private set; }
    public bool IsOpen { get; private set; }

    public void Apply(BiddingOpened @event)
    {
        ListingId = @event.ListingId;
        SellerId = @event.SellerId;
        StartingBid = @event.StartingBid;
        ReserveThreshold = @event.ReserveThreshold;
        BuyItNowPrice = @event.BuyItNowPrice;
        ScheduledCloseAt = @event.ScheduledCloseAt;
        OriginalCloseAt = @event.ScheduledCloseAt;
        ExtendedBiddingEnabled = @event.ExtendedBiddingEnabled;
        ExtendedBiddingTriggerWindow = @event.ExtendedBiddingTriggerWindow;
        ExtendedBiddingExtension = @event.ExtendedBiddingExtension;
        MaxDuration = @event.MaxDuration;

        CurrentHighBid = 0m;
        CurrentHighBidderId = null;
        BidCount = 0;
        BuyItNowAvailable = @event.BuyItNowPrice.HasValue;
        ReserveMet = false;
        IsOpen = true;
    }
}
