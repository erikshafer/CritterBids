namespace CritterBids.Settlement;

/// <summary>
/// Emitted by the saga's <c>Handle(CalculateFee)</c> phase per workshop 003 scenarios
/// §4.1 (standard 10% fee) and §4.2 (banker's rounding). Computes the platform fee and
/// the resulting seller payout via <c>Math.Round(HammerPrice * (FeePercentage / 100m), 2,
/// MidpointRounding.ToEven)</c> per W003 §4.2's MVP rounding convention. The two fields
/// (<see cref="FeeAmount"/> + <see cref="SellerPayout"/>) are non-nullable on this event
/// so subsequent phases can read them without null guards.
///
/// <para><b>Stream-internal — not in <c>CritterBids.Contracts.Settlement.*</c>.</b> The
/// fee-calculation phase is a Settlement-internal concern; the integration-out
/// <see cref="CritterBids.Contracts.Settlement.SettlementCompleted"/> carries the same
/// <c>FeeAmount</c> + <c>SellerPayout</c> values for cross-BC consumers.</para>
/// </summary>
public sealed record FinalValueFeeCalculated(
    Guid SettlementId,
    decimal HammerPrice,
    decimal FeePercentage,
    decimal FeeAmount,
    decimal SellerPayout,
    DateTimeOffset CalculatedAt);
