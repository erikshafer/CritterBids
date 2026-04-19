namespace CritterBids.Auctions;

/// <summary>
/// Lifecycle states of the Auction Closing saga. The complete set of five values lands in
/// S5 even though Closing and Resolved are only entered by S5b handlers — declaring the full
/// enum now avoids type-level churn when S5b adds Handle(CloseAuction) real evaluation and
/// the Handle(BuyItNowPurchased) / Handle(ListingWithdrawn) terminal paths.
///
/// State transitions (S5 forward path only):
///   AwaitingBids → Active     (first BidPlaced)
///   AwaitingBids → Extended   (direct extend from no-bid state — possible in pathological
///                              scenarios; modeled for completeness)
///   Active       → Extended   (ExtendedBiddingTriggered)
///   Extended     → Extended   (chained extension — new event, same status)
/// Closing and Resolved are entered only via S5b handlers.
/// </summary>
public enum AuctionClosingStatus
{
    AwaitingBids = 0,
    Active = 1,
    Extended = 2,
    Closing = 3,
    Resolved = 4,
}
