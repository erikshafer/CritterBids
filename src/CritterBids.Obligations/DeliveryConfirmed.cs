namespace CritterBids.Obligations;

/// <summary>
/// Obligations-internal domain event recording that delivery auto-confirmed for an obligation —
/// the clock-triggered happy-path terminal transition. Appended to the obligation's event stream
/// by <see cref="PostSaleCoordinationSaga"/>'s <c>ConfirmDelivery</c> handler immediately before
/// the integration event <see cref="CritterBids.Contracts.Obligations.ObligationFulfilled"/> is
/// emitted and the saga calls <c>MarkCompleted()</c>.
///
/// <para>BC-internal event, not an integration contract — it is not published on the bus and
/// carries no "Event" suffix per the global naming rule. It is the stream marker the
/// <c>ObligationStatusView</c> projection applies to reach the <c>Fulfilled</c> state; the
/// external announcement is the <c>ObligationFulfilled</c> integration event.</para>
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier (the stream key).
/// - <c>ConfirmedAt</c> — handler-stamped timestamp delivery auto-confirmed.
/// </summary>
public sealed record DeliveryConfirmed(
    Guid ObligationId,
    DateTimeOffset ConfirmedAt);
