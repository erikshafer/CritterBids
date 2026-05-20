using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// End-to-end dispatch smoke test for <see cref="AttachListingToSession"/>. Verifies the
/// command is routable through Wolverine's standard handler-discovery path AND that
/// <c>[WriteAggregate]</c> codegen resolves cleanly against the new
/// <see cref="Session"/> aggregate (M4-S5 OQ8 first-use surface — halt-and-consult if
/// codegen fails). Mirrors the <see cref="RegisterProxyBidDispatchTests"/> shape.
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class AttachListingToSessionDispatchTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public AttachListingToSessionDispatchTests(AuctionsTestFixture fixture)
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
    public async Task AttachListingToSession_DispatchedViaBus_AppendsListingAttachedToSession()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(title: "Dispatch Smoke Test");
        await _fixture.SeedPublishedListingAsync(listingId, sellerId);

        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new AttachListingToSession(sessionId, listingId));

        await using var session = _fixture.GetDocumentSession();
        var aggregate = await session.Events.AggregateStreamAsync<Session>(sessionId);
        aggregate.ShouldNotBeNull();
        aggregate!.AttachedListingIds.ShouldHaveSingleItem().ShouldBe(listingId);
    }
}
