using CritterBids.Settlement.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Settlement.Tests;

[Collection(SettlementTestCollection.Name)]
public class SettlementModuleTests
{
    private readonly SettlementTestFixture _fixture;

    public SettlementModuleTests(SettlementTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SettlementModule_BootsClean()
    {
        // Verify the test host started without throwing — AlbaHost construction succeeded.
        // This proves AddSettlementModule() registered cleanly alongside the primary Marten
        // store and that Program.cs's Wolverine assembly discovery for the Settlement assembly
        // did not surface a code-gen failure. It also indirectly verifies the three cross-BC
        // discovery exclusions (Selling / Auctions / Listings) correctly suppress foreign-BC
        // handlers without breaking host startup.
        _fixture.Host.ShouldNotBeNull();

        // Verify the primary IDocumentStore is resolvable from the DI container.
        // Per ADR 009/011, all Marten BCs share a single IDocumentStore registered in
        // Program.cs — Settlement contributes its types to it via services.ConfigureMarten().
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        store.ShouldNotBeNull();
    }
}
