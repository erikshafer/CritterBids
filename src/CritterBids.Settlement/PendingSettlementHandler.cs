using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Contracts.Settlement;
using Marten;
using Wolverine.Attributes;
using SellingListingWithdrawn = CritterBids.Contracts.Selling.ListingWithdrawn;

namespace CritterBids.Settlement;

/// <summary>
/// Wolverine handler that maintains the <see cref="PendingSettlement"/> projection from the
/// five cross-BC integration events feeding its lifecycle:
/// <list type="bullet">
///   <item><c>ListingPublished</c> (Selling) — creates the row in <see cref="PendingSettlementStatus.Pending"/> per scenario §8.1; idempotent under at-least-once redelivery per §8.8.</item>
///   <item><c>ListingPassed</c> (Auctions) — transitions <see cref="PendingSettlementStatus.Pending"/> to <see cref="PendingSettlementStatus.Expired"/> per §8.4.</item>
///   <item><c>ListingWithdrawn</c> (Selling) — transitions <see cref="PendingSettlementStatus.Pending"/> to <see cref="PendingSettlementStatus.Expired"/> per §8.5.</item>
///   <item><c>SettlementCompleted</c> (Settlement self-publish) — transitions <see cref="PendingSettlementStatus.Pending"/> to <see cref="PendingSettlementStatus.Consumed"/> per §8.6.</item>
///   <item><c>PaymentFailed</c> (Settlement self-publish) — transitions <see cref="PendingSettlementStatus.Pending"/> to <see cref="PendingSettlementStatus.Failed"/> per §8.7.</item>
/// </list>
///
/// <para><b>Tolerant-upsert shape per handler.</b> <c>LoadAsync</c> by <c>ListingId</c>; if absent
/// (cross-queue race or first arrival), construct a minimal row; mutate via record <c>with</c>;
/// <c>session.Store</c>. <c>AutoApplyTransactions()</c> commits after <c>Handle</c> returns. No
/// <c>OutgoingMessages</c>, no <c>IMessageBus</c>. Mirrors the M3-S6 <see cref="Listings.AuctionStatusHandler"/>
/// shape per <c>marten-projections.md</c> §"Handler-Driven Projections — Tolerant Upsert".</para>
///
/// <para><b>Status preservation under terminal collisions.</b> Terminal statuses
/// (<see cref="PendingSettlementStatus.Consumed"/>, <see cref="PendingSettlementStatus.Expired"/>,
/// <see cref="PendingSettlementStatus.Failed"/>) are absorbing. A handler that would otherwise
/// transition the row only does so when the current status is <see cref="PendingSettlementStatus.Pending"/>;
/// otherwise the existing terminal status is preserved. This guards against unobserved race conditions
/// (e.g. <c>ListingPassed</c> arriving on a row already <c>Consumed</c> by an out-of-order delivery)
/// without requiring scenario-level disambiguation rules.</para>
///
/// <para><b>Idempotency for re-delivered <c>ListingPublished</c>.</b> A second delivery of the
/// same <c>ListingPublished</c> upserts the same field values; Wolverine inbox dedup should
/// prevent the second delivery in production, but the upsert is safe under at-least-once redelivery
/// either way. The handler does not regress an already-terminal row's status to
/// <see cref="PendingSettlementStatus.Pending"/>.</para>
///
/// <para><b>METHOD-level sticky bindings (ADR 027, M8-S3c).</b> This one class consumes from
/// THREE Settlement-owned queues, so the sticky bindings sit on the methods, not the class
/// (the fluent <c>AddStickyHandler</c> form is type-level and cannot express this split —
/// the reason the attribute form was chosen for the whole slice): the Selling-source events
/// ride <c>settlement-selling-events</c>, <c>ListingPassed</c> rides
/// <c>settlement-auctions-events</c>, and the BC's own <c>SettlementCompleted</c> /
/// <c>PaymentFailed</c> ride the new <c>settlement-settlement-events</c> self-consumption
/// queue (before S3c they were fan-out-fed from queues other BCs own).</para>
/// </summary>
public static class PendingSettlementHandler
{
    [StickyHandler("settlement-selling-events")]
    public static async Task Handle(
        ListingPublished message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, cancellationToken);

        // First delivery: create with Status = Pending. Re-delivery against an existing row
        // preserves the row's current Status (which may be terminal if a later event arrived
        // first under at-least-once redelivery).
        var status = existing?.Status ?? PendingSettlementStatus.Pending;

        session.Store(new PendingSettlement
        {
            Id             = message.ListingId,
            SellerId       = message.SellerId,
            ReservePrice   = message.ReservePrice,
            BuyItNowPrice  = message.BuyItNow,
            FeePercentage  = message.FeePercentage,
            PublishedAt    = message.PublishedAt,
            Status         = status,
        });
    }

    [StickyHandler("settlement-auctions-events")]
    public static async Task Handle(
        ListingPassed message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, cancellationToken)
            ?? new PendingSettlement { Id = message.ListingId };

        if (existing.Status != PendingSettlementStatus.Pending) return;

        session.Store(existing with { Status = PendingSettlementStatus.Expired });
    }

    [StickyHandler("settlement-selling-events")]
    public static async Task Handle(
        SellingListingWithdrawn message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, cancellationToken)
            ?? new PendingSettlement { Id = message.ListingId };

        if (existing.Status != PendingSettlementStatus.Pending) return;

        session.Store(existing with { Status = PendingSettlementStatus.Expired });
    }

    [StickyHandler("settlement-settlement-events")]
    public static async Task Handle(
        SettlementCompleted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, cancellationToken)
            ?? new PendingSettlement { Id = message.ListingId };

        if (existing.Status != PendingSettlementStatus.Pending) return;

        session.Store(existing with { Status = PendingSettlementStatus.Consumed });
    }

    [StickyHandler("settlement-settlement-events")]
    public static async Task Handle(
        PaymentFailed message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, cancellationToken)
            ?? new PendingSettlement { Id = message.ListingId };

        if (existing.Status != PendingSettlementStatus.Pending) return;

        session.Store(existing with { Status = PendingSettlementStatus.Failed });
    }
}
