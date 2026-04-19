using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.ScheduledMessageManagement;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Integration tests for the Auction Closing saga's forward path (M3-S5 scope, scenarios
/// 3.1–3.4). Each test dispatches the saga's input integration events through the Wolverine
/// bus and asserts the resulting saga document state + scheduled-message store.
///
/// Production path: DCB handlers append events to the listing's Marten stream, and
/// UseFastEventForwarding=true on IntegrateWithWolverine republishes those events to the
/// Wolverine bus inside the same outbox scope (see AuctionsModule / Program.cs). Tests
/// dispatch directly to the bus — semantically equivalent from the saga's perspective, and
/// decoupled from the session-listener lifecycle that only wires forwarding on the
/// handler-scoped IDocumentSession.
///
/// Correlation: Saga.Id == ListingId via [SagaIdentityFrom(nameof(X.ListingId))] on each
/// handler parameter (M3-S5 OQ1 Path A — zero contract changes).
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class AuctionClosingSagaTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public AuctionClosingSagaTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
        await CancelAllScheduledCloseAuctionsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BiddingOpened_StartsSaga_SchedulesClose()
    {
        var listingId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, closeAt));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.Id.ShouldBe(listingId);
        saga.ListingId.ShouldBe(listingId);
        saga.Status.ShouldBe(AuctionClosingStatus.AwaitingBids);
        saga.BidCount.ShouldBe(0);
        saga.ReserveHasBeenMet.ShouldBeFalse();
        saga.ScheduledCloseAt.ShouldBe(closeAt);
        saga.OriginalCloseAt.ShouldBe(closeAt);

        var allPending = await QueryAllScheduledAsync();
        var pending = await QueryPendingCloseAuctionsAsync();
        if (pending.Count == 0)
            throw new Xunit.Sdk.XunitException($"No CloseAuction found. All scheduled: [{string.Join(", ", allPending.Select(m => $"{m.MessageType}@{m.ScheduledTime:O}"))}]");
        pending.ShouldHaveSingleItem();
        pending[0].ScheduledTime.ShouldNotBeNull();
        pending[0].ScheduledTime!.Value.ShouldBe(closeAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task FirstBid_TransitionsToActive()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, closeAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(new BidPlaced(
            ListingId: listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: bidderId,
            Amount: 30m,
            BidCount: 1,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.Status.ShouldBe(AuctionClosingStatus.Active);
        saga.BidCount.ShouldBe(1);
        saga.CurrentHighBid.ShouldBe(30m);
        saga.CurrentHighBidderId.ShouldBe(bidderId);
    }

    [Fact]
    public async Task ReserveMet_UpdatesSagaState()
    {
        var listingId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, closeAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(new ReserveMet(
            ListingId: listingId,
            Amount: 100m,
            MetAt: DateTimeOffset.UtcNow));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.ReserveHasBeenMet.ShouldBeTrue();
    }

    [Fact]
    public async Task ExtendedBidding_CancelsAndReschedules()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var originalCloseAt = DateTimeOffset.UtcNow.AddHours(1);
        var extendedCloseAt = originalCloseAt.AddMinutes(2);

        await _fixture.Host.InvokeMessageAndWaitAsync(BuildBiddingOpened(listingId, originalCloseAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(new ExtendedBiddingTriggered(
            ListingId: listingId,
            PreviousCloseAt: originalCloseAt,
            NewCloseAt: extendedCloseAt,
            TriggeredByBidderId: bidderId,
            TriggeredAt: DateTimeOffset.UtcNow));

        var saga = await _fixture.LoadSaga<AuctionClosingSaga>(listingId);
        saga.ShouldNotBeNull();
        saga!.Status.ShouldBe(AuctionClosingStatus.Extended);
        saga.ScheduledCloseAt.ShouldBe(extendedCloseAt);

        var pending = await QueryPendingCloseAuctionsAsync();
        pending.ShouldHaveSingleItem();
        pending[0].ScheduledTime!.Value.ShouldBe(extendedCloseAt, TimeSpan.FromSeconds(1));
        pending.ShouldNotContain(m =>
            m.ScheduledTime.HasValue &&
            Math.Abs((m.ScheduledTime.Value - originalCloseAt).TotalMilliseconds) < 100);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static BiddingOpened BuildBiddingOpened(Guid listingId, DateTimeOffset closeAt) =>
        new(
            ListingId: listingId,
            SellerId: Guid.CreateVersion7(),
            StartingBid: 25m,
            ReserveThreshold: null,
            BuyItNowPrice: null,
            ScheduledCloseAt: closeAt,
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromMinutes(2),
            ExtendedBiddingExtension: TimeSpan.FromMinutes(2),
            MaxDuration: TimeSpan.FromHours(24),
            OpenedAt: DateTimeOffset.UtcNow);

    private async Task<IReadOnlyList<ScheduledMessageSummary>> QueryPendingCloseAuctionsAsync()
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var result = await store.ScheduledMessages.QueryAsync(
            new ScheduledMessageQuery { PageSize = 1000 },
            CancellationToken.None);
        return result.Messages
            .Where(m => m.MessageType != null && m.MessageType.Contains(nameof(CloseAuction)))
            .ToList();
    }

    private async Task<IReadOnlyList<ScheduledMessageSummary>> QueryAllScheduledAsync()
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var result = await store.ScheduledMessages.QueryAsync(
            new ScheduledMessageQuery { PageSize = 1000 },
            CancellationToken.None);
        return result.Messages;
    }

    private async Task CancelAllScheduledCloseAuctionsAsync()
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var all = await QueryAllScheduledAsync();
        var ids = all.Where(m => m.MessageType != null && m.MessageType.Contains(nameof(CloseAuction)))
            .Select(m => m.Id).ToArray();
        if (ids.Length == 0) return;
        await store.ScheduledMessages.CancelAsync(
            new ScheduledMessageQuery { MessageIds = ids },
            CancellationToken.None);
    }
}
