namespace CritterBids.Settlement;

/// <summary>
/// Self-send continuation command emitted by the saga's <c>Handle(CheckReserve)</c> at the
/// reserve-not-met branch per workshop 003 scenario §3.2. The saga's <c>Handle(FailSettlement)</c>
/// appends <see cref="CritterBids.Contracts.Settlement.PaymentFailed"/> to the financial event
/// stream, emits the integration event via <c>OutgoingMessages</c>, mutates state to
/// <see cref="SettlementStatus.Failed"/> with <see cref="SettlementSaga.FailureReason"/> set,
/// and calls <c>MarkCompleted()</c> — the failure-path terminal phase per scenario §9.3.
///
/// <para><b>Reason vocabulary for M5.</b> Only the literal <c>"ReserveNotMet"</c> is produced
/// at M5, matching <see cref="CritterBids.Contracts.Settlement.PaymentFailed.Reason"/>'s
/// single-value posture per the contract docstring's field-rationale section. Post-MVP failure
/// modes (insufficient credit, payment-provider rejection, ledger divergence) extend the set
/// without command-shape changes; the field stays a free-form string.</para>
///
/// <para><b>Two-field shape.</b> Mirrors the five M5-S4 self-send commands' shape but adds
/// the <see cref="Reason"/> classification string so the failure rationale survives the
/// self-send hop into <c>PaymentFailed.Reason</c> verbatim.</para>
/// </summary>
public sealed record FailSettlement(Guid SettlementId, string Reason);
