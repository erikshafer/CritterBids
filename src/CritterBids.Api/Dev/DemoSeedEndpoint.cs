using CritterBids.Auctions;
using CritterBids.Listings;
using CritterBids.Participants.Features.RegisterAsSeller;
using CritterBids.Participants.Features.StartParticipantSession;
using CritterBids.Selling;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Http;

namespace CritterBids.Api.Dev;

/// <summary>
/// DEV-ONLY manual-testing seed (not part of any milestone slice). Drives a single Flash listing
/// all the way to <c>Open</c> so the bidder SPA has something to bid on. It exists because the
/// seller-submit and operator-attach commands are deliberately bus-only (their UIs are future
/// milestones — M9 seller console, M8-S5/S6 ops dashboard) and the staff session endpoints 401 in
/// dev (no token configured). The composition-root host is the only place that may reference every
/// BC's command types, so the orchestration lives here, behind an <c>IsDevelopment()</c> gate.
///
/// One call performs the whole pipeline via the in-process Wolverine bus, polling the cross-BC
/// read models (fed asynchronously over RabbitMQ) between stages:
///   session → register-seller → draft → submit(publish) → create-session → attach → start.
/// </summary>
public static class DemoSeedEndpoint
{
    public sealed record SeedRequest(
        string? Title,
        decimal? StartingBid,
        decimal? ReservePrice,
        decimal? BuyItNowPrice,
        int? DurationMinutes);

    [WolverinePost("/api/dev/seed-flash")]
    [AllowAnonymous]
    public static async Task<IResult> Post(
        SeedRequest request,
        IMessageBus bus,
        IDocumentStore store,
        IHostEnvironment env,
        CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return Results.NotFound();

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? "Vintage Mechanical Keyboard"
            : request.Title!;
        var startingBid = request.StartingBid ?? 25m;
        var reserve = request.ReservePrice ?? 50m;
        var buyItNow = request.BuyItNowPrice ?? 100m;
        var durationMinutes = request.DurationMinutes ?? 5;

        // 1. A seller participant with an active session (RegisterAsSeller requires HasActiveSession).
        var seller = await bus.InvokeAsync<CreationResponse<Guid>>(new StartParticipantSession(), ct);
        var sellerId = seller.Value;

        // 2. Register as seller. Participants emits SellerRegistered + the SellerRegistrationCompleted
        //    integration event; Selling's RegisteredSeller projection consumes it over RabbitMQ.
        await bus.InvokeAsync(new RegisterAsSeller(sellerId), ct);
        await PollAsync(store, async q =>
            await q.LoadAsync<RegisteredSeller>(sellerId, ct) is not null, ct);

        // 3. Draft listing (CreateDraftListing.ValidateAsync gates on the seller being registered).
        var draft = await bus.InvokeAsync<CreationResponse<Guid>>(new CreateDraftListing(
            SellerId: sellerId,
            Title: title,
            Format: ListingFormat.Flash,
            StartingBid: startingBid,
            ReservePrice: reserve,
            BuyItNowPrice: buyItNow,
            Duration: null, // Flash listings carry no Duration — the session schedule drives the close.
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
            ExtendedBiddingExtension: TimeSpan.FromSeconds(15)), ct);
        var listingId = draft.Value;

        // 4. Submit → the handler emits Submitted+Approved+Published and publishes ListingPublished.
        //    Auctions' PublishedListings + Listings' CatalogListingView update over RabbitMQ.
        await bus.InvokeAsync(new SubmitListing(listingId, sellerId), ct);
        await PollAsync(store, async q =>
            await q.LoadAsync<PublishedListings>(listingId, ct) is { Status: PublishedListingsStatus.Published }, ct);

        // 5. Create the Flash session.
        var session = await bus.InvokeAsync<CreationResponse<Guid>>(
            new CreateSession($"Demo Flash — {title}", durationMinutes), ct);
        var sessionId = session.Value;

        // 6. Attach the published listing (validates the listing reached Auctions as PublishedListings).
        await bus.InvokeAsync(new AttachListingToSession(sessionId, listingId), ct);

        // 7. Start the session → SessionStarted → BiddingOpened fan-out → CatalogListingView "Open".
        await bus.InvokeAsync(new StartSession(sessionId), ct);
        await PollAsync(store, async q =>
            await q.LoadAsync<CatalogListingView>(listingId, ct) is { Status: "Open" }, ct);

        return Results.Ok(new
        {
            listingId,
            sessionId,
            sellerId,
            detailPath = $"/listing/{listingId}",
            durationMinutes,
            message = "Flash listing published and opened for bidding.",
        });
    }

    // Poll a fresh query session (no identity-map caching of a stale null) until the cross-BC read
    // model catches up, or time out.
    private static async Task PollAsync(
        IDocumentStore store,
        Func<IQuerySession, Task<bool>> predicate,
        CancellationToken ct,
        int timeoutSeconds = 30)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var query = store.QuerySession();
            if (await predicate(query)) return;
            await Task.Delay(250, ct);
        }

        throw new TimeoutException("Seed precondition was not met within the timeout.");
    }
}
