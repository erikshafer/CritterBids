namespace CritterBids.Auctions;

/// <summary>
/// Lifecycle states for <see cref="ProxyBidManagerSaga"/> per Workshop 002 §4.
///
/// <para><see cref="Active"/> — the saga is defending the bidder's position against
/// competing bids up to <c>MaxAmount</c>.</para>
///
/// <para><see cref="Exhausted"/> — a competing bid has been observed for which the next
/// defensive bid would exceed <c>MaxAmount</c> (scenarios 4.3, 4.9). Terminal. Reached at
/// M4-S4; declared at S3 to match the <see cref="AuctionClosingStatus"/> "declare full enum
/// at skeleton" precedent (M3-S5).</para>
///
/// <para><see cref="ListingClosed"/> — the listing's bidding cycle terminated under one of
/// the auction's outcome events (scenarios 4.6 / 4.7 / 4.8). Terminal. Reached at M4-S4.</para>
/// </summary>
public enum ProxyBidManagerStatus
{
    Active,
    Exhausted,
    ListingClosed,
}
