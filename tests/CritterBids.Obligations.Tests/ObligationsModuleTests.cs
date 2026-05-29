using CritterBids.Obligations.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CritterBids.Obligations.Tests;

[Collection(ObligationsTestCollection.Name)]
public class ObligationsModuleTests
{
    private readonly ObligationsTestFixture _fixture;

    public ObligationsModuleTests(ObligationsTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void ObligationsModule_BootsClean()
    {
        // Verify the test host started without throwing — AlbaHost construction succeeded.
        // This proves AddObligationsModule() registered cleanly alongside the primary Marten
        // store and that Program.cs's Wolverine assembly discovery for the Obligations assembly
        // (which includes the SettlementCompletedHandler saga-start) did not surface a code-gen
        // failure. It also indirectly verifies the four cross-BC discovery exclusions
        // (Selling / Auctions / Listings / Settlement) correctly suppress foreign-BC handlers
        // without breaking host startup.
        _fixture.Host.ShouldNotBeNull();

        // Verify the primary IDocumentStore is resolvable from the DI container.
        // Per ADR 009/011, all Marten BCs share a single IDocumentStore registered in
        // Program.cs — Obligations contributes its types to it via services.ConfigureMarten().
        var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
        store.ShouldNotBeNull();
    }

    [Fact]
    public void ObligationsOptions_BindsWithDefaults()
    {
        // ObligationsOptions binds from the (absent) "Obligations" config section, falling back
        // to the record's initializer defaults. Production durations are the active set when
        // DemoMode is false (the default), so the ship-by deadline is the 5-day production window.
        var options = _fixture.Host.Services.GetRequiredService<IOptions<ObligationsOptions>>().Value;

        options.DemoMode.ShouldBeFalse();
        options.Active.ShipByDeadline.ShouldBe(options.Production.ShipByDeadline);
        options.Production.ShipByDeadline.ShouldBe(TimeSpan.FromDays(5));
    }
}
