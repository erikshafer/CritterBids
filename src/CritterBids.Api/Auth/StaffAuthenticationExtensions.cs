using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CritterBids.Api.Auth;

/// <summary>
/// The single production wiring for the ADR-024 staff-auth posture, shared verbatim by
/// <c>Program.cs</c> and the M7-S6 real-Kestrel auth fixture so the host and its tests exercise one
/// definition (no prod/test drift). Implements ADR-024 Decision items 1, 2, and 4.
/// </summary>
public static class StaffAuthenticationExtensions
{
    /// <summary>
    /// Registers the <see cref="StaffTokenAuthenticationHandler"/> as the <b>default authenticate +
    /// challenge scheme</b> (ADR-024 item 1) — the fix for the no-<c>DefaultChallengeScheme</c>
    /// runtime trap. Replaces the bare <c>AddAuthentication()</c> call.
    /// </summary>
    public static AuthenticationBuilder AddStaffTokenAuthentication(this IServiceCollection services) =>
        services
            .AddAuthentication(StaffAuthConstants.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, StaffTokenAuthenticationHandler>(
                StaffAuthConstants.SchemeName, _ => { });

    /// <summary>
    /// Registers the single MVP <c>StaffOnly</c> policy (ADR-024 item 2):
    /// <c>RequireAuthenticatedUser()</c> + the fixed <c>staff</c> claim the scheme issues. The policy
    /// names no scheme — the default scheme covers it. This is the only policy added in M7.
    /// </summary>
    public static IServiceCollection AddStaffAuthorizationPolicy(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(StaffAuthConstants.PolicyName, policy =>
                policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(StaffAuthConstants.StaffClaimType, StaffAuthConstants.StaffClaimValue));
        });

        return services;
    }

    /// <summary>
    /// ADR-024 item 4: fail host startup outside test/dev when the staff-gated surfaces are enabled
    /// and no staff token is configured, rather than booting with silently inaccessible (or worse,
    /// accidentally open) staff surfaces. The guard fires only in the Production environment — the
    /// in-memory and real-Kestrel test fixtures (Development / Testing) and local dev are exempt, so
    /// a missing token there is a 401-everything host, not a boot failure.
    /// </summary>
    public static void EnsureStaffTokenConfigured(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
            return;

        var token = configuration[StaffAuthConstants.StaffTokenConfigKey];
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                $"Staff-gated surfaces are enabled but no staff token is configured at " +
                $"'{StaffAuthConstants.StaffTokenConfigKey}'. Set it via configuration " +
                "(appsettings / user-secrets / environment) before starting in Production (ADR-024 item 4).");
    }
}
