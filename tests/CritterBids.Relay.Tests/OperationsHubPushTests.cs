using CritterBids.Contracts;
using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Listings;
using CritterBids.Contracts.Obligations;
using CritterBids.Contracts.Participants;
using CritterBids.Contracts.Selling;
using CritterBids.Relay.Notifications;
using CritterBids.Relay.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace CritterBids.Relay.Tests;

[Collection(RelayHubTestCollection.Name)]
public class OperationsHubPushTests
{
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(10);

    private readonly RelayHubTestFixture _fixture;

    public OperationsHubPushTests(RelayHubTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BidPlaced_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new BidPlaced(
            Guid.CreateVersion7(),
            BidId: Guid.CreateVersion7(),
            BidderId: Guid.CreateVersion7(),
            Amount: 125m,
            BidCount: 2,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe("BidPlacedOperations");
    }

    [Fact]
    public async Task ListingSold_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ListingSold(
            Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            WinnerId: Guid.CreateVersion7(),
            HammerPrice: 300m,
            BidCount: 5,
            SoldAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe("ListingSoldOperations");
    }

    [Fact]
    public async Task SessionCreated_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new SessionCreated(
            SessionId: Guid.CreateVersion7(),
            Title: "Flash Session",
            DurationMinutes: 30,
            CreatedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(SessionCreated));
    }

    [Fact]
    public async Task SessionStarted_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new SessionStarted(
            SessionId: Guid.CreateVersion7(),
            ListingIds: [Guid.CreateVersion7(), Guid.CreateVersion7()],
            StartedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(SessionStarted));
    }

    [Fact]
    public async Task ListingAttachedToSession_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ListingAttachedToSession(
            SessionId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            AttachedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingAttachedToSession));
    }

    [Fact]
    public async Task ParticipantSessionStarted_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ParticipantSessionStarted(
            ParticipantId: Guid.CreateVersion7(),
            DisplayName: "SwiftFerret42",
            BidderId: "Bidder 4217",
            CreditCeiling: 500m,
            StartedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ParticipantSessionStarted));
    }

    [Fact]
    public async Task SellerRegistrationCompleted_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new SellerRegistrationCompleted(
            ParticipantId: Guid.CreateVersion7(),
            CompletedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(SellerRegistrationCompleted));
    }

    [Fact]
    public async Task ListingPublished_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ListingPublished(
            ListingId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            Title: "Keyboard",
            Format: "Flash",
            StartingBid: 50m,
            ReservePrice: 100m,
            BuyItNow: null,
            Duration: TimeSpan.FromMinutes(30),
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
            ExtendedBiddingExtension: TimeSpan.FromSeconds(30),
            FeePercentage: 10m,
            PublishedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingPublished));
    }

    [Fact]
    public async Task ListingRevised_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ListingRevised(
            ListingId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            Title: "Keyboard v2",
            Description: "Updated description",
            ShippingTerms: "Ground",
            RevisedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingRevised));
    }

    [Fact]
    public async Task ListingEndedEarly_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ListingEndedEarly(
            ListingId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            Reason: "SellerRequest",
            EndedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingEndedEarly));
    }

    [Fact]
    public async Task LotWatchAdded_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new LotWatchAdded(
            ListingId: Guid.CreateVersion7(),
            ParticipantId: Guid.CreateVersion7(),
            AddedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(LotWatchAdded));
    }

    [Fact]
    public async Task LotWatchRemoved_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new LotWatchRemoved(
            ListingId: Guid.CreateVersion7(),
            ParticipantId: Guid.CreateVersion7(),
            RemovedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(LotWatchRemoved));
    }

    [Fact]
    public async Task DisputeOpened_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new DisputeOpened(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            DisputeId: Guid.CreateVersion7(),
            RaisedBy: Guid.CreateVersion7(),
            Reason: "NonDelivery",
            OpenedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(DisputeOpened));
    }

    [Fact]
    public async Task DisputeResolved_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new DisputeResolved(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            DisputeId: Guid.CreateVersion7(),
            ResolutionType: "Closed",
            ResolvedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(DisputeResolved));
    }

    // ── M8-S6b ops-feed completion: the eight events the topology invariant found missing ──

    [Fact]
    public async Task SettlementCompleted_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        var listingId = Guid.CreateVersion7();
        await _fixture.Bus.PublishAsync(new CritterBids.Contracts.Settlement.SettlementCompleted(
            SettlementId: Guid.CreateVersion7(),
            ListingId: listingId,
            WinnerId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            HammerPrice: 300m,
            FeeAmount: 30m,
            SellerPayout: 270m,
            CompletedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe("SettlementCompleted");
        pushed.ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task SellerPayoutIssued_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new CritterBids.Contracts.Settlement.SellerPayoutIssued(
            SettlementId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            PayoutAmount: 270m,
            FeeDeducted: 30m,
            IssuedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe("SellerPayoutIssued");
        pushed.ListingId.ShouldBeNull(); // the contract carries no ListingId
    }

    [Fact]
    public async Task PaymentFailed_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new CritterBids.Contracts.Settlement.PaymentFailed(
            SettlementId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            WinnerId: Guid.CreateVersion7(),
            Reason: "InsufficientCredit",
            FailedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe("PaymentFailed");
        pushed.Payload.ShouldContain("InsufficientCredit");
    }

    [Fact]
    public async Task DeadlineEscalated_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new DeadlineEscalated(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            EscalatedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(DeadlineEscalated));
    }

    [Fact]
    public async Task ObligationFulfilled_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ObligationFulfilled(
            ObligationId: Guid.CreateVersion7(),
            ListingId: Guid.CreateVersion7(),
            WinnerId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            FulfilledAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ObligationFulfilled));
    }

    [Fact]
    public async Task BiddingOpened_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new BiddingOpened(
            ListingId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            StartingBid: 50m,
            ReserveThreshold: null,
            BuyItNowPrice: null,
            ScheduledCloseAt: DateTimeOffset.UtcNow.AddMinutes(30),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            MaxDuration: TimeSpan.FromMinutes(30),
            OpenedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(BiddingOpened));
    }

    [Fact]
    public async Task ListingPassed_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ListingPassed(
            ListingId: Guid.CreateVersion7(),
            Reason: "NoBids",
            HighestBid: null,
            BidCount: 0,
            PassedAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingPassed));
    }

    [Fact]
    public async Task ListingWithdrawn_PushesToOperationsHub()
    {
        var tcs = NewTcs<OperationsFeedNotification>();
        await using var connection = BuildOperationsConnection();
        connection.On<OperationsFeedNotification>("ReceiveMessage", tcs.SetResult);
        await connection.StartAsync();

        await _fixture.Bus.PublishAsync(new ListingWithdrawn(
            ListingId: Guid.CreateVersion7(),
            WithdrawnBy: Guid.CreateVersion7(),
            Reason: "SellerRequest",
            WithdrawnAt: DateTimeOffset.UtcNow));

        var pushed = await tcs.Task.WaitAsync(PushTimeout);
        pushed.EventType.ShouldBe(nameof(ListingWithdrawn));
    }

    private HubConnection BuildOperationsConnection() =>
        new HubConnectionBuilder()
            .WithUrl(_fixture.OperationsHubUrl)
            .Build();

    private static TaskCompletionSource<T> NewTcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
