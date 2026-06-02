using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Auctions;

/// <summary>
/// Staff-facing HTTP surface for the two Flash-Session staff mutations (M7-S6, ADR-024). M4/M5 wired
/// <see cref="CreateSession"/> and <see cref="StartSession"/> for <c>IMessageBus</c> dispatch only;
/// this slice gives each a gated HTTP entry point without touching
/// <see cref="CreateSessionHandler"/> or <see cref="StartSessionHandler"/> — attaching an HTTP
/// attribute to either handler would deregister it as a message handler and break the M4/M5 dispatch
/// tests. These thin endpoints cascade the command and return 202; the owning handlers apply them
/// asynchronously.
///
/// <para>Both endpoints are gated by the <c>StaffOnly</c> policy (session orchestration is a staff
/// action). The literal policy string is used because the Auctions BC cannot reference the host
/// where the policy-name constant lives (ADR-024).</para>
/// </summary>
public static class SessionStaffEndpoints
{
    [WolverinePost("/api/sessions")]
    [Authorize(Policy = "StaffOnly")]
    public static (IResult, CreateSession) CreateSession(CreateSession command)
        => (Results.Accepted("/api/sessions"), command);

    [WolverinePost("/api/sessions/start")]
    [Authorize(Policy = "StaffOnly")]
    public static (IResult, StartSession) StartSession(StartSession command)
        => (Results.Accepted($"/api/sessions/{command.SessionId}"), command);
}
