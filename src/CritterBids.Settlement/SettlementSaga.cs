namespace CritterBids.Settlement;

/// <summary>
/// Settlement BC's seven-phase financial workflow document. Wolverine Saga per ADR-019
/// (Settlement Workflow Hosting). Persisted by Marten under a deterministic UUID v5
/// SettlementId per W003 Phase 1 Part 6 (UuidV5(AuctionsNamespace, $"settlement:{ListingId}")).
///
/// M5-S2 scope: empty shell. The saga registers cleanly via SettlementModule's
/// Schema.For&lt;SettlementSaga&gt;().Identity(...).UseNumericRevisions(true) so the schema
/// is created at host startup, but the saga has no Handle methods, no state fields beyond
/// Id, and no MarkCompleted() calls.
///
/// S4 adds the SettlementStatus enum (Initiated → ReserveChecked → WinnerCharged →
/// FeeCalculated → PayoutIssued → Completed, with a Failed exit per W003 Phase 1 Part 3),
/// the per-phase Handle methods for CheckReserve / ChargeWinner / CalculateFee /
/// IssueSellerPayout / CompleteSettlement, the seven Settlement-internal events
/// (SettlementInitiated / ReserveCheckCompleted / WinnerCharged / FinalValueFeeCalculated /
/// SellerPayoutIssued plus the integration-out trio), and the MarkCompleted() calls at
/// terminal states. S5 adds the FailSettlement / PaymentFailed branch and the BIN-source
/// short-circuit through the reserve-check phase per W003 Phase 1 Part 5.
/// </summary>
public sealed class SettlementSaga : Wolverine.Saga
{
    public Guid Id { get; set; }
}
