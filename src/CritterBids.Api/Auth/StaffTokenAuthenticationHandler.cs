using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CritterBids.Api.Auth;

/// <summary>
/// The custom config-bound staff-authentication handler — ADR-024 Option A. Registered as the
/// default authenticate + challenge scheme (<see cref="StaffAuthConstants.SchemeName"/>), it closes
/// the no-<c>DefaultChallengeScheme</c> runtime trap ADR-024 names: with no default scheme a
/// guarded request 500s rather than 401s.
///
/// <para><b>Credential transport.</b> For the SignalR <see cref="StaffAuthConstants.OperationsHubPath"/>
/// path it reads the credential from the <see cref="StaffAuthConstants.AccessTokenQueryKey"/> query
/// string (the WebSocket transport cannot carry a custom header — ASP.NET does not wire the query
/// string into a custom scheme automatically, so this read is explicit). For every other (HTTP)
/// path it reads the <see cref="StaffAuthConstants.StaffTokenHeader"/> header; non-hub paths never
/// accept a query-string credential.</para>
///
/// <para><b>Validation, not trust (ADR-024 item 4).</b> The presented token is compared to a single
/// value bound from configuration (<see cref="StaffAuthConstants.StaffTokenConfigKey"/>). An empty
/// or missing configured token authenticates <b>no</b> request — an empty presented credential must
/// never match an empty configured secret. On a constant-time match the handler issues an
/// authenticated principal carrying the fixed <c>staff</c> claim; on absence or mismatch it returns
/// <see cref="AuthenticateResult.NoResult"/> so the authorization middleware issues a 401 challenge.
/// Because the scheme only ever issues a principal <i>carrying</i> the <c>staff</c> claim, an
/// authenticated request is always a staff request in the single-shared-secret MVP — there is no
/// authenticated-but-non-staff principal, so 403 is structural headroom for the post-MVP per-user
/// model, never observed under this scheme (ADR-024 item 7).</para>
/// </summary>
public sealed class StaffTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public StaffTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var configuredToken = _configuration[StaffAuthConstants.StaffTokenConfigKey];

        // ADR-024 item 4: an empty/missing configured token authenticates nothing — it must never
        // match an empty presented credential. Returning NoResult lets the challenge issue a 401.
        if (string.IsNullOrEmpty(configuredToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        // ADR-024 item 6: the SignalR transport reads access_token from the query string; every
        // other path reads the X-Staff-Token header. Non-hub paths never accept a query credential.
        string? presented = Request.Path.StartsWithSegments(StaffAuthConstants.OperationsHubPath)
            ? Request.Query[StaffAuthConstants.AccessTokenQueryKey]
            : Request.Headers[StaffAuthConstants.StaffTokenHeader];

        if (string.IsNullOrEmpty(presented))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (!FixedTimeEquals(presented, configuredToken))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[] { new Claim(StaffAuthConstants.StaffClaimType, StaffAuthConstants.StaffClaimValue) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Constant-time comparison of the presented and configured tokens (relative to length).
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> returns false for differing lengths,
    /// so a length-mismatch is a clean non-match rather than an early-out timing oracle.
    /// </summary>
    private static bool FixedTimeEquals(string presented, string configured) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(configured));
}
