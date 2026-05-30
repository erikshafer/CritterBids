namespace CritterBids.Relay.Hubs;

/// <summary>
/// Marker interface for notifications delivered over <see cref="OperationsHub"/>. Operations
/// notifications broadcast to the single <c>ops:staff</c> group, so no structural targeting
/// metadata is required. Retained per the <c>wolverine-signalr.md</c> convention; the full
/// OperationsHub push handler set lands in M6-S6.
/// </summary>
public interface IOperationsHubMessage
{
}
