using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;
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

        // Verify ISellingDocumentStore is resolvable from the DI container.
        // This confirms the named Marten store was correctly registered and
        // is distinct from the default IDocumentStore (which is intentionally absent).
        var store = _fixture.Host.Services.GetRequiredService<ISellingDocumentStore>();
        store.ShouldNotBeNull();
    }
}
