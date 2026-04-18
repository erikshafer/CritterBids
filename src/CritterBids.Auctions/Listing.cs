using CritterBids.Contracts.Auctions;

namespace CritterBids.Auctions;

/// <summary>
/// Event-sourced aggregate for the Auctions-side view of a listing. Stream ID is the
/// UUID v7 flowed through from Selling's ListingPublished (ADR 007 stream-ID guidance).
///
/// Live-aggregated via LiveStreamAggregation&lt;Listing&gt; — state is rebuilt from the
/// stream's events on each load. S4 lands the first real Apply (BiddingOpened). S5 adds
/// Apply for BiddingClosed, ListingSold, ListingPassed; BidPlaced / ReserveMet /
/// ExtendedBiddingTriggered stay off the primary stream in S4 because they flow through
/// the DCB boundary — whether the live projection picks them up is Open Question 3 on
/// the S4 prompt and is answered empirically in the S4 retro.
/// </summary>
public class Listing
{
    public Guid Id { get; set; }

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
        Id = @event.ListingId;
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
