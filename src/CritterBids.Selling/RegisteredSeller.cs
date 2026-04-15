namespace CritterBids.Selling;

/// <summary>
/// Marten document representing a seller who has completed registration in the Participants BC.
/// Populated by <see cref="SellerRegistrationCompletedHandler"/> when a
/// <c>SellerRegistrationCompleted</c> integration event arrives via RabbitMQ.
/// </summary>
/// <remarks>
/// This is a projection document, not an event-sourced aggregate — no Apply() methods, no event
/// stream. Only <c>Id</c> (the seller's ParticipantId) is stored because
/// <see cref="ISellerRegistrationService.IsRegisteredAsync"/> only checks existence by ID;
/// it does not query name, email, or registration timestamp. If a downstream consumer requires
/// those fields, they should be added to this document at that time (S4+).
/// </remarks>
public sealed record RegisteredSeller
{
    /// <summary>
    /// Document ID — set from <c>SellerRegistrationCompleted.ParticipantId</c>.
    /// Acts as both the Marten document identity and the seller's BC-level identifier.
    /// </summary>
    public Guid Id { get; set; }
}
