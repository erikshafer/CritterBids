namespace CritterBids.Operations.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture that ensures all Operations integration tests share a single
/// AlbaHost instance (and therefore a single Testcontainers PostgreSQL instance) and
/// execute sequentially. Sequential execution prevents DDL concurrency errors when multiple
/// test classes start schema creation simultaneously.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class OperationsTestCollection : ICollectionFixture<OperationsTestFixture>
{
    public const string Name = "Operations Integration Tests";
}
