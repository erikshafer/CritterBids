using CritterBids.Contracts;
using CritterBids.Participants.Tests.Fixtures;
using RegisterAsSellerCommand = CritterBids.Participants.Features.RegisterAsSeller.RegisterAsSeller;
using SellerRegisteredEvent = CritterBids.Participants.Features.RegisterAsSeller.SellerRegistered;
using StartParticipantSessionCommand = CritterBids.Participants.Features.StartParticipantSession.StartParticipantSession;

namespace CritterBids.Participants.Tests.RegisterAsSeller;

[Collection(ParticipantsTestCollection.Name)]
public class RegisterAsSellerTests : IAsyncLifetime
{
    private readonly ParticipantsTestFixture _fixture;

    public RegisterAsSellerTests(ParticipantsTestFixture fixture)
        => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.CleanAllMartenDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Scenario: Happy path — participant becomes a seller.
    /// Given:  ParticipantSessionStarted { ParticipantId: "participant-001" }
    /// When:   POST /api/participants/{id}/register-seller with RegisterAsSeller { ParticipantId: id }
    /// Then:   HTTP 200; SellerRegistered event in Polecat stream; SellerRegistrationCompleted
    ///         enqueued on the Wolverine outbox.
    /// </summary>
    [Fact]
    public async Task RegisterAsSeller_WithActiveSession_ProducesSellerRegistrationCompleted()
    {
        // Arrange — start a session to create the participant stream
        var (_, sessionResult) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new StartParticipantSessionCommand()).ToUrl("/api/participants/session");
            s.StatusCodeShouldBe(201);
        });

        // Parse participant ID from Location header — see S5 retro for why body parsing fails
        var location = sessionResult.Context.Response.Headers.Location.ToString();
        location.ShouldNotBeNullOrWhiteSpace();
        var participantId = Guid.Parse(location.Split('/').Last());
        participantId.ShouldNotBe(Guid.Empty);

        // Act — register as seller; TrackedHttpCall waits for all Wolverine side effects
        var (tracked, _) = await _fixture.TrackedHttpCall(s =>
        {
            // (a) Assert HTTP success status — 200 OK (append to existing resource, not 201 Created)
            s.Post.Json(new RegisterAsSellerCommand(participantId))
                .ToUrl($"/api/participants/{participantId}/register-seller");
            s.StatusCodeShouldBe(200);
        });

        // (b) Assert domain event in Marten stream
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(participantId);

        events.Count.ShouldBe(2); // ParticipantSessionStarted + SellerRegistered
        var sellerRegistered = events[1].Data.ShouldBeOfType<SellerRegisteredEvent>();
        sellerRegistered.ParticipantId.ShouldBe(participantId);
        sellerRegistered.CompletedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(-30));

        // (c) Assert SellerRegistrationCompleted enqueued on Wolverine outbox
        // MessagesOf<T> checks the tracked session's sent/outgoing messages.
        tracked.Sent.MessagesOf<SellerRegistrationCompleted>().ShouldHaveSingleItem();
        var integration = tracked.Sent.MessagesOf<SellerRegistrationCompleted>().Single();
        integration.ParticipantId.ShouldBe(participantId);
    }

    /// <summary>
    /// Scenario: Reject — no active session.
    /// Given:  (empty stream — no session for this participant)
    /// When:   POST /api/participants/{id}/register-seller where {id} has no event stream
    /// Then:   HTTP 4xx (404 from Wolverine's OnMissing.Simple404 — no stream = participant
    ///         not found); no events appended to the stream.
    /// </summary>
    [Fact]
    public async Task RegisterAsSeller_WithoutActiveSession_IsRejected()
    {
        // Arrange — use a random ID that has never started a session (no stream exists)
        var participantId = Guid.NewGuid();

        // Act
        await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new RegisterAsSellerCommand(participantId))
                .ToUrl($"/api/participants/{participantId}/register-seller");
            // Wolverine's [WriteAggregate] with OnMissing.Simple404 returns 404 before
            // Before() is called when no stream exists.
            s.StatusCodeShouldBe(404);
        });

        // Assert — no events were appended (stream does not exist)
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(participantId);
        events.ShouldBeEmpty();
    }

    /// <summary>
    /// Scenario: Reject — already registered (idempotent).
    /// Given:  ParticipantSessionStarted + SellerRegistered already in stream
    /// When:   RegisterAsSeller issued again for the same participant
    /// Then:   HTTP 409 Conflict; stream still has exactly 2 events (no new SellerRegistered).
    /// </summary>
    [Fact]
    public async Task RegisterAsSeller_WhenAlreadyRegistered_IsRejectedIdempotently()
    {
        // Arrange — start a session
        var (_, sessionResult) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new StartParticipantSessionCommand()).ToUrl("/api/participants/session");
            s.StatusCodeShouldBe(201);
        });
        var location = sessionResult.Context.Response.Headers.Location.ToString();
        var participantId = Guid.Parse(location.Split('/').Last());

        // Arrange — register as seller (happy path)
        await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new RegisterAsSellerCommand(participantId))
                .ToUrl($"/api/participants/{participantId}/register-seller");
            s.StatusCodeShouldBe(200);
        });

        // Act — attempt to register again
        await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new RegisterAsSellerCommand(participantId))
                .ToUrl($"/api/participants/{participantId}/register-seller");
            // Before() returns ProblemDetails { Status = 409 } when IsRegisteredSeller is true
            s.StatusCodeShouldBe(409);
        });

        // Assert — stream unchanged: still exactly 2 events (ParticipantSessionStarted + SellerRegistered)
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(participantId);
        events.Count.ShouldBe(2);
    }
}
