using CritterBids.Auctions.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Auctions.Tests;

[Collection(AuctionsTestCollection.Name)]
public class AuctionsModuleTests
{
    private readonly AuctionsTestFixture _fixture;

    public AuctionsModuleTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AuctionsModule_BootsClean()
    {
        // Verify the test host started without throwing — AlbaHost construction succeeded.
        // This proves AddAuctionsModule() registered cleanly alongside the primary Marten
        // store and that Program.cs's Wolverine assembly discovery for the Auctions assembly
        // did not surface a code-gen failure.
        _fixture.Host.ShouldNotBeNull();

        // Verify the primary IDocumentStore is resolvable from the DI container.
        // Per ADR 009/011, all Marten BCs share a single IDocumentStore registered in
        // Program.cs — Auctions contributes its types to it via services.ConfigureMarten().
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        store.ShouldNotBeNull();
    }
}
