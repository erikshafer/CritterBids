// The single SignalR client method every Relay push is delivered on (mirrors
// CritterBids.Relay.Hubs.RelayHubMethods.ReceiveMessage, ADR 023). The client switches on the
// parsed payload shape, not on the method name.
export const RECEIVE_MESSAGE = "ReceiveMessage";
