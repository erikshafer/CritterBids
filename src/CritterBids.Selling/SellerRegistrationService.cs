using Marten;

namespace CritterBids.Selling;

/// <summary>
/// Concrete implementation of <see cref="ISellerRegistrationService"/>.
/// Opens a read-only <see cref="IQuerySession"/> per call from the Selling BC's named store.
/// </summary>
/// <remarks>
/// DI lifetime: Transient — the service holds no shared state. <see cref="ISellingDocumentStore"/>
/// is a singleton and is safe to inject into transient services. A new <see cref="IQuerySession"/>
/// is created and disposed on each call to <see cref="IsRegisteredAsync"/>.
///
/// <see cref="IDocumentStore"/> (the default store) is intentionally not registered in this
/// process (see ADR 0002). <see cref="ISellingDocumentStore"/> must be used directly.
/// </remarks>
public sealed class SellerRegistrationService : ISellerRegistrationService
{
    private readonly ISellingDocumentStore _store;

    public SellerRegistrationService(ISellingDocumentStore store)
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
