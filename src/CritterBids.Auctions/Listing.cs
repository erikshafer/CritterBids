namespace CritterBids.Auctions;

public class Listing
{
    public Guid Id { get; set; }
    // S4 adds bidding state fields (CurrentHighBid, BidderId, BidCount, ReserveStatus, BuyItNowAvailable, ScheduledCloseAt) and Apply() methods per the DCB boundary model.
}
