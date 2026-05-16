using CritterBids.Contracts.Participants;
using CritterBids.Settlement.Tests.Fixtures;
using Marten;

namespace CritterBids.Settlement.Tests;

[Collection(SettlementTestCollection.Name)]
public class BidderCreditViewTests : IAsyncLifetime
{
    private readonly SettlementTestFixture _fixture;

    public BidderCreditViewTests(SettlementTestFixture fixture)
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

    // ───────────────────────────────────────────────────────────────────────────
    // ParticipantSessionStarted — seeds row at CreditCeiling
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParticipantSessionStarted_InitializesRowAtCreditCeiling()
    {
        var participantId = Guid.CreateVersion7();
        var startedAt = DateTimeOffset.UtcNow;

        var message = new ParticipantSessionStarted(
            ParticipantId: participantId,
            DisplayName: "BoldFerret4217",
            BidderId: "Bidder 4217",
            CreditCeiling: 500m,
            StartedAt: startedAt);

        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<BidderCreditView>(participantId);

        row.ShouldNotBeNull();
        row.BidderId.ShouldBe(participantId);
        row.RemainingCredit.ShouldBe(500m);
        row.LastChargedSettlementId.ShouldBeNull();
        row.UpdatedAt.ShouldBe(startedAt);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // WinnerCharged — debits remaining credit
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WinnerCharged_DebitsRemainingCredit()
    {
        var participantId = Guid.CreateVersion7();
        var settlementId = Guid.CreateVersion7();
        var chargedAt = DateTimeOffset.UtcNow;

        // Seed via ParticipantSessionStarted at 500.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new ParticipantSessionStarted(
                    ParticipantId: participantId,
                    DisplayName: "BoldFerret4217",
                    BidderId: "Bidder 4217",
                    CreditCeiling: 500m,
                    StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
                session, default);
            await session.SaveChangesAsync();
        }

        // Charge 85.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new WinnerCharged(
                    SettlementId: settlementId,
                    WinnerId: participantId,
                    Amount: 85m,
                    ChargedAt: chargedAt),
                session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<BidderCreditView>(participantId);

        row.ShouldNotBeNull();
        row.RemainingCredit.ShouldBe(415m); // 500 - 85
        row.LastChargedSettlementId.ShouldBe(settlementId);
        row.UpdatedAt.ShouldBe(chargedAt);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // WinnerCharged idempotency — duplicate SettlementId is a no-op
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WinnerCharged_Idempotent_OnDuplicateSettlementId()
    {
        var participantId = Guid.CreateVersion7();
        var settlementId = Guid.CreateVersion7();

        // Seed at 500.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new ParticipantSessionStarted(
                    ParticipantId: participantId,
                    DisplayName: "BoldFerret4217",
                    BidderId: "Bidder 4217",
                    CreditCeiling: 500m,
                    StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
                session, default);
            await session.SaveChangesAsync();
        }

        // First charge of 85.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new WinnerCharged(
                    SettlementId: settlementId,
                    WinnerId: participantId,
                    Amount: 85m,
                    ChargedAt: DateTimeOffset.UtcNow.AddSeconds(-30)),
                session, default);
            await session.SaveChangesAsync();
        }

        // Re-deliver the same WinnerCharged (same SettlementId). Per W003 Phase 1 Part 7
        // idempotency-by-LastChargedSettlementId, this is a no-op.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new WinnerCharged(
                    SettlementId: settlementId,
                    WinnerId: participantId,
                    Amount: 85m,
                    ChargedAt: DateTimeOffset.UtcNow),
                session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<BidderCreditView>(participantId);

        row.ShouldNotBeNull();
        // Still 415 — second debit was rejected.
        row.RemainingCredit.ShouldBe(415m);
        row.LastChargedSettlementId.ShouldBe(settlementId);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // WinnerCharged lazy-init — no prior row yields negative-credit sentinel
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WinnerCharged_LazyInit_WhenNoParticipantSessionStartedSeed()
    {
        var participantId = Guid.CreateVersion7();
        var settlementId = Guid.CreateVersion7();
        var chargedAt = DateTimeOffset.UtcNow;

        // No seed — go straight to WinnerCharged.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new WinnerCharged(
                    SettlementId: settlementId,
                    WinnerId: participantId,
                    Amount: 85m,
                    ChargedAt: chargedAt),
                session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<BidderCreditView>(participantId);

        row.ShouldNotBeNull();
        // The lazy-init posture per W003 Phase 1 Part 7 / BidderCreditView docstring:
        // RemainingCredit = -Amount marks "row created from WinnerCharged without a prior
        // session-started seed" as data. Downstream consumers read the value verbatim.
        row.RemainingCredit.ShouldBe(-85m);
        row.LastChargedSettlementId.ShouldBe(settlementId);
        row.UpdatedAt.ShouldBe(chargedAt);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // ParticipantSessionStarted re-delivery — preserves already-charged row
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParticipantSessionStarted_DoesNotRegressAlreadyChargedRow()
    {
        // Combined idempotency assertion: if a charge has landed before a re-delivery of
        // ParticipantSessionStarted (cross-queue race or at-least-once redelivery), the
        // already-charged row must be preserved — re-seeding to CreditCeiling would erase
        // the debit. The handler's `if (existing is { LastChargedSettlementId: not null })`
        // guard is what protects this; this test exercises that guard.
        var participantId = Guid.CreateVersion7();
        var settlementId = Guid.CreateVersion7();

        // Seed at 500.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new ParticipantSessionStarted(
                    ParticipantId: participantId,
                    DisplayName: "BoldFerret4217",
                    BidderId: "Bidder 4217",
                    CreditCeiling: 500m,
                    StartedAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
                session, default);
            await session.SaveChangesAsync();
        }

        // Charge 85.
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new WinnerCharged(
                    SettlementId: settlementId,
                    WinnerId: participantId,
                    Amount: 85m,
                    ChargedAt: DateTimeOffset.UtcNow),
                session, default);
            await session.SaveChangesAsync();
        }

        // Re-deliver ParticipantSessionStarted (would set RemainingCredit back to 500 if
        // the guard weren't in place).
        await using (var session = _fixture.GetDocumentSession())
        {
            await BidderCreditViewHandler.Handle(
                new ParticipantSessionStarted(
                    ParticipantId: participantId,
                    DisplayName: "BoldFerret4217",
                    BidderId: "Bidder 4217",
                    CreditCeiling: 500m,
                    StartedAt: DateTimeOffset.UtcNow),
                session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<BidderCreditView>(participantId);

        row.ShouldNotBeNull();
        row.RemainingCredit.ShouldBe(415m); // Preserved, not reset to 500.
        row.LastChargedSettlementId.ShouldBe(settlementId);
    }
}
