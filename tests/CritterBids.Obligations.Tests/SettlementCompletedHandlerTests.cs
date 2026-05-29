using CritterBids.Contracts.Settlement;
using CritterBids.Obligations.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Obligations.Tests;

[Collection(ObligationsTestCollection.Name)]
public class SettlementCompletedHandlerTests : IAsyncLifetime
{
    private readonly ObligationsTestFixture _fixture;

    public SettlementCompletedHandlerTests(ObligationsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _fixture.CleanAllMartenDataAsync();
        }
        catch (ObjectDisposedException)
        {
            // Host failed to start — let the test fail with a clearer message rather than
            // cascading ObjectDisposedExceptions.
        }
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

    // ───────────────────────────────────────────────────────────────────────────
    // Saga-start happy path — SettlementCompleted opens the obligation
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SettlementCompleted_StartsSaga_AndOpensObligationStream()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();

        var before = DateTimeOffset.UtcNow;

        // Dispatch SettlementCompleted — the Obligations saga-start handler runs, persists the
        // PostSaleCoordinationSaga, and opens its event stream. InvokeMessageAndWaitAsync drains
        // the local queue before returning.
        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));

        // The saga's Id is deterministic — derive it the same way the handler did.
        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);

        await using var querySession = _fixture.GetDocumentSession();

        // The saga document exists and carries the routing identities + AwaitingShipment status.
        var saga = await querySession.LoadAsync<PostSaleCoordinationSaga>(obligationId);
        saga.ShouldNotBeNull();
        saga.ListingId.ShouldBe(listingId);
        saga.WinnerId.ShouldBe(winnerId);
        saga.SellerId.ShouldBe(sellerId);
        saga.HammerPrice.ShouldBe(85m);
        saga.Status.ShouldBe(ObligationStatus.AwaitingShipment);

        // The obligation event stream contains a single PostSaleCoordinationStarted whose
        // ShipByDeadline equals start time + the active (production) ship-by window of 5 days.
        var events = await querySession.Events.FetchStreamAsync(obligationId);
        events.Count.ShouldBe(1);

        var started = events[0].Data.ShouldBeOfType<PostSaleCoordinationStarted>();
        started.ObligationId.ShouldBe(obligationId);
        started.ListingId.ShouldBe(listingId);
        started.WinnerId.ShouldBe(winnerId);
        started.SellerId.ShouldBe(sellerId);
        started.HammerPrice.ShouldBe(85m);

        started.ShipByDeadline.ShouldBe(started.StartedAt + TimeSpan.FromDays(5));
        started.StartedAt.ShouldBeGreaterThanOrEqualTo(before);
        saga.ShipByDeadline.ShouldBe(started.ShipByDeadline);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Idempotent start on duplicate settlement completion
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateSettlementCompleted_IsNoOp()
    {
        var listingId = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();

        // First completion starts the saga + opens the stream via the bus.
        await _fixture.Host.InvokeMessageAndWaitAsync(NewSettlementCompleted(listingId, winnerId, sellerId));

        var obligationId = ObligationsIdentityNamespaces.ObligationId(listingId);
        var options = _fixture.Host.Services.GetRequiredService<IOptions<ObligationsOptions>>();

        // A second SettlementCompleted for the same listing resolves to the same deterministic
        // ObligationId. Invoke the handler directly (rather than re-dispatching the start message
        // through the bus) and assert it returns a null saga — the existing-saga guard's no-op.
        // The handler is invoked directly here, mirroring the Settlement BC's idempotency-coverage
        // precedent: Wolverine's saga-start wrapper inserts whatever saga the handler returns, so a
        // bus re-dispatch of a start message against a still-live saga is not the framework's
        // supported path. The handler's existence-check is the correctness guarantee and is what
        // this test exercises.
        await using (var session = _fixture.GetDocumentSession())
        {
            var (saga, messages) = await SettlementCompletedHandler.Handle(
                NewSettlementCompleted(listingId, winnerId, sellerId), session, options, default);

            saga.ShouldBeNull();
            messages.ShouldBeEmpty();

            // The guard returns before any StartStream call, so saving changes appends nothing.
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();

        // No second event was appended to the obligation stream.
        var events = await querySession.Events.FetchStreamAsync(obligationId);
        events.Count.ShouldBe(1);

        // The original saga remains untouched in its starting state.
        var existing = await querySession.LoadAsync<PostSaleCoordinationSaga>(obligationId);
        existing.ShouldNotBeNull();
        existing.Status.ShouldBe(ObligationStatus.AwaitingShipment);
    }
}
