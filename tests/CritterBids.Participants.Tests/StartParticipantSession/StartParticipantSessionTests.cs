using CritterBids.Participants.Tests.Fixtures;
using ParticipantSessionStarted = CritterBids.Participants.Features.StartParticipantSession.ParticipantSessionStarted;
using StartParticipantSessionCommand = CritterBids.Participants.Features.StartParticipantSession.StartParticipantSession;

namespace CritterBids.Participants.Tests.StartParticipantSession;

[Collection(ParticipantsTestCollection.Name)]
public class StartParticipantSessionTests : IAsyncLifetime
{
    private readonly ParticipantsTestFixture _fixture;

    public StartParticipantSessionTests(ParticipantsTestFixture fixture)
        => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.CleanAllPolecatDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Scenario: Happy path — new participant session.
    /// Given: (empty stream — no prior events for this participant)
    /// When:  POST /api/participants/session with StartParticipantSession {}
    /// Then:  HTTP 201 with participant ID; ParticipantSessionStarted event in stream.
    /// </summary>
    [Fact]
    public async Task StartingSession_FromEmptyStream_ProducesParticipantSessionStarted()
    {
        // Act — TrackedHttpCall waits for Wolverine's async transaction commit before returning,
        // eliminating the race condition between HTTP response and event stream availability.
        var (_, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new StartParticipantSessionCommand()).ToUrl("/api/participants/session");
            s.StatusCodeShouldBe(201);
        });

        // Assert HTTP response — 201 with Location header pointing to the new participant resource.
        // CreationResponse<Guid> sets the Location header to /api/participants/{guid} and serializes
        // the value as a JSON object; we parse the participant ID from the Location header URL.
        var location = result.Context.Response.Headers.Location.ToString();
        location.ShouldNotBeNullOrWhiteSpace();
        var participantId = Guid.Parse(location.Split('/').Last());
        participantId.ShouldNotBe(Guid.Empty);

        // Assert event stream — ParticipantSessionStarted persisted with correct field values
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(participantId);

        events.ShouldHaveSingleItem();
        var sessionStarted = events[0].Data.ShouldBeOfType<ParticipantSessionStarted>();
        sessionStarted.ParticipantId.ShouldBe(participantId);
        sessionStarted.DisplayName.ShouldNotBeNullOrWhiteSpace();
        sessionStarted.BidderId.ShouldStartWith("Bidder ");
        sessionStarted.CreditCeiling.ShouldBeInRange(200m, 1000m);
        sessionStarted.StartedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-30));
    }

    /// <summary>
    /// Scenario: Display name is unique within active sessions.
    /// Given:  ParticipantSessionStarted for participant-001
    /// When:   StartParticipantSession {} (second request)
    /// Then:   ParticipantSessionStarted for participant-002 with a different DisplayName.
    ///
    /// Uniqueness is guaranteed by design: display names are derived from UUID v7 random bytes
    /// (bytes 8–11), which are independently randomized for each stream ID. No DB read required.
    /// </summary>
    [Fact]
    public async Task StartingSecondSession_ProducesDifferentDisplayName_ThanActiveSessions()
    {
        // Act — start first session
        var (_, result1) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new StartParticipantSessionCommand()).ToUrl("/api/participants/session");
            s.StatusCodeShouldBe(201);
        });
        var location1 = result1.Context.Response.Headers.Location.ToString();
        var participantId1 = Guid.Parse(location1.Split('/').Last());

        // Act — start second session
        var (_, result2) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new StartParticipantSessionCommand()).ToUrl("/api/participants/session");
            s.StatusCodeShouldBe(201);
        });
        var location2 = result2.Context.Response.Headers.Location.ToString();
        var participantId2 = Guid.Parse(location2.Split('/').Last());

        // Assert — two distinct participants were created
        participantId1.ShouldNotBe(participantId2);

        // Assert — their display names differ
        await using var session = _fixture.GetDocumentSession();

        var events1 = await session.Events.FetchStreamAsync(participantId1);
        var events2 = await session.Events.FetchStreamAsync(participantId2);

        var displayName1 = events1[0].Data.ShouldBeOfType<ParticipantSessionStarted>().DisplayName;
        var displayName2 = events2[0].Data.ShouldBeOfType<ParticipantSessionStarted>().DisplayName;

        displayName1.ShouldNotBeNullOrWhiteSpace();
        displayName2.ShouldNotBeNullOrWhiteSpace();
        displayName1.ShouldNotBe(displayName2);
    }
}
