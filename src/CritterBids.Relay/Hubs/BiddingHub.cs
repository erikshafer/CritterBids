using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Hubs;

/// <summary>
/// Participant-facing SignalR hub at <c>/hub/bidding</c>. Delivers the real-time bid feed,
/// listing-outcome notifications, and the winner's settlement confirmation to anonymous
/// participants during a live auction.
///
/// Plain <see cref="Hub"/> (not <c>WolverineHub</c>) per ADR 023 path (b): SignalR is
/// outbound-only here — participants drive the server through HTTP endpoints, and Relay's
/// Wolverine handlers push to this hub via <c>IHubContext&lt;BiddingHub&gt;</c>.
///
/// Group keys follow the M6-S1 convention captured in <c>docs/skills/wolverine-signalr.md</c>
/// § Hub Group Management: <c>listing:{listingId}</c> (live feed for everyone watching a listing)
/// and <c>bidder:{bidderId}</c> (notifications for a specific participant).
/// </summary>
public sealed class BiddingHub : Hub
{
    /// <summary>
    /// Enrols the connection into its <c>listing:{id}</c> and/or <c>bidder:{id}</c> groups from
    /// the <c>listingId</c> / <c>bidderId</c> query-string parameters. Query-string identity is
    /// acceptable here — anonymous sessions carry no sensitive commercial data and the
    /// <c>BidderId</c> is a display identifier, not a trust anchor.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();

        var listingId = httpContext?.Request.Query["listingId"].ToString();
        if (!string.IsNullOrEmpty(listingId) && Guid.TryParse(listingId, out var listingGuid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"listing:{listingGuid}");
        }

        var bidderId = httpContext?.Request.Query["bidderId"].ToString();
        if (!string.IsNullOrEmpty(bidderId) && Guid.TryParse(bidderId, out var bidderGuid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"bidder:{bidderGuid}");
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Explicitly enrols the calling connection into a listing group. Awaiting this client
    /// invocation guarantees server-side group enrolment has completed — the deterministic,
    /// race-free join path used by the integration tests (and available to clients that connect
    /// before they know which listing to watch).
    /// </summary>
    public Task JoinListingGroup(Guid listingId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"listing:{listingId}");

    /// <summary>
    /// Explicitly enrols the calling connection into a bidder group. See
    /// <see cref="JoinListingGroup"/> for the determinism rationale.
    /// </summary>
    public Task JoinBidderGroup(Guid bidderId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"bidder:{bidderId}");
}
