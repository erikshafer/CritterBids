namespace CritterBids.Obligations;

/// <summary>
/// Obligations-internal domain event recording that the single shipping reminder fired for an
/// obligation still awaiting tracking. Appended to the obligation's event stream by
/// <see cref="PostSaleCoordinationSaga"/>'s <c>SendShippingReminder</c> handler.
///
/// <para>BC-internal event, not an integration contract — it is not published on the bus and
/// carries no "Event" suffix per the global naming rule. The reminder is a nudge to the seller;
/// the <c>ObligationStatusView</c> projection surfaces its timestamp.</para>
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier (the stream key).
/// - <c>SellerId</c> — the participant nudged to ship; carried so a downstream notifier need not
///   re-read saga state.
/// - <c>SentAt</c> — handler-stamped timestamp the reminder fired.
/// </summary>
public sealed record ShippingReminderSent(
    Guid ObligationId,
    Guid SellerId,
    DateTimeOffset SentAt);
