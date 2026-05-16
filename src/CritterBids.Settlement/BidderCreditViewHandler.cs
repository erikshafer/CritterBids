using CritterBids.Contracts.Participants;
using Marten;

namespace CritterBids.Settlement;

/// <summary>
/// Wolverine handler that maintains the <see cref="BidderCreditView"/> projection from the
/// two events feeding its lifecycle per W003 Phase 1 Part 7:
/// <list type="bullet">
///   <item><c>ParticipantSessionStarted</c> (Participants integration) — seeds the row at
///         <see cref="BidderCreditView.RemainingCredit"/> = <c>CreditCeiling</c>; idempotent
///         under re-delivery; never regresses an already-charged row.</item>
///   <item><c>WinnerCharged</c> (Settlement-internal, saga-emitted) — debits the row by the
///         charge amount; idempotent via <see cref="BidderCreditView.LastChargedSettlementId"/>
///         equality; lazy-inits a row with the negative-credit sentinel if no prior row exists.</item>
/// </list>
///
/// <para><b>Tolerant-upsert shape per handler.</b> <c>LoadAsync</c> by <c>BidderId</c>;
/// branch on absent vs present; mutate via record <c>with</c>; <c>session.Store</c>.
/// <c>AutoApplyTransactions()</c> commits after <c>Handle</c> returns. No
/// <c>OutgoingMessages</c>, no <c>IMessageBus</c>. Mirrors the M5-S3
/// <see cref="PendingSettlementHandler"/> shape per <c>marten-projections.md</c>
/// §"Handler-Driven Projections — Tolerant Upsert".</para>
///
/// <para><b>Idempotency contract per W003 Phase 1 Part 7.</b> On
/// <c>ParticipantSessionStarted</c> re-delivery for an already-charged row
/// (<see cref="BidderCreditView.LastChargedSettlementId"/> not null), the existing row is
/// preserved — re-seeding would regress the bidder's balance. On <c>WinnerCharged</c>
/// re-delivery (same <c>SettlementId</c>), the handler returns without mutation — the row
/// has already absorbed the debit.</para>
/// </summary>
public static class BidderCreditViewHandler
{
    public static async Task Handle(
        ParticipantSessionStarted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<BidderCreditView>(message.ParticipantId, cancellationToken);

        // Already-charged row preservation: if the bidder's credit row exists and has been
        // debited at least once (lazy-init or normal flow), re-seeding from a re-delivered
        // session-started event would erase the debit. Preserve the existing row.
        if (existing is { LastChargedSettlementId: not null }) return;

        session.Store(new BidderCreditView
        {
            BidderId               = message.ParticipantId,
            RemainingCredit        = message.CreditCeiling,
            LastChargedSettlementId = null,
            UpdatedAt              = message.StartedAt,
        });
    }

    public static async Task Handle(
        WinnerCharged message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<BidderCreditView>(message.WinnerId, cancellationToken);

        // Lazy-init: no prior ParticipantSessionStarted has seeded a row for this bidder.
        // The negative-credit sentinel marks "row created from WinnerCharged without a prior
        // session-started seed" as data per the BidderCreditView docstring.
        if (existing is null)
        {
            session.Store(new BidderCreditView
            {
                BidderId               = message.WinnerId,
                RemainingCredit        = -message.Amount,
                LastChargedSettlementId = message.SettlementId,
                UpdatedAt              = message.ChargedAt,
            });
            return;
        }

        // Idempotent re-delivery: same SettlementId already debited. No-op per W003 Phase 1 Part 7.
        if (existing.LastChargedSettlementId == message.SettlementId) return;

        session.Store(existing with
        {
            RemainingCredit        = existing.RemainingCredit - message.Amount,
            LastChargedSettlementId = message.SettlementId,
            UpdatedAt              = message.ChargedAt,
        });
    }
}
