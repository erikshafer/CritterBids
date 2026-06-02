using CritterBids.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CritterBids.Api.Tests;

/// <summary>
/// Unit tests for the empty-token startup guard (ADR-024 item 4). These need no host or container —
/// <see cref="StaffAuthenticationExtensions.EnsureStaffTokenConfigured"/> is a pure function of an
/// <see cref="IConfiguration"/> and an <see cref="IHostEnvironment"/>. The guard fires only in
/// Production with no configured token; Development/Testing and any environment with a token present
/// boot cleanly (so the M1–M6 Alba fixtures and local dev are never tripped by it).
/// </summary>
public sealed class StaffTokenStartupGuardTests
{
    private static IConfiguration ConfigWith(string? token)
    {
        var values = new Dictionary<string, string?>();
        if (token is not null)
            values[StaffAuthConstants.StaffTokenConfigKey] = token;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Production_with_no_token_throws()
    {
        var env = new StubHostEnvironment(Environments.Production);

        Should.Throw<InvalidOperationException>(() =>
            StaffAuthenticationExtensions.EnsureStaffTokenConfigured(ConfigWith(null), env));
    }

    [Fact]
    public void Production_with_empty_token_throws()
    {
        var env = new StubHostEnvironment(Environments.Production);

        Should.Throw<InvalidOperationException>(() =>
            StaffAuthenticationExtensions.EnsureStaffTokenConfigured(ConfigWith(string.Empty), env));
    }

    [Fact]
    public void Production_with_token_does_not_throw()
    {
        var env = new StubHostEnvironment(Environments.Production);

        Should.NotThrow(() =>
            StaffAuthenticationExtensions.EnsureStaffTokenConfigured(ConfigWith("a-real-token"), env));
    }

    [Fact]
    public void Development_with_no_token_does_not_throw()
    {
        var env = new StubHostEnvironment(Environments.Development);

        Should.NotThrow(() =>
            StaffAuthenticationExtensions.EnsureStaffTokenConfigured(ConfigWith(null), env));
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string environmentName) => EnvironmentName = environmentName;

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "CritterBids.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
