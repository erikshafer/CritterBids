using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Participants;
using Marten;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Idempotency tests for <see cref="ParticipantCreditCeilingHandler"/>. The handler
/// consumes <see cref="ParticipantSessionStarted"/> on the
/// <c>auctions-participants-events</c> queue and seeds an Auctions-side
/// <see cref="ParticipantCreditCeiling"/> document. M4-S4 OQ8 confirmed the event does
/// not require <c>AddEventType</c> registration (handler-consumed integration events
/// are routed by Wolverine independently of Marten event-type registration).
///
/// <para>Tests call <see cref="ParticipantCreditCeilingHandler.Handle"/> directly against
/// an isolated <see cref="IDocumentSession"/> — bypasses Wolverine to keep the assertion
/// focused on the projection's upsert behaviour. Same shape as
/// <c>BidderCreditViewTests</c> in the Settlement BC (M5-S5).</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class ParticipantCreditCeilingProjectionTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public ParticipantCreditCeilingProjectionTests(AuctionsTestFixture fixture)
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
    public async Task ParticipantSessionStarted_InitializesRowAtCreditCeiling()
    {
        var participantId = Guid.CreateVersion7();
        var startedAt = DateTimeOffset.UtcNow;

        var message = new ParticipantSessionStarted(
            ParticipantId: participantId,
            DisplayName: "SwiftFerret42",
            BidderId: "Bidder 4217",
            CreditCeiling: 500m,
            StartedAt: startedAt);

        await using (var session = _fixture.GetDocumentSession())
        {
            await ParticipantCreditCeilingHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var query = _fixture.GetDocumentSession();
        var row = await query.LoadAsync<ParticipantCreditCeiling>(participantId);

        row.ShouldNotBeNull();
        row.BidderId.ShouldBe(participantId);
        row.CreditCeiling.ShouldBe(500m);
        row.RegisteredAt.ShouldBe(startedAt);
    }

    [Fact]
    public async Task ParticipantSessionStarted_Redelivered_PreservesExistingRow()
    {
        var participantId = Guid.CreateVersion7();
        var firstDelivery = DateTimeOffset.UtcNow.AddMinutes(-10);
        var redeliveryWithDifferentCeiling = DateTimeOffset.UtcNow;

        var first = new ParticipantSessionStarted(
            ParticipantId: participantId,
            DisplayName: "SwiftFerret42",
            BidderId: "Bidder 4217",
            CreditCeiling: 500m,
            StartedAt: firstDelivery);

        await using (var session = _fixture.GetDocumentSession())
        {
            await ParticipantCreditCeilingHandler.Handle(first, session, default);
            await session.SaveChangesAsync();
        }

        // Redeliver the SAME ParticipantId with a different CreditCeiling + StartedAt to
        // prove the handler does not overwrite either field. The contract is "immutable
        // after first seed" per the projection's docstring; in practice the upstream event
        // is also immutable per ParticipantSessionStarted.cs's "payload is immutable for
        // the participant's lifetime" — but the test still verifies the projection's local
        // guarantee.
        var second = new ParticipantSessionStarted(
            ParticipantId: participantId,
            DisplayName: "SwiftFerret42",
            BidderId: "Bidder 4217",
            CreditCeiling: 999m,
            StartedAt: redeliveryWithDifferentCeiling);

        await using (var session = _fixture.GetDocumentSession())
        {
            await ParticipantCreditCeilingHandler.Handle(second, session, default);
            await session.SaveChangesAsync();
        }

        await using var query = _fixture.GetDocumentSession();
        var row = await query.LoadAsync<ParticipantCreditCeiling>(participantId);

        row.ShouldNotBeNull();
        row.CreditCeiling.ShouldBe(500m, "redelivery must not overwrite the original CreditCeiling");
        row.RegisteredAt.ShouldBe(firstDelivery, "redelivery must not re-stamp RegisteredAt");
    }
}
