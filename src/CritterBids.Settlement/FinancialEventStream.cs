namespace CritterBids.Settlement;

/// <summary>
/// Stream-type marker for the per-settlement financial event stream. Required because
/// Program.cs's <c>opts.Events.UseMandatoryStreamTypeDeclaration = true</c> forces every
/// new stream to declare its type at <c>session.Events.StartStream&lt;T&gt;</c>. Mirrors
/// the <see cref="CritterBids.Auctions.BidRejectionAudit"/> shape — not projected into a
/// live aggregate, not registered with <c>LiveStreamAggregation</c>; sole purpose is
/// satisfying Marten's mandatory stream-type-declaration rule.
///
/// <para>The stream key (<see cref="Id"/>) equals the deterministic <c>SettlementId</c>
/// derived via <see cref="SettlementsIdentityNamespaces.SettlementId"/>. The stream stores
/// the audit log of every event in a settlement's lifecycle per W003 §"Financial Event
/// Stream"; never deleted.</para>
/// </summary>
public class FinancialEventStream
{
    public Guid Id { get; set; }
}
