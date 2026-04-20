namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Integration event published by the Auctions BC when a Proxy Bid Manager saga is created.
/// Audit-shape — records the saga's initial state for downstream consumers that need to
/// know the proxy exists without caring about its ongoing reaction cycle.
///
/// Authored at M4-S1 as a vocabulary-lock stub; produced at M4-S3 by the Proxy Bid Manager
/// saga-start handler.
///
/// Transport queue: TBD (consumers are post-M5). Relay (post-M5) is the only known
/// consumer — queue name finalized when Relay subscribes.
///
/// Consumed by:
/// - Relay BC (post-M5): Push a "your proxy has been registered" notification to the
///   bidder's participant session. Distinct from the generic bid-placed broadcast — the
///   proxy registration itself does not move price; the notification confirms the
///   registration succeeded and the saga is now defending the bidder's position.
/// - Operations BC (post-M5): Live-board indicator counting active proxies per listing,
///   informs ops staff of demo-moment "how many proxies are active right now?" surfaces.
/// - Auctions BC internally: Not consumed — the saga itself produces the event via
///   <c>OutgoingMessages</c>; internal reactions to registration are handled by the
///   saga-start handler directly, not a subscriber.
///
/// Full payload per integration-messaging.md L2 — <c>RegisteredAt</c> and <c>MaxAmount</c>
/// are required by both known consumers at first commit, even though the Relay/Operations
/// subscribers land in M5+.
/// </summary>
public sealed record ProxyBidRegistered(
    Guid ListingId,
    Guid BidderId,
    decimal MaxAmount,
    DateTimeOffset RegisteredAt);
