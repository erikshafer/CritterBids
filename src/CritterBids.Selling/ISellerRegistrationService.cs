namespace CritterBids.Selling;

/// <summary>
/// Module seam exposing seller registration state from the Selling BC to the API layer.
/// Registered in <see cref="SellingModule.AddSellingModule"/> as a transient service.
/// </summary>
/// <remarks>
/// Used by the <c>POST /api/listings/draft</c> endpoint (S4) to gate access for unregistered
/// sellers. The API layer never queries the Selling BC's Marten store directly — all access
/// goes through this interface. This is the canonical pattern for API-layer cross-BC state
/// checks in CritterBids (see M2 milestone §6).
/// </remarks>
public interface ISellerRegistrationService
{
    /// <summary>
    /// Returns <c>true</c> if the seller identified by <paramref name="sellerId"/> has a
    /// <see cref="RegisteredSeller"/> document in the Selling BC's store; otherwise <c>false</c>.
    /// </summary>
    Task<bool> IsRegisteredAsync(Guid sellerId, CancellationToken ct = default);
}
