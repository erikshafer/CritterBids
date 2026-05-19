using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// End-to-end dispatch smoke test for <see cref="RegisterProxyBid"/>. Verifies the command
/// is routable through Wolverine's standard handler-discovery path (M4-S3 exit criterion
/// "one dispatch test per new command"), and that the start handler's saga creation +
/// <see cref="ProxyBidRegistered"/> emission both occur.
///
/// <para>The four scenario tests in <see cref="ProxyBidManagerSagaTests"/> already exercise
/// the saga's internal state through the same dispatch shape; this test covers the
/// remaining half of the same exit criterion by asserting the end-to-end-routable shape
/// in isolation. Mirrors the <see cref="PlaceBidDispatchTests"/> shape (M3-S4): the host's
/// <c>InvokeMessageAndWaitAsync</c> resolves <c>IMessageBus</c> from a per-call scope under
/// the covers, so this is genuinely an <c>IMessageBus</c> dispatch path.</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class RegisterProxyBidDispatchTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public RegisterProxyBidDispatchTests(AuctionsTestFixture fixture)
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
    public async Task RegisterProxyBid_DispatchedViaBus_StartsSagaAndEmitsRegistered()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var expectedSagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new RegisterProxyBid(
                ListingId: listingId,
                BidderId: bidderId,
                MaxAmount: 120m));

        var saga = await _fixture.LoadSaga<ProxyBidManagerSaga>(expectedSagaId);
        saga.ShouldNotBeNull();
        saga!.Id.ShouldBe(expectedSagaId);
        saga.ListingId.ShouldBe(listingId);
        saga.BidderId.ShouldBe(bidderId);
        saga.MaxAmount.ShouldBe(120m);
        saga.Status.ShouldBe(ProxyBidManagerStatus.Active);

        var registered = tracked.NoRoutes.MessagesOf<ProxyBidRegistered>().ShouldHaveSingleItem();
        registered.ListingId.ShouldBe(listingId);
        registered.BidderId.ShouldBe(bidderId);
        registered.MaxAmount.ShouldBe(120m);
    }
}
