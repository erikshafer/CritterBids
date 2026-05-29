namespace CritterBids.Obligations;

/// <summary>
/// Staff-facing todo-list read model (opsx 8.2): one row per obligation that has shipped but not
/// yet had delivery confirmed — the "awaiting delivery" work queue. A row appears when the seller
/// provides tracking (<see cref="CritterBids.Contracts.Obligations.TrackingInfoProvided"/>) and
/// self-removes when delivery auto-confirms (<see cref="DeliveryConfirmed"/>), so the view holds
/// exactly the in-flight shipments at any moment.
///
/// <para>Built by <see cref="ObligationsAwaitingDeliveryProjection"/> as an Inline single-stream
/// projection keyed on <c>ObligationId</c>. Distinct from <see cref="ObligationStatusView"/> (which
/// is the per-obligation lifecycle record that persists through every state) — this view exists
/// only for the awaiting-delivery window.</para>
///
/// Field rationale:
/// - <c>Id</c> — the deterministic UUID v5 <c>ObligationId</c> (the stream + document key).
/// - <c>ListingId</c>, <c>SellerId</c> — routing identities carried from the tracking event.
/// - <c>TrackingNumber</c> — the carrier tracking string the seller entered.
/// - <c>TrackingProvidedAt</c> — when tracking was recorded (the row's age in the queue).
/// </summary>
public sealed record ObligationsAwaitingDelivery
{
    public Guid Id { get; init; }
    public Guid ListingId { get; init; }
    public Guid SellerId { get; init; }
    public string TrackingNumber { get; init; } = string.Empty;
    public DateTimeOffset TrackingProvidedAt { get; init; }
}
