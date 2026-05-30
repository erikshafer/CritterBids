using CritterBids.Contracts.Obligations;
using Marten.Events.Aggregation;

namespace CritterBids.Obligations;

/// <summary>
/// Inline single-stream projection building <see cref="ObligationStatusView"/> from the
/// obligation's event stream (opsx 8.1). Inline so the view is strongly consistent — immediately
/// queryable after the appending transaction commits, which the M6-S3 happy-path test relies on.
///
/// <para>The view's <c>Fulfilled</c> state is driven by the BC-internal
/// <see cref="DeliveryConfirmed"/> marker (the external announcement is the
/// <see cref="ObligationFulfilled"/> integration event, emitted on the bus rather than appended).
/// Tracking is driven by <see cref="TrackingInfoProvided"/>, which is both appended to the stream
/// and emitted. The escalation/dispute transitions land in M6-S4.</para>
/// </summary>
public sealed partial class ObligationStatusViewProjection : SingleStreamProjection<ObligationStatusView, Guid>
{
    public static ObligationStatusView Create(PostSaleCoordinationStarted started) =>
        new()
        {
            Id = started.ObligationId,
            ListingId = started.ListingId,
            WinnerId = started.WinnerId,
            SellerId = started.SellerId,
            HammerPrice = started.HammerPrice,
            Status = ObligationStatus.AwaitingShipment,
            ShipByDeadline = started.ShipByDeadline,
        };

    public static ObligationStatusView Apply(ShippingReminderSent reminder, ObligationStatusView view) =>
        view with { ReminderSentAt = reminder.SentAt };

    public static ObligationStatusView Apply(TrackingInfoProvided tracking, ObligationStatusView view) =>
        view with
        {
            Status = ObligationStatus.Shipped,
            TrackingNumber = tracking.TrackingNumber,
            TrackingProvidedAt = tracking.ProvidedAt,
        };

    public static ObligationStatusView Apply(DeliveryConfirmed confirmed, ObligationStatusView view) =>
        view with
        {
            Status = ObligationStatus.Fulfilled,
            FulfilledAt = confirmed.ConfirmedAt,
        };

    public static ObligationStatusView Apply(DeadlineEscalated escalated, ObligationStatusView view) =>
        view with
        {
            Status = ObligationStatus.Escalated,
            EscalatedAt = escalated.EscalatedAt,
        };

    public static ObligationStatusView Apply(DisputeOpened opened, ObligationStatusView view) =>
        view with
        {
            Status = ObligationStatus.Disputed,
            DisputeId = opened.DisputeId,
            DisputeReason = opened.Reason,
            DisputeOpenedAt = opened.OpenedAt,
        };

    // Extension recovery returns the obligation to awaiting-shipment; Refund/Closed leave it in the
    // terminal Disputed state with the resolution recorded. The ShipByDeadline for an Extension is
    // replayed from the internal ShipByDeadlineExtended event (the frozen contract cannot carry it).
    public static ObligationStatusView Apply(DisputeResolved resolved, ObligationStatusView view) =>
        view with
        {
            DisputeResolution = resolved.ResolutionType,
            DisputeResolvedAt = resolved.ResolvedAt,
            Status = resolved.ResolutionType == DisputeResolutions.Extension
                ? ObligationStatus.AwaitingShipment
                : view.Status,
        };

    public static ObligationStatusView Apply(ShipByDeadlineExtended extended, ObligationStatusView view) =>
        view with
        {
            Status = ObligationStatus.AwaitingShipment,
            ShipByDeadline = extended.NewShipByDeadline,
        };
}
