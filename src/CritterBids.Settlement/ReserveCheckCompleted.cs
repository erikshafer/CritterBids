namespace CritterBids.Settlement;

/// <summary>
/// Emitted by the saga's <c>Handle(CheckReserve)</c> phase per workshop 003 scenarios
/// §2.1 (reserve met), §2.2 (reserve not met — defense-in-depth), and §2.3 (no reserve set —
/// always met). The <see cref="WasMet"/> field is the binding financial verification by
/// Settlement (the BC that owns reserve enforcement); Auctions' earlier <c>ReserveMet</c>
/// integration event is the UX-grade real-time signal published during the auction.
///
/// <para><b>Stream-internal — not in <c>CritterBids.Contracts.Settlement.*</c>.</b> Per
/// W003 §"Integration in/out", the reserve-check decision is entirely a Settlement-internal
/// concern; downstream consumers do not subscribe to it. The financial event stream stores
/// it as audit ground.</para>
///
/// <para><b>Per-event payload only.</b> Unlike most settlement-internal events, this one
/// does NOT carry <c>SettlementId</c> per W003's stream-internal scoping rule (§canonical
/// payload preamble). The event is scoped to the saga's own state machine via the stream
/// it appends to; downstream consumers do not need the correlation field.</para>
/// </summary>
public sealed record ReserveCheckCompleted(
    decimal Price,
    decimal? ReservePrice,
    bool WasMet,
    DateTimeOffset CompletedAt);
