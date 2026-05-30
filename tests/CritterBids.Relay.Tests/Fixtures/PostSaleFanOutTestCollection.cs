namespace CritterBids.Relay.Tests.Fixtures;

/// <summary>
/// xUnit collection for the M6-S7 post-sale fan-out integration test. A single shared
/// <see cref="PostSaleFanOutTestFixture"/> (one composed real-Kestrel + Marten host running both the
/// Obligations and Relay BCs) executed sequentially — the composed host is expensive to stand up and
/// the test owns its own fresh stream/group identities.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class PostSaleFanOutTestCollection : ICollectionFixture<PostSaleFanOutTestFixture>
{
    public const string Name = "Post-Sale Fan-Out Tests";
}
