using Marten;

namespace CritterBids.Selling;

/// <summary>
/// DI key for the Selling BC's named Marten store.
/// All components that need a Selling BC session resolve this type, not IDocumentStore directly.
/// Registered via AddMartenStore&lt;ISellingDocumentStore&gt;() in AddSellingModule().
/// </summary>
public interface ISellingDocumentStore : IDocumentStore { }
