namespace CritterBids.Settlement;

/// <summary>
/// Self-send continuation command emitted by the saga's <c>Handle(ChargeWinner)</c>. The
/// saga's <c>Handle(CalculateFee)</c> computes the platform fee and seller payout per
/// workshop 003 §4.1 / §4.2 and advances state to <see cref="SettlementStatus.FeeCalculated"/>.
/// </summary>
public sealed record CalculateFee(Guid SettlementId);
