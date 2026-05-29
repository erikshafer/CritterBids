namespace CritterBids.Obligations;

/// <summary>
/// Obligations-internal domain event recording that a dispute's <c>Extension</c> resolution
/// recomputed and rescheduled the obligation's ship-by deadline. Appended to the obligation's
/// event stream by <see cref="PostSaleCoordinationSaga"/>'s <c>ResolveDispute</c> handler on the
/// one non-terminal resolution path (W005 slice 5.8 / narrative 008 — the operator grants an
/// extension instead of refunding or closing).
///
/// <para><b>Why this internal event exists.</b> The frozen <c>DisputeResolved(Extension)</c>
/// integration contract carries only the <c>ResolutionType</c> string — not the new deadline. The
/// <see cref="ObligationStatusView"/> projection is rebuilt purely from the stream, so without a
/// stream event carrying the recomputed <c>NewShipByDeadline</c> the view could not replay the
/// post-extension deadline. This BC-internal event closes that gap, keeping the projection
/// rebuild-correct. It is not published on the bus and carries no "Event" suffix per the global
/// naming rule.</para>
///
/// Field rationale:
/// - <c>ObligationId</c> — the deterministic UUID v5 obligation identifier (the stream key).
/// - <c>NewShipByDeadline</c> — the freshly recomputed ship-by deadline the saga rescheduled to.
/// - <c>ExtendedAt</c> — handler-stamped timestamp when the extension was granted.
/// </summary>
public sealed record ShipByDeadlineExtended(
    Guid ObligationId,
    DateTimeOffset NewShipByDeadline,
    DateTimeOffset ExtendedAt);
