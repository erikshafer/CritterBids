namespace CritterBids.Contracts;

/// <summary>
/// Integration event published when a participant successfully registers as a seller.
/// Consumed by the Selling BC in M2 to build the RegisteredSellers projection.
/// Published via Wolverine OutgoingMessages (transactional outbox) — never via IMessageBus directly.
/// </summary>
public sealed record SellerRegistrationCompleted(Guid ParticipantId, DateTimeOffset CompletedAt);
