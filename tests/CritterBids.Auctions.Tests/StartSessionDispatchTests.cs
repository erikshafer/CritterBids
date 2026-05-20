using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// End-to-end dispatch smoke test for <see cref="StartSession"/>. Verifies the command is
/// routable through Wolverine's standard handler-discovery path AND that
/// <c>[WriteAggregate]</c> codegen resolves cleanly against the new
/// <see cref="Session"/> aggregate (M4-S5 OQ8 first-use surface). Mirrors the
/// <see cref="RegisterProxyBidDispatchTests"/> shape.
///
/// <para>Dispatching <see cref="StartSession"/> via the bus causes the appended
/// <see cref="SessionStarted"/> to be forwarded by UseFastEventForwarding to the in-BC
/// <see cref="SessionStartedHandler"/> — which then appends <see cref="BiddingOpened"/>
/// to each attached listing's stream. The test does NOT assert on the downstream
/// BiddingOpened cascade; the SessionStartedFanOutTests cover that surface
/// independently. This test asserts only that the dispatch + aggregate state
/// transition completed without exception.</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class StartSessionDispatchTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public StartSessionDispatchTests(AuctionsTestFixture fixture)
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
    public async Task StartSession_DispatchedViaBus_MarksSessionStarted()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(
            title: "Dispatch Smoke Test",
            attachedListingIds: new[] { listingId });

        // PublishedListings row needed by SessionStartedHandler when it cascades the
        // BiddingOpened append for the attached listing. Without it the fan-out would
        // silently skip the listing (data-availability constraint per the handler's
        // docstring); not fatal for this test but matches production wiring.
        await _fixture.SeedPublishedListingAsync(listingId, sellerId);

        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new StartSession(sessionId));

        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Session>(sessionId);
        aggregate.ShouldNotBeNull();
        aggregate!.StartedAt.ShouldNotBeNull();
        aggregate.AttachedListingIds.ShouldHaveSingleItem().ShouldBe(listingId);
    }
}
