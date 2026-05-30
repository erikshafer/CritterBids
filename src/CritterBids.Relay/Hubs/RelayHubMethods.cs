namespace CritterBids.Relay.Hubs;

/// <summary>
/// Well-known SignalR client method names. All Relay server-to-client pushes are delivered on a
/// single client method (<see cref="ReceiveMessage"/>) — the client switches on the payload type —
/// matching the convention documented in <c>docs/skills/wolverine-signalr.md</c>.
/// </summary>
internal static class RelayHubMethods
{
    /// <summary>The single client method every Relay notification is delivered on.</summary>
    public const string ReceiveMessage = "ReceiveMessage";
}
