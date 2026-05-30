namespace CritterBids.Relay.Hubs;

/// <summary>
/// Marker interface for notifications delivered over <see cref="BiddingHub"/>. Carries the
/// structural metadata (<see cref="ListingId"/>, <see cref="BidderId"/>) used to derive the
/// target hub group key.
///
/// Under ADR 023 (reactive-broadcast architecture) CritterBids uses <b>path (b)</b>: plain
/// <c>Hub</c> subclasses driven by Wolverine handlers that inject <c>IHubContext&lt;BiddingHub&gt;</c>
/// and call <c>SendAsync</c> directly, choosing the group explicitly. Group targeting is therefore
/// performed in the handler — it is NOT derived automatically from this interface. The interface is
/// retained for documentation continuity with <c>docs/skills/wolverine-signalr.md</c> and to keep
/// the documented path-(a) <c>WolverineHub</c> + <c>MessagesImplementing&lt;IBiddingHubMessage&gt;().ToSignalR()</c>
/// migration door open without a contract change.
/// </summary>
public interface IBiddingHubMessage
{
    /// <summary>Listing the notification concerns — used to target the <c>listing:{listingId}</c> group. Null for broadcast.</summary>
    Guid? ListingId { get; }

    /// <summary>Bidder the notification concerns — used to target the <c>bidder:{bidderId}</c> group. Null for listing-wide notifications.</summary>
    Guid? BidderId { get; }
}
