// The single SignalR client method every Relay push is delivered on (mirrors
// CritterBids.Relay.Hubs.RelayHubMethods.ReceiveMessage, ADR 023). Duplicated from the bidder app
// for now — the client/shared/ extraction is a planned housekeeping task, not an S5 gate (ADR 025).
export const RECEIVE_MESSAGE = "ReceiveMessage";

/** The staff-gated operations hub path (ADR 024; mirrors StaffAuthConstants.OperationsHubPath). */
export const OPERATIONS_HUB_URL = "/hub/operations";
