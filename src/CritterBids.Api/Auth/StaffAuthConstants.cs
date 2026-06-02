namespace CritterBids.Api.Auth;

/// <summary>
/// The single contract shared by the <see cref="StaffTokenAuthenticationHandler"/>, the
/// <c>StaffOnly</c> authorization policy, and the M7-S6 auth tests — implementing ADR-024
/// (Staff-Authentication-Posture Resumption). Centralizing the scheme name, policy name, claim
/// type/value, credential transports, and the configuration key keeps the handler and its tests on
/// one definition rather than scattered magic strings (ADR-024 Decision item 2).
///
/// <para>The auth primitives live in <c>CritterBids.Api</c> (the host owns auth — ADR-024). BC
/// call sites that gate an endpoint or hub use the <b>literal</b> policy string <c>"StaffOnly"</c>
/// (a BC cannot reference the host, and adding a Contracts type is barred by the slice's acceptance
/// criteria); those call sites cite ADR-024 in a comment. These constants are consumed only where
/// the handler and policy are wired (the host) and by the host's test project.</para>
/// </summary>
public static class StaffAuthConstants
{
    /// <summary>The custom authentication-scheme name, registered as the default authenticate + challenge scheme.</summary>
    public const string SchemeName = "StaffToken";

    /// <summary>The single MVP authorization-policy name (ADR-024 — the only policy added in M7).</summary>
    public const string PolicyName = "StaffOnly";

    /// <summary>The HTTP request header carrying the staff credential on non-hub paths.</summary>
    public const string StaffTokenHeader = "X-Staff-Token";

    /// <summary>
    /// The query-string key carrying the staff credential on the <see cref="OperationsHubPath"/>
    /// SignalR path. The browser WebSocket/SSE transports cannot set an <c>Authorization</c> header,
    /// so the hub credential rides the query string (the same convention <c>AddJwtBearer</c> uses).
    /// Accepted <b>only</b> for the hub path (ADR-024 item 6); non-hub paths never read it.
    /// </summary>
    public const string AccessTokenQueryKey = "access_token";

    /// <summary>The SignalR hub path whose credential is read from <see cref="AccessTokenQueryKey"/>.</summary>
    public const string OperationsHubPath = "/hub/operations";

    /// <summary>The claim type the scheme issues on a token match and the <c>StaffOnly</c> policy requires.</summary>
    public const string StaffClaimType = "staff";

    /// <summary>The claim value the scheme issues on a token match and the <c>StaffOnly</c> policy requires.</summary>
    public const string StaffClaimValue = "true";

    /// <summary>The configuration key the configured staff token is bound from (never hard-coded).</summary>
    public const string StaffTokenConfigKey = "OperationsAuth:StaffToken";
}
