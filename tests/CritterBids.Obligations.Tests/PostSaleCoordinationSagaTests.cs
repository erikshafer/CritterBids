using CritterBids.Contracts.Obligations;
using CritterBids.Contracts.Settlement;
using CritterBids.Obligations.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Durability.ScheduledMessageManagement;
using Wolverine.Tracking;

namespace CritterBids.Obligations.Tests;

/// <summary>
/// Integration tests for the <see cref="PostSaleCoordinationSaga"/> happy path + cancellable
/// reminder chain (M6-S3, opsx scenarios 9.1 + 9.3). Runs against the demo-mode fixture so the
/// demo-duration config seam (W001-6) is exercised; the durations are minute-scale, so no scheduled
/// timer fires on its own during a run and every transition is driven deterministically via direct
/// <c>InvokeMessageAndWaitAsync</c> — the AuctionClosingSagaTests precedent.
///
/// <para><b>Scheduled-message assertions.</b> Timers are scheduled via <c>bus.ScheduleAsync</c> and
/// land in <see cref="IMessageStore"/>'s scheduled-message store; tests query that store directly
/// (mirroring AuctionClosingSagaTests). Cancellation is verified by the targeted message types
/// disappearing from the store after <see cref="ProvideTracking"/>.</para>
///
/// <para><b>Integration-event assertions.</b> The relay-obligations-events publish routes live
/// inside Program.cs's RabbitMQ block, which the test fixture skips (no broker). So the saga's
/// OutgoingMessages-emitted <see cref="TrackingInfoProvided"/> / <see cref="ObligationFulfilled"/>
/// have no route and land in <c>tracked.NoRoutes</c> — the established CritterBids fixture stance
/// for cross-BC events whose consumer has not shipped.</para>
/// </summary>
[Collection(ObligationsLifecycleTestCollection.Name)]
public class PostSaleCoordinationSagaTests : IAsyncLifetime
{
    private readonly ObligationsLifecycleTestFixture _fixture;

