namespace CritterBids.Obligations.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture for the demo-mode <see cref="ObligationsLifecycleTestFixture"/>. Kept
/// separate from <see cref="ObligationsTestCollection"/> so the lifecycle (demo-duration) tests
/// run against their own AlbaHost / PostgreSQL instance and do not share state or duration config
/// with the production-duration saga-start tests. Sequential execution prevents DDL concurrency
/// errors during schema creation.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ObligationsLifecycleTestCollection : ICollectionFixture<ObligationsLifecycleTestFixture>
{
    public const string Name = "Obligations Lifecycle Integration Tests";
}
