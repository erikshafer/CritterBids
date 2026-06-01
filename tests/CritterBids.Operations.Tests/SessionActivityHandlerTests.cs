using CritterBids.Contracts.Auctions;
using CritterBids.Operations.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Operations.Tests;

/// <summary>
/// End-to-end Testcontainers projection tests for the M7-S5 session activity board (W006 §5a). Each
/// test dispatches the Auctions session events through the in-process Wolverine bus so the full path
/// is exercised — handler discovery + code-gen, the injected Marten session, and the
/// AutoApplyTransactions commit, not just the projection arithmetic. The foreign BCs are excluded in
/// <see cref="OperationsTestFixture"/>, so <see cref="SessionActivityHandler"/> is the sole consumer
/// of each session event within this fixture; <c>SessionStarted</c> is also consumed by Auctions'
/// own <c>SessionStartedHandler</c> in production, but Auctions is excluded here, so
/// <see cref="TestingExtensions.InvokeMessageAndWaitAsync"/> dispatches inline to the single
/// remaining handler.
///
/// Coverage: the <c>SessionCreated</c> seed in <see cref="SessionActivityStatus.Created"/>;
/// <c>ListingAttachedToSession</c> id accumulation; the <c>SessionStarted</c> advance to
/// <see cref="SessionActivityStatus.Started"/> with <c>StartedAt</c> and the set-union+dedupe of its
/// <c>ListingIds</c> with the already-attached ids; the out-of-order <c>ListingAttachedToSession</c>
/// after start (adds id, no status regress); the re-delivered <c>SessionCreated</c> after start (no
/// status/StartedAt regress); and the pure-consumer (no OutgoingMessages) contract.
/// </summary>
[Collection(OperationsTestCollection.Name)]
public class SessionActivityHandlerTests : IAsyncLifetime
{
    private readonly OperationsTestFixture _fixture;

    public SessionActivityHandlerTests(OperationsTestFixture fixture)
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
            // Host failed to start — let the test fail with a clearer message.
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Fixed, strictly-increasing timestamps so the lifecycle ordering is deterministic.
    private static readonly DateTimeOffset CreatedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AttachedAt = CreatedAt.AddMinutes(1);
    private static readonly DateTimeOffset StartedAt = CreatedAt.AddMinutes(2);

    [Fact]
    public async Task SessionCreated_SeedsRow_InCreatedStatus()
    {
        var sessionId = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Friday Flash Lineup", 30, CreatedAt));

        var view = await Load(sessionId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SessionActivityStatus.Created);
        view.Title.ShouldBe("Friday Flash Lineup");
        view.DurationMinutes.ShouldBe(30);
        view.CreatedAt.ShouldBe(CreatedAt);
        view.StartedAt.ShouldBeNull();
        view.AttachedListingIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListingAttachedToSession_AccumulatesIds_WithoutChangingStatus()
    {
        var sessionId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();
        var listingB = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Lineup", 20, CreatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingAttachedToSession(sessionId, listingA, AttachedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingAttachedToSession(sessionId, listingB, AttachedAt.AddSeconds(1)));

        var view = await Load(sessionId);
        view.ShouldNotBeNull();
        view.AttachedListingIds.ShouldBe(new[] { listingA, listingB });
        view.Status.ShouldBe(SessionActivityStatus.Created);
        view.StartedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ListingAttachedToSession_Redelivery_IsDeduped()
    {
        var sessionId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingAttachedToSession(sessionId, listingA, AttachedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingAttachedToSession(sessionId, listingA, AttachedAt));

        var view = await Load(sessionId);
        view.ShouldNotBeNull();
        view.AttachedListingIds.ShouldBe(new[] { listingA });
    }

    [Fact]
    public async Task SessionStarted_Advances_UnionsListingIds_AndDedupes()
    {
        var sessionId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();
        var listingB = Guid.CreateVersion7();
        var listingC = Guid.CreateVersion7();

        // Attach A and B before start.
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Lineup", 15, CreatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingAttachedToSession(sessionId, listingA, AttachedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingAttachedToSession(sessionId, listingB, AttachedAt.AddSeconds(1)));

        // SessionStarted carries B (duplicate) and C (new) — union, not replace, with dedupe.
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionStarted(sessionId, new[] { listingB, listingC }, StartedAt));

        var view = await Load(sessionId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SessionActivityStatus.Started);
        view.StartedAt.ShouldBe(StartedAt);
        view.AttachedListingIds.ShouldBe(new[] { listingA, listingB, listingC });
    }

    [Fact]
    public async Task ListingAttachedToSession_AfterStart_AddsId_WithoutRegressingStatus()
    {
        var sessionId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();
        var listingLate = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Lineup", 10, CreatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionStarted(sessionId, new[] { listingA }, StartedAt));

        // Out-of-order attachment arriving AFTER start: adds its id but must not regress to Created
        // nor null StartedAt (W006 §5a).
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingAttachedToSession(sessionId, listingLate, StartedAt.AddSeconds(1)));

        var view = await Load(sessionId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SessionActivityStatus.Started);
        view.StartedAt.ShouldBe(StartedAt);
        view.AttachedListingIds.ShouldBe(new[] { listingA, listingLate });
    }

    [Fact]
    public async Task SessionCreated_Redelivery_AfterStart_DoesNotRegressStatusOrStartedAt()
    {
        var sessionId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Lineup", 25, CreatedAt));
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionStarted(sessionId, new[] { listingA }, StartedAt));

        // Re-delivered SessionCreated after start must not pull Status back to Created or null
        // StartedAt (the monotone Created → Started guard, W006 §5a).
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Lineup", 25, CreatedAt));

        var view = await Load(sessionId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SessionActivityStatus.Started);
        view.StartedAt.ShouldBe(StartedAt);
    }

    [Fact]
    public async Task SessionStarted_BeforeSessionCreated_PreservesStatus_WhenCreateArrivesLate()
    {
        var sessionId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();

        // SessionStarted arrives first (out-of-order): seeds a Started row.
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionStarted(sessionId, new[] { listingA }, StartedAt));

        // Late SessionCreated fills Title/DurationMinutes/CreatedAt but must NOT regress Status to
        // Created nor null StartedAt (the monotone guard, W006 §5a).
        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Late Lineup", 40, CreatedAt));

        var view = await Load(sessionId);
        view.ShouldNotBeNull();
        view.Status.ShouldBe(SessionActivityStatus.Started);
        view.StartedAt.ShouldBe(StartedAt);
        view.AttachedListingIds.ShouldBe(new[] { listingA });
        // Late create still backfills its own fields.
        view.Title.ShouldBe("Late Lineup");
        view.DurationMinutes.ShouldBe(40);
        view.CreatedAt.ShouldBe(CreatedAt);
    }

    [Fact]
    public async Task HandlerFamily_PublishesNothing_PureConsumer()
    {
        var sessionId = Guid.CreateVersion7();
        var listingA = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Lineup", 30, CreatedAt));

        var tracked = await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionStarted(sessionId, new[] { listingA }, StartedAt));

        // Pure consumer (ADR-014 Path A): no integration messages are published. Re-emitting any
        // consumed session event would surface here.
        tracked.Sent.MessagesOf<SessionCreated>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<SessionStarted>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<ListingAttachedToSession>().ShouldBeEmpty();
    }

    private async Task<SessionActivityView?> Load(Guid sessionId)
    {
        await using var session = _fixture.GetDocumentSession();
        return await session.LoadAsync<SessionActivityView>(sessionId);
    }
}
