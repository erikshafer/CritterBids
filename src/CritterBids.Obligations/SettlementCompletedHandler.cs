using CritterBids.Contracts.Settlement;
using Marten;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Attributes;

namespace CritterBids.Obligations;

/// <summary>
/// Starts the <see cref="PostSaleCoordinationSaga"/> when the Settlement BC publishes
/// <c>SettlementCompleted</c> — the trigger for the post-sale commitment chain between the
/// winning bidder and the seller. Runs as a separate static class — per the wolverine-sagas
/// skill the Start pattern lives outside the saga type so Wolverine can distinguish
/// "create + persist" from "load existing and handle".
///
/// <para><b>Idempotency at two layers.</b> First, the deterministic UUID v5 <c>ObligationId</c>:
/// the same <c>ListingId</c> always derives the same id, so a duplicate <c>SettlementCompleted</c>
/// consumption resolves to the same saga document key. Second, the existing-saga check below:
/// when a saga is already at this id (re-delivery of <c>SettlementCompleted</c>), the handler
/// returns <c>null</c> — Wolverine skips saga creation and no second obligation, stream, or set
/// of timers is created. This satisfies the spec's "Idempotent start on duplicate settlement
/// completion" scenario.</para>
///
/// <para><b>Ship-by deadline.</b> Computed as start time + the configured ship-by window from
/// <see cref="ObligationsOptions"/> (<see cref="ObligationsOptions.Active"/> selects production
/// or demo durations). The deadline is carried on saga state and on
/// <see cref="PostSaleCoordinationStarted"/> rather than in a separate event (design.md).</para>
///
/// <para><b>Timer chain (M6-S3).</b> After opening the stream, the handler schedules — via
/// <c>bus.ScheduleAsync()</c> — a <c>SendShippingReminder</c> at the reminder offset and a
/// <c>SendDeadlineEscalation</c> at the ship-by deadline. Both scheduled instants are persisted on
/// saga state so the saga can cancel them when tracking is provided (Wolverine 5.x cancellation is
/// keyed on the scheduled instant — see <see cref="PostSaleCoordinationSaga.CancelScheduledAsync"/>).
/// <c>bus.ScheduleAsync()</c> is the only justified <c>IMessageBus</c> use in a handler per the
/// project conventions.</para>
/// </summary>
[StickyHandler("obligations-settlement-events")]
public static class SettlementCompletedHandler
{
    public static async Task<(PostSaleCoordinationSaga?, OutgoingMessages)> Handle(
        SettlementCompleted message,
        IDocumentSession session,
        IMessageBus bus,
        IOptions<ObligationsOptions> options,
        CancellationToken cancellationToken)
    {
        var obligationId = ObligationsIdentityNamespaces.ObligationId(message.ListingId);

        // Idempotent re-delivery guard: if a saga already exists at this id, the inbound
        // SettlementCompleted has been consumed before. Skip creation and dispatch nothing.
        // The saga element is returned as null inside the tuple so Wolverine skips persistence
        // (a bare null saga return is mishandled — mirrors StartSettlementSagaHandler).
        var existing = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId, cancellationToken);
        if (existing is not null)
        {
            return (null, new OutgoingMessages());
        }

        var startedAt = DateTimeOffset.UtcNow;
        var durations = options.Value.Active;
        var shipByDeadline = startedAt + durations.ShipByDeadline;
        var reminderAt = startedAt + durations.ReminderOffset;

        var saga = new PostSaleCoordinationSaga
        {
            Id = obligationId,
            ListingId = message.ListingId,
            WinnerId = message.WinnerId,
            SellerId = message.SellerId,
            HammerPrice = message.HammerPrice,
            ShipByDeadline = shipByDeadline,
            Status = ObligationStatus.AwaitingShipment,
            ReminderScheduledAt = reminderAt,
            EscalationScheduledAt = shipByDeadline,
        };

        // Open the obligation's event stream at the deterministic ObligationId. StartStream<T>
        // is required by opts.Events.UseMandatoryStreamTypeDeclaration = true; the marker type
        // ObligationEventStream exists for this purpose.
        session.Events.StartStream<ObligationEventStream>(
            obligationId,
            new PostSaleCoordinationStarted(
                obligationId,
                message.ListingId,
                message.WinnerId,
                message.SellerId,
                message.HammerPrice,
                shipByDeadline,
                startedAt));

        // Schedule the cancellable reminder + escalation timers. The instants are persisted on saga
        // state above so the ProvideTracking handler can cancel them. bus.ScheduleAsync is the only
        // justified IMessageBus use in a handler (CLAUDE.md); cancellation is via IMessageStore.
        await bus.ScheduleAsync(new SendShippingReminder(obligationId), reminderAt);
        await bus.ScheduleAsync(new SendDeadlineEscalation(obligationId), shipByDeadline);

        return (saga, new OutgoingMessages());
    }
}
