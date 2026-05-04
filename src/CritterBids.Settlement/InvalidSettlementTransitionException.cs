namespace CritterBids.Settlement;

/// <summary>
/// Thrown by the saga's <c>Handle</c> methods when a continuation command arrives in an
/// incompatible <see cref="SettlementStatus"/>. Per workshop 003 scenarios §1.3 / §2.4 /
/// §3.3 / §3.4 / §4.3 / §5.2 / §6.2 — the seven invalid-transition cases the decider
/// rejects.
///
/// <para><b>Idempotency relationship.</b> Re-delivery of <c>ListingSold</c> is handled at
/// <see cref="StartSettlementSagaHandler"/> (returns null when an existing saga is found);
/// re-delivery of a self-send continuation command (e.g. a duplicate <c>ChargeWinner</c>
/// after the saga already advanced past <see cref="SettlementStatus.WinnerCharged"/>)
/// throws this exception. Wolverine's inbox dedup should prevent the latter in practice;
/// the exception is the contract guarantee if dedup fails.</para>
///
/// <para><b>Not retryable.</b> Unlike <see cref="PendingSettlementNotFoundException"/>,
/// invalid transitions are deterministic faults — retrying the same command in the same
/// state would throw again. The exception is not registered in
/// <see cref="SettlementsConcurrencyRetryPolicies"/>.</para>
/// </summary>
public sealed class InvalidSettlementTransitionException : InvalidOperationException
{
    public InvalidSettlementTransitionException(
        Guid settlementId,
        SettlementStatus currentStatus,
        string commandType)
        : base($"Cannot apply command '{commandType}' to settlement '{settlementId}' in status '{currentStatus}'.")
    {
        SettlementId = settlementId;
        CurrentStatus = currentStatus;
        CommandType = commandType;
    }

    public Guid SettlementId { get; }
    public SettlementStatus CurrentStatus { get; }
    public string CommandType { get; }
}
