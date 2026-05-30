namespace CritterBids.Relay.Tests.Fixtures;

/// <summary>
/// xUnit collection for the Relay boots-clean tests. A single shared <see cref="RelayTestFixture"/>
/// (one AlbaHost + one Testcontainers PostgreSQL) executed sequentially to avoid DDL concurrency.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class RelayTestCollection : ICollectionFixture<RelayTestFixture>
{
    public const string Name = "Relay Integration Tests";
}
