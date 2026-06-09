using CritterBids.Contracts.Auctions;
using JasperFx.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Http;

namespace CritterBids.Api.Dev;

/// <summary>
/// DEV-ONLY routing diagnostic (Bug #2 investigation). Asks the LIVE, fully-started Wolverine
/// runtime how it would route the messages involved in bid propagation, via
/// <c>IMessageBus.PreviewSubscriptions</c> — the exact same
/// <c>RoutingFor(message.GetType()).RouteForPublish(...)</c> path that
/// <c>UseFastEventForwarding</c>'s <c>PublishIncomingEventsBeforeCommit</c> listener hits when it
/// publishes each pending <c>IEvent</c> wrapper before <c>SaveChanges</c>. Unlike
/// <c>wolverine-diagnostics describe-routing</c>, this runs outside description mode (real senders,
/// no null-Sender NRE) and can preview the <c>Event&lt;T&gt;</c> WRAPPER types, which the CLI's
/// message-type search cannot reach.
/// </summary>
public static class RoutingProbeEndpoint
{
    public sealed record RouteInfo(string Destination, string? EnvelopeMessageType, string? TransformedMessageType);

    [WolverineGet("/api/dev/routing-probe")]
    [AllowAnonymous]
    public static IResult Get(IMessageBus bus, IHostEnvironment env)
    {
        if (!env.IsDevelopment())
            return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var bidPlaced = new BidPlaced(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 30m, 1, false, now);
        var biddingOpened = new BiddingOpened(
            Guid.NewGuid(), Guid.NewGuid(), 25m, 50m, 100m, now.AddMinutes(5),
            true, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(10), now);
        var reserveMet = new ReserveMet(Guid.NewGuid(), 55m, now);

        var probes = new Dictionary<string, object>
        {
            ["BidPlaced (raw)"] = bidPlaced,
            ["Event<BidPlaced> (forwarding wrapper)"] = new Event<BidPlaced>(bidPlaced),
            ["BiddingOpened (raw)"] = biddingOpened,
            ["Event<BiddingOpened> (forwarding wrapper)"] = new Event<BiddingOpened>(biddingOpened),
            ["ReserveMet (raw)"] = reserveMet,
            ["Event<ReserveMet> (forwarding wrapper)"] = new Event<ReserveMet>(reserveMet),
        };

        var results = new Dictionary<string, object>();
        foreach (var (label, message) in probes)
        {
            try
            {
                var envelopes = bus.PreviewSubscriptions(message);
                results[label] = envelopes.Count == 0
                    ? "NO ROUTES"
                    : envelopes.Select(e => new RouteInfo(
                        e.Destination?.ToString() ?? "<null>",
                        e.MessageType,
                        e.Message?.GetType().FullName)).ToList();
            }
            catch (Exception ex)
            {
                results[label] = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}";
            }
        }

        return Results.Json(results);
    }
}
