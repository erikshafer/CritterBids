using CritterBids.Api.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR.Client;

namespace CritterBids.Api.Tests;

/// <summary>
/// M7-S6 (ADR-024 item 6): the <c>OperationsHub</c> at <c>/hub/operations</c> is <c>StaffOnly</c>-gated.
/// A connection without the <c>access_token</c> query credential must be rejected at negotiate; a
/// connection presenting the valid token must establish. Run on the real Kestrel + Testcontainers host
/// (the SignalR WebSocket transport cannot run under an in-memory TestServer — ADR-024 item 8).
/// </summary>
[Collection(StaffAuthTestCollection.Name)]
public sealed class StaffAuthHubTests
{
    private readonly StaffAuthTestFixture _fixture;

    public StaffAuthHubTests(StaffAuthTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task OperationsHub_without_access_token_is_rejected()
    {
        await using var connection = _fixture.BuildOperationsConnection(accessToken: null);

        await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync());

        connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task OperationsHub_with_invalid_access_token_is_rejected()
    {
        await using var connection = _fixture.BuildOperationsConnection(accessToken: "not-the-staff-token");

        await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync());

        connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    [Fact]
    public async Task OperationsHub_with_valid_access_token_connects()
    {
        await using var connection = _fixture.BuildOperationsConnection(StaffAuthTestFixture.ValidStaffToken);

        await connection.StartAsync();

        connection.State.ShouldBe(HubConnectionState.Connected);

        await connection.StopAsync();
    }
}
