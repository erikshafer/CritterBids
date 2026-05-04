namespace CritterBids.Settlement;

/// <summary>
/// Self-send continuation command emitted by <see cref="StartSettlementSagaHandler"/> at
/// the saga's <c>Initiated</c> phase. The saga's <c>Handle(CheckReserve)</c> verifies
/// reserve met / not-met / no-reserve per workshop 003 §2.1 / §2.2 / §2.3 and advances
/// state to <see cref="SettlementStatus.ReserveChecked"/>.
/// </summary>
public sealed record CheckReserve(Guid SettlementId);
