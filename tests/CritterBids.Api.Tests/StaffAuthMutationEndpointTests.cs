using System.Net;
using System.Net.Http.Json;
using CritterBids.Api.Tests.Fixtures;

namespace CritterBids.Api.Tests;

/// <summary>
/// M7-S6 (ADR-024): the four staff mutations wired-then-gated in their owning BCs —
/// <c>POST /api/selling/listings/withdraw</c>, <c>POST /api/sessions</c>,
/// <c>POST /api/sessions/start</c>, and <c>POST /api/obligations/disputes/resolve</c>. Each must
/// reject an unauthenticated or wrong-token request with 401, and with the valid staff token return
/// 202 Accepted (the thin endpoint cascades the command and returns before it is handled). Run on the
/// real Kestrel + Testcontainers host (ADR-024 item 8).
/// </summary>
[Collection(StaffAuthTestCollection.Name)]
public sealed class StaffAuthMutationEndpointTests
{
    private readonly StaffAuthTestFixture _fixture;

    public StaffAuthMutationEndpointTests(StaffAuthTestFixture fixture) => _fixture = fixture;

    public static IEnumerable<object[]> Mutations =>
    [
        ["/api/selling/listings/withdraw", (object)new { ListingId = Guid.CreateVersion7(), WithdrawnBy = Guid.CreateVersion7() }],
        ["/api/sessions", new { Title = "Flash session", DurationMinutes = 30 }],
        ["/api/sessions/start", new { SessionId = Guid.CreateVersion7() }],
        ["/api/obligations/disputes/resolve", new { ObligationId = Guid.CreateVersion7(), DisputeId = Guid.CreateVersion7(), ResolutionType = "Closed" }],
    ];

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Mutation_without_token_is_401(string route, object body)
    {
        using var client = _fixture.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync(route, body);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Mutation_with_invalid_token_is_401(string route, object body)
    {
        using var client = _fixture.CreateInvalidTokenClient();

        var response = await client.PostAsJsonAsync(route, body);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task Mutation_with_token_is_202(string route, object body)
    {
        using var client = _fixture.CreateStaffClient();

        var response = await client.PostAsJsonAsync(route, body);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }
}
