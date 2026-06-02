using CritterBids.Api.Auth;
using CritterBids.Relay;
using CritterBids.Relay.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// The staff token this fixture configures and the <c>OperationsHub</c> connections present.
    /// M7-S6 (ADR-024) gated <c>OperationsHub</c> with the <c>StaffOnly</c> policy, so the hub now
    /// requires the <c>StaffToken</c> scheme; this fixture wires that exact production scheme and
    /// supplies a known token so the existing push tests still connect.
    /// </summary>
    private const string StaffToken = "relay-hub-test-staff-token";

    public string BaseUrl { get; private set; } = null!;

    public IMessageBus Bus => _app.Services.GetRequiredService<IMessageBus>();

    public string BiddingHubUrl => $"{BaseUrl}/hub/bidding";

    // The OperationsHub is StaffOnly-gated (M7-S6); SignalR transports cannot set a custom header, so
    // the staff credential rides the access_token query string the StaffToken scheme reads for this
    // hub path. Existing push tests connect through this property and so are authenticated unchanged.
    public string OperationsHubUrl => $"{BaseUrl}/hub/operations?{StaffAuthConstants.AccessTokenQueryKey}={StaffToken}";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();

        // Ephemeral port on loopback — discovered from app.Urls after start.
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // M7-S6 (ADR-024): configure the staff token and wire the exact production StaffToken scheme
        // + StaffOnly policy so the now-gated OperationsHub authenticates rather than faulting on a
        // missing authorization middleware.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [StaffAuthConstants.StaffTokenConfigKey] = StaffToken,
        });

        builder.Services.AddRelayModule();
        builder.Services.AddStaffTokenAuthentication();
        builder.Services.AddStaffAuthorizationPolicy();

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

        // M7-S6 (ADR-024): the OperationsHub endpoint carries StaffOnly authorization metadata, so the
        // pipeline must run authentication then authorization before the hub endpoints.
        _app.UseAuthentication();
        _app.UseAuthorization();

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
