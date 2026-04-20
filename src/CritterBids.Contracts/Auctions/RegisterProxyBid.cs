namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Command carrying a participant's intent to register a proxy bid on a listing. Starts the
/// Proxy Bid Manager saga (one instance per <c>ListingId + BidderId</c> composite key, UUID v5
/// derived per M4-D1 pin in
/// <c>src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs</c>).
///
/// Authored at M4-S1 as a vocabulary-lock stub; the handler that consumes it (the saga-start
/// handler in the Auctions BC) lands at M4-S3.
///
/// Publisher:
/// - Dispatched via <c>IMessageBus.InvokeAsync</c> from participant-facing command paths.
///   Until M6 opens the HTTP endpoint surface, dispatch is exercised through integration
///   tests (M4-S3 `RegisterProxyBidDispatchTests`).
/// Transport queue: none — internal command, no cross-BC routing. Handled in-process inside
/// the Auctions BC.
///
/// Consumed by:
/// - Auctions BC (M4-S3): Proxy Bid Manager saga-start handler — creates the saga document,
///   emits <see cref="ProxyBidRegistered"/> audit event, and seeds tracked state
///   (<c>MaxAmount</c>, <c>BidderCreditCeiling</c> loaded from the participant projection,
///   <c>Status: Active</c>).
///
/// Rejection conditions (saga-start handler at M4-S3):
/// - Listing not open for bidding (no <c>BiddingOpened</c> yet).
/// - <c>MaxAmount</c> exceeds the participant's credit ceiling (the saga would exhaust
///   immediately — rejected up-front rather than created-and-immediately-terminated).
/// - An active Proxy Bid Manager saga already exists for this <c>ListingId + BidderId</c>
///   (proxy cancellation/modification is out of MVP scope per M4 non-goals).
/// </summary>
public sealed record RegisterProxyBid(
    Guid ListingId,
    Guid BidderId,
    decimal MaxAmount);
