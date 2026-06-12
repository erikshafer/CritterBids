using System.Reflection;
using CritterBids.Operations;
using CritterBids.Relay.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CritterBids.Relay.Tests;

/// <summary>
/// Enforces the M8-S6b ops-feed topology invariant mechanically: <b>every integration event that
/// mutates an Operations BC read model has a corresponding <c>OperationsFeedNotification</c>
/// publication in Relay</c></b> (the ADR 026 push-equals-re-query contract is total for the ops
/// dashboard — no board depends on polling). The invariant was stated by both M8-S6b decision
/// evaluations (<c>docs/research/ops-feed-completion-evaluation-comparison.md</c>); this test is
/// Evaluation A's "enforced, not just documented" carry-forward.
///
/// <para><b>How each side is derived.</b> The Operations side enumerates every <c>Handle</c>
/// method in <c>CritterBids.Operations</c> whose first parameter is a <c>CritterBids.Contracts</c>
/// type — the lived consumed-event vocabulary, so a future Operations consumer joins the invariant
/// automatically. The Relay side enumerates every <c>Handle</c> method in <c>CritterBids.Relay</c>
/// that both receives the event type and injects <c>IHubContext&lt;OperationsHub&gt;</c> — the
/// established dual-push template (<c>BidPlacedHandler</c> / <c>ListingSoldHandler</c> /
/// <c>ObligationsRelayHandler</c>), where taking the ops hub context is what a publication looks
/// like. Injecting the context without sending would defeat the proxy, but no lived handler does
/// (the per-event push tests in <see cref="OperationsHubPushTests"/> cover the send itself).</para>
///
/// <para>The invariant is deliberately one-directional: Relay may push ops-feed events Operations
/// does not consume (e.g. <c>LotWatchAdded</c>, <c>SellerRegistrationCompleted</c> — feed-only
/// color for the dashboard); those need no Operations handler.</para>
/// </summary>
public class OperationsFeedTopologyTests
{
    [Fact]
    public void EveryOperationsConsumedEvent_HasARelayOperationsFeedPublication()
    {
        var consumedByOperations = OperationsConsumedEventTypes();

        // Sanity floor: the lived Operations surface consumes well over a dozen integration
        // events; an empty/short scan means the reflection went stale, not that the invariant holds.
        consumedByOperations.Count.ShouldBeGreaterThanOrEqualTo(15);

        var pushedToOperationsHub = RelayOperationsHubEventTypes();

        var missing = consumedByOperations
            .Where(eventType => !pushedToOperationsHub.Contains(eventType))
            .Select(eventType => eventType.Name)
            .OrderBy(name => name)
            .ToList();

        missing.ShouldBeEmpty(
            "Every integration event consumed by an Operations BC handler must have a Relay " +
            "handler that publishes an OperationsFeedNotification (a Handle method receiving the " +
            "event and injecting IHubContext<OperationsHub>). Missing ops-feed publications: " +
            string.Join(", ", missing));
    }

    /// <summary>
    /// The integration events the Operations BC consumes: the first parameter of every
    /// <c>Handle</c> method in the Operations assembly whose type lives in
    /// <c>CritterBids.Contracts</c>. (<c>BidActivityHandler.AppendActivityAsync</c> is deliberately
    /// not named <c>Handle</c> — ADR 027 consolidated it under
    /// <c>LotBoardAuctionsHandler.Handle(BidPlaced)</c>, which this scan sees.)
    /// </summary>
    private static IReadOnlyCollection<Type> OperationsConsumedEventTypes() =>
        HandledContractEventTypes(typeof(OperationsModule).Assembly)
            .Select(handled => handled.EventType)
            .ToHashSet();

    /// <summary>
    /// The integration events Relay publishes to the ops feed: every <c>Handle</c> method in the
    /// Relay assembly that injects <c>IHubContext&lt;OperationsHub&gt;</c> alongside the event.
    /// </summary>
    private static IReadOnlyCollection<Type> RelayOperationsHubEventTypes() =>
        HandledContractEventTypes(typeof(OperationsHub).Assembly)
            .Where(handled => handled.Method
                .GetParameters()
                .Any(p => p.ParameterType == typeof(IHubContext<OperationsHub>)))
            .Select(handled => handled.EventType)
            .ToHashSet();

    private static IEnumerable<(Type EventType, MethodInfo Method)> HandledContractEventTypes(
        Assembly assembly) =>
        assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.Name == "Handle")
            .Select(method => (Method: method, Parameters: method.GetParameters()))
            .Where(m => m.Parameters.Length > 0
                        && m.Parameters[0].ParameterType.Namespace?.StartsWith(
                            "CritterBids.Contracts", StringComparison.Ordinal) == true)
            .Select(m => (m.Parameters[0].ParameterType, m.Method));
}
