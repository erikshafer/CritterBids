using CritterBids.Contracts.Settlement;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Settlement;

/// <summary>
/// Wolverine handler that maintains the <see cref="SellerSettlementSummary"/> document from
/// <see cref="SettlementCompleted"/>. Tolerant-upsert shape: LoadAsync by ListingId, construct
/// if absent, store. Mirrors the <see cref="PendingSettlementHandler"/> pattern from M5-S3.
///
/// <para><b>METHOD-level sticky binding (ADR 027).</b> Rides
/// <c>settlement-settlement-events</c> — the Settlement BC's self-consumption queue, matching
/// <see cref="PendingSettlementHandler"/>'s <c>Handle(SettlementCompleted)</c> method binding.
/// Two handlers for the same message type on the same queue each get their own chain under
/// <c>MultipleHandlerBehavior.Separated</c>.</para>
///
/// <para><b>Idempotency.</b> A re-delivery of the same <c>SettlementCompleted</c> upserts
/// identical field values (deterministic <c>ListingId</c> key). Wolverine inbox dedup
/// prevents re-delivery in practice; the upsert is safe under at-least-once regardless.</para>
/// </summary>
public static class SellerSettlementSummaryHandler
{
    [StickyHandler("settlement-settlement-events")]
    public static async Task Handle(
        SettlementCompleted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        session.Store(new SellerSettlementSummary
        {
            Id = message.ListingId,
            SettlementId = message.SettlementId,
            SellerId = message.SellerId,
            WinnerId = message.WinnerId,
            HammerPrice = message.HammerPrice,
            FeeAmount = message.FeeAmount,
            SellerPayout = message.SellerPayout,
            CompletedAt = message.CompletedAt,
        });
    }
}
