namespace CritterBids.Participants.Features.RegisterAsSeller;

/// <summary>
/// Domain event appended to the Participant stream when the participant successfully registers
/// as a seller. Distinct from <see cref="CritterBids.Contracts.SellerRegistrationCompleted"/>
/// (the integration event) to maintain BC boundary separation: domain events belong to the BC,
/// integration events belong to CritterBids.Contracts.
/// </summary>
public sealed record SellerRegistered(Guid ParticipantId, DateTimeOffset CompletedAt);
