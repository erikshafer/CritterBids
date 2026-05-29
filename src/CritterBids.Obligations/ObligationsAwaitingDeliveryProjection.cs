using CritterBids.Contracts.Obligations;
using Marten.Events.Aggregation;

namespace CritterBids.Obligations;

/// <summary>
/// Inline single-stream projection building the <see cref="ObligationsAwaitingDelivery"/> todo-list
/// view (opsx 8.2). A row is created when the seller provides tracking
/// (<see cref="TrackingInfoProvided"/>) and deleted when delivery auto-confirms
/// (<see cref="DeliveryConfirmed"/>) via the <c>ShouldDelete</c> convention — so the view contains
/// exactly the obligations currently in flight between shipment and delivery confirmation.
///
/// <para>Inline (strongly consistent) so the queue is queryable immediately after the appending
/// transaction commits, matching <see cref="ObligationStatusViewProjection"/>. The obligation's
/// earlier <c>PostSaleCoordinationStarted</c> / <c>ShippingReminderSent</c> events have no
/// Create/Apply here and are skipped — the row only exists for the awaiting-delivery window.</para>
/// </summary>
public sealed class ObligationsAwaitingDeliveryProjection
    : SingleStreamProjection<ObligationsAwaitingDelivery, Guid>
{
    public static ObligationsAwaitingDelivery Create(TrackingInfoProvided tracking) =>
        new()
        {
            Id = tracking.ObligationId,
            ListingId = tracking.ListingId,
            SellerId = tracking.SellerId,
            TrackingNumber = tracking.TrackingNumber,
            TrackingProvidedAt = tracking.ProvidedAt,
        };

    public static bool ShouldDelete(DeliveryConfirmed confirmed) => true;
}
