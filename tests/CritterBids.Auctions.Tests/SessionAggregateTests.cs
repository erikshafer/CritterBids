using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Integration tests for the M4-S5 Session aggregate. Workshop 002 §5 scenarios; method
/// names per <c>docs/milestones/M4-auctions-bc-completion.md</c> §7 §5 verbatim.
///
/// <para>Tests call the three handler static methods directly against the real Marten
/// store (via <see cref="AuctionsTestFixture"/>'s Testcontainers Postgres). The bus-
/// dispatch path is covered by the three dispatch tests in
/// <c>CreateSessionDispatchTests</c> / <c>AttachListingToSessionDispatchTests</c> /
/// <c>StartSessionDispatchTests</c>. Direct handler invocation keeps the assertion
/// focused on the aggregate's state transitions + the rejection invariants.</para>
///
/// <para>For each happy-path scenario the test:
/// <list type="bullet">
///   <item>Seeds prior state via <see cref="AuctionsTestFixture.SeedSessionAsync"/> and
///     (where needed) <see cref="AuctionsTestFixture.SeedPublishedListingAsync"/>.</item>
///   <item>Loads the Session aggregate via
///     <c>session.Events.AggregateStreamAsync&lt;Session&gt;(sessionId)</c>.</item>
///   <item>Calls the handler directly with the loaded aggregate.</item>
///   <item>Appends the returned events to the Session stream via
///     <c>session.Events.Append</c> + <c>SaveChangesAsync</c>.</item>
///   <item>Re-loads the aggregate via <c>AggregateStreamAsync</c> and asserts the
///     transitioned state.</item>
/// </list>
/// For rejection scenarios: skip the append step; <c>Should.ThrowAsync</c> /
/// <c>Should.Throw</c> catches the exception thrown by the handler.</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class SessionAggregateTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public SessionAggregateTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── §5.1 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_ProducesSessionCreated()
    {
        var cmd = new CreateSession(Title: "Nebraska.Code() Live Auction", DurationMinutes: 5);

        var (response, startStream) = CreateSessionHandler.Handle(cmd);

        startStream.AggregateType.ShouldBe(typeof(Session));
        startStream.Events.Count.ShouldBe(1);
        var created = startStream.Events[0].ShouldBeOfType<SessionCreated>();

        created.SessionId.ShouldBe(response.Value);
        created.Title.ShouldBe("Nebraska.Code() Live Auction");
        created.DurationMinutes.ShouldBe(5);
        created.CreatedAt.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));

        response.Url.ShouldStartWith("/api/sessions/");

        // Persist via Marten StartStream and verify the live-aggregated Session reads back.
        await using (var session = _fixture.GetDocumentSession())
        {
            session.Events.StartStream<Session>(created.SessionId, created);
            await session.SaveChangesAsync();
        }

        await using var query = _fixture.GetDocumentSession();
        var aggregate = await query.Events.AggregateStreamAsync<Session>(created.SessionId);
        aggregate.ShouldNotBeNull();
        aggregate!.Id.ShouldBe(created.SessionId);
        aggregate.Title.ShouldBe("Nebraska.Code() Live Auction");
        aggregate.DurationMinutes.ShouldBe(5);
        aggregate.AttachedListingIds.ShouldBeEmpty();
        aggregate.StartedAt.ShouldBeNull();
    }

    // ─── §5.2 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AttachListing_Published_ProducesListingAttachedToSession()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(title: "Live Auction");
        await _fixture.SeedPublishedListingAsync(listingId, sellerId);

        var cmd = new AttachListingToSession(sessionId, listingId);

        Events emitted;
        await using (var session = _fixture.GetDocumentSession())
        {
            var aggregate = (await session.Events.AggregateStreamAsync<Session>(sessionId))!;
            emitted = await AttachListingToSessionHandler.Handle(cmd, aggregate, session, default);
            session.Events.Append(sessionId, emitted.OfType<object>().ToArray());
            await session.SaveChangesAsync();
        }

        emitted.Count.ShouldBe(1);
        var attached = emitted[0].ShouldBeOfType<ListingAttachedToSession>();
        attached.SessionId.ShouldBe(sessionId);
        attached.ListingId.ShouldBe(listingId);

        await using var query = _fixture.GetDocumentSession();
        var updated = (await query.Events.AggregateStreamAsync<Session>(sessionId))!;
        updated.AttachedListingIds.ShouldHaveSingleItem().ShouldBe(listingId);
        updated.StartedAt.ShouldBeNull();
    }

    // ─── §5.3 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AttachListing_NotPublished_Rejected()
    {
        var sessionId = await _fixture.SeedSessionAsync();
        var unpublishedListingId = Guid.CreateVersion7();
        // Deliberately do NOT call SeedPublishedListingAsync — the projection row is absent,
        // which the handler interprets the same way as a Withdrawn row per §5.3.

        var cmd = new AttachListingToSession(sessionId, unpublishedListingId);

        await using var session = _fixture.GetDocumentSession();
        var aggregate = (await session.Events.AggregateStreamAsync<Session>(sessionId))!;

        var ex = await Should.ThrowAsync<ListingNotPublishedException>(
            () => AttachListingToSessionHandler.Handle(cmd, aggregate, session, default));
        ex.ListingId.ShouldBe(unpublishedListingId);
    }

    // ─── §5.4 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AttachListing_SessionStarted_Rejected()
    {
        var alreadyAttachedListingId = Guid.CreateVersion7();
        var newListingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(
            attachedListingIds: new[] { alreadyAttachedListingId },
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-5));
        await _fixture.SeedPublishedListingAsync(newListingId, sellerId);

        var cmd = new AttachListingToSession(sessionId, newListingId);

        await using var session = _fixture.GetDocumentSession();
        var aggregate = (await session.Events.AggregateStreamAsync<Session>(sessionId))!;

        var ex = await Should.ThrowAsync<SessionAlreadyStartedException>(
            () => AttachListingToSessionHandler.Handle(cmd, aggregate, session, default));
        ex.SessionId.ShouldBe(sessionId);
    }

    // ─── §5.5 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartSession_WithAttachedListings_ProducesSessionStarted()
    {
        var listingA = Guid.CreateVersion7();
        var listingB = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(
            attachedListingIds: new[] { listingA, listingB });

        var cmd = new StartSession(sessionId);

        Events emitted;
        await using (var session = _fixture.GetDocumentSession())
        {
            var aggregate = (await session.Events.AggregateStreamAsync<Session>(sessionId))!;
            emitted = StartSessionHandler.Handle(cmd, aggregate);
            session.Events.Append(sessionId, emitted.OfType<object>().ToArray());
            await session.SaveChangesAsync();
        }

        emitted.Count.ShouldBe(1);
        var started = emitted[0].ShouldBeOfType<SessionStarted>();
        started.SessionId.ShouldBe(sessionId);
        started.ListingIds.ShouldBe(new[] { listingA, listingB });
        started.StartedAt.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));

        await using var query = _fixture.GetDocumentSession();
        var updated = (await query.Events.AggregateStreamAsync<Session>(sessionId))!;
        updated.StartedAt.ShouldNotBeNull();
    }

    // ─── §5.6 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartSession_NoListings_Rejected()
    {
        var sessionId = await _fixture.SeedSessionAsync();

        var cmd = new StartSession(sessionId);

        await using var session = _fixture.GetDocumentSession();
        var aggregate = (await session.Events.AggregateStreamAsync<Session>(sessionId))!;

        var ex = Should.Throw<SessionHasNoListingsException>(
            () => StartSessionHandler.Handle(cmd, aggregate));
        ex.SessionId.ShouldBe(sessionId);
    }

    // ─── §5.7 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartSession_AlreadyStarted_Rejected()
    {
        var listingA = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(
            attachedListingIds: new[] { listingA },
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-5));

        var cmd = new StartSession(sessionId);

        await using var session = _fixture.GetDocumentSession();
        var aggregate = (await session.Events.AggregateStreamAsync<Session>(sessionId))!;

        var ex = Should.Throw<SessionAlreadyStartedException>(
            () => StartSessionHandler.Handle(cmd, aggregate));
        ex.SessionId.ShouldBe(sessionId);
    }
}
