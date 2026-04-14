namespace CritterBids.Selling.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture that ensures all Selling integration tests share a single
/// AlbaHost instance (and therefore a single Testcontainers PostgreSQL instance) and
/// execute sequentially. Sequential execution prevents DDL concurrency errors when multiple
/// test classes start schema creation simultaneously.
/// </summary>
[CollectionDefinition(Name)]
public class SellingTestCollection : ICollectionFixture<SellingTestFixture>
{
    public const string Name = "Selling Integration Tests";
}
