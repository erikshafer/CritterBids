namespace CritterBids.Obligations;

/// <summary>
/// Read model surfacing a single obligation's post-sale coordination lifecycle, built by
/// <see cref="ObligationStatusViewProjection"/> as an Inline single-stream projection over the
/// obligation's event stream (keyed by <c>ObligationId</c>). Backs the seller/winner-facing
/// obligation status surface (narrative 006); the operations escalation/dispute boards are
/// separate projections landing in M6-S4.
///
/// Field rationale:
/// - <c>Id</c> — the deterministic UUID v5 <c>ObligationId</c> (the stream + document key).
/// - <c>ListingId</c>, <c>WinnerId</c>, <c>SellerId</c> — routing identities carried from start.
/// - <c>HammerPrice</c> — the final sale price, carried for display without a cross-BC read.
/// - <c>Status</c> — current lifecycle state (<see cref="ObligationStatus"/>).
/// - <c>ShipByDeadline</c> — the seller's ship-by deadline.
/// - <c>TrackingNumber</c> — the carrier tracking string once provided; null before.
/// - <c>ReminderSentAt</c>, <c>TrackingProvidedAt</c>, <c>FulfilledAt</c> — lifecycle timestamps;
///   null until the corresponding transition occurs.
/// </summary>
public sealed record ObligationStatusView
{
    public Guid Id { get; init; }
    public Guid ListingId { get; init; }
    public Guid WinnerId { get; init; }
    public Guid SellerId { get; init; }
    public decimal HammerPrice { get; init; }
    public ObligationStatus Status { get; init; }
    public DateTimeOffset ShipByDeadline { get; init; }
    public string? TrackingNumber { get; init; }
    public DateTimeOffset? ReminderSentAt { get; init; }
    public DateTimeOffset? TrackingProvidedAt { get; init; }
    public DateTimeOffset? FulfilledAt { get; init; }
}