    public PostSaleCoordinationSagaTests(ObligationsLifecycleTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static SettlementCompleted NewSettlementCompleted(Guid listingId, Guid winnerId, Guid sellerId) =>
        new(
            SettlementId: Guid.CreateVersion7(),
            ListingId: listingId,
            WinnerId: winnerId,
            SellerId: sellerId,
            HammerPrice: 85m,
            FeeAmount: 8.50m,
            SellerPayout: 76.50m,
            CompletedAt: DateTimeOffset.UtcNow);

    private async Task<IReadOnlyList<ScheduledMessageSummary>> QueryScheduledOfTypeAsync(Type messageType)
    {
        var store = _fixture.Host.Services.GetRequiredService<IMessageStore>();
        var result = await store.ScheduledMessages.QueryAsync(
            new ScheduledMessageQuery { PageSize = 1000 },
            CancellationToken.None);
        return result.Messages
            .Where(m => m.MessageType != null && m.MessageType.Contains(messageType.Name))
            .ToList();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 9.1 — Happy path: start → reminder → tracking → auto-confirm → fulfilled
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_StartToFulfilled_DrivesEntireLifecycle()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);

        // ── Step 1: SettlementCompleted starts the saga and schedules the reminder + escalation ──
        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));

        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga.ShouldNotBeNull();
            saga.Status.ShouldBe(ObligationStatus.AwaitingShipment);

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view.ShouldNotBeNull();
            view.Status.ShouldBe(ObligationStatus.AwaitingShipment);
            view.TrackingNumber.ShouldBeNull();
            view.ReminderSentAt.ShouldBeNull();
        }

        // Both timers are pending in the scheduled-message store.
        (await QueryScheduledOfTypeAsync(typeof(SendShippingReminder))).ShouldHaveSingleItem();
        (await QueryScheduledOfTypeAsync(typeof(SendDeadlineEscalation))).ShouldHaveSingleItem();

        // ── Step 2: the shipping reminder fires (simulated by direct invocation) ──
        await _fixture.Host.InvokeMessageAndWaitAsync(new SendShippingReminder(obligationId));

        await using (var session = _fixture.GetDocumentSession())
        {
            var events = await session.Events.FetchStreamAsync(obligationId);
            events.Select(e => e.Data).OfType<ShippingReminderSent>().ShouldHaveSingleItem();

            // Reminder does not advance state — still awaiting shipment.
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga!.Status.ShouldBe(ObligationStatus.AwaitingShipment);

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.AwaitingShipment);
            view.ReminderSentAt.ShouldNotBeNull();
        }

        // ── Step 3: the seller provides tracking — cancels timers, emits TrackingInfoProvided, schedules auto-confirm ──
        var trackingTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ProvideTracking(obligationId, "1Z999AA10123456784"));

        // TrackingInfoProvided emitted on the bus (no route in test → NoRoutes) carrying the tracking number.
        var trackingEmitted = trackingTracked.NoRoutes.MessagesOf<TrackingInfoProvided>().ShouldHaveSingleItem();
        trackingEmitted.ObligationId.ShouldBe(obligationId);
        trackingEmitted.TrackingNumber.ShouldBe("1Z999AA10123456784");

        // Both pending timers were cancelled; the auto-confirm timer is now scheduled.
        (await QueryScheduledOfTypeAsync(typeof(SendShippingReminder))).ShouldBeEmpty();
        (await QueryScheduledOfTypeAsync(typeof(SendDeadlineEscalation))).ShouldBeEmpty();
        (await QueryScheduledOfTypeAsync(typeof(ConfirmDelivery))).ShouldHaveSingleItem();

        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga!.Status.ShouldBe(ObligationStatus.Shipped);
            saga.TrackingNumber.ShouldBe("1Z999AA10123456784");

            var events = await session.Events.FetchStreamAsync(obligationId);
            events.Select(e => e.Data).OfType<TrackingInfoProvided>().ShouldHaveSingleItem();

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Shipped);
            view.TrackingNumber.ShouldBe("1Z999AA10123456784");
            view.TrackingProvidedAt.ShouldNotBeNull();
        }

        // ── Step 4: delivery auto-confirms (simulated by direct invocation) — fulfills + completes ──
        var confirmTracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ConfirmDelivery(obligationId));

        // ObligationFulfilled emitted on the bus (no route in test → NoRoutes).
        var fulfilled = confirmTracked.NoRoutes.MessagesOf<ObligationFulfilled>().ShouldHaveSingleItem();
        fulfilled.ObligationId.ShouldBe(obligationId);
        fulfilled.ListingId.ShouldBe(listingId);
        fulfilled.WinnerId.ShouldBe(winnerId);
        fulfilled.SellerId.ShouldBe(sellerId);

        await using (var session = _fixture.GetDocumentSession())
        {
            // MarkCompleted() deleted the saga document.
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga.ShouldBeNull();

            var events = await session.Events.FetchStreamAsync(obligationId);
            events.Select(e => e.Data).OfType<DeliveryConfirmed>().ShouldHaveSingleItem();

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Fulfilled);
            view.FulfilledAt.ShouldNotBeNull();
        }
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 9.3 — A reminder that fires after tracking was provided is a no-op
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StaleReminderAfterTracking_IsNoOp()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);

        // Start the saga, then provide tracking — the obligation advances to Shipped.
        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new ProvideTracking(obligationId, "1Z999AA10123456784"));

        int countBefore;
        await using (var session = _fixture.GetDocumentSession())
        {
            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga!.Status.ShouldBe(ObligationStatus.Shipped);

            var events = await session.Events.FetchStreamAsync(obligationId);
            countBefore = events.Count;
        }

        // A shipping reminder that slipped through cancellation fires after tracking. The no-op
        // guard (Status != AwaitingShipment) drops it — nothing appended, no state change.
        await _fixture.Host.InvokeMessageAndWaitAsync(new SendShippingReminder(obligationId));

        await using (var session = _fixture.GetDocumentSession())
        {
            var events = await session.Events.FetchStreamAsync(obligationId);
            events.Count.ShouldBe(countBefore);
            events.Select(e => e.Data).OfType<ShippingReminderSent>().ShouldBeEmpty();

            var saga = await session.LoadAsync<PostSaleCoordinationSaga>(obligationId);
            saga!.Status.ShouldBe(ObligationStatus.Shipped);

            var view = await session.LoadAsync<ObligationStatusView>(obligationId);
            view!.Status.ShouldBe(ObligationStatus.Shipped);
            view.ReminderSentAt.ShouldBeNull();
        }
    }
}
