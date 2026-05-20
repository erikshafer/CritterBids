using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Http;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// End-to-end dispatch smoke test for <see cref="CreateSession"/>. Verifies the command
/// is routable through Wolverine's standard handler-discovery path (M4-S5 acceptance
/// criterion "one dispatch test per new command"). The seven scenario tests in
/// <see cref="SessionAggregateTests"/> exercise the aggregate's state transitions via
/// direct handler invocation; this test covers the bus-routing path in isolation.
///
/// <para>Mirrors the <see cref="RegisterProxyBidDispatchTests"/> shape (M4-S3).</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class CreateSessionDispatchTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public CreateSessionDispatchTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateSession_DispatchedViaBus_AppendsSessionCreatedToNewStream()
    {
        // Dispatch via IMessageBus.InvokeAsync<TResponse> to capture the
        // CreationResponse<Guid> return value — UseFastEventForwarding's forwarded
        // SessionCreated does not land in tracked.Sent/NoRoutes synchronously within
        // InvokeMessageAndWaitAsync, so the tracked-bucket assertion shape used by
        // RegisterProxyBidDispatchTests (which inspects ProxyBidRegistered emitted
        // directly via OutgoingMessages from the start handler) doesn't apply to
        // aggregate-creation handlers that return IStartStream. The response value is
        // the load-bearing assertion target.
        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var response = await bus.InvokeAsync<CreationResponse<Guid>>(new CreateSession(
            Title: "Nebraska.Code() Live Auction",
            DurationMinutes: 5));

        response.ShouldNotBeNull();
        response.Url.ShouldStartWith("/api/sessions/");

        // Verify the Session stream was created and the aggregate reads back correctly —
        // the OQ8 codegen-success surface (StartStream<Session> via MartenOps wired
        // through the Wolverine pipeline against the new sealed-record aggregate).
        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Session>(response.Value);
        aggregate.ShouldNotBeNull();
        aggregate!.Id.ShouldBe(response.Value);
        aggregate.Title.ShouldBe("Nebraska.Code() Live Auction");
        aggregate.DurationMinutes.ShouldBe(5);
        aggregate.StartedAt.ShouldBeNull();
    }
}
