using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Obligations;
using CritterBids.Contracts.Selling;
using CritterBids.Contracts.Settlement;
using CritterBids.Relay.Notifications;
using CritterBids.Relay.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace CritterBids.Relay.Tests;

[Collection(RelayHubTestCollection.Name)]
public class BiddingHubRemainingPushTests
{
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(10);

    private readonly RelayHubTestFixture _fixture;

    public BiddingHubRemainingPushTests(RelayHubTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BiddingOpened_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new BiddingOpened(
            listingId,
            SellerId: Guid.CreateVersion7(),
            StartingBid: 50m,
            ReserveThreshold: 100m,
            BuyItNowPrice: null,
            ScheduledCloseAt: DateTimeOffset.UtcNow.AddMinutes(20),
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
            ExtendedBiddingExtension: TimeSpan.FromSeconds(30),
            MaxDuration: TimeSpan.FromMinutes(5),
            OpenedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(BiddingOpened));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task BidRejected_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new BidRejected(
            listingId,
            bidderId,
            AttemptedAmount: 120m,
            CurrentHighBid: 125m,
            Reason: "BelowMinimumBid",
            RejectedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(BidRejected));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task ReserveMet_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new ReserveMet(
            listingId,
            Amount: 300m,
            MetAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ReserveMet));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task ExtendedBiddingTriggered_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new ExtendedBiddingTriggered(
            listingId,
            PreviousCloseAt: DateTimeOffset.UtcNow.AddMinutes(1),
            NewCloseAt: DateTimeOffset.UtcNow.AddMinutes(2),
            TriggeredByBidderId: Guid.CreateVersion7(),
            TriggeredAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ExtendedBiddingTriggered));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task ListingPassed_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new ListingPassed(
            listingId,
            Reason: "NoBids",
            HighestBid: null,
            BidCount: 0,
            PassedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingPassed));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task ListingWithdrawn_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new ListingWithdrawn(
            listingId,
            WithdrawnBy: Guid.CreateVersion7(),
            Reason: "OpsIntervention",
            WithdrawnAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingWithdrawn));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task ProxyBidExhausted_PushesToBidderGroup()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var tcs = NewTcs<BidderGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<BidderGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", bidderId);

        await _fixture.Bus.PublishAsync(new ProxyBidExhausted(
            listingId,
            bidderId,
            MaxAmount: 400m,
            ExhaustedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ProxyBidExhausted));
        pushed.BidderId.ShouldBe(bidderId);
    }

    [Fact]
    public async Task BuyItNowPurchased_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new BuyItNowPurchased(
            listingId,
            BuyerId: Guid.CreateVersion7(),
            Price: 500m,
            PurchasedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(BuyItNowPurchased));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task BuyItNowOptionRemoved_PushesToListingGroup()
    {
        var listingId = Guid.CreateVersion7();
        var tcs = NewTcs<ListingGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<ListingGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinListingGroup", listingId);

        await _fixture.Bus.PublishAsync(new BuyItNowOptionRemoved(
            listingId,
            RemovedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(BuyItNowOptionRemoved));
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task SellerPayoutIssued_PushesToSellerGroup()
    {
        var sellerId = Guid.CreateVersion7();
        var tcs = NewTcs<BidderGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<BidderGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", sellerId);

        await _fixture.Bus.PublishAsync(new SellerPayoutIssued(
            SettlementId: Guid.CreateVersion7(),
            SellerId: sellerId,
            PayoutAmount: 288m,
            FeeDeducted: 32m,
            IssuedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(SellerPayoutIssued));
        pushed.BidderId.ShouldBe(sellerId);
    }

    [Fact]
    public async Task TrackingInfoProvided_PushesToBidderGroup()
    {
        var sellerId = Guid.CreateVersion7();
        var tcs = NewTcs<BidderGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<BidderGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", sellerId);

        await _fixture.Bus.PublishAsync(new TrackingInfoProvided(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            SellerId: sellerId,
            TrackingNumber: "TRACK123",
            ProvidedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(TrackingInfoProvided));
        pushed.BidderId.ShouldBe(sellerId);
    }

    [Fact]
    public async Task ObligationFulfilled_PushesToWinnerGroup()
    {
        var winnerId = Guid.CreateVersion7();
        var tcs = NewTcs<BidderGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<BidderGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", winnerId);

        await _fixture.Bus.PublishAsync(new ObligationFulfilled(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            WinnerId: winnerId,
            SellerId: Guid.CreateVersion7(),
            FulfilledAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ObligationFulfilled));
        pushed.BidderId.ShouldBe(winnerId);
    }

    [Fact]
    public async Task DisputeOpened_PushesToRaisedByBidderGroup()
    {
        var raisedBy = Guid.CreateVersion7();
        var tcs = NewTcs<BidderGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<BidderGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", raisedBy);

        await _fixture.Bus.PublishAsync(new DisputeOpened(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            DisputeId: Guid.CreateVersion7(),
            RaisedBy: raisedBy,
            Reason: "NonDelivery",
            OpenedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(DisputeOpened));
        pushed.BidderId.ShouldBe(raisedBy);
    }

    [Fact]
    public async Task DisputeResolved_WithParticipantId_PushesToBidderGroup()
    {
        var participantId = Guid.CreateVersion7();
        var tcs = NewTcs<BidderGroupNotification>();
        await using var connection = BuildBiddingConnection();
        connection.On<BidderGroupNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", participantId);

        await _fixture.Bus.PublishAsync(new DisputeResolved(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            DisputeId: Guid.CreateVersion7(),
            ResolutionType: "Closed",
            ResolvedAt: DateTimeOffset.UtcNow,
            ParticipantId: participantId));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(DisputeResolved));
        pushed.BidderId.ShouldBe(participantId);
    }

    private HubConnection BuildBiddingConnection() =>
        new HubConnectionBuilder()
            .WithUrl(_fixture.BiddingHubUrl)
            .Build();

    private static TaskCompletionSource<T> NewTcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
