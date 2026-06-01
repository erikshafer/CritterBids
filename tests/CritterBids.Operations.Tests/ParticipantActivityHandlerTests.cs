using CritterBids.Contracts.Participants;
using CritterBids.Operations.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Operations.Tests;

/// <summary>
/// End-to-end Testcontainers projection tests for the M7-S5 participant activity board (W006 §5b).
/// Each test dispatches <c>ParticipantSessionStarted</c> through the in-process Wolverine bus so the
/// full path is exercised — handler discovery + code-gen, the injected Marten session, and the
/// AutoApplyTransactions commit. The foreign BCs (including Settlement and Auctions, which also
/// consume this event) are excluded in <see cref="OperationsTestFixture"/>, so
/// <see cref="ParticipantActivityHandler"/> is the sole consumer within this fixture and
/// <see cref="TestingExtensions.InvokeMessageAndWaitAsync"/> dispatches inline.
///
/// Coverage: the five-field seed asserting <c>BidderId</c> round-trips as a <c>string</c>; the
/// idempotent re-delivery (one row, no duplicate); and the pure-consumer (no OutgoingMessages)
/// contract.
/// </summary>
[Collection(OperationsTestCollection.Name)]
public class ParticipantActivityHandlerTests : IAsyncLifetime
{
    private readonly OperationsTestFixture _fixture;

    public ParticipantActivityHandlerTests(OperationsTestFixture fixture)
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

    private static readonly DateTimeOffset StartedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ParticipantSessionStarted_SeedsRow_WithAllFiveFields_BidderIdAsString()
    {
        var participantId = Guid.CreateVersion7();

        await _fixture.Host.InvokeMessageAndWaitAsync(
            new ParticipantSessionStarted(participantId, "Aqua Heron", "Bidder 4217", 5000m, StartedAt));

        var view = await Load(participantId);
        view.ShouldNotBeNull();
        view.ParticipantId.ShouldBe(participantId);
        view.DisplayName.ShouldBe("Aqua Heron");
        view.BidderId.ShouldBe("Bidder 4217"); // string — not Guid; never "paddle"
        view.BidderId.ShouldBeOfType<string>();
        view.CreditCeiling.ShouldBe(5000m);
        view.StartedAt.ShouldBe(StartedAt);
    }

    [Fact]
    public async Task ParticipantSessionStarted_Redelivery_IsIdempotent_SingleRow()
    {
        var participantId = Guid.CreateVersion7();
        var message = new ParticipantSessionStarted(participantId, "Cobalt Otter", "Bidder 0099", 2500m, StartedAt);

        await _fixture.Host.InvokeMessageAndWaitAsync(message);
        var tracked = await _fixture.Host.InvokeMessageAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var rows = await session.Query<ParticipantActivityView>()
            .Where(x => x.ParticipantId == participantId)
            .ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].BidderId.ShouldBe("Bidder 0099");

        // Pure consumer (ADR-014 Path A): re-emitting the consumed event would surface here.
        tracked.Sent.MessagesOf<ParticipantSessionStarted>().ShouldBeEmpty();
    }

    private async Task<ParticipantActivityView?> Load(Guid participantId)
    {
        await using var session = _fixture.GetDocumentSession();
        return await session.LoadAsync<ParticipantActivityView>(participantId);
    }
}
