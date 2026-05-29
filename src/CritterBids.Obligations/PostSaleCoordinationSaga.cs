namespace CritterBids.Obligations;

/// <summary>
/// Obligations BC's post-sale coordination workflow. Wolverine Saga per ADR-022 (confirming the
/// ADR-019 Settlement precedent). State is a single mutable document persisted via Marten under
/// the deterministic UUID v5 <c>ObligationId</c> (per
/// <see cref="ObligationsIdentityNamespaces.ObligationId"/>) — one obligation per sold listing.
///
/// <para><b>Lifecycle.</b> The saga starts via <see cref="SettlementCompletedHandler"/> on
/// inbound <c>SettlementCompleted</c> (M6-S2), entering <see cref="ObligationStatus.AwaitingShipment"/>
/// with a computed <see cref="ShipByDeadline"/>. The cancellable reminder/escalation chain
/// (<c>bus.ScheduleAsync()</c>), seller tracking intake, clock-triggered delivery auto-confirmation,
/// and the dispute sub-workflow land in M6-S3/S4. This M6-S2 scaffold defines the saga state and
/// its start; the timer-driven and command-driven transitions are added by the later slices.</para>
///
/// <para><b>Numeric revisions</b> provide optimistic concurrency for saga writes, mirroring
/// <c>SettlementSaga</c> / <c>AuctionClosingSaga</c>. <see cref="Id"/> binds the saga document's
/// primary key to the deterministic <c>ObligationId</c> computed at saga-start time.</para>
/// </summary>
public sealed class PostSaleCoordinationSaga : Wolverine.Saga
{
    /// <summary>Deterministic <c>ObligationId</c> (UUID v5 from <c>ListingId</c>); the saga's stream + document key.</summary>
    public Guid Id { get; set; }

    /// <summary>The sold listing this obligation coordinates.</summary>
    public Guid ListingId { get; set; }

    /// <summary>The winning bidder owed delivery.</summary>
    public Guid WinnerId { get; set; }

    /// <summary>The seller responsible for shipping.</summary>
    public Guid SellerId { get; set; }

    /// <summary>The final sale price carried from <c>SettlementCompleted</c>.</summary>
    public decimal HammerPrice { get; set; }

    /// <summary>The seller's ship-by deadline (start time + the configured ship-by window).</summary>
    public DateTimeOffset ShipByDeadline { get; set; }

    /// <summary>Current lifecycle state; <see cref="ObligationStatus.AwaitingShipment"/> at start.</summary>
    public ObligationStatus Status { get; set; }
}
