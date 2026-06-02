using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Hubs;

/// <summary>
/// Staff-facing SignalR hub at <c>/hub/operations</c>. Drives the live ops dashboard. Set up and
/// <c>MapHub</c>-registered in M6-S5 so host wiring is done once, but its per-event push handlers
/// — the staff-facing duplicate of the auctions feed plus dispute / escalation / session-board
/// pushes — land in M6-S6. No OperationsHub push handlers exist this slice.
///
/// Plain <see cref="Hub"/> per ADR 023 path (b). Connections enrol into the single
/// <c>ops:staff</c> group.
///
/// <para><b>Staff authentication (M7-S6, ADR-024).</b> The hub is gated by the <c>StaffOnly</c>
/// policy. The browser SignalR transports cannot set a custom header, so the staff credential rides
/// the <c>access_token</c> query string, which the <c>StaffToken</c> scheme reads only for this hub
/// path. The literal policy string is used because the Relay BC cannot reference the host where the
/// policy-name constant lives. Per-staff-group targeting refinement is deferred past M7
/// (ADR-024 item 6); <c>Clients.All</c> is an all-staff broadcast once gated. The
/// <c>BiddingHub</c> stays anonymous.</para>
/// </summary>
[Authorize(Policy = "StaffOnly")]
public sealed class OperationsHub : Hub
{
    /// <summary>Enrols every staff connection into the broadcast <c>ops:staff</c> group.</summary>
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "ops:staff");
        await base.OnConnectedAsync();
    }
}
