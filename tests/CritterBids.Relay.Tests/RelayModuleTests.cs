using CritterBids.Relay.Hubs;
using CritterBids.Relay.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Tests;

[Collection(RelayTestCollection.Name)]
public class RelayModuleTests
{
    private readonly RelayTestFixture _fixture;

    public RelayModuleTests(RelayTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void RelayModule_BootsClean()
    {
        // AlbaHost construction succeeding proves the full Program host wired up with Relay active:
        // AddRelayModule() registered AddSignalR(), the unconditional app.MapHub<BiddingHub>() /
        // app.MapHub<OperationsHub>() calls resolved their services, and Wolverine code-genned
        // Relay's three notification handlers without error. It also indirectly verifies the five
        // cross-BC discovery exclusions suppress foreign-BC handlers without breaking startup.
        _fixture.Host.ShouldNotBeNull();
    }

    [Fact]
    public void BiddingHubContext_IsResolvable()
    {
        // The participant-facing hub context must be resolvable — it is what Relay's handlers inject
        // to push notifications.
        var hub = _fixture.Host.Services.GetService(typeof(IHubContext<BiddingHub>));
        hub.ShouldNotBeNull();
    }

    [Fact]
    public void OperationsHubContext_IsResolvable()
    {
        // OperationsHub is mapped now (host wiring done once) even though its push handlers land in
        // M6-S6 — its context must resolve so the hub route works.
        var hub = _fixture.Host.Services.GetService(typeof(IHubContext<OperationsHub>));
        hub.ShouldNotBeNull();
    }
}
