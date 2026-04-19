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
/// <c>public Guid Id</c> is required even though Wolverine's University DCB example omits it
/// — Marten 8 treats the tag-aggregate as a document once <c>RegisterTagType.ForAggregate</c>
/// is wired, so <c>CleanAllMartenDataAsync</c> (and schema validation generally) throw
/// <c>InvalidDocumentException</c> at fixture startup without an Id/id property. Empirical
/// answer to M3-S4 Open Question 2.
///
/// <c>Apply(BiddingOpened)</c> populates <c>BuyItNowPrice</c> and <c>BuyItNowAvailable</c>.
/// <c>Apply(BuyItNowPurchased)</c> was added in M3-S4b to mark the listing terminal — a
/// second BuyNow on the same listing then rejects via the "BuyItNowNotAvailable" reason
/// code on the next <c>FetchForWritingByTags</c> cycle, and the DCB consistency assertion
/// prevents concurrent attempts from both committing.
/// </summary>
public class BidConsistencyState
{
    public Guid Id { get; set; }
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

    public void Apply(BidPlaced @event)
    {
        CurrentHighBid = @event.Amount;
        CurrentHighBidderId = @event.BidderId;
        BidCount = @event.BidCount;
    }

    public void Apply(BuyItNowOptionRemoved @event) => BuyItNowAvailable = false;

    public void Apply(ReserveMet @event) => this.ReserveMet = true;

    public void Apply(ExtendedBiddingTriggered @event) => ScheduledCloseAt = @event.NewCloseAt;

    public void Apply(BuyItNowPurchased @event)
    {
        IsOpen = false;
        BuyItNowAvailable = false;
    }
}
