namespace CritterBids.Settlement.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture that ensures all Settlement integration tests share a single
/// AlbaHost instance (and therefore a single Testcontainers PostgreSQL instance) and
/// execute sequentially. Sequential execution prevents DDL concurrency errors when multiple
/// test classes start schema creation simultaneously.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class SettlementTestCollection : ICollectionFixture<SettlementTestFixture>
{
    public const string Name = "Settlement Integration Tests";
}
