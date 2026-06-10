using CritterBids.Contracts.Settlement;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Operations;

/// <summary>
/// Operations BC's Settlement-family consumer — the single ADR-014 Path A sibling handler that
/// folds the Settlement BC's three integration events into <see cref="SettlementQueueView"/>.
/// One sibling class per source BC; Settlement is the only source for the settlement queue, so
/// the three <c>Handle</c> overloads (one per event) live together here. The handler returns
/// <see cref="Task"/> and writes only via the injected Marten session — Operations is a pure
/// consumer, so there are <b>no</b> <c>OutgoingMessages</c> and <b>no</b> <c>IMessageBus</c>
/// (it publishes nothing).
///
/// <para><b>Tolerant upsert.</b> Each overload loads-or-constructs the row by
/// <see cref="SettlementQueueView.SettlementId"/>, mutates via record <c>with</c>, and stores —
/// the lived shape from Listings' <c>SettlementStatusHandler</c> and Settlement's
/// <c>PendingSettlementHandler</c>. Re-delivery and out-of-order arrival are absorbed by the
/// load-mutate-store discipline, not optimistic concurrency.</para>
///
/// <para><b>Status derivation + guards</b> (W006 §1, no more, no fewer):
/// <c>PaymentFailed</c> → <see cref="SettlementQueueStatus.Failed"/>; <c>SettlementCompleted</c>
/// → <see cref="SettlementQueueStatus.Completed"/> unless already
/// <see cref="SettlementQueueStatus.PaidOut"/> (the one mandated preservation guard:
/// <c>PaidOut</c> does not regress on a re-delivered <c>SettlementCompleted</c>);
/// <c>SellerPayoutIssued</c> → <see cref="SettlementQueueStatus.PaidOut"/>. <c>ListingId</c> and
/// <c>WinnerId</c> are set-once via the <see cref="System.Guid.Empty"/> sentinel;
/// <c>SellerPayoutIssued</c> touches neither (it carries neither). <c>LastUpdatedAt</c> is
/// latest-wins (max of the existing stamp and the incoming event time), so a re-delivered older
/// event never rewinds it.</para>
/// </summary>
[StickyHandler("operations-settlement-events")]
public static class SettlementQueueHandler
{
    public static async Task Handle(
        PaymentFailed message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.SettlementId, cancellationToken);

        // PaymentFailed and the success path are mutually exclusive on a real settlement, so
        // Status is set to Failed unconditionally — W006 §1 mandates no Failed-regression guard
        // (only PaidOut must not regress to Completed). ListingId/WinnerId follow the set-once
        // guard; FailureReason is the staff-attention flag.
        session.Store(view with
        {
            ListingId     = view.ListingId == Guid.Empty ? message.ListingId : view.ListingId,
            WinnerId      = view.WinnerId == Guid.Empty ? message.WinnerId : view.WinnerId,
            FailureReason = message.Reason,
            Status        = SettlementQueueStatus.Failed,
            LastUpdatedAt = Latest(view.LastUpdatedAt, message.FailedAt),
        });
    }

    public static async Task Handle(
        SettlementCompleted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.SettlementId, cancellationToken);

        session.Store(view with
        {
            ListingId    = view.ListingId == Guid.Empty ? message.ListingId : view.ListingId,
            WinnerId     = view.WinnerId == Guid.Empty ? message.WinnerId : view.WinnerId,
            SellerId     = message.SellerId,
            HammerPrice  = message.HammerPrice,
            FeeAmount    = message.FeeAmount,
            SellerPayout = message.SellerPayout,
            // Preservation guard (W006 §1): a re-delivered SettlementCompleted after the payout
            // must not regress PaidOut back to Completed.
            Status        = view.Status == SettlementQueueStatus.PaidOut
                ? SettlementQueueStatus.PaidOut
                : SettlementQueueStatus.Completed,
            LastUpdatedAt = Latest(view.LastUpdatedAt, message.CompletedAt),
        });
    }

    public static async Task Handle(
        SellerPayoutIssued message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var view = await LoadOrCreate(session, message.SettlementId, cancellationToken);

        // SellerPayoutIssued carries no ListingId/WinnerId — it must not invent them, so those
        // set-once fields are left exactly as a prior PaymentFailed/SettlementCompleted set them
        // (or Guid.Empty if this is the first event to arrive for the settlement). PaidOut is
        // terminal; it is reached only via this event.
        session.Store(view with
        {
            SellerId      = message.SellerId,
            PayoutAmount  = message.PayoutAmount,
            FeeDeducted   = message.FeeDeducted,
            Status        = SettlementQueueStatus.PaidOut,
            LastUpdatedAt = Latest(view.LastUpdatedAt, message.IssuedAt),
        });
    }

    private static async Task<SettlementQueueView> LoadOrCreate(
        IDocumentSession session,
        Guid settlementId,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<SettlementQueueView>(settlementId, cancellationToken);
        return existing ?? new SettlementQueueView { SettlementId = settlementId };
    }

    private static DateTimeOffset Latest(DateTimeOffset existing, DateTimeOffset incoming) =>
        incoming > existing ? incoming : existing;
}
