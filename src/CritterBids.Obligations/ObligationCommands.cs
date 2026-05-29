// Obligations-internal saga messages — the timer/command vocabulary driving the post-sale
// coordination lifecycle. All carry the deterministic UUID v5 ObligationId, which equals
// PostSaleCoordinationSaga.Id; each saga handler routes on it via
// [SagaIdentityFrom(nameof(X.ObligationId))]. These are BC-internal commands, not integration
// contracts — they never cross a BC boundary and so live in the Obligations project rather than
// CritterBids.Contracts. SendShippingReminder and SendDeadlineEscalation are scheduled at saga
// start via bus.ScheduleAsync() and cancelled (via IMessageStore.ScheduledMessages) when tracking
// is provided; ConfirmDelivery is scheduled when tracking is provided; ProvideTracking is
// dispatched by the seller-facing HTTP endpoint.

namespace CritterBids.Obligations;

/// <summary>Scheduled timer: fire the single shipping reminder for an obligation still awaiting tracking.</summary>
public sealed record SendShippingReminder(Guid ObligationId);

/// <summary>
/// Scheduled timer: the ship-by deadline elapsed. In M6-S3 the handler is a routable no-op stub so
/// the timer can be scheduled at start and cancelled on tracking; the <c>DeadlineEscalated</c>
/// emission and <c>Escalated</c> transition land in M6-S4.
/// </summary>
public sealed record SendDeadlineEscalation(Guid ObligationId);

/// <summary>
/// Command: the seller provides shipping tracking. Cancels the pending reminder + escalation
/// timers, records <see cref="CritterBids.Contracts.Obligations.TrackingInfoProvided"/>, and
/// schedules <see cref="ConfirmDelivery"/>. Carries no carrier field — the frozen
/// <c>TrackingInfoProvided</c> contract treats <c>TrackingNumber</c> as the opaque carrier string.
/// </summary>
public sealed record ProvideTracking(Guid ObligationId, string TrackingNumber);

/// <summary>Scheduled timer: auto-confirm delivery after the configured auto-confirm window.</summary>
public sealed record ConfirmDelivery(Guid ObligationId);

/// <summary>
/// Command: open a dispute against an obligation (M6-S4). Dispatched by the in-process
/// <see cref="OpenDisputeEndpoint"/>; the saga appends + emits the frozen
/// <see cref="CritterBids.Contracts.Obligations.DisputeOpened"/> contract and advances to
/// <see cref="ObligationStatus.Disputed"/> without terminating. The command <i>is</i> the HTTP
/// request body.
///
/// <para>Field rationale: <c>ObligationId</c> routes to the saga; <c>DisputeId</c> is the
/// caller-supplied dispute instance key carried onto saga state so the matching
/// <see cref="ResolveDispute"/> can be validated against it; <c>RaisedBy</c> is the participant or
/// operator who raised it; <c>Reason</c> is the string-valued enum from the frozen contract
/// (<c>NonDelivery | ItemCondition | MissedDeadline</c>).</para>
/// </summary>
public sealed record OpenDispute(Guid ObligationId, Guid DisputeId, Guid RaisedBy, string Reason);

/// <summary>
/// Command: resolve an open dispute (M6-S4). Dispatched by the in-process
/// <see cref="ResolveDisputeEndpoint"/>; the saga appends + emits the frozen
/// <see cref="CritterBids.Contracts.Obligations.DisputeResolved"/> contract. <c>Refund</c> and
/// <c>Closed</c> terminate the saga via <c>MarkCompleted()</c>; <c>Extension</c> recomputes and
/// reschedules a fresh ship-by deadline and returns the saga to awaiting-tracking without
/// terminating. The command <i>is</i> the HTTP request body.
///
/// <para>Field rationale: <c>ObligationId</c> routes to the saga; <c>DisputeId</c> is validated
/// against the open dispute's id so a stale resolution cannot resolve a different later dispute;
/// <c>ResolutionType</c> is the string-valued enum from the frozen contract
/// (<c>Refund | Extension | Closed</c>) the saga branches on.</para>
/// </summary>
public sealed record ResolveDispute(Guid ObligationId, Guid DisputeId, string ResolutionType);
