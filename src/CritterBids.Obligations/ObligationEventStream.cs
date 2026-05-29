namespace CritterBids.Obligations;

/// <summary>
/// Marker class for the per-obligation event stream. Required by
/// <c>opts.Events.UseMandatoryStreamTypeDeclaration = true</c> in <c>Program.cs</c>; its sole
/// purpose is satisfying the mandatory-stream-type-declaration rule at
/// <c>session.Events.StartStream&lt;ObligationEventStream&gt;(obligationId, ...)</c> in
/// <see cref="SettlementCompletedHandler"/>. The obligation's domain events
/// (<see cref="PostSaleCoordinationStarted"/> and the M6-S3/S4 events) are appended to this
/// stream; the M6-S3/S4 <c>ObligationStatusView</c> projection reads it.
///
/// Mirrors the Settlement BC's <c>FinancialEventStream</c> marker.
/// </summary>
public sealed class ObligationEventStream
{
    public Guid Id { get; set; }
}
