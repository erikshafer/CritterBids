namespace CritterBids.Obligations;

/// <summary>
/// Lifecycle state of a single post-sale obligation, held on
/// <see cref="PostSaleCoordinationSaga.Status"/> and surfaced by the obligation read models.
///
/// <para>M6-S2 only sets <see cref="AwaitingShipment"/> at saga start. The remaining states
/// are driven by the M6-S3 (tracking / auto-confirm) and M6-S4 (escalation / dispute) slices.</para>
/// </summary>
public enum ObligationStatus
{
    /// <summary>Saga started on <c>SettlementCompleted</c>; awaiting seller tracking before the ship-by deadline.</summary>
    AwaitingShipment,

    /// <summary>Tracking provided; awaiting clock-triggered delivery auto-confirmation (M6-S3).</summary>
    Shipped,

    /// <summary>Ship-by deadline passed with no tracking; non-terminal — late tracking still recovers (M6-S4).</summary>
    Escalated,

    /// <summary>Delivery auto-confirmed; obligation fulfilled and saga completed (M6-S3).</summary>
    Fulfilled,

    /// <summary>A dispute is open against the obligation; awaiting resolution (M6-S4).</summary>
    Disputed,
}
