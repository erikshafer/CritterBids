using CritterBids.Selling.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Selling.Tests;

[Collection(SellingTestCollection.Name)]
public class SellingModuleTests
{
    private readonly SellingTestFixture _fixture;

    public SellingModuleTests(SellingTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SellingModule_BootsClean()
    {
        // Verify the test host started without throwing — AlbaHost construction succeeded.
        _fixture.Host.ShouldNotBeNull();

        // Verify the primary IDocumentStore is resolvable from the DI container.
        // With ADR 0003, all Marten BCs share a single IDocumentStore registered in Program.cs.
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        store.ShouldNotBeNull();
    }
}
