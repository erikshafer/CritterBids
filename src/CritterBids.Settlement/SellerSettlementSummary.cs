namespace CritterBids.Settlement;

/// <summary>
/// Read model surfacing the financial outcome of a completed settlement, built by
/// <see cref="SellerSettlementSummaryHandler"/> as a handler-driven tolerant upsert from
/// <see cref="CritterBids.Contracts.Settlement.SettlementCompleted"/>. Backs the seller
/// console's payout confirmation view (M9-S3).
///
/// <para><b>Marten Id convention.</b> The <see cref="Id"/> property is the Marten document
/// key; its value is the <c>ListingId</c> for the listing this settlement closed (one
/// listing = one settlement). Matches the <see cref="PendingSettlement.Id"/> = <c>ListingId</c>
/// natural-key-as-id pattern from M5-S3.</para>
/// </summary>
public sealed record SellerSettlementSummary
{
    public Guid Id { get; init; }
    public Guid SettlementId { get; init; }
    public Guid SellerId { get; init; }
    public Guid WinnerId { get; init; }
    public decimal HammerPrice { get; init; }
    public decimal FeeAmount { get; init; }
    public decimal SellerPayout { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}
