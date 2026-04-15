namespace CritterBids.Participants.Tests.Fixtures;

/// <summary>
/// xUnit collection fixture that ensures all Participants integration tests share a single
/// AlbaHost instance (and therefore a single Testcontainers PostgreSQL instance) and
/// execute sequentially. Sequential execution prevents DDL concurrency errors when multiple
/// test classes start schema creation simultaneously.
/// </summary>
[CollectionDefinition(Name)]
public class ParticipantsTestCollection : ICollectionFixture<ParticipantsTestFixture>
{
    public const string Name = "Participants Integration Tests";
}
