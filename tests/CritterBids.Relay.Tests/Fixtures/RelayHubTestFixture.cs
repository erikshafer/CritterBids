using CritterBids.Relay;
using CritterBids.Relay.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace CritterBids.Relay.Tests.Fixtures;

/// <summary>
/// Real-Kestrel fixture for the SignalR push integration tests. SignalR cannot be hosted by
/// <c>WebApplicationFactory</c> / Alba's in-memory <c>TestServer</c> — the WebSocket transport needs
/// a real socket (see <c>docs/skills/wolverine-signalr.md</c> §9). This fixture stands up a minimal
/// real-Kestrel host on an ephemeral localhost port containing only what a Relay push needs:
/// <list type="bullet">
///   <item>Relay's SignalR services + the two mapped hubs;</item>
///   <item>Wolverine with Relay's handlers discovered, all external transports disabled, solo mode
///         (no Marten, so messages route through in-memory local queues only);</item>
/// </list>
/// Tests connect a real <see cref="Microsoft.AspNetCore.SignalR.Client.HubConnection"/>, join a
/// group via an awaited hub invocation (race-free enrolment), publish the source integration event
/// through <see cref="IMessageBus"/>, and await the resulting push on a
/// <see cref="TaskCompletionSource"/>. The only wall-clock wait is a failsafe timeout, so the tests
/// stay deterministic.
/// </summary>
public class RelayHubTestFixture : IAsyncLifetime
{
    private WebApplication _app = null!;

    public string BaseUrl { get; private set; } = null!;

    public IMessageBus Bus => _app.Services.GetRequiredService<IMessageBus>();

    public string BiddingHubUrl => $"{BaseUrl}/hub/bidding";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();

        // Ephemeral port on loopback — discovered from app.Urls after start.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRelayModule();

        builder.Host.UseWolverine(opts =>
        {
            // This host transitively references every BC (through CritterBids.Api), so Wolverine's
            // conventional discovery scans all of them and their handlers would co-consume the events
            // these tests publish — e.g. Auctions' BidPlaced saga handler faults with
            // UnknownSagaException and the message never reaches Relay's push. Exclude every other BC
            // (reusing the proven discovery-exclusion extensions) so this is a true Relay-only host.
            opts.Discovery.IncludeAssembly(typeof(BiddingHub).Assembly);

            opts.Services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new AuctionsBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new ObligationsBcDiscoveryExclusion());
            opts.Services.AddSingleton<IWolverineExtension>(new ParticipantsBcDiscoveryExclusion());

            opts.Services.DisableAllExternalWolverineTransports();
            opts.Services.RunWolverineInSoloMode();
        });

        _app = builder.Build();

        _app.MapHub<BiddingHub>("/hub/bidding").DisableAntiforgery();
        _app.MapHub<OperationsHub>("/hub/operations").DisableAntiforgery();

        await _app.StartAsync();

        BaseUrl = _app.Urls.Single();
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }
}
