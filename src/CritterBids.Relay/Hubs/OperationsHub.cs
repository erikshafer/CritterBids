using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Hubs;

/// <summary>
/// Staff-facing SignalR hub at <c>/hub/operations</c>. Drives the live ops dashboard. Set up and
/// <c>MapHub</c>-registered in M6-S5 so host wiring is done once, but its per-event push handlers
/// — the staff-facing duplicate of the auctions feed plus dispute / escalation / session-board
/// pushes — land in M6-S6. No OperationsHub push handlers exist this slice.
///
/// Plain <see cref="Hub"/> per ADR 023 path (b). Connections enrol into the single
/// <c>ops:staff</c> group. Staff authentication (passphrase / JWT bearer per
/// <c>docs/skills/wolverine-signalr.md</c>) is deferred to M6-S6 alongside the first
/// OperationsHub push handlers.
/// </summary>
public sealed class OperationsHub : Hub
{
    /// <summary>Enrols every staff connection into the broadcast <c>ops:staff</c> group.</summary>
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "ops:staff");
        await base.OnConnectedAsync();
    }
}
