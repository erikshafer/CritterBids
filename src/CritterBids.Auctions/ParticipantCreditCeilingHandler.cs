using CritterBids.Contracts.Participants;
using Marten;
using Wolverine.Attributes;

namespace CritterBids.Auctions;

/// <summary>
/// Wolverine handler that maintains the <see cref="ParticipantCreditCeiling"/> Marten
/// document from <c>ParticipantSessionStarted</c> integration events flowing in on the
/// <c>auctions-participants-events</c> RabbitMQ queue (wired in <c>Program.cs</c>).
///
/// <para><b>Tolerant-upsert shape per the marten-projections skill.</b> <c>LoadAsync</c>
/// by <c>ParticipantId</c>; if an existing row is present, preserve it verbatim and
/// return — re-delivery of <c>ParticipantSessionStarted</c> never regresses
/// <see cref="ParticipantCreditCeiling.RegisteredAt"/> and never overwrites
/// <see cref="ParticipantCreditCeiling.CreditCeiling"/>. The
/// <c>AutoApplyTransactions()</c> policy commits the session after <c>Handle</c> returns.
/// No <see cref="Wolverine.OutgoingMessages"/>; no <see cref="Wolverine.IMessageBus"/>.</para>
///
/// <para><b>Why duplicate from Settlement's <see cref="CritterBids.Settlement.BidderCreditViewHandler"/></b>:
/// per ADR 011 and integration-messaging skill §L2, BCs maintain their own local copies of
/// upstream seed data rather than read across BC boundaries. This is the M4-D4 duplicate-
/// projection pattern's second lived application — Settlement's <c>BidderCreditView</c>
/// shipped at M5-S5; the Auctions projection here ships at M4-S4. Each consumes
/// <c>ParticipantSessionStarted</c> on a BC-specific RabbitMQ queue. The duplication is
/// intentional and load-bearing for saga-start latency:
/// <see cref="StartProxyBidManagerSagaHandler"/> reads the cached row instead of crossing
/// the BC boundary to the Participants aggregate.</para>
/// </summary>
[StickyHandler("auctions-participants-events")]
public static class ParticipantCreditCeilingHandler
{
    public static async Task Handle(
        ParticipantSessionStarted message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var existing = await session.LoadAsync<ParticipantCreditCeiling>(
            message.ParticipantId, cancellationToken);

        // Re-delivery preservation: a row already present absorbs the duplicate without
        // overwriting RegisteredAt or CreditCeiling. Mirrors Settlement.BidderCreditViewHandler's
        // already-charged-row guard, but the trigger here is "row already created" rather
        // than "row already debited" because this projection has no per-bidder mutation
        // downstream on the Auctions side.
        if (existing is not null) return;

        session.Store(new ParticipantCreditCeiling
        {
            BidderId      = message.ParticipantId,
            CreditCeiling = message.CreditCeiling,
            RegisteredAt  = message.StartedAt,
        });
    }
}
