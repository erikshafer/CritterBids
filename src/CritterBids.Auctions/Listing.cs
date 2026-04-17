namespace CritterBids.Auctions;

public class Listing
{
    public Guid Id { get; set; }
    // S4 adds bidding state fields (CurrentHighBid, BidderId, BidCount, ReserveStatus, BuyItNowAvailable, ScheduledCloseAt) and Apply() methods per the DCB boundary model.

    // ─── S2 scaffold placeholder — removed in S4 ───────────────────────────────
    // Marten 8's JasperFxAggregationProjectionBase.AssembleAndAssertValidity() requires at
    // least one Apply/Create/ShouldDelete method on any aggregate registered via
    // LiveStreamAggregation<T>. The prompt's Open Question #2 anticipated this shape and
    // directed a stop-and-flag if the validator rejected the empty-Apply case; the call
    // was escalated in-session and the resolution chosen was this placeholder.
    //
    // The ScaffoldPlaceholder event is deliberately internal, never appended anywhere,
    // and never crosses the Contracts boundary. S4 replaces this file's entire
    // placeholder block with real Apply methods for BiddingOpened, BidPlaced,
    // BuyItNowOptionRemoved, ReserveMet, ExtendedBiddingTriggered, BuyItNowPurchased,
    // BiddingClosed, ListingSold, and ListingPassed per the DCB boundary model.
    public sealed record ScaffoldPlaceholder(Guid Id);

    public void Apply(ScaffoldPlaceholder @event)
    {
        // No-op — see placeholder comment above. Removed in S4.
    }
}
