using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Settlement;
using CritterBids.Relay.Notifications;
using CritterBids.Relay.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Wolverine;

namespace CritterBids.Relay.Tests;

/// <summary>
/// End-to-end push tests for <c>BiddingHub</c>: a real SignalR client joins a group, the source
/// integration event is published through Wolverine, and the resulting notification is asserted on
/// the wire. Each test joins its group via an awaited hub invocation (race-free) and awaits the push
/// on a <see cref="TaskCompletionSource"/> with a failsafe timeout — deterministic, no real-clock
/// polling.
/// </summary>
[Collection(RelayHubTestCollection.Name)]
public class BiddingHubPushTests
{
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(10);

    private readonly RelayHubTestFixture _fixture;

    public BiddingHubPushTests(RelayHubTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BidPlaced_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = new TaskCompletionSource<BidPlacedNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = BuildConnection();
        connection.On<BidPlacedNotification>("ReceiveMessage", tcs.SetResult);

        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        var placed = new BidPlaced(
            listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: Guid.CreateVersion7(),
            Amount: 150m,
            BidCount: 3,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow);

        await _fixture.Bus.PublishAsync(placed);

        var pushed = await tcs.Task.WaitAsync(PushTimeout);

        pushed.ListingId.ShouldBe(placed.ListingId);
        pushed.BidId.ShouldBe(placed.BidId);
        pushed.BidderId.ShouldBe(placed.BidderId);
        pushed.Amount.ShouldBe(placed.Amount);
        pushed.BidCount.ShouldBe(placed.BidCount);
        pushed.OccurredAt.ShouldBe(placed.PlacedAt);
    }

    [Fact]
    public async Task ListingSold_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = new TaskCompletionSource<ListingSoldNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = BuildConnection();
        connection.On<ListingSoldNotification>("ReceiveMessage", tcs.SetResult);

        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        var sold = new ListingSold(
            listingId,
            SellerId: Guid.CreateVersion7(),
            WinnerId: Guid.CreateVersion7(),
            HammerPrice: 320m,
            BidCount: 7,
            SoldAt: DateTimeOffset.UtcNow);

        await _fixture.Bus.PublishAsync(sold);

        var pushed = await tcs.Task.WaitAsync(PushTimeout);

        pushed.ListingId.ShouldBe(sold.ListingId);
        pushed.WinnerId.ShouldBe(sold.WinnerId);
        pushed.HammerPrice.ShouldBe(sold.HammerPrice);
        pushed.BidCount.ShouldBe(sold.BidCount);
        pushed.SoldAt.ShouldBe(sold.SoldAt);
    }

    [Fact]
    public async Task SettlementCompleted_PushesToWinnerGroup()
    {
        var winnerId = Guid.CreateVersion7();
        var tcs = new TaskCompletionSource<SettlementCompletedNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = BuildConnection();
        connection.On<SettlementCompletedNotification>("ReceiveMessage", tcs.SetResult);

        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", winnerId);

        var completed = new SettlementCompleted(
            SettlementId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            WinnerId: winnerId,
            SellerId: Guid.CreateVersion7(),
            HammerPrice: 320m,
            FeeAmount: 32m,
            SellerPayout: 288m,
            CompletedAt: DateTimeOffset.UtcNow);

        await _fixture.Bus.PublishAsync(completed);

        var pushed = await tcs.Task.WaitAsync(PushTimeout);

        pushed.SettlementId.ShouldBe(completed.SettlementId);
        pushed.ListingId.ShouldBe(completed.ListingId);
        pushed.WinnerId.ShouldBe(completed.WinnerId);
        pushed.HammerPrice.ShouldBe(completed.HammerPrice);
        pushed.CompletedAt.ShouldBe(completed.CompletedAt);
    }

    private HubConnection BuildConnection() =>
        new HubConnectionBuilder()
            .WithUrl(_fixture.BiddingHubUrl)
            .Build();
}
