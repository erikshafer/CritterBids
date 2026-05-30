namespace CritterBids.Relay.Tests.Fixtures;

/// <summary>
/// xUnit collection for the SignalR hub-push tests. A single shared
/// <see cref="RelayHubTestFixture"/> (one real-Kestrel host) executed sequentially.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class RelayHubTestCollection : ICollectionFixture<RelayHubTestFixture>
{
    public const string Name = "Relay Hub Push Tests";
}
