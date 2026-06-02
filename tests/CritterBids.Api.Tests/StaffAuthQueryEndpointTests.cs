using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CritterBids.Api.Tests.Fixtures;
using CritterBids.Operations;
using Marten;

namespace CritterBids.Api.Tests;

/// <summary>
/// M7-S6 (ADR-024): the staff query surface over the six Operations views. Each endpoint must reject
/// an unauthenticated or wrong-token request with 401 and, with the valid staff token, return the
/// real seeded rows. The two obligations endpoints additionally prove the queue-state filter
/// (escalations = only <c>Escalated</c>, disputes = only <c>Disputed</c>; terminal/Active excluded).
/// Run on the real Kestrel + Testcontainers host (ADR-024 item 8).
/// </summary>
[Collection(StaffAuthTestCollection.Name)]
public sealed class StaffAuthQueryEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly StaffAuthTestFixture _fixture;

    public StaffAuthQueryEndpointTests(StaffAuthTestFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> QueryRoutes =>
    [
        ["/api/operations/settlement-queue"],
        ["/api/operations/lot-board"],
        ["/api/operations/bid-activity"],
        ["/api/operations/obligations/escalations"],
        ["/api/operations/obligations/disputes"],
        ["/api/operations/sessions"],
        ["/api/operations/participants"],
    ];

    [Theory]
    [MemberData(nameof(QueryRoutes))]
    public async Task Query_without_token_is_401(string route)
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await client.GetAsync(route);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(QueryRoutes))]
    public async Task Query_with_invalid_token_is_401(string route)
    {
        using var client = _fixture.CreateInvalidTokenClient();

        var response = await client.GetAsync(route);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SettlementQueue_with_token_returns_seeded_rows()
    {
        await _fixture.ResetMartenAsync();
        var settlementId = Guid.CreateVersion7();
        await SeedAsync(new SettlementQueueView
        {
            SettlementId = settlementId,
            ListingId = Guid.CreateVersion7(),
            WinnerId = Guid.CreateVersion7(),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        });

        var rows = await GetStaffAsync<SettlementQueueView>("/api/operations/settlement-queue");

        rows.ShouldHaveSingleItem().SettlementId.ShouldBe(settlementId);
    }

    [Fact]
    public async Task LotBoard_with_token_returns_seeded_rows()
    {
        await _fixture.ResetMartenAsync();
        var listingId = Guid.CreateVersion7();
        await SeedAsync(new LotBoardView
        {
            ListingId = listingId,
            SellerId = Guid.CreateVersion7(),
            LastUpdatedAt = DateTimeOffset.UtcNow,
        });

        var rows = await GetStaffAsync<LotBoardView>("/api/operations/lot-board");

        rows.ShouldHaveSingleItem().ListingId.ShouldBe(listingId);
    }

    [Fact]
    public async Task BidActivity_with_token_returns_seeded_rows()
    {
        await _fixture.ResetMartenAsync();
        var bidId = Guid.CreateVersion7();
        await SeedAsync(new BidActivityEntry
        {
            BidId = bidId,
            ListingId = Guid.CreateVersion7(),
            BidderId = Guid.CreateVersion7(),
            Amount = 100m,
            BidCount = 1,
            PlacedAt = DateTimeOffset.UtcNow,
        });

        var rows = await GetStaffAsync<BidActivityEntry>("/api/operations/bid-activity");

        rows.ShouldHaveSingleItem().BidId.ShouldBe(bidId);
    }

    [Fact]
    public async Task Sessions_with_token_returns_seeded_rows()
    {
        await _fixture.ResetMartenAsync();
        var sessionId = Guid.CreateVersion7();
        await SeedAsync(new SessionActivityView
        {
            SessionId = sessionId,
            Title = "Flash session",
            DurationMinutes = 30,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var rows = await GetStaffAsync<SessionActivityView>("/api/operations/sessions");

        rows.ShouldHaveSingleItem().SessionId.ShouldBe(sessionId);
    }

    [Fact]
    public async Task Participants_with_token_returns_seeded_rows()
    {
        await _fixture.ResetMartenAsync();
        var participantId = Guid.CreateVersion7();
        await SeedAsync(new ParticipantActivityView
        {
            ParticipantId = participantId,
            DisplayName = "Test Staff",
            BidderId = "Bidder 4217",
            CreditCeiling = 5000m,
            StartedAt = DateTimeOffset.UtcNow,
        });

        var rows = await GetStaffAsync<ParticipantActivityView>("/api/operations/participants");

        rows.ShouldHaveSingleItem().ParticipantId.ShouldBe(participantId);
    }

    [Fact]
    public async Task Obligations_endpoints_filter_by_queue_state()
    {
        await _fixture.ResetMartenAsync();

        var escalated = NewObligation(QueueState.Escalated, escalatedAt: DateTimeOffset.UtcNow);
        var disputed = NewObligation(QueueState.Disputed, disputeOpenedAt: DateTimeOffset.UtcNow);
        var active = NewObligation(QueueState.Active);
        var fulfilled = NewObligation(QueueState.Fulfilled);
        await SeedAsync(escalated, disputed, active, fulfilled);

        var escalations = await GetStaffAsync<OperationsObligationsView>("/api/operations/obligations/escalations");
        var disputes = await GetStaffAsync<OperationsObligationsView>("/api/operations/obligations/disputes");

        escalations.ShouldHaveSingleItem().ObligationId.ShouldBe(escalated.ObligationId);
        disputes.ShouldHaveSingleItem().ObligationId.ShouldBe(disputed.ObligationId);
    }

    private static OperationsObligationsView NewObligation(
        QueueState state,
        DateTimeOffset? escalatedAt = null,
        DateTimeOffset? disputeOpenedAt = null) =>
        new()
        {
            ObligationId = Guid.CreateVersion7(),
            ListingId = Guid.CreateVersion7(),
            QueueState = state,
            EscalatedAt = escalatedAt,
            DisputeOpenedAt = disputeOpenedAt,
        };

    private async Task SeedAsync<T>(params T[] docs) where T : notnull
    {
        await using var session = _fixture.DocumentStore.LightweightSession();
        session.Store(docs);
        await session.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<T>> GetStaffAsync<T>(string route)
    {
        using var client = _fixture.CreateStaffClient();
        var response = await client.GetAsync(route);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = await response.Content.ReadFromJsonAsync<List<T>>(Json);
        return rows ?? [];
    }
}
