namespace CritterBids.Obligations;

/// <summary>
/// Obligations-internal domain event marking the start of the post-sale coordination lifecycle
/// for a sold listing. Appended to the obligation's event stream (keyed by <c>ObligationId</c>)
/// by <see cref="SettlementCompletedHandler"/> when the saga starts on <c>SettlementCompleted</c>.
///
/// <para>This is a BC-internal event, not an integration contract — it is not published on the
/// bus and carries no "Event" suffix per the global naming rule. The ship-by deadline lives on
/// the event (and on saga state) rather than in a separate event whose only job is to record a
/// timestamp the start already knows (design.md "Ship-by deadline carried as saga state").</para>
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier (one per listing).
/// - <c>ListingId</c>, <c>WinnerId</c>, <c>SellerId</c> — routing identities carried verbatim
///   from the triggering <c>SettlementCompleted</c> for reminders, escalation, and read models.
/// - <c>HammerPrice</c> — the final sale price, carried for downstream read models so they do
///   not re-derive it.
/// - <c>ShipByDeadline</c> — the computed deadline (start time + the configured ship-by window
///   from <see cref="ObligationsOptions"/>); reminders and escalation compute off it.
/// - <c>StartedAt</c> — handler-stamped saga-start timestamp.
/// </summary>
public sealed record PostSaleCoordinationStarted(
    Guid ObligationId,
    Guid ListingId,
    Guid WinnerId,
    Guid SellerId,
    decimal HammerPrice,
    DateTimeOffset ShipByDeadline,
    DateTimeOffset StartedAt);
