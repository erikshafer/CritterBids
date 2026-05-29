namespace CritterBids.Obligations.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture that ensures all Obligations integration tests share a single
/// AlbaHost instance (and therefore a single Testcontainers PostgreSQL instance) and
/// execute sequentially. Sequential execution prevents DDL concurrency errors when multiple
/// test classes start schema creation simultaneously.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ObligationsTestCollection : ICollectionFixture<ObligationsTestFixture>
{
    public const string Name = "Obligations Integration Tests";
}
