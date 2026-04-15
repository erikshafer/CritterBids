using Marten;

namespace CritterBids.Selling;

/// <summary>
/// Concrete implementation of <see cref="ISellerRegistrationService"/>.
/// Opens a read-only <see cref="IQuerySession"/> per call from the primary Marten store.
/// </summary>
/// <remarks>
/// DI lifetime: Transient — the service holds no shared state. <see cref="IDocumentStore"/>
/// is a singleton and safe to inject into transient services. A new <see cref="IQuerySession"/>
/// is created and disposed on each call to <see cref="IsRegisteredAsync"/>.
/// </remarks>
public sealed class SellerRegistrationService : ISellerRegistrationService
{
    private readonly IDocumentStore _store;

    public SellerRegistrationService(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<bool> IsRegisteredAsync(Guid sellerId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        var seller = await session.LoadAsync<RegisteredSeller>(sellerId, ct);
        return seller is not null;
    }
}
