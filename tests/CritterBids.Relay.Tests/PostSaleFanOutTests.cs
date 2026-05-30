using CritterBids.Contracts.Settlement;
using CritterBids.Obligations;
using CritterBids.Relay.Notifications;
using CritterBids.Relay.Tests.Fixtures;
using Marten;
using Microsoft.AspNetCore.SignalR.Client;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Relay.Tests;

/// <summary>
/// M6-S7 close-out integration test. Proves the post-sale <b>fan-out</b> end-to-end in one composed
/// host: a single <see cref="SettlementCompleted"/> drives <b>two independent sibling consumers</b> —
/// <list type="number">
///   <item>the Obligations <c>SettlementCompletedHandler</c>, which starts
///         <c>PostSaleCoordinationSaga</c> (emitting <c>PostSaleCoordinationStarted</c> under the
///         deterministic UUID-v5 <c>ObligationId</c>, projected to an <see cref="ObligationStatusView"/>
///         in <see cref="ObligationStatus.AwaitingShipment"/>); and</item>
///   <item>the Relay <c>SettlementCompletedHandler</c>, which pushes a
///         <see cref="SettlementCompletedNotification"/> to the winner's <c>bidder:{WinnerId}</c>
///         <c>BiddingHub</c> group.</item>
/// </list>
///
/// <para>Neither consumer depends on the other — the test asserts <b>both</b> outcomes from one
/// publish, which is the structural claim of the fan-out: sibling consumers off one event, not a
/// chain. <c>MultipleHandlerBehavior.Separated</c> + <c>MessageIdentity.IdAndDestination</c> in the
/// fixture make the two handlers true independent destinations (mirroring <c>Program.cs</c>).</para>
///
/// <para><b>Determinism.</b> The publish is driven through Wolverine activity tracking, which drains
/// both separated handler queues and commits the Inline <c>ObligationStatusView</c> projection before
/// returning; the SignalR push is observed on a <see cref="TaskCompletionSource"/> with a failsafe
/// timeout. The saga's <c>bus.ScheduleAsync</c> reminder/escalation timers target day-out instants
/// (default Production <see cref="ObligationsOptions"/>) and never fire — so there are no real-clock
/// waits. The deterministic <c>ObligationId</c> derivation itself stays unit-covered in
/// Obligations.Tests; here it is proven structurally by the view, saga, and event stream all sharing
/// the one key.</para>
/// </summary>
[Collection(PostSaleFanOutTestCollection.Name)]
public class PostSaleFanOutTests
{
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TrackingTimeout = TimeSpan.FromSeconds(30);

    private readonly PostSaleFanOutTestFixture _fixture;

    public PostSaleFanOutTests(PostSaleFanOutTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SettlementCompleted_FansOutToObligationsSagaAndRelayWinnerPush()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();

        // --- Relay half: subscribe the winner before publishing (race-free enrolment) ---
        var pushTcs = new TaskCompletionSource<SettlementCompletedNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = new HubConnectionBuilder()
            .WithUrl(_fixture.BiddingHubUrl)
            .Build();
        connection.On<SettlementCompletedNotification>(
            "ReceiveMessage",
            n => pushTcs.TrySetResult(n));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinBidderGroup", winnerId);

        var completed = new SettlementCompleted(
            SettlementId: Guid.CreateVersion7(),
            ListingId: listingId,
            WinnerId: winnerId,
            SellerId: sellerId,
            HammerPrice: 320m,
            FeeAmount: 32m,
            SellerPayout: 288m,
            CompletedAt: DateTimeOffset.UtcNow);

        // One publish, tracked until BOTH separated handler queues drain and the Inline projection
        // commits — no polling, no clock wait.
        await _fixture.Host
            .TrackActivity()
            .Timeout(TrackingTimeout)
            .PublishMessageAndWaitAsync(completed);

        // --- Assert Relay half: the winner received the settlement push on the wire ---
        var pushed = await pushTcs.Task.WaitAsync(PushTimeout);
        pushed.SettlementId.ShouldBe(completed.SettlementId);
        pushed.ListingId.ShouldBe(listingId);
        pushed.WinnerId.ShouldBe(winnerId);
        pushed.HammerPrice.ShouldBe(completed.HammerPrice);
        pushed.CompletedAt.ShouldBe(completed.CompletedAt);

        // --- Assert Obligations half: the saga started and the read model is AwaitingShipment ---
        await using var session = _fixture.DocumentStore.QuerySession();

        var views = await session.Query<ObligationStatusView>()
            .Where(v => v.ListingId == listingId)
            .ToListAsync();

        views.Count.ShouldBe(1, "exactly one obligation should start per sold listing");
        var view = views[0];
        view.Status.ShouldBe(ObligationStatus.AwaitingShipment);
        view.WinnerId.ShouldBe(winnerId);
        view.SellerId.ShouldBe(sellerId);
        view.HammerPrice.ShouldBe(completed.HammerPrice);

        // Prove the deterministic key without recomputing the v5 hash: the view, its backing stream,
        // and the start event all share the one ObligationId. The start event must live on the stream
        // whose id equals the view id, and carry the same listing.
        var events = await session.Events.FetchStreamAsync(view.Id);
        var started = events
            .Select(e => e.Data)
            .OfType<PostSaleCoordinationStarted>()
            .ShouldHaveSingleItem();
        started.ObligationId.ShouldBe(view.Id);
        started.ListingId.ShouldBe(listingId);
        started.WinnerId.ShouldBe(winnerId);
        started.SellerId.ShouldBe(sellerId);
    }
}
