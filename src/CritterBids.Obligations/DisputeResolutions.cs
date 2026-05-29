namespace CritterBids.Obligations;

/// <summary>
/// The string-valued resolution constants the <see cref="PostSaleCoordinationSaga"/> branches on,
/// matching the frozen <see cref="CritterBids.Contracts.Obligations.DisputeResolved"/>
/// <c>ResolutionType</c> wire values exactly. Centralized (rather than duplicated as literals in
/// the saga and the <see cref="ObligationStatusViewProjection"/>) so the terminal-vs-continue
/// branch and the projection compare against one source of truth.
///
/// <para><b>String-match over enum (carried open question, resolved).</b> The wire contract is
/// string-valued (the <c>ListingPassed.Reason</c> precedent); rather than introduce a parallel
/// internal enum and a string↔enum mapping, the saga pattern-matches these constants directly. An
/// unrecognized <c>ResolutionType</c> falls through to the terminal branch defensively. Recorded
/// in the M6-S4 retro.</para>
/// </summary>
internal static class DisputeResolutions
{
    public const string Refund = "Refund";
    public const string Extension = "Extension";
    public const string Closed = "Closed";
}
