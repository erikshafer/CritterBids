namespace CritterBids.Contracts.Settlement;

/// <summary>
/// Integration event published by the Settlement BC when the seven-phase financial
/// workflow exits via the failure path. Emitted by the Settlement Saga from the
/// <c>ReserveChecked(WasMet: false)</c> state per workshop 003 scenario §3.2.
/// MVP scope: only the reserve-not-met path produces <c>PaymentFailed</c>;
/// real-payment-processor failure paths are post-MVP per W003 Phase 1 Part 3's
/// compensation-design deferral.
/// Transported via RabbitMQ on the publisher-side queue routes wired in M5-S5.
/// Inbound queue routes are post-M5 (Operations BC has not shipped at M5 close).
///
/// Consumed by:
/// - Operations BC (post-M5): Surfaces the failed settlement to the auction operator's
///   dashboard for intervention. The <c>Reason</c> field drives the dashboard's
///   diagnostic copy.
/// - Listings BC (post-M5): May update <c>CatalogListingView.Status</c> to a failure
///   indicator distinct from <c>"Settled"</c>; exact disposition decided when Operations
///   ships and the failure-path UX surfaces in the demo. The PendingSettlement projection
///   already transitions to <c>Status: Failed</c> on this event per workshop 003 scenario
///   §8.7.
///
/// Field rationale:
/// - <c>SettlementId</c> — the deterministic UUID v5 stream identifier (same field
///   semantics as <c>SettlementCompleted</c>). Carried so consumers can correlate the
///   failure with the specific settlement instance and the stream's earlier events
///   (<c>SettlementInitiated</c>, <c>ReserveCheckCompleted</c>).
/// - <c>ListingId</c> — the source listing. Carried so consumers do not need to load
///   the financial event stream to attribute the failure to a listing.
/// - <c>WinnerId</c> — the bidder who was about to be charged but was not. Carried for
///   ops-dashboard display ("which bidder is unaffected?"). The credit ledger does not
///   debit on this path; <c>BidderCreditView</c> remains at its pre-failure balance.
/// - <c>Reason</c> — a short failure-classification string. M5 produces the literal
///   <c>"ReserveNotMet"</c> per workshop 003 scenario §3.2. Post-MVP failure modes
///   (insufficient credit, payment-provider rejection, ledger divergence) extend the
///   set; the field shape is open for future values without contract change.
/// - <c>FailedAt</c> — handler-stamped timestamp at the failure-emission phase.
/// </summary>
public sealed record PaymentFailed(
    Guid SettlementId,
    Guid ListingId,
    Guid WinnerId,
    string Reason,
    DateTimeOffset FailedAt);
