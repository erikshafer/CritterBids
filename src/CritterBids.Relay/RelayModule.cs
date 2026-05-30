using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Relay;

/// <summary>
/// Relay BC registration. Relay is a <b>pure-consumer reactive module</b>: it consumes integration
/// events from other BCs and pushes SignalR notifications to participant hub groups. Its only output
/// is a SignalR push — no integration events, no <c>OutgoingMessages</c>, no <c>IMessageBus</c>.
/// </summary>
public static class RelayModule
{
    /// <summary>
    /// Registers the SignalR services that back <c>BiddingHub</c> and <c>OperationsHub</c>. The hubs
    /// themselves are <c>MapHub</c>-registered in <c>Program.cs</c>, and Relay's Wolverine handlers
    /// are contributed via <c>Program.cs</c>'s <c>opts.Discovery.IncludeAssembly(...)</c> call.
    ///
    /// This method is intentionally called <b>unconditionally</b> in <c>Program.cs</c> (outside the
    /// PostgreSQL-guarded block): Relay owns no Marten documents, and <c>AddSignalR()</c> must be
    /// present for the unconditional <c>app.MapHub&lt;...&gt;()</c> calls to resolve their services
    /// — including in test hosts that skip the PostgreSQL-guarded module block.
    ///
    /// No <c>AddMarten()</c> / <c>ConfigureMarten()</c> call: Relay registers no document in M6-S5.
    /// The <c>NotificationHistoryView</c> Marten projection is deferred to M6-S6 — intentionally NOT
    /// stubbed here.
    /// </summary>
    public static IServiceCollection AddRelayModule(this IServiceCollection services)
    {
        services.AddSignalR();
        return services;
    }
}
