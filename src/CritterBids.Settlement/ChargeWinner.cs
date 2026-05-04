namespace CritterBids.Settlement;

/// <summary>
/// Self-send continuation command emitted by the saga's <c>Handle(CheckReserve)</c> at the
/// happy-path branch (<c>WasMet: true</c> or <c>ReservePrice: null</c>). The saga's
/// <c>Handle(ChargeWinner)</c> debits the winner's credit ledger per workshop 003 §3.1
/// and advances state to <see cref="SettlementStatus.WinnerCharged"/>.
/// </summary>
public sealed record ChargeWinner(Guid SettlementId);
